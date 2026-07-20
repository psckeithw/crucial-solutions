using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using ServiceNowToAdo.Services;
using Xunit;

namespace ServiceNowToAdo.Tests;

public class AdoClientTests
{
    private static AdoOptions DefaultOptions => new()
    {
        Organization = "myorg",
        PersonalAccessToken = "pat",
        CustomIncidentField = "Custom.ServiceNowIncidentNumber",
        WorkItemType = "User Story",
        EnableCrossProjectDedupe = true
    };

    private static IAdoClient BuildClient(HttpMessageHandler handler, AdoOptions? opts = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new System.Uri("https://dev.azure.com/myorg/") };
        return new AdoClient(http, Options.Create(opts ?? DefaultOptions));
    }

    private static HttpMessageHandlerMock CreateHandler(Dictionary<string, (HttpStatusCode status, string body)> responses)
        => new() { Responses = responses };

    [Fact]
    public async Task ProjectExistsAsync_ProjectMissing_ReturnsFalse()
    {
        var handler = CreateHandler(new()
        {
            ["_apis/projects/UPO?api-version=7.1"] = (HttpStatusCode.NotFound, "{}")
        });
        var client = BuildClient(handler);

        var exists = await client.ProjectExistsAsync("UPO", CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task ProjectExistsAsync_ProjectExists_ReturnsTrue()
    {
        var handler = CreateHandler(new()
        {
            ["_apis/projects/UPO?api-version=7.1"] = (HttpStatusCode.OK, "{\"name\":\"UPO\"}")
        });
        var client = BuildClient(handler);

        var exists = await client.ProjectExistsAsync("UPO", CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task FindByIncidentNumberAsync_ScopedDuplicate_ReturnsExisting()
    {
        var body = JsonSerializer.Serialize(new { workItems = new[] { new { id = 42, url = "u" } } });
        var handler = CreateHandler(new()
        {
            ["UPO/_apis/wit/wiql?api-version=7.1"] = (HttpStatusCode.OK, body)
        });
        var client = BuildClient(handler);

        var result = await client.FindByIncidentNumberAsync("UPO", "INC123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(42, result!.Id);
        Assert.Equal("UPO", result.Project);
        Assert.False(result.IsAmbiguousMatch);
    }

    [Fact]
    public async Task FindByIncidentNumberAsync_ScopedMultiMatch_PickFirstAndMarkAmbiguous()
    {
        var body = JsonSerializer.Serialize(new { workItems = new[] { new { id = 1, url = "u1" }, new { id = 2, url = "u2" } } });
        var handler = CreateHandler(new()
        {
            ["UPO/_apis/wit/wiql?api-version=7.1"] = (HttpStatusCode.OK, body)
        });
        var client = BuildClient(handler);

        var result = await client.FindByIncidentNumberAsync("UPO", "INC123", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Id);
        Assert.True(result.IsAmbiguousMatch);
    }

    [Fact]
    public async Task FindByIncidentNumberAsync_ScopedMiss_OrgWideFallbackEnabled_ReturnsOtherProject()
    {
        var empty = JsonSerializer.Serialize(new { workItems = System.Array.Empty<object>() });
        var orgHit = JsonSerializer.Serialize(new { workItems = new[] { new { id = 99, url = "u" } } });
        var wiGet = JsonSerializer.Serialize(new { fields = new Dictionary<string, string> { ["System.TeamProject"] = "Other" } });
        var handler = CreateHandler(new()
        {
            ["UPO/_apis/wit/wiql?api-version=7.1"] = (HttpStatusCode.OK, empty),
            ["_apis/wit/wiql?api-version=7.1"] = (HttpStatusCode.OK, orgHit),
            ["_apis/wit/workitems/99?fields=System.TeamProject&api-version=7.1"] = (HttpStatusCode.OK, wiGet)
        });
        var client = BuildClient(handler);

        var result = await client.FindByIncidentNumberAsync("UPO", "INC999", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(99, result!.Id);
        Assert.Equal("Other", result.Project);
        Assert.False(result.IsAmbiguousMatch);
    }

    [Fact]
    public async Task FindByIncidentNumberAsync_ScopedMiss_OrgWideDisabled_ReturnsNull()
    {
        var empty = JsonSerializer.Serialize(new { workItems = System.Array.Empty<object>() });
        var handler = CreateHandler(new()
        {
            ["UPO/_apis/wit/wiql?api-version=7.1"] = (HttpStatusCode.OK, empty)
        });
        var opts = new AdoOptions { Organization = "myorg", PersonalAccessToken = "pat", EnableCrossProjectDedupe = false };
        var client = BuildClient(handler, opts);

        var result = await client.FindByIncidentNumberAsync("UPO", "INC999", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateUserStoryAsync_AddsIncidentLink_WhenIncidentUrlProvided()
    {
        var created = JsonSerializer.Serialize(new { id = 123, url = "u" });
        var handler = CreateHandler(new()
        {
            ["UPO/_apis/wit/workitems/$User%20Story?api-version=7.1"] = (HttpStatusCode.Created, created)
        });
        var client = BuildClient(handler);

        await client.CreateUserStoryAsync("UPO", "Bug title", "Bug desc", "INC123", "https://example.com/inc/123", CancellationToken.None);

        var sentBody = handler.SentBodies["UPO/_apis/wit/workitems/$User%20Story?api-version=7.1"];
        var desc = TestHelpers.GetPatchValue(sentBody, "/fields/System.Description");
        Assert.Equal("<a href=\"https://example.com/inc/123\">INC123</a><br><br>Bug desc", desc);

        var hasRelation = TestHelpers.HasPatchPath(sentBody, "/relations/-");
        Assert.True(hasRelation);
    }

    [Fact]
    public async Task CreateUserStoryAsync_NoIncidentUrl_WhenMissing()
    {
        var created = JsonSerializer.Serialize(new { id = 123, url = "u" });
        var handler = CreateHandler(new()
        {
            ["UPO/_apis/wit/workitems/$User%20Story?api-version=7.1"] = (HttpStatusCode.Created, created)
        });
        var client = BuildClient(handler);

        await client.CreateUserStoryAsync("UPO", "Bug title", "Bug desc", "INC123", null, CancellationToken.None);

        var sentBody = handler.SentBodies["UPO/_apis/wit/workitems/$User%20Story?api-version=7.1"];
        var desc = TestHelpers.GetPatchValue(sentBody, "/fields/System.Description");
        Assert.Equal("Bug desc", desc);
        
        // Verify no relation op was sent
        var hasRelation = TestHelpers.HasPatchPath(sentBody, "/relations/-");
        Assert.False(hasRelation);
    }

    [Fact]
    public async Task CreateUserStoryAsync_NoIncidentUrl_DoesNotSendRelationOp()
    {
        var created = JsonSerializer.Serialize(new { id = 123, url = "u" });
        var handler = CreateHandler(new()
        {
            ["UPO/_apis/wit/workitems/$User%20Story?api-version=7.1"] = (HttpStatusCode.Created, created)
        });
        var client = BuildClient(handler);

        await client.CreateUserStoryAsync("UPO", "Bug title", "Bug desc", "INC123", null, CancellationToken.None);

        var sentBody = handler.SentBodies["UPO/_apis/wit/workitems/$User%20Story?api-version=7.1"];
        var hasRelation = TestHelpers.HasPatchPath(sentBody, "/relations/-");
        Assert.False(hasRelation);
    }

    [Fact]
    public async Task ProjectExistsAsync_Ado401_ThrowsAdoException()
    {
        var handler = CreateHandler(new()
        {
            ["_apis/projects/UPO?api-version=7.1"] = (HttpStatusCode.Unauthorized, "{}")
        });
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<AdoClient.AdoException>(async () => await client.ProjectExistsAsync("UPO", CancellationToken.None));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    // W4 tests ---------------------------------------------------------------

    [Fact]
    public async Task FindByIncidentNumber_WiqlBodyContainsTop200()
    {
        var body = System.Text.Json.JsonSerializer.Serialize(new { workItems = new[] { new { id = 1, url = "u" } } });
        var handler = CreateHandler(new()
        {
            ["UPO/_apis/wit/wiql?api-version=7.1"] = (HttpStatusCode.OK, body)
        });
        var client = BuildClient(handler);

        await client.FindByIncidentNumberAsync("UPO", "INC123", CancellationToken.None);

        var sent = handler.SentBodies["UPO/_apis/wit/wiql?api-version=7.1"];
        Assert.Contains("TOP 200", sent);
    }

    [Fact]
    public async Task FindByIncidentNumber_ScopedMiss_CrossProjectFalse_ReturnsNullWithoutOrgCall()
    {
        var empty = System.Text.Json.JsonSerializer.Serialize(new { workItems = System.Array.Empty<object>() });
        var handler = CreateHandler(new()
        {
            ["UPO/_apis/wit/wiql?api-version=7.1"] = (HttpStatusCode.OK, empty)
        });
        var opts = new AdoOptions { Organization = "myorg", PersonalAccessToken = "pat", EnableCrossProjectDedupe = false };
        var client = BuildClient(handler, opts);

        var result = await client.FindByIncidentNumberAsync("UPO", "INC999", CancellationToken.None);

        Assert.Null(result);
        Assert.False(handler.SentBodies.ContainsKey("_apis/wit/wiql?api-version=7.1"), "Org-wide WIQL should not be called when cross-project dedup is disabled");
    }

    [Fact]
    public async Task FindByIncidentNumber_WiqlTimeout_ThrowsOperationCanceledException()
    {
        var handler = new SlowHandler();
        var http = new System.Net.Http.HttpClient(handler) { BaseAddress = new System.Uri("https://dev.azure.com/myorg/") };
        var client = new AdoClient(http, Microsoft.Extensions.Options.Options.Create(DefaultOptions));

        // Pass an already-cancelled token to simulate the outer caller cancelling;
        // the linked CTS inside FindByIncidentNumberAsync will propagate it immediately.
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.FindByIncidentNumberAsync("UPO", "INC123", cts.Token));
    }
}

/// <summary>Handler that never returns, so the 30s CancellationTokenSource fires first.</summary>
public class SlowHandler : System.Net.Http.HttpMessageHandler
{
    protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
        return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK);
    }
}

public class HttpMessageHandlerMock : HttpMessageHandler
{
    public Dictionary<string, (HttpStatusCode status, string body)> Responses { get; set; } = new();
    public Dictionary<string, string> SentBodies { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var raw = request.RequestUri?.PathAndQuery.TrimStart('/') ?? "";
        // AdoClient sets BaseAddress to https://dev.azure.com/{org}/, so requests include the org segment.
        // Normalize: try the full path first, then the path with the leading org segment stripped.
        var key = ResolveKey(raw);
        if (Responses.TryGetValue(key, out var resp))
        {
            if (request.Content != null)
            {
                using var ms = new MemoryStream();
                request.Content.CopyToAsync(ms, cancellationToken).Wait(cancellationToken);
                ms.Position = 0;
                using var reader = new StreamReader(ms);
                SentBodies[key] = reader.ReadToEnd();
            }
            var content = resp.body != null ? new StringContent(resp.body, System.Text.Encoding.UTF8, "application/json") : null;
            return Task.FromResult(new HttpResponseMessage(resp.status) { Content = content });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No mock for {raw}")
        });
    }

    private string ResolveKey(string raw)
    {
        if (Responses.TryGetValue(raw, out _)) return raw;
        var slash = raw.IndexOf('/');
        if (slash >= 0 && slash < raw.Length - 1)
        {
            var stripped = raw.Substring(slash + 1);
            if (Responses.TryGetValue(stripped, out _)) return stripped;
        }
        return raw;
    }
}

internal static class TestHelpers
{
    public static string? GetPatchValue(string body, string path)
    {
        var ops = JsonSerializer.Deserialize<JsonDocument>(body)!.RootElement;
        foreach (var op in ops.EnumerateArray())
        {
            if (op.GetProperty("path").GetString() == path)
            {
                return op.GetProperty("value").GetString();
            }
        }
        return null;
    }

    public static bool HasPatchPath(string body, string path)
    {
        var ops = JsonSerializer.Deserialize<JsonDocument>(body)!.RootElement;
        foreach (var op in ops.EnumerateArray())
        {
            if (op.GetProperty("path").GetString() == path)
            {
                return true;
            }
        }
        return false;
    }
}
