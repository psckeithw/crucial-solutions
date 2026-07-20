using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace ServiceNowToAdo.Services;

/// <summary>
/// W5 — Shared retry policy for transient ADO failures. Applied to the
/// <see cref="IAdoClient"/> HttpClient pipeline. Retries on network errors,
/// 5xx responses and 429 (Too Many Requests); never retries other 4xx.
/// Retry budget is bounded well under the 60s blob-lease TTL used by W3.
/// </summary>
public static class AdoResilience
{
    public static void Configure(HttpStandardResilienceOptions options)
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        // Cap any single delay so the total retry budget stays well under 15s.
        options.Retry.MaxDelay = TimeSpan.FromSeconds(15);
        options.Retry.ShouldHandle = args => args.Outcome switch
        {
            { Exception: HttpRequestException } => PredicateResult.True(),
            { Result: { IsSuccessStatusCode: false, StatusCode: >= HttpStatusCode.InternalServerError } } => PredicateResult.True(),
            { Result: { StatusCode: HttpStatusCode.TooManyRequests } } => PredicateResult.True(),
            _ => PredicateResult.False()
        };
    }
}
