using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceNowToAdo.Functions;
using ServiceNowToAdo.Models;
using ServiceNowToAdo.Services;
using Xunit;

namespace ServiceNowToAdo.Tests;

public class CreateUserStoryFunctionTests
{
    private static CreateUserStoryFunction BuildFunction(IAdoClient adoClient, IIdempotencyLockService? lockService = null)
        => BuildFunction(adoClient, true, lockService);

    private static CreateUserStoryFunction BuildFunction(IAdoClient adoClient, bool enableDeduplication, IIdempotencyLockService? lockService = null)
    {
        var log = NullLogger<CreateUserStoryFunction>.Instance;
        var validator = new PayloadValidator();
        var adoOpts = Options.Create(new AdoOptions
        {
            Organization = "myorg",
            PersonalAccessToken = "pat",
            EnableDeduplication = enableDeduplication
        });
        var logOpts = Options.Create(new LoggingOptions { VerbosePayload = false });
        return new CreateUserStoryFunction(log, validator, adoClient, lockService ?? new StubIdempotencyLockService(), adoOpts, logOpts);
    }

    private static HttpRequestData BuildRequest(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = new TestFunctionContext();
        var request = new TestHttpRequestData(
            context,
            body,
            new Uri("https://localhost/api/servicenow/userstory"),
            "POST");
        request.Headers.Add("Content-Type", "application/json");
        return request;
    }

