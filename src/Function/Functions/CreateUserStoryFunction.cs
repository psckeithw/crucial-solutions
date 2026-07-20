using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceNowToAdo.Models;
using ServiceNowToAdo.Services;

namespace ServiceNowToAdo.Functions;

public sealed class CreateUserStoryFunction
{
    private readonly ILogger<CreateUserStoryFunction> _log;
    private readonly IPayloadValidator _validator;
    private readonly IAdoClient _ado;
    private readonly IIdempotencyLockService _lockService;
    private readonly AdoOptions _adoOpts;
    private readonly LoggingOptions _logOpts;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CreateUserStoryFunction(
        ILogger<CreateUserStoryFunction> log,
        IPayloadValidator validator,
        IAdoClient ado,
        IIdempotencyLockService lockService,
        IOptions<AdoOptions> adoOpts,
        IOptions<LoggingOptions> logOpts)
    {
        _log = log;
        _validator = validator;
        _ado = ado;
        _lockService = lockService;
        _adoOpts = adoOpts.Value;
        _logOpts = logOpts.Value;
    }

    [Function("CreateUserStory")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "servicenow/userstory")] HttpRequestData req,
        CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow;
        ServiceNowPayload? payload;

        try
        {
            payload = await JsonSerializer.DeserializeAsync<ServiceNowPayload>(req.Body, JsonOpts, ct);
        }
        catch (JsonException)
        {
            _log.LogWarning("Outcome=validation-failed Reason=empty-body");
            return await Json(req, HttpStatusCode.BadRequest, new ErrorResponse { Message = "Invalid JSON payload." }, ct);
        }

        if (!_validator.TryValidate(payload, out var validationError))
        {
            _log.LogWarning("Outcome=validation-failed Reason=missing-fields Error={Error}", validationError);
            return await Json(req, HttpStatusCode.BadRequest, new ErrorResponse { Message = validationError }, ct);
        }

        var incident = payload!.IncidentNumber!;
        var project = payload.TeamProject!;

        if (_logOpts.VerbosePayload)
        {
            _log.LogInformation("VerbosePayload enabled. Incident={Incident} Project={Project}", incident, project);
        }

        try
        {
            if (_adoOpts.EnableDeduplication)
            {
                await using var lease = await _lockService.TryAcquireAsync(_adoOpts.Organization, incident, ct);
                if (lease is DuplicateSentinel duplicate)
                {
                    _log.LogInformation("Outcome=duplicate Incident={Incident} Project={Project} WorkItemId={WorkItemId}",
                        incident, duplicate.Existing.Project, duplicate.Existing.Id);

                    return await Json(req, HttpStatusCode.OK, new CreateResponse
                    {
                        Status = "duplicate",
                        WorkItemId = duplicate.Existing.Id,
                        Url = duplicate.Existing.Url,
                        Project = duplicate.Existing.Project
                    }, ct);
                }

                if (!await _ado.ProjectExistsAsync(project, ct))
                {
                    _log.LogWarning("Outcome=validation-failed Reason=unknown-project Project={Project}", project);
                    return await Json(req, HttpStatusCode.BadRequest, new ErrorResponse { Message = $"Project '{project}' not found or not reachable in Azure DevOps." }, ct);
                }

                var existing = await _ado.FindByIncidentNumberAsync(project, incident, ct);
                if (existing is not null)
                {
                    if (existing.IsAmbiguousMatch)
                    {
                        _log.LogWarning("Outcome=duplicate AmbiguousMatch=true Incident={Incident} Project={Project} WorkItemId={WorkItemId}",
                            incident, existing.Project, existing.Id);
                    }
                    else
                    {
                        _log.LogInformation("Outcome=duplicate Incident={Incident} Project={Project} WorkItemId={WorkItemId}",
                            incident, existing.Project, existing.Id);
                    }

                    return await Json(req, HttpStatusCode.OK, new CreateResponse
                    {
                        Status = "duplicate",
                        WorkItemId = existing.Id,
                        Url = existing.Url,
                        Project = existing.Project
                    }, ct);
                }
            }
            else
            {
                if (!await _ado.ProjectExistsAsync(project, ct))
                {
                    _log.LogWarning("Outcome=validation-failed Reason=unknown-project Project={Project}", project);
                    return await Json(req, HttpStatusCode.BadRequest, new ErrorResponse { Message = $"Project '{project}' not found or not reachable in Azure DevOps." }, ct);
                }
            }

            var created = await _ado.CreateUserStoryAsync(project, payload.Title!, payload.Description!, incident, payload.IncidentUrl, ct);

            _log.LogInformation("Outcome=created Incident={Incident} Project={Project} WorkItemId={WorkItemId}",
                incident, project, created.Id);

            return await Json(req, HttpStatusCode.Created, new CreateResponse
            {
                Status = "created",
                WorkItemId = created.Id,
                Url = created.Url,
                Project = created.Project
            }, ct);
        }
        catch (AdoClient.AdoException ex)
        {
            var status = (int)ex.StatusCode;
            if (status == 401 || status == 403)
            {
                _log.LogError("Outcome=error Reason=ado-auth Status={Status} Incident={Incident} Project={Project}", status, incident, project);
                return await Json(req, HttpStatusCode.ServiceUnavailable, new ErrorResponse { Message = "Azure DevOps authentication failed." }, ct);
            }
            if (status >= 500)
            {
                _log.LogError("Outcome=error Reason=ado-upstream Status={Status} Incident={Incident} Project={Project}", status, incident, project);
                return await Json(req, HttpStatusCode.BadGateway, new ErrorResponse { Message = "Azure DevOps upstream error." }, ct);
            }
            _log.LogError(ex, "Outcome=error Reason=ado Status={Status} Incident={Incident} Project={Project}", status, incident, project);
            return await Json(req, HttpStatusCode.BadGateway, new ErrorResponse { Message = "Azure DevOps request failed." }, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Outcome=error Incident={Incident} Project={Project} Error={Error}", incident, project, ex.Message);
            return await Json(req, HttpStatusCode.InternalServerError, new ErrorResponse { Message = "Internal error while creating work item." }, ct);
        }
    }

    private static async Task<HttpResponseData> Json(HttpRequestData req, HttpStatusCode statusCode, object body, CancellationToken ct)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOpts), ct);
        return response;
    }
}
