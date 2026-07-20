using System.ComponentModel.DataAnnotations;

namespace ServiceNowToAdo.Services;

public sealed class AdoOptions
{
    [Required]
    public string Organization { get; set; } = "";

    [Required]
    public string PersonalAccessToken { get; set; } = "";

    public string CustomIncidentField { get; set; } = "Custom.ServiceNowIncidentNumber";

    public string WorkItemType { get; set; } = "User Story";

    public bool EnableDeduplication { get; set; } = true;

    public bool EnableCrossProjectDedupe { get; set; } = false;

    /// <summary>
    /// Optional project name used by the startup PAT liveness probe.
    /// When set, the probe calls ProjectExistsAsync with this name.
    /// When null/empty, the probe falls back to AnyProjectReachableAsync (_apis/projects?$top=1).
    /// </summary>
    public string? HealthCheckProject { get; set; }

    public string BaseUrl => $"https://dev.azure.com/{Organization}";
}

public sealed class ApiKeyOptions
{
    [Required]
    public string ApiKey { get; set; } = "";

    public string HeaderName { get; set; } = "X-API-Key";
}

public sealed class LoggingOptions
{
    public bool VerbosePayload { get; set; } = false;
}

public sealed class AzureOpenAiOptions
{
    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string DeploymentName { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public string ApiVersion { get; set; } = "2024-10-21";
}
