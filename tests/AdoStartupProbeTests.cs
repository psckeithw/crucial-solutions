using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceNowToAdo.Services;
using Xunit;

namespace ServiceNowToAdo.Tests;

public class AdoStartupProbeTests
{
    private static AdoOptions DefaultOpts(string? healthCheckProject = null) => new()
    {
        Organization = "myorg",
        PersonalAccessToken = "pat",
        HealthCheckProject = healthCheckProject
    };

    // ── fake IAdoClient ──────────────────────────────────────────────────────

    private sealed class FakeAdoClient : IAdoClient
    {
        public Func<CancellationToken, Task<bool>>? OnAnyProjectReachable { get; set; }
        public Func<string, CancellationToken, Task<bool>>? OnProjectExists { get; set; }

        public Task<bool> AnyProjectReachableAsync(CancellationToken ct) =>
            OnAnyProjectReachable?.Invoke(ct) ?? Task.FromResult(true);

        public Task<bool> ProjectExistsAsync(string project, CancellationToken ct) =>
            OnProjectExists?.Invoke(project, ct) ?? Task.FromResult(true);

        public Task<ExistingWorkItem?> FindByIncidentNumberAsync(string project, string incidentNumber, CancellationToken ct) =>
            Task.FromResult<ExistingWorkItem?>(null);

        public Task<ExistingWorkItem> CreateUserStoryAsync(string project, string title, string description, string incidentNumber, string? incidentUrl, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    // ── capture logger ───────────────────────────────────────────────────────

    private sealed class CapturingLogger : ILogger<AdoStartupProbe>
    {
        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                WarningCount++;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (AdoStartupProbe probe, CapturingLogger log) Build(
        FakeAdoClient client,
        AdoOptions? opts = null)
    {
        var logger = new CapturingLogger();
        var probe = new AdoStartupProbe(client, Options.Create(opts ?? DefaultOpts()), logger);
        return (probe, logger);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartupProbe_200_CompletesNormally()
    {
        var client = new FakeAdoClient
        {
            OnAnyProjectReachable = _ => Task.FromResult(true)
        };
        var (probe, log) = Build(client);

        await probe.StartAsync(CancellationToken.None);

        Assert.Equal(0, log.WarningCount);
    }

    [Fact]
    public async Task StartupProbe_401_ThrowsInvalidOperationException()
    {
        var client = new FakeAdoClient
        {
            OnAnyProjectReachable = _ => throw new AdoClient.AdoException("Unauthorized", HttpStatusCode.Unauthorized)
        };
        var (probe, _) = Build(client);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => probe.StartAsync(CancellationToken.None));

        Assert.Contains("401", ex.Message);
        Assert.Contains("Ado__PersonalAccessToken", ex.Message);
        Assert.IsType<AdoClient.AdoException>(ex.InnerException);
    }

    [Fact]
    public async Task StartupProbe_403_ThrowsInvalidOperationException()
    {
        var client = new FakeAdoClient
        {
            OnAnyProjectReachable = _ => throw new AdoClient.AdoException("Forbidden", HttpStatusCode.Forbidden)
        };
        var (probe, _) = Build(client);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => probe.StartAsync(CancellationToken.None));

        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public async Task StartupProbe_503_LogsWarningAndContinues()
    {
        var client = new FakeAdoClient
        {
            OnAnyProjectReachable = _ => throw new AdoClient.AdoException("Service Unavailable", HttpStatusCode.ServiceUnavailable)
        };
        var (probe, log) = Build(client);

        // Should not throw
        await probe.StartAsync(CancellationToken.None);

        Assert.True(log.WarningCount > 0);
    }

    [Fact]
    public async Task StartupProbe_NetworkError_LogsWarningAndContinues()
    {
        var client = new FakeAdoClient
        {
            OnAnyProjectReachable = _ => throw new System.Net.Http.HttpRequestException("network failure")
        };
        var (probe, log) = Build(client);

        // Should not throw
        await probe.StartAsync(CancellationToken.None);

        Assert.True(log.WarningCount > 0);
    }

    [Fact]
    public async Task StartupProbe_WithHealthCheckProject_CallsProjectExists()
    {
        string? capturedProject = null;
        var client = new FakeAdoClient
        {
            OnProjectExists = (p, _) =>
            {
                capturedProject = p;
                return Task.FromResult(true);
            }
        };
        var (probe, log) = Build(client, DefaultOpts("MyProject"));

        await probe.StartAsync(CancellationToken.None);

        Assert.Equal("MyProject", capturedProject);
        Assert.Equal(0, log.WarningCount);
    }

    [Fact]
    public async Task StartupProbe_WithHealthCheckProject_ProjectNotFound_LogsWarning()
    {
        var client = new FakeAdoClient
        {
            OnProjectExists = (_, _) => Task.FromResult(false)
        };
        var (probe, log) = Build(client, DefaultOpts("MissingProject"));

        await probe.StartAsync(CancellationToken.None);

        Assert.True(log.WarningCount > 0);
    }

    [Fact]
    public async Task StartupProbe_NoHealthCheckProject_CallsAnyProjectReachable()
    {
        bool anyCalled = false;
        var client = new FakeAdoClient
        {
            OnAnyProjectReachable = _ =>
            {
                anyCalled = true;
                return Task.FromResult(true);
            }
        };
        var (probe, _) = Build(client, DefaultOpts(null));

        await probe.StartAsync(CancellationToken.None);

        Assert.True(anyCalled);
    }
}
