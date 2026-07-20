#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceNowToAdo.Functions;
using ServiceNowToAdo.Middleware;
using ServiceNowToAdo.Services;

namespace ServiceNowToAdo.Tests;

public class HealthFunctionTests
{
    [Fact]
    public async Task HealthEndpoint_Returns200_WithoutApiKey()
    {
        var function = new HealthFunction();
        var context = new TestFunctionContext();
        var request = new TestHttpRequestData(
            context,
            new MemoryStream(),
            new System.Uri("https://localhost/api/health"),
            "GET");

        var response = await function.Run(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthMiddleware_BypassesApiKeyCheck_WhenRouteIsHealth()
    {
        var middleware = BuildMiddleware("expected-key");
        var context = BuildHealthContext(apiKey: null);
        var nextCalled = false;

        await middleware.Invoke(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCalled);
        Assert.Null(context.GetInvocationResult().Value);
    }

    [Fact]
    public async Task HealthMiddleware_BypassesApiKeyCheck_WhenRouteIsHealth_WithValidKey()
    {
        var middleware = BuildMiddleware("expected-key");
        var context = BuildHealthContext(apiKey: "expected-key");
        var nextCalled = false;

        await middleware.Invoke(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        Assert.True(nextCalled);
        Assert.Null(context.GetInvocationResult().Value);
    }

    private static ApiKeyMiddleware BuildMiddleware(string apiKey)
    {
        var opts = Options.Create(new ApiKeyOptions
        {
            HeaderName = "X-API-Key",
            ApiKey = apiKey
        });
        return new ApiKeyMiddleware(opts, NullLogger<ApiKeyMiddleware>.Instance);
    }

    private static TestFunctionContext BuildHealthContext(string? apiKey)
    {
        var context = new TestFunctionContext();
        var request = new TestHttpRequestData(
            context,
            new MemoryStream(),
            new System.Uri("https://localhost/api/health"),
            "GET");

        if (apiKey is not null)
            request.Headers.Add("X-API-Key", apiKey);

        context.Features.Set<IHttpRequestDataFeature>(new HealthStaticHttpRequestDataFeature(request));
        AttachFunctionBindingsFeature(context);
        return context;
    }

    private static void AttachFunctionBindingsFeature(Microsoft.Azure.Functions.Worker.FunctionContext context)
    {
        var bindingsFeatureType = System.Type.GetType(
            "Microsoft.Azure.Functions.Worker.Context.Features.IFunctionBindingsFeature, Microsoft.Azure.Functions.Worker.Core",
            throwOnError: true)!;

        var featureProxy = System.Reflection.DispatchProxy.Create(bindingsFeatureType, typeof(HealthFunctionBindingsProxy));
        var setMethod = context.Features.GetType().GetMethod(nameof(TestInvocationFeatures.Set))!;
        var genericSetMethod = setMethod.MakeGenericMethod(bindingsFeatureType);
        genericSetMethod.Invoke(context.Features, [featureProxy]);
    }

    private sealed class HealthStaticHttpRequestDataFeature(HttpRequestData requestData) : IHttpRequestDataFeature
    {
        public System.Threading.Tasks.ValueTask<HttpRequestData?> GetHttpRequestDataAsync(Microsoft.Azure.Functions.Worker.FunctionContext context)
            => System.Threading.Tasks.ValueTask.FromResult<HttpRequestData?>(requestData);
    }

    private class HealthFunctionBindingsProxy : System.Reflection.DispatchProxy
    {
        private static readonly System.Collections.Generic.IReadOnlyDictionary<string, object> EmptyReadOnlyMap = new System.Collections.Generic.Dictionary<string, object>();
        private static readonly System.Collections.Generic.IDictionary<string, object> EmptyMutableMap = new System.Collections.Generic.Dictionary<string, object>();
        private object? _invocationResult;

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_TriggerMetadata" => EmptyReadOnlyMap,
                "get_InputData" => EmptyReadOnlyMap,
                "get_OutputBindingData" => EmptyMutableMap,
                "get_OutputBindingsInfo" => null,
                "get_InvocationResult" => _invocationResult,
                "set_InvocationResult" => _invocationResult = args![0],
                _ => throw new System.NotSupportedException($"Unsupported member: {targetMethod?.Name}")
            };
        }
    }
}
