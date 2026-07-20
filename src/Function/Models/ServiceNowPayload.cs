using System.Text.Json.Serialization;

namespace ServiceNowToAdo.Models;

public sealed class ServiceNowPayload
{
    [JsonPropertyName("IncidentNumber")]
    public string? IncidentNumber { get; set; }

    [JsonPropertyName("TeamProject")]
    public string? TeamProject { get; set; }

    [JsonPropertyName("Title")]
    public string? Title { get; set; }

    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    [JsonPropertyName("IncidentUrl")]
    public string? IncidentUrl { get; set; }
}

public sealed class CreateResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("workItemId")]
    public int WorkItemId { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("project")]
    public string Project { get; set; } = "";
}

public sealed class ErrorResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
