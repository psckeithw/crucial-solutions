using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceNowToAdo.Logging;
using ServiceNowToAdo.Services;
using ServiceNowToAdo.Middleware;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(worker =>
    {
        worker.UseMiddleware<ApiKeyMiddleware>();
    })
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;
        services.AddApplicationInsightsTelemetryWorkerService();

        services.AddOptions<AdoOptions>()
            .Bind(configuration.GetSection("Ado"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ApiKeyOptions>()
            .Bind(configuration.GetSection("ApiKey"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LoggingOptions>()
            .Bind(configuration.GetSection("Logging"));

        services.AddOptions<BlobStorageOptions>()
            .Bind(configuration.GetSection("BlobStorage"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IAdoClient, AdoClient>()
            .AddStandardResilienceHandler(AdoResilience.Configure);
        // TEMP: Disable startup probe to diagnose 500 error
        // services.AddHostedService<AdoStartupProbe>();
        services.AddSingleton<IIdempotencyLockService, IdempotencyLockService>();
        services.AddSingleton<IPayloadValidator, PayloadValidator>();

        // W6: wrap every registered ILoggerProvider so the Incident value is
        // redacted by default (full value only when Logging__VerbosePayload=true).
        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType != typeof(ILoggerProvider))
            {
                continue;
            }

            services[i] = ServiceDescriptor.Describe(
                typeof(ILoggerProvider),
                sp =>
                {
                    var inner = (ILoggerProvider)(descriptor.ImplementationInstance
                        ?? descriptor.ImplementationFactory?.Invoke(sp)
                        ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
                    var loggingOptions = sp.GetRequiredService<IOptions<LoggingOptions>>().Value;
                    return new RedactingLoggerProvider(inner, loggingOptions);
                },
                descriptor.Lifetime);
        }
    })
    .Build();

host.Run();