    [Fact]
    public async Task NonAmbiguousDuplicate_Returns200Duplicate_NoPatchSent()
    {
        var existing = new ExistingWorkItem(99, "https://dev.azure.com/myorg/UPO/_workitems/edit/99", "UPO", IsAmbiguousMatch: false);
        var stub = new StubAdoClient
        {
            ProjectExists = true,
            ExistingItem = existing
        };

        var function = BuildFunction(stub);
        var req = BuildRequest(new
        {
            IncidentNumber = "INC0012456",
            TeamProject = "UPO",
            Title = "VPN issue",
            Description = "Cannot connect.",
            IncidentUrl = (string?)null
        });

        var result = await function.Run(req, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        result.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<CreateResponse>(result.Body);
        Assert.NotNull(response);
        Assert.Equal("duplicate", response.Status);
        Assert.Equal(99, response.WorkItemId);

        Assert.False(stub.CreateCalled, "CreateUserStoryAsync must not be called for a duplicate");
    }

    [Fact]
    public async Task LockAcquired_ProceedsToCreatePath_Returns201()
    {
        var stubAdo = new StubAdoClient
        {
            ProjectExists = true,
            ExistingItem = null
        };
        var function = BuildFunction(stubAdo);
        var req = BuildRequest(new
        {
            IncidentNumber = "INC0012457",
            TeamProject = "UPO",
            Title = "VPN issue",
            Description = "Cannot connect.",
            IncidentUrl = (string?)null
        });

        var result = await function.Run(req, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.True(stubAdo.CreateCalled, "CreateUserStoryAsync should be called when lock is acquired.");
    }

    [Fact]
    public async Task LockAlreadyHeld_Returns200DuplicateFromSentinel()
    {
        var existing = new ExistingWorkItem(101, "https://dev.azure.com/myorg/UPO/_workitems/edit/101", "UPO");
        var lockService = new StubIdempotencyLockService
        {
            Handle = new DuplicateSentinel(existing)
        };
        var stubAdo = new StubAdoClient();
        var function = BuildFunction(stubAdo, lockService);
        var req = BuildRequest(new
        {
            IncidentNumber = "INC0012458",
            TeamProject = "UPO",
            Title = "VPN issue",
            Description = "Cannot connect.",
            IncidentUrl = (string?)null
        });

        var result = await function.Run(req, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        result.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<CreateResponse>(result.Body);
        Assert.NotNull(response);
        Assert.Equal("duplicate", response.Status);
        Assert.Equal(101, response.WorkItemId);
        Assert.Equal(0, stubAdo.ProjectExistsCalls);
        Assert.False(stubAdo.CreateCalled);
    }

    [Fact]
    public async Task DeduplicationDisabled_SkipsLockAndCreatesWorkItem()
    {
        var stubAdo = new StubAdoClient
        {
            ProjectExists = true,
            ExistingItem = new ExistingWorkItem(202, "https://dev.azure.com/myorg/UPO/_workitems/edit/202", "UPO")
        };
        var lockService = new StubIdempotencyLockService();
        var function = BuildFunction(stubAdo, false, lockService);
        var req = BuildRequest(new
        {
            IncidentNumber = "INC0012459",
            TeamProject = "UPO",
            Title = "VPN issue",
            Description = "Cannot connect.",
            IncidentUrl = (string?)null
        });

        var result = await function.Run(req, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.Equal(0, lockService.TryAcquireCalls);
        Assert.True(stubAdo.CreateCalled);
    }
}

internal sealed class TestFunctionContext : FunctionContext
{
    private IServiceProvider _instanceServices = new TestServiceProvider();
    private IDictionary<object, object> _items = new Dictionary<object, object>();
    private readonly IInvocationFeatures _features = new TestInvocationFeatures();

    public override string InvocationId => Guid.NewGuid().ToString();
    public override string FunctionId => "CreateUserStory";
    public override TraceContext TraceContext => throw new NotImplementedException();
    public override BindingContext BindingContext => throw new NotImplementedException();
    public override RetryContext RetryContext => throw new NotImplementedException();
    public override IServiceProvider InstanceServices { get => _instanceServices; set => _instanceServices = value; }
    public override FunctionDefinition FunctionDefinition => throw new NotImplementedException();
    public override IDictionary<object, object> Items { get => _items; set => _items = value; }
    public override IInvocationFeatures Features => _features;
}

internal sealed class TestHttpRequestData : HttpRequestData
{
    public TestHttpRequestData(FunctionContext functionContext, Stream body, Uri url, string method) : base(functionContext)
    {
        Body = body;
        Url = url;
        Method = method;
    }

    public override Stream Body { get; }
    public override HttpHeadersCollection Headers { get; } = new();
    public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = Array.Empty<IHttpCookie>();
    public override Uri Url { get; }
    public override IEnumerable<ClaimsIdentity> Identities { get; } = Array.Empty<ClaimsIdentity>();
    public override string Method { get; }

    public override HttpResponseData CreateResponse() => new TestHttpResponseData(FunctionContext);
}

internal sealed class TestHttpResponseData : HttpResponseData
{
    public TestHttpResponseData(FunctionContext functionContext) : base(functionContext)
    {
    }

    public override HttpStatusCode StatusCode { get; set; }
    public override HttpHeadersCollection Headers { get; set; } = new();
    public override Stream Body { get; set; } = new MemoryStream();
    public override HttpCookies Cookies { get; } = new TestHttpCookies();
}

internal sealed class TestHttpCookies : HttpCookies
{
    private readonly List<IHttpCookie> _cookies = new();

    public override void Append(string name, string value)
        => _cookies.Add(new TestHttpCookie { Name = name, Value = value });

    public override void Append(IHttpCookie cookie)
        => _cookies.Add(cookie);

    public override IHttpCookie CreateNew() => new TestHttpCookie();
}

internal sealed class TestHttpCookie : IHttpCookie
{
    public string Domain { get; set; } = string.Empty;
    public DateTimeOffset? Expires { get; set; }
    public bool? HttpOnly { get; set; }
    public double? MaxAge { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public SameSite SameSite { get; set; }
    public bool? Secure { get; set; }
    public string Value { get; set; } = string.Empty;
}

internal sealed class TestInvocationFeatures : IInvocationFeatures
{
    private readonly Dictionary<Type, object> _features = new();

    public T? Get<T>()
    {
        return _features.TryGetValue(typeof(T), out var value)
            ? (T)value
            : default;
    }

    public void Set<T>(T instance)
    {
        if (instance is null)
        {
            _features.Remove(typeof(T));
            return;
        }
        _features[typeof(T)] = instance;
    }

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => _features.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _features.GetEnumerator();
}

internal sealed class TestServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}

internal sealed class StubAdoClient : IAdoClient
{
    public bool ProjectExists { get; set; } = true;
    public ExistingWorkItem? ExistingItem { get; set; }
    public bool CreateCalled { get; private set; }
    public int ProjectExistsCalls { get; private set; }

    public Task<bool> ProjectExistsAsync(string project, CancellationToken ct)
    {
        ProjectExistsCalls++;
        return Task.FromResult(ProjectExists);
    }

    public Task<bool> AnyProjectReachableAsync(CancellationToken ct)
        => Task.FromResult(true);

    public Task<ExistingWorkItem?> FindByIncidentNumberAsync(string project, string incidentNumber, CancellationToken ct)
        => Task.FromResult(ExistingItem);

    public Task<ExistingWorkItem> CreateUserStoryAsync(string project, string title, string description, string incidentNumber, string? incidentUrl, CancellationToken ct)
    {
        CreateCalled = true;
        return Task.FromResult(new ExistingWorkItem(1, "url", project));
    }
}

internal sealed class StubIdempotencyLockService : IIdempotencyLockService
{
    public IAsyncDisposable? Handle { get; set; } = new NoopLockHandle();
    public int TryAcquireCalls { get; private set; }

    public Task<IAsyncDisposable?> TryAcquireAsync(string org, string incidentNumber, CancellationToken ct)
    {
        TryAcquireCalls++;
        return Task.FromResult(Handle);
    }
}

internal sealed class NoopLockHandle : IAsyncDisposable
{
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
