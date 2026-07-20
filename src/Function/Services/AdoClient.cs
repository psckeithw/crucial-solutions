using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ServiceNowToAdo.Services;

public interface IAdoClient
{
    Task<bool> ProjectExistsAsync(string project, CancellationToken ct);
    Task<bool> AnyProjectReachableAsync(CancellationToken ct);
    Task<ExistingWorkItem?> FindByIncidentNumberAsync(string project, string incidentNumber, CancellationToken ct);
    Task<ExistingWorkItem> CreateUserStoryAsync(string project, string title, string description, string incidentNumber, string? incidentUrl, CancellationToken ct);
}

public sealed record ExistingWorkItem(int Id, string Url, string Project, bool IsAmbiguousMatch = false);

public sealed class AdoClient : IAdoClient
{
    private readonly HttpClient _http;
    private readonly AdoOptions _opts;
    private const string ApiVersion = "7.1";

    public AdoClient(HttpClient http, IOptions<AdoOptions> opts)
    {
        _opts = opts.Value;
        _http = http;
        _http.BaseAddress = new Uri(_opts.BaseUrl.TrimEnd('/') + "/");

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_opts.PersonalAccessToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<bool> ProjectExistsAsync(string project, CancellationToken ct)
    {
        var uri = $"_apis/projects/{Uri.EscapeDataString(project)}?api-version={ApiVersion}";
        using var resp = await _http.GetAsync(uri, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return false;
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            throw new AdoException($"ADO project lookup failed ({(int)resp.StatusCode}): {errBody}", resp.StatusCode);
        }
        return true;
    }

    public async Task<bool> AnyProjectReachableAsync(CancellationToken ct)
    {
        var uri = $"_apis/projects?$top=1&api-version={ApiVersion}";
        using var resp = await _http.GetAsync(uri, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            throw new AdoException($"ADO projects probe failed ({(int)resp.StatusCode}): {errBody}", resp.StatusCode);
        }
        return true;
    }

    public async Task<ExistingWorkItem?> FindByIncidentNumberAsync(string project, string incidentNumber, CancellationToken ct)
    {
        var escapedInc = incidentNumber.Replace("'", "''");

        var scopedWiql = new
        {
            query = $"SELECT TOP 200 [System.Id] FROM WorkItems " +
                    $"WHERE [System.TeamProject] = @project " +
                    $"AND [{_opts.CustomIncidentField}] = '{escapedInc}'"
        };

        var scopedUri = $"{Uri.EscapeDataString(project)}/_apis/wit/wiql?api-version={ApiVersion}";
        using (var scopedCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            scopedCts.CancelAfter(TimeSpan.FromSeconds(30));
            using (var body = new StringContent(JsonSerializer.Serialize(scopedWiql), Encoding.UTF8, "application/json"))
            using (var resp = await _http.PostAsync(scopedUri, body, scopedCts.Token))
            {
                var scoped = await ReadWorkItemsAsync(resp, ct);
                if (scoped is { Count: > 0 } scopedItems)
                {
                    var first = scopedItems[0];
                    var ambiguous = scopedItems.Count > 1;
                    return new ExistingWorkItem(first.Id, BuildBrowserUrl(project, first.Id), project, ambiguous);
                }
            }
        }

        if (!_opts.EnableCrossProjectDedupe)
        {
            return null;
        }

        var orgWiql = new
        {
            query = $"SELECT TOP 200 [System.Id] FROM WorkItems " +
                    $"WHERE [{_opts.CustomIncidentField}] = '{escapedInc}'"
        };

        var orgUri = $"_apis/wit/wiql?api-version={ApiVersion}";
        using (var orgCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            orgCts.CancelAfter(TimeSpan.FromSeconds(30));
            using (var body = new StringContent(JsonSerializer.Serialize(orgWiql), Encoding.UTF8, "application/json"))
            using (var resp = await _http.PostAsync(orgUri, body, orgCts.Token))
            {
                var org = await ReadWorkItemsAsync(resp, ct);
                if (org is { Count: > 0 } orgItems)
                {
                    var first = orgItems[0];
                    var ambiguous = orgItems.Count > 1;
                    var actualProject = await GetWorkItemProjectAsync(first.Id, ct);
                    return new ExistingWorkItem(first.Id, BuildBrowserUrl(actualProject, first.Id), actualProject, ambiguous);
                }
            }
        }

        return null;
    }

    private async Task<List<WiqlItem>?> ReadWorkItemsAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (!resp.IsSuccessStatusCode) return null;
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        var result = await JsonSerializer.DeserializeAsync<WiqlResult>(stream, cancellationToken: ct);
        return result?.WorkItems;
    }

