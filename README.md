# ServiceNow → Azure DevOps User Story Integration

This repository contains a **single canonical .NET 8 isolated Azure Function**
that receives incident payloads from ServiceNow and creates (or deduplicates)
Azure DevOps User Stories tagged with the ServiceNow incident number.

> **If you are new to this codebase start with the `docs/` files.** They are
> the modern handoff set; the rest of this README is a quick-start summary.
>
> Prefer reading offline? Every doc below is also rendered as a standalone
> **static HTML** file in `docs/*.html` (no server, no JS, no external
> resources). Open `docs/index.html` in any browser to browse the whole set
> with a sidebar and per-page table of contents. Rebuild with
> `python3 docs/_build_html.py` after editing the Markdown.

---

## Documentation Map

| Document | Audience / When to read |
|---|---|
| [docs/FUNCTIONALITY.md](docs/FUNCTIONALITY.md) | What the integration does — endpoint contract, dedup algorithm, flows, error mapping. Start here. |
| [docs/TECH_STACK.md](docs/TECH_STACK.md) | Inventory of every runtime, library, external service, and tool. Read when planning upgrades. |
| [docs/CODEBASE.md](docs/CODEBASE.md) | File-by-file walkthrough with gotchas. Read before editing any module. |
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | Local setup, build/test, coding conventions, known behavior gaps, contributor workflow. Read before your first PR. |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Infra bootstrap, app-settings matrix, CI/CD, ops runbooks (rotate / restart / DR). Read when standing up or adopting an environment. |
| [docs/DIAGNOSTICS.md](docs/DIAGNOSTICS.md) | Observability, Kusto query sheets, troubleshooting, known test/impl mismatches. Read during incidents. |
| [docs/SECURITY.md](docs/SECURITY.md) | Threat model, secret storage, **committed-secret exposure requiring action**, hardening roadmap. **Read first**; ⚠️ a live PAT is in source. |

---

## At a Glance

- **One HTTP endpoint**: `POST /api/servicenow/userstory`, anonymous host-level
  auth gated by a custom `X-API-Key` header.
- **PascalCase JSON request / response** (see `ServiceNowPayload`).
- **Idempotent** on `IncidentNumber`: re-fires return the existing User
  Story unchanged.
- **Single ADO org** via PAT; supports scoped (per-team-project) and
  org-wide deduplication.
- **Stateless** function app: all durable state lives in ADO. Re-deploys
  are cheap.
- **Dev shape**: Linux Y1 Consumption, .NET 8 isolated,
  `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated`, system-assigned MI with
  Key Vault access.

---

## Quick Start

```powershell
# 1. Provide local secrets (gitignored; not auto-created)
@"
AZURE_DEVOPS_ORG=<your ADO org>
AZURE_DEVOPS_PROJECTS=<comma list of projects>
ADO_PAT=<PAT with Work Items Read/Write>
INTEGRATION_API_KEY=<choose any long random string>
"@ | Out-File -FilePath .env -Encoding utf8

# 2. Build, test, run the function locally
pwsh scripts\dev\dev.ps1

# 3. (in another PowerShell session) send a sample payload
$env:API_KEY="snowsync-dev-api-key"; pwsh scripts\dev\send.ps1 samples/valid-payload.json
```

Detailed prerequisites and workflows: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

---

## Project Structure

```
.
├── docs/                # ← handoff documentation set (start here)
│   └── archive/         # outdated/session-specific docs retained for history
├── src/Function/        # .NET 8 isolated Azure Function project
│   ├── Functions/       # the single HTTP trigger
│   ├── Middleware/      # API-key gate
│   ├── Models/          # request/response DTOs
│   └── Services/        # ADO client, options, validator
├── tests/               # xUnit unit tests
├── infra/               # idempotent Azure infra bootstrap + KV sync scripts
├── scripts/             # dev/bootstrap/diagnostics entrypoints
├── tools/               # ad hoc Python utilities
├── samples/             # canned JSON payloads for local/deployed testing
└── azure-pipelines.yml  # CI/CD pipeline
```

Annotated version with per-file notes: [docs/CODEBASE.md](docs/CODEBASE.md) §1.

---

## Configuration

The function reads **double-underscore** (`__`) nested configuration keys.
Required app settings:

