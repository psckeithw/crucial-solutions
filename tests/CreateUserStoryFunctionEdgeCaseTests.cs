using System;
using System.IO;
using System.Net;
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

public class CreateUserStoryFunctionEdgeCaseTests
{
    private static CreateUserStoryFunction BuildFunction(IAdoClient adoClient)
    {
        var log = NullLogger<CreateUserStoryFunction>.Instance;
        var validator = new PayloadValidator();
        var adoOpts = Options.Create(new AdoOptions
        {
            Organization = "myorg",
            PersonalAccessToken = "pat"
        });
        var logOpts = Options.Create(new LoggingOptions { VerbosePayload = false });
        return new CreateUserStoryFunction(log, validator, adoClient, new StubIdempotencyLockService(), adoOpts, logOpts);
    }

    private static HttpRequestData BuildRequest(string rawBody)
    {
        var body = new MemoryStream(Encoding.UTF8.GetBytes(rawBody));
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
    public async Task InvalidJsonBody_Returns400BadRequest()
    {
        var stub = new StubAdoClient();
        var function = BuildFunction(stub);
        var req = BuildRequest("{ invalid json ");

        var result = await function.Run(req, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        result.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<ErrorResponse>(result.Body);
        Assert.Equal("Invalid JSON payload.", response?.Message);
    }

    [Fact]
    public async Task MissingRequiredFields_Returns400BadRequest()
    {
        var stub = new StubAdoClient();
        var function = BuildFunction(stub);
        var req = BuildRequest(JsonSerializer.Serialize(new { IncidentNumber = "INC123" })); // Missing project, title, etc.

        var result = await function.Run(req, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        result.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<ErrorResponse>(result.Body);
        Assert.Contains("Missing or empty required field(s)", response?.Message);
    }

    [Fact]
    public async Task ProjectNotFound_Returns400BadRequest()
    {
        var stub = new StubAdoClient { ProjectExists = false };
        var function = BuildFunction(stub);
        var req = CreateUserStoryFunctionTestsHelper.BuildRequest(new
        {
            IncidentNumber = "INC001",
            TeamProject = "NonExistent",
            Title = "Title",
            Description = "Desc"
        });

        var result = await function.Run(req, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        result.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<ErrorResponse>(result.Body);
        Assert.Contains("not found or not reachable", response?.Message);
    }

    [Fact]
    public async Task AdoUnauthorized_Returns503ServiceUnavailable()
    {
        var stub = new MockAdoClient { ThrowOnCreate = new AdoClient.AdoException("Unauthorized", HttpStatusCode.Unauthorized) };
        var function = BuildFunction(stub);
        var req = CreateUserStoryFunctionTestsHelper.BuildRequest(new
        {
            IncidentNumber = "INC002",
            TeamProject = "UPO",
            Title = "Title",
            Description = "Desc"
        });

        var result = await function.Run(req, CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        result.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<ErrorResponse>(result.Body);
        Assert.Equal("Azure DevOps authentication failed.", response?.Message);
    }

    [Fact]
    public async Task AdoUpstreamError_Returns502BadGateway()
    {
        var stub = new MockAdoClient { ThrowOnCreate = new AdoClient.AdoException("Internal Server Error", HttpStatusCode.InternalServerError) };
        var function = BuildFunction(stub);
        var req = CreateUserStoryFunctionTestsHelper.BuildRequest(new
        {
            IncidentNumber = "INC003",
            TeamProject = "UPO",
            Title = "Title",
            Description = "Desc"
        });

        var result = await function.Run(req, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
        result.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<ErrorResponse>(result.Body);
        Assert.Equal("Azure DevOps upstream error.", response?.Message);
    }
}

internal static class CreateUserStoryFunctionTestsHelper
{
    public static HttpRequestData BuildRequest(object payload)
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
}

internal sealed class MockAdoClient : IAdoClient
{
    public bool ProjectExists { get; set; } = true;
    public Exception? ThrowOnCreate { get; set; }

    public Task<bool> ProjectExistsAsync(string project, CancellationToken ct) => Task.FromResult(ProjectExists);
    public Task<bool> AnyProjectReachableAsync(CancellationToken ct) => Task.FromResult(true);
    public Task<ExistingWorkItem?> FindByIncidentNumberAsync(string project, string incidentNumber, CancellationToken ct) => Task.FromResult<ExistingWorkItem?>(null);

    public async Task<ExistingWorkItem> CreateUserStoryAsync(string project, string title, string description, string incidentNumber, string? incidentUrl, CancellationToken ct)
    {
        if (ThrowOnCreate != null) throw ThrowOnCreate;
        return new ExistingWorkItem(123, "url", project);
    }
}
