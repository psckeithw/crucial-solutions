# Handoff: ServiceNow→ADO Function – Debug & Deploy

## Summary
- Code deployed to Azure Function App `func-snowsync` (RG: `rg-snowsync-dev`).
- Secrets synced to Key Vault `kv-snowsync`.
- Mock UI dropdown updated to: `azure-infra`, `gold-leaf-ag`, `Heartbeat`, `vector-em`.
- **Issue**: Health and create endpoints return 404; function may not be running correctly.

## What Was Done

### 1. Script naming cleanup
- `scripts/dev/dev.ps1`: RG=`rg-snowsync-dev`, Func=`func-snowsync`, KV=`kv-snowsync`
- `scripts/dev/deploy.ps1`: RG=`rg-snowsync-dev`, Func=`func-snowsync`
- `infra/Sync-KvSecrets.ps1`: RG=`rg-snowsync-dev`, Func=`func-snowsync`, KV=`kv-snowsync`
- `azure-pipelines.yml`: already used correct names.

### 2. Build & Deploy (pwsh)
- `dev.ps1 -SkipServe` built with .NET SDK 8.0.423, created `build/functionapp.zip`.
- `az functionapp deploy` (zip) to `func-snowsync`.
- `Sync-KvSecrets.ps1 -Location southcentralus`:
  - Created KV secrets from `.env`.
  - Set Function App app settings to use Key Vault references.
  - Function identity has **Key Vault Secrets User** role.

### 3. Mock HTML update
- `samples/service-now-mockup.html` dropdown now uses fixed list:
  `azure-infra`, `gold-leaf-ag`, `Heartbeat`, `vector-em`.

---

## Current State & Config

| Item | Value |
|------|-------|
| Resource group | `rg-snowsync-dev` |
| Location | South Central US |
| Function App | `func-snowsync` (Running) |
| Default hostname | `https://func-snowsync.azurewebsites.net` |
| Key Vault | `kv-snowsync` (RBAC enabled) |
| Function identity (principalId) | `7db3af49-bbc4-454f-9883-16de31b3fb94` |
| KV role assignment | `Key Vault Secrets User` on vault scope |
| App Settings (KV refs) | `Ado__Organization`, `Ado__PersonalAccessToken`, `Ado__CustomIncidentField`, `Ado__WorkItemType`, `Ado__EnableCrossProjectDedupe`, `ApiKey__ApiKey`, `ApiKey__HeaderName`, `Logging__VerbosePayload` |
| Storage account | `snowsyncsnowsync` (used for content share) |
| App Insights conn string | `InstrumentationKey=babe4b46...` |
| Deployment method | Run‑From‑Package (zip) |

---

## Observed Symptoms

- `GET https://func-snowsync.azurewebsites.net/api/health` → **404**
- `POST https://func-snowsync.azurewebsites.net/api/servicenow/userstory` with valid API key → **404**
- Function list: `az functionapp function list` returns 0 or command not producing output; need to re‑check.

Interpretation: Function host may have failed to start or failed to load the functions due to a startup exception (e.g., Key Vault refs, missing secret, .NET runtime issue).

---

## Debug Info

### App Settings (abbreviated)
```
FUNCTIONS_WORKER_RUNTIME = dotnet-isolated
Ado__Organization         = @Microsoft.KeyVault(VaultName=kv-snowsync;SecretName=AdoOrganization)
ApiKey__ApiKey            = @Microsoft.KeyVault(VaultName=kv-snowsync;SecretName=IntegrationApiKey)
...
```

### KV Secrets present? (expected all 8)
- AdoOrganization (value from `.env`: `AZURE_DEVOPS_ORG`)
- AdoPersonalAccessToken (from `.env`: `ADO_PAT`)
- AdoCustomIncidentField = `Custom.ServiceNowIncidentNumber`
- AdoWorkItemType = `User Story`
- AdoEnableCrossProjectDedupe = `true`
- IntegrationApiKey (from `.env`: `INTEGRATION_API_KEY`)
- ApiKeyHeaderName = `X-API-Key`
- LoggingVerbosePayload = `false`