    private async Task<string> GetWorkItemProjectAsync(int id, CancellationToken ct)
    {
        var uri = $"_apis/wit/workitems/{id}?fields=System.TeamProject&api-version={ApiVersion}";
        using var resp = await _http.GetAsync(uri, ct);
        if (!resp.IsSuccessStatusCode)
        {
            return "unknown";
        }
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        var wi = await JsonSerializer.DeserializeAsync<WorkItemFields>(stream, cancellationToken: ct);
        return wi?.Fields?.Project ?? "unknown";
    }

    public async Task<ExistingWorkItem> CreateUserStoryAsync(string project, string title, string description, string incidentNumber, string? incidentUrl, CancellationToken ct)
    {
        var htmlDescription = BuildHtmlDescription(description, incidentNumber, incidentUrl);

        var opsList = new List<object>
        {
            new { op = "add", path = "/fields/System.Title", value = title },
            new { op = "add", path = "/fields/System.Description", value = htmlDescription },
            new { op = "add", path = "/multilineFieldsFormat/System.Description", value = "Html" },
            new { op = "add", path = $"/fields/{_opts.CustomIncidentField}", value = incidentNumber }
        };

        if (!string.IsNullOrEmpty(incidentUrl))
        {
            opsList.Add(new { op = "add", path = "/relations/-", value = new { rel = "Hyperlink", url = incidentUrl, attributes = new { comment = $"ServiceNow Incident {incidentNumber}" } } });
        }

        var ops = opsList.ToArray();

        var uri = $"{Uri.EscapeDataString(project)}/_apis/wit/workitems/${Uri.EscapeDataString(_opts.WorkItemType)}?api-version={ApiVersion}";
        using var body = new StringContent(JsonSerializer.Serialize(ops), Encoding.UTF8, "application/json-patch+json");
        using var req = new HttpRequestMessage(HttpMethod.Post, uri) { Content = body };

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync(ct);
            throw new AdoException($"ADO create failed ({(int)resp.StatusCode}): {errBody}", resp.StatusCode);
        }

        var stream = await resp.Content.ReadAsStreamAsync(ct);
        var wi = await JsonSerializer.DeserializeAsync<CreatedWorkItem>(stream, cancellationToken: ct)
                 ?? throw new InvalidOperationException("Empty response from ADO create.");

        return new ExistingWorkItem(wi.Id, BuildBrowserUrl(project, wi.Id), project);
    }

    private string BuildHtmlDescription(string description, string incidentNumber, string? incidentUrl)
    {
        if (string.IsNullOrEmpty(incidentUrl))
        {
            return description;
        }

        var link = $"<a href=\"{incidentUrl}\">{incidentNumber}</a>";
        return $"{link}<br><br>{description}";
    }

    private string BuildBrowserUrl(string project, int id) =>
        $"{_opts.BaseUrl}/{Uri.EscapeDataString(project)}/_workitems/edit/{id}";

    public sealed class AdoException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public AdoException(string message, HttpStatusCode statusCode) : base(message)
        {
            StatusCode = statusCode;
        }
    }

    private sealed class WiqlResult
    {
        [JsonPropertyName("workItems")] public List<WiqlItem>? WorkItems { get; set; }
    }
    private sealed class WiqlItem
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
    private sealed class CreatedWorkItem
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
    private sealed class WorkItemFields
    {
        [JsonPropertyName("fields")] public FieldsDict? Fields { get; set; }
    }
    private sealed class FieldsDict
    {
        [JsonPropertyName("System.TeamProject")] public string? Project { get; set; }
    }
}
