using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Net;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ServiceNowToAdo.Services;

namespace ServiceNowToAdo.Middleware;

public sealed class ApiKeyMiddleware : IFunctionsWorkerMiddleware
{
    private readonly ApiKeyOptions _opts;
    private readonly ILogger<ApiKeyMiddleware> _log;

    public ApiKeyMiddleware(IOptions<ApiKeyOptions> opts, ILogger<ApiKeyMiddleware> log)
    {
        _opts = opts.Value;
        _log = log;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var reqData = await context.GetHttpRequestDataAsync();
        if (reqData is null)
        {
            await next(context);
            return;
        }

        if (reqData.Url.AbsolutePath.EndsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var isAuthorized = false;
        if (reqData.Headers.TryGetValues(_opts.HeaderName, out var values))
        {
            var providedKey = values.FirstOrDefault() ?? string.Empty;
            var expectedBytes = Encoding.UTF8.GetBytes(_opts.ApiKey);
            var providedBytes = Encoding.UTF8.GetBytes(providedKey);
            var maxLength = Math.Max(expectedBytes.Length, providedBytes.Length);
            var paddedExpected = new byte[maxLength];
            var paddedProvided = new byte[maxLength];
            expectedBytes.CopyTo(paddedExpected, 0);
            providedBytes.CopyTo(paddedProvided, 0);
            isAuthorized = CryptographicOperations.FixedTimeEquals(paddedExpected, paddedProvided);
        }

        if (!isAuthorized)
        {
            _log.LogWarning("Outcome=validation-failed Reason=api-key Header={Header}", _opts.HeaderName);

            var resp = reqData.CreateResponse();
            resp.StatusCode = System.Net.HttpStatusCode.Unauthorized;
            resp.Headers.Add("Content-Type", "application/json");
            var err = JsonSerializer.Serialize(new { message = "Missing or invalid API key." });
            await resp.WriteStringAsync(err);
            context.GetInvocationResult().Value = resp;
            return;
        }

        await next(context);
    }
}
