using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ServiceNowToAdo.Services;

/// <summary>
/// IHostedService that runs at startup to verify the ADO PAT is valid.
/// ADO 401/403 → throws, preventing the host from starting.
/// ADO 5xx or network errors → logs warning and continues.
/// </summary>
public sealed class AdoStartupProbe : IHostedService
{
    private readonly IAdoClient _ado;
    private readonly AdoOptions _opts;
    private readonly ILogger<AdoStartupProbe> _log;

    public AdoStartupProbe(IAdoClient ado, IOptions<AdoOptions> opts, ILogger<AdoStartupProbe> log)
    {
        _ado = ado;
        _opts = opts.Value;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            bool reachable = _opts.HealthCheckProject is { Length: > 0 }
                ? await _ado.ProjectExistsAsync(_opts.HealthCheckProject, ct)
                : await _ado.AnyProjectReachableAsync(ct);

            if (!reachable)
                _log.LogWarning("ADO probe: HealthCheckProject '{Project}' not found.", _opts.HealthCheckProject);
        }
        catch (AdoClient.AdoException ex) when ((int)ex.StatusCode == 401 || (int)ex.StatusCode == 403)
        {
            throw new InvalidOperationException(
                $"ADO PAT authentication failed at startup (HTTP {(int)ex.StatusCode}). Check Ado__PersonalAccessToken.", ex);
        }
        catch (AdoClient.AdoException ex) when ((int)ex.StatusCode >= 500)
        {
            _log.LogWarning("ADO probe: upstream returned {Status} at startup — continuing.", (int)ex.StatusCode);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ADO probe: network error at startup — continuing.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
