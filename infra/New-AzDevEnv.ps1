<#
.SYNOPSIS
  Idempotent Azure dev-environment bootstrap for ServiceNow→ADO Function.
  Creates RG, storage, LAW, App Insights, KV, Function App (Y1), MI,
  KV access policy. Stores ALL app settings as KV secrets. Emits
  local.settings.json-ready values and re-run command.

.EXAMPLE
  pwsh .\infra\New-AzDevEnv.ps1 -AdoOrg "AzureDevOpsDFW"
#>

[CmdletBinding()]
param(
  [string]$Location = "eastus",
  [string]$ResourceGroup = "rg-ado-snowsync-poc-01",
  [string]$SubscriptionId = "fcdfebaa-fd41-4c69-994d-8c3f746a48c2",
  [Parameter(Mandatory)][string]$AdoOrg,
  [string]$AdoPat,
  [string]$ApiKey,
  [switch]$SkipConfirm
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-EnvFile([string]$Path) {
  if (-not (Test-Path $Path)) { return @{} }
  $m = @{}
  foreach ($line in Get-Content $Path) {
    if ($line -match '^\s*(#|$)') { continue }
    if ($line -match '^([^=]+)=(.*)$') { $m[$matches[1].Trim()] = $matches[2].Trim() }
  }
  return $m
}

function Get-Secret([string]$value, [string]$label) {
  if ($value) { return $value }
  if (Test-Path .env) {
    $m = Read-EnvFile ".env"
    # Try exact label, common variants, and case-insensitive matches
    $candidates = @($label, $label.ToUpper(), $label.ToLower())
    if ($label -eq 'AZURE_DEVOPS_PAT') { $candidates += 'ADO_PAT' }
    foreach ($k in $candidates) {
      if ($m.ContainsKey($k)) { return $m[$k] }
    }
  }
  $secure = Read-Host "$label (input hidden)" -AsSecureString
  $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
  try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
  finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
}

# returns null if resource doesn't exist, otherwise the value
function AzShow([string]$cmd) {
  $tmpOut = [System.IO.Path]::GetTempFileName()
  $tmpErr = [System.IO.Path]::GetTempFileName()
  $args = ($cmd -replace '^az\s+','').Split(" ", [StringSplitOptions]::RemoveEmptyEntries)
  $p = Start-Process -FilePath "az" -ArgumentList $args -NoNewWindow -Wait -PassThru -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr
  $out = ""
  $err = ""
  if (Test-Path $tmpOut) { $out = Get-Content $tmpOut -Raw }
  if (Test-Path $tmpErr) { $err = Get-Content $tmpErr -Raw }
  Remove-Item $tmpOut,$tmpErr -Force -ErrorAction SilentlyContinue
  if ($p.ExitCode -ne 0) { return $null }
  if ([string]::IsNullOrWhiteSpace($out)) { return $null }
  return $out
}

# Ensure user is logged in and (optionally) select subscription
Write-Host "`n=== Azure login / subscription ==="
$currentAcc = AzShow "az account show --query id -o tsv"
if (-not $currentAcc) {
  Write-Host "Not logged in to Azure CLI; launching 'az login' (interactive)..."
  az login | Out-Null
  $currentAcc = AzShow "az account show --query id -o tsv"
  if (-not $currentAcc) { Write-Error "az login failed or no account available. Aborting."; exit 1 }
}
if ($SubscriptionId) {
  Write-Host "Setting subscription to $SubscriptionId"
  az account set --subscription $SubscriptionId | Out-Null
}

# ── 01. Resolve secrets ────────────────────────────────────────────────────────

Write-Host "`n=== 01. Resolve secrets ==="
$AdoPat = Get-Secret $AdoPat "AZURE_DEVOPS_PAT"
$ApiKey = Get-Secret $ApiKey "INTEGRATION_API_KEY"

# ── 02. Derive static names ────────────────────────────────────────────────────

# Extract base name from ResourceGroup: rg-ado-snowsync-poc-01 → ado-snowsync-poc-01
$baseName = ($ResourceGroup -replace '^rg-', '').ToLower()
$base = $baseName.Replace('-', '')
$storageName = $base.Substring(0, [Math]::Min(24, $base.Length))
$kvName      = "kv-$baseName"
$funcName    = "fun-$baseName"
$appiName    = "appi-$baseName"
$logName     = "log-$baseName"

Write-Host "ResourceGroup : $ResourceGroup"
Write-Host "Location      : $Location"
Write-Host "Storage       : $storageName"
Write-Host "KeyVault      : $kvName"
Write-Host "Function      : $funcName"
Write-Host "AppInsights   : $appiName"
Write-Host "LogAnalytics  : $logName"

if (-not $SkipConfirm) {
  if (-not (Read-Host "Continue? [y/N]" -Default "y").StartsWith("y", [StringComparison]::OrdinalIgnoreCase)) { exit 0 }
}

# ── 02. Resource Group ────────────────────────────────────────────────────────

Write-Host "`n=== 02. Resource group ==="
$rgExists = & az group exists --name $ResourceGroup --query exists -o tsv 2>$null
if ($LASTEXITCODE -ne 0 -or $rgExists -ne "true") {
  az group create --name $ResourceGroup --location $Location --output none | Out-Null
  Write-Host "Created."
} else {
  Write-Host "Exists; reusing."
}

# ── 03. Storage ───────────────────────────────────────────────────────────────

Write-Host "`n=== 03. Storage account ==="
if (-not (AzShow "az storage account show --name $storageName --resource-group $ResourceGroup --query id -o tsv")) {
  az storage account create --name $storageName --resource-group $ResourceGroup --location $Location --sku Standard_LRS --kind StorageV2 --output none | Out-Null
  Write-Host "Created."
} else {
  Write-Host "Exists; reusing."
}

# ── 04. Log Analytics ─────────────────────────────────────────────────────────

Write-Host "`n=== 04. Log Analytics workspace ==="
if (-not (AzShow "az monitor log-analytics workspace show --workspace-name $logName --resource-group $ResourceGroup --query id -o tsv")) {
  az monitor log-analytics workspace create --resource-group $ResourceGroup --location $Location --workspace-name $logName --output none | Out-Null
  Write-Host "Created."
} else {
  Write-Host "Exists; reusing."
}
$logId = (az monitor log-analytics workspace show --resource-group $ResourceGroup --workspace-name $logName --query id -o tsv).Trim()

# ── 05. App Insights ──────────────────────────────────────────────────────────

Write-Host "`n=== 05. Application Insights ==="
if (-not (AzShow "az monitor app-insights component show --app $appiName --resource-group $ResourceGroup --query id -o tsv")) {
  az monitor app-insights component create --app $appiName --resource-group $ResourceGroup --location $Location --workspace-id $logId --output none 2>&1 | Out-Null
  Write-Host "Created."
} else {
  Write-Host "Exists; reusing."
}
$appInsightsConn = @()
$appInsightsConn = (az monitor app-insights component show --app $appiName --resource-group $ResourceGroup --query connectionString -o tsv 2>&1 | Where-Object { $_ -notmatch "ERROR" }) 2>&1
if (-not $appInsightsConn) { $appInsightsConn = "InstrumentationKey=placeholder" }
$appInsightsConn = $appInsightsConn.Trim()

# ── 06. Key Vault ────────────────────────────────────────────────────────────

Write-Host "`n=== 06. Key Vault ==="
if (-not (AzShow "az keyvault show --name $kvName --query id -o tsv")) {
  az keyvault create --name $kvName --resource-group $ResourceGroup --location $Location --output none | Out-Null
  Write-Host "Created."
} else {
  Write-Host "Exists; reusing."
}

# ── 07. KV secrets ────────────────────────────────────────────────────────────

Write-Host "`n=== 07. KV secrets (all app config + secrets) ==="

az keyvault secret set --vault-name $kvName --name "AdoPersonalAccessToken" --value $AdoPat --output none 2>$null | Out-Null
az keyvault secret set --vault-name $kvName --name "IntegrationApiKey"     --value $ApiKey --output none 2>$null | Out-Null

$config = @{
  AdoOrganization               = $AdoOrg
  AdoCustomIncidentField        = "Custom.ServiceNowIncidentNumber"
  AdoWorkItemType               = "User Story"
  AdoEnableCrossProjectDedupe   = "true"
  ApiKeyHeaderName              = "X-API-Key"
  LoggingVerbosePayload         = "false"
}
foreach ($kvp in $config.GetEnumerator()) {
  az keyvault secret set --vault-name $kvName --name $kvp.Key --value $kvp.Value --output none 2>$null | Out-Null
}
Write-Host "Written."

# ── 08. Function App ──────────────────────────────────────────────────────────

Write-Host "`n=== 08. Function App ==="
if (-not (AzShow "az functionapp show --name $funcName --resource-group $ResourceGroup --query id -o tsv")) {
  az functionapp create `
    --name $funcName `
    --resource-group $ResourceGroup `
    --storage-account $storageName `
    --consumption-plan-location $Location `
    --runtime dotnet `
    --runtime-version 8 `
    --functions-version 4 `
    --os-type Linux `
    --output none | Out-Null
  Write-Host "Created."
} else {
  Write-Host "Exists; reusing."
}
$funcMiId = (az functionapp show --name $funcName --resource-group $ResourceGroup --query identity.principalId -o tsv).Trim()
if (-not $funcMiId) {
  Write-Host "Enabling system-assigned MI..."
  $funcMiId = (az functionapp identity assign --name $funcName --resource-group $ResourceGroup --query principalId -o tsv).Trim()
}

# ── 09. KV authorization (RBAC if enabled, else access policy) ─────────────────

Write-Host "`n=== 09. KV authorization ==="

$kvUseRbac = (az keyvault show --name $kvName --query "properties.enableRbacAuthorization" -o tsv).Trim()
if ($kvUseRbac -eq "true") {
  az role assignment create `
    --role "Key Vault Secrets User" `
    --assignee-object-id $funcMiId `
    --scope (az keyvault show --name $kvName --query id -o tsv) `
    --output none 2>$null | Out-Null
  Write-Host "RBAC role assignment set (Key Vault Secrets User)."
} else {
  az keyvault set-policy --name $kvName --resource-group $ResourceGroup --object-id $funcMiId --secret-permissions get list --output none 2>$null | Out-Null
  Write-Host "Access policy set."
}

# ── 10. Function App app settings (KV references) ─────────────────────────────

Write-Host "`n=== 10. Function App app settings ==="

az functionapp config appsettings set `
  --name $funcName `
  --resource-group $ResourceGroup `
  --settings `
    "Ado__Organization=@Microsoft.KeyVault(VaultName=$kvName;SecretName=AdoOrganization)" `
    "Ado__PersonalAccessToken=@Microsoft.KeyVault(VaultName=$kvName;SecretName=AdoPersonalAccessToken)" `
    "Ado__CustomIncidentField=@Microsoft.KeyVault(VaultName=$kvName;SecretName=AdoCustomIncidentField)" `
    "Ado__WorkItemType=@Microsoft.KeyVault(VaultName=$kvName;SecretName=AdoWorkItemType)" `
    "Ado__EnableCrossProjectDedupe=@Microsoft.KeyVault(VaultName=$kvName;SecretName=AdoEnableCrossProjectDedupe)" `
    "ApiKey__ApiKey=@Microsoft.KeyVault(VaultName=$kvName;SecretName=IntegrationApiKey)" `
    "ApiKey__HeaderName=@Microsoft.KeyVault(VaultName=$kvName;SecretName=ApiKeyHeaderName)" `
    "Logging__VerbosePayload=@Microsoft.KeyVault(VaultName=$kvName;SecretName=LoggingVerbosePayload)" `
    "APPLICATIONINSIGHTS_CONNECTION_STRING=$appInsightsConn" `
    "FUNCTIONS_WORKER_RUNTIME=dotnet-isolated" `
  --output none 2>$null | Out-Null

Write-Host "Written."

# ── 11. Output ────────────────────────────────────────────────────────────────

Write-Host "`n=== local.settings.json values ==="
Write-Host "AzureWebJobsStorage              = <fill from portal>"
Write-Host "FUNCTIONS_WORKER_RUNTIME         = dotnet-isolated"
Write-Host "APPLICATIONINSIGHTS_CONNECTION_STRING = $appInsightsConn"
Write-Host "Ado__Organization                = $AdoOrg"
Write-Host "Ado__PersonalAccessToken         = <from KV: AdoPersonalAccessToken>"
Write-Host "Ado__CustomIncidentField         = Custom.ServiceNowIncidentNumber"
Write-Host "Ado__WorkItemType                = User Story"
Write-Host "Ado__EnableCrossProjectDedupe    = true"
Write-Host "ApiKey__ApiKey                   = <from KV: IntegrationApiKey>"
Write-Host "ApiKey__HeaderName               = X-API-Key"
Write-Host "Logging__VerbosePayload          = false"

Write-Host @"

=== Function App settings (KV references — copy these into CI/CD or portal) ===
Ado__Organization                        = @Microsoft.KeyVault(VaultName=$kvName;SecretName=AdoOrganization)
Ado__PersonalAccessToken                 = @Microsoft.KeyVault(VaultName=$kvName;SecretName=AdoPersonalAccessToken)
Ado__CustomIncidentField                 = @Microsoft.KeyVault(VaultName=$kvName;SecretName=AdoCustomIncidentField)
Ado__WorkItemType                        = @Microsoft.KeyVault(VaultName=$kvName;SecretName=AdoWorkItemType)
Ado__EnableCrossProjectDedupe            = @Microsoft.KeyVault(VaultName=$kvName;SecretName=AdoEnableCrossProjectDedupe)
ApiKey__ApiKey                           = @Microsoft.KeyVault(VaultName=$kvName;SecretName=IntegrationApiKey)
ApiKey__HeaderName                       = @Microsoft.KeyVault(VaultName=$kvName;SecretName=ApiKeyHeaderName)
Logging__VerbosePayload                  = @Microsoft.KeyVault(VaultName=$kvName;SecretName=LoggingVerbosePayload)
APPLICATIONINSIGHTS_CONNECTION_STRING    = $appInsightsConn
FUNCTIONS_WORKER_RUNTIME                 = dotnet-isolated
"@

$defaultHost = (az functionapp show --name $funcName --resource-group $ResourceGroup --query defaultHostName -o tsv).Trim()
$apiUrl = "https://$defaultHost/api/servicenow/userstory"

Write-Host "=== Endpoint ==="
Write-Host "POST $apiUrl"
Write-Host ""
Write-Host "=== Re-run command ==="
Write-Host "pwsh .\infra\New-AzDevEnv.ps1 -ResourceGroup $ResourceGroup -AdoOrg '$AdoOrg' -AdoPat '<your-pat>' -ApiKey '<your-api-key>' -SkipConfirm"
