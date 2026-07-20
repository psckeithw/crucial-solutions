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
using ServiceNowToAdo.Middleware;
using ServiceNowToAdo.Services;

namespace ServiceNowToAdo.Tests;

public class ApiKeyMiddlewareTests
{
    [Fact]
    public async Task ValidApiKey_CallsNext()
    {
        var middleware = BuildMiddleware("expected-key");
        var context = BuildContext("expected-key");
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
    public async Task MissingApiKeyHeader_RejectsWith401()
    {
        var middleware = BuildMiddleware("expected-key");
        var context = BuildContext(null);
        var nextCalled = false;

        await middleware.Invoke(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var response = Assert.IsType<TestHttpResponseData>(context.GetInvocationResult().Value);
        Assert.False(nextCalled);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongApiKey_RejectsWith401()
    {
        var middleware = BuildMiddleware("expected-key");
        var context = BuildContext("wrong-key");
        var nextCalled = false;

        await middleware.Invoke(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var response = Assert.IsType<TestHttpResponseData>(context.GetInvocationResult().Value);
        Assert.False(nextCalled);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    private static TestFunctionContext BuildContext(string? apiKey)
    {
        var context = new TestFunctionContext();
        var request = new TestHttpRequestData(
            context,
            new MemoryStream(),
            new Uri("https://localhost/api/servicenow/userstory"),
            "POST");

        if (apiKey is not null)
        {
            request.Headers.Add("X-API-Key", apiKey);
        }

        context.Features.Set<IHttpRequestDataFeature>(new StaticHttpRequestDataFeature(request));
        AttachFunctionBindingsFeature(context);
        return context;
    }

    private static void AttachFunctionBindingsFeature(FunctionContext context)
    {
        var bindingsFeatureType = Type.GetType(
            "Microsoft.Azure.Functions.Worker.Context.Features.IFunctionBindingsFeature, Microsoft.Azure.Functions.Worker.Core",
            throwOnError: true)!;

        var featureProxy = DispatchProxy.Create(bindingsFeatureType, typeof(FunctionBindingsFeatureProxy));
        var setMethod = context.Features.GetType().GetMethod(nameof(TestInvocationFeatures.Set))!;
        var genericSetMethod = setMethod.MakeGenericMethod(bindingsFeatureType);
        genericSetMethod.Invoke(context.Features, [featureProxy]);
    }

    private sealed class StaticHttpRequestDataFeature(HttpRequestData requestData) : IHttpRequestDataFeature
    {
        public ValueTask<HttpRequestData?> GetHttpRequestDataAsync(FunctionContext context)
            => ValueTask.FromResult<HttpRequestData?>(requestData);
    }

    private class FunctionBindingsFeatureProxy : DispatchProxy
    {
        private static readonly IReadOnlyDictionary<string, object> EmptyReadOnlyMap = new Dictionary<string, object>();
        private static readonly IDictionary<string, object> EmptyMutableMap = new Dictionary<string, object>();
        private object? _invocationResult;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                "get_TriggerMetadata" => EmptyReadOnlyMap,
                "get_InputData" => EmptyReadOnlyMap,
                "get_OutputBindingData" => EmptyMutableMap,
                "get_OutputBindingsInfo" => null,
                "get_InvocationResult" => _invocationResult,
                "set_InvocationResult" => _invocationResult = args![0],
                _ => throw new NotSupportedException($"Unsupported member: {targetMethod?.Name}")
            };
        }
    }
}