| Key | Description |
|-----|-------------|
| `Ado__Organization` | Azure DevOps organization name. Forms `https://dev.azure.com/<org>`. |
| `Ado__PersonalAccessToken` | PAT with Work Items read/write scope. **Key Vault reference** in production. |
| `Ado__CustomIncidentField` | Custom field name for incident numbers. Default: `Custom.ServiceNowIncidentNumber`. |
| `Ado__WorkItemType` | Work item type to create. Default: `User Story`. |
| `Ado__EnableCrossProjectDedupe` | `true` (default) enables org-wide duplicate detection fallback. |
| `ApiKey__ApiKey` | Integration API-key secret. **Key Vault reference** in production. |
| `ApiKey__HeaderName` | Header name to check for the API key. Default: `X-API-Key`. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Set on the Function App at deploy time (plain value). |
| `FUNCTIONS_WORKER_RUNTIME` | Must be `dotnet-isolated` (plain value). |
| `AzureWebJobsStorage` | Storage connection string (plain value). |
| `Logging__VerbosePayload` | `true` to log incident + project (default `false`). |

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) §"Configuration matrix" for the
**complete** Key Vault secret-name mapping and the manual `AzureWebJobsStorage`
step. **Secret rotation requires a Function App restart** —
the PAT and API key are bound into `IOptions<T>` at host startup.

---

## Endpoint Contract (abridged)

`POST /api/servicenow/userstory` with JSON body (`PascalCase`):

```json
{
  "IncidentNumber": "INC0012456",
  "TeamProject":   "UPO",
  "Title":         "VPN connectivity issue",
  "Description":   "User unable to connect after password reset.",
  "IncidentUrl":   "https://.../INC0012456"
}
```

`IncidentNumber`, `TeamProject`, `Title`, `Description` are required.
`IncidentUrl` is optional and, when provided, is attached as both an HTML
link in `System.Description` and a `Hyperlink` relation on the work item.

Responses:

| Status | Body shape | Condition |
|---|---|---|
| `201 Created` | `{ "status": "created", "workItemId": …, "url": …, "project": … }` | New User Story was created |
| `200 OK` | `{ "status": "duplicate", … }` | Existing match found (across same or other project). ServiceNow must **not retry** on this status. |
| `400 BadRequest` | `{ "message": … }` | Bad JSON, missing fields, unknown project |
| `401 Unauthorized` | `{ "message": … }` | Missing / invalid `X-API-Key` |
| `500 / 502 / 503` | `{ "message": … }` | ADO auth/upstream failure or internal error |

Full contract, dedup algorithm, error mapping, and spec amendments:
[docs/FUNCTIONALITY.md](docs/FUNCTIONALITY.md).

---

## Auth

The endpoint uses `AuthorizationLevel.Anonymous` at the host level so that
secrets are managed entirely outside the Function host auth layer. The
**sole gate** is `ApiKeyMiddleware`, which checks the configured header
(`X-API-Key` by default) against the Key Vault-supplied API key. A missing
or invalid key returns `401` before the function body runs.

Threat model, committed-secret exposure, and hardening roadmap:
[docs/SECURITY.md](docs/SECURITY.md).

---

## Cross-Team UX Contract (ServiceNow business rule)

The ServiceNow business rule must:

- Fire on initial save after Team Project selection.
- Send payload fields in **PascalCase** (`IncidentNumber`, `TeamProject`,
  `Title`, `Description`, optional `IncidentUrl`).
- Include the `X-API-Key` header.
- **Never retry** on `status: "duplicate"`, including when the returned
  `project` differs from the payload's `TeamProject`.

The endpoint is a passive receiver. It does not query ServiceNow state and
cannot itself enforce "fire only on initial selection." Its idempotency
contract (return-existing on duplicate, including across projects) is the
safety net for cases where the Snow rule re-fires.

---

## Infrastructure

Infrastructure (RG, Storage, Log Analytics, App Insights, Service Plan,
Function App, Key Vault, MI access) is provisioned by
`infra/New-AzDevEnv.ps1`, an idempotent Azure CLI bootstrap. The deployed
Function App is configured with app settings as **Key Vault references**
(`@Microsoft.KeyVault(VaultName=<kv>;SecretName=<secret>)`); secrets are
never plaintext.

Full bootstrap, CI/CD pipeline (`azure-pipelines.yml`),
app-settings matrix, and operational runbooks (rotate PAT, restart,
disaster recovery, prod checklist): [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

> `Custom.ServiceNowIncidentNumber` must exist on the `User Story` work item
> type in the shared inherited ADO process template (this is **the** hard
> external dependency). It is owned by the ADO admin. `Heartbeat` is the
> first project using it; more will follow.

## Future Work

- Two-way sync between ServiceNow and ADO
- Bug WIT provisioning
- SPN/MI outbound auth to ADO (replace PAT)
- Flex Consumption SKU adoption
- Alerting on repeated `AmbiguousMatch=true` occurrences
- Authorization-level `Function` or network-restricted deployment for
  corp-network environments

See also `docs/DEVELOPMENT.md` §11 "Future Hardening" and `docs/SECURITY.md`
§7 "Hardening Roadmap" for more concrete proposals.
