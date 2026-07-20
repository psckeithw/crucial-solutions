using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ServiceNowToAdo.Services;
using Xunit;

namespace ServiceNowToAdo.Tests;

public class AdoResilienceTests
{
    private static AdoOptions DefaultOptions => new()
    {
        Organization = "myorg",
        PersonalAccessToken = "pat",
        EnableCrossProjectDedupe = false
    };

    private static (IAdoClient client, SequenceHandler handler) BuildClient(params (HttpStatusCode status, string body)[] responses)
    {
        var handler = new SequenceHandler(responses);
        var services = new ServiceCollection();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(DefaultOptions));
        var builder = services.AddHttpClient<IAdoClient, AdoClient>();
        builder.ConfigurePrimaryHttpMessageHandler(() => handler);
        builder.AddStandardResilienceHandler(options =>
        {
            AdoResilience.Configure(options);
            // Keep the same ShouldHandle predicate but eliminate delay for fast tests.
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
        });

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IAdoClient>(), handler);
    }

    [Fact]
    public async Task AdoReturns500ThenSuccess_RetriesAndSucceeds()
    {
        var (client, handler) = BuildClient(
            (HttpStatusCode.InternalServerError, "{}"),
            (HttpStatusCode.InternalServerError, "{}"),
            (HttpStatusCode.OK, "{\"name\":\"UPO\"}"));

        var exists = await client.ProjectExistsAsync("UPO", CancellationToken.None);

        Assert.True(exists);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task AdoReturns400_NeverRetried()
    {
        var (client, handler) = BuildClient(
            (HttpStatusCode.BadRequest, "{}"));

        await Assert.ThrowsAsync<AdoClient.AdoException>(
            async () => await client.ProjectExistsAsync("UPO", CancellationToken.None));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task AdoReturns429_Retried()
    {
        var (client, handler) = BuildClient(
            (HttpStatusCode.TooManyRequests, "{}"),
            (HttpStatusCode.OK, "{\"name\":\"UPO\"}"));

        var exists = await client.ProjectExistsAsync("UPO", CancellationToken.None);

        Assert.True(exists);
        Assert.Equal(2, handler.RequestCount);
    }

    /// <summary>
    /// Returns the queued responses in order; repeats the last one if exhausted.
    /// Tracks how many requests were observed so tests can assert retry behavior.
    /// </summary>
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode status, string body)> _responses;
        private (HttpStatusCode status, string body) _last;
        public int RequestCount { get; private set; }

        public SequenceHandler(IEnumerable<(HttpStatusCode status, string body)> responses)
        {
            _responses = new Queue<(HttpStatusCode, string)>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var next = _responses.Count > 0 ? _responses.Dequeue() : _last;
            _last = next;
            return Task.FromResult(new HttpResponseMessage(next.status)
            {
                Content = new StringContent(next.body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