Verify:
```bash
az keyvault secret list -n kv-snowsync -o table
```

### Identity / RBAC
- Function identity exists and has Key Vault Secrets User role on the vault (RBAC enabled).
- Confirm:
  ```bash
  az role assignment list --assignee 7db3af49-bbc4-454f-9883-16de31b3fb94 --scope $(az keyvault show -n kv-snowsync -g rg-snowsync-dev --query id -o tsv) -o table
  ```

### Function deployment
- Zip deployed successfully (no error).
- App is set to run from package (WEBSITE_RUN_FROM_PACKAGE set to a blob URL).
- Restart performed; still 404.

### .NET runtime
- Project targets `net8.0`; `global.json` requires SDK `8.0.423`. Build succeeded with installed SDK `8.0.423`.
- Azure Functions runtime on the platform is `DOTNET-ISOLATED|8.0`. Note: .NET 8 reaches EOL 2026‑11‑10; consider planning migration to .NET 10.

---

## Next Steps to Diagnose

1. **Check Function list directly**
   ```bash
   az functionapp function list -g rg-snowsync-dev -n func-snowsync -o json
   ```
   If empty, host failed to initialize.

2. **Stream logs**
   - Portal: *func-snowsync* → **Log stream** (under Monitoring).
   - CLI:
     ```bash
     az monitor log-analytics query --workspace <law-id> --analytics-query "AzureDiagnostics | where Category == 'FunctionAppLogs' | top 50 by TimeGenerated desc" -o table
     ```
   Or use Application Insights (if connected) to view exceptions.

3. **Try the test payload manually** (to isolate UI vs server):
   ```bash
   API_KEY=$(az keyvault secret show -n IntegrationApiKey -vault-name kv-snowsync -o tsv --query value)
   curl -i -H "Content-Type: application/json" -H "X-API-Key: $API_KEY" \
     -d '{"incident_number":"DEBUGO-001","short_description":"debug","description":"debug","priority":"3","TeamProject":"Heartbeat"}' \
     https://func-snowsync.azurewebsites.net/api/servicenow/userstory
   ```
   - If still 404, function not loaded.
   - If 400, note error (likely project not accessible or PAT issue).

4. **Verify Key Vault references resolve** – if any referenced secret is missing, the app will fail to start. Check **App Service logs** → **Filesystem** or **Log stream** for messages like `Secret reference ... could not be resolved`.

5. **Restart after fixes**:
   ```bash
   az functionapp restart -g rg-snowsync-dev -n func-snowsync
   ```

6. **Check ADO PAT permissions** – ensure the secret `AdoPersonalAccessToken` has access to the `Heartbeat` project. If not, the function will return 400 once the host is running.

---

## Files Modified

- `scripts/dev/dev.ps1` (unchanged values but intentionally left with correct defaults)
- `scripts/dev/deploy.ps1` (RG changed to `rg-snowsync-dev`, Func to `func-snowsync`)
- `infra/Sync-KvSecrets.ps1` (support KV/func naming)
- `samples/service-now-mockup.html` (project dropdown list)
- `azure-pipelines.yml` (no change needed; already had correct names)

---

## Outstanding Unknowns

- Are all KV secrets present and correctly set from `.env`?
- Does the Function App have any startup errors (needs log inspection)?
- Does the ADO PAT have access to the specified projects (`Heartbeat`, etc.)?
- Is there any packaging issue (e.g., missing `host.json`, broken dll)?

---

## Suggested Immediate Action

1. Run the manual `curl` test above and paste full response.
2. Check Function App **Log stream** in Azure Portal; copy any error traces.
3. Run `az functionapp function list` and confirm `CreateUserStory` appears.
4. If function list is empty or errors appear in logs, share them to determine root cause (likely Key Vault resolution or startup failure).

--- 

Once the function is responding 200/201, the mock page should work as expected.
