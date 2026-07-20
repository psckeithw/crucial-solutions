<#
.SYNOPSIS
  Run in Azure Cloud Shell.
  Idempotent: syncs secrets into kv-snowsync and wires Function App app settings.
#>

[CmdletBinding()]
param(
  [string]$VaultName = "kv-ado-snowsync-poc-01",
  [string]$ResourceGroup = "rg-ado-snowsync-poc-01",
  [string]$FuncName = "fun-ado-snowsync-poc-01",
  [string]$Location = "eastus"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-EnvFile([string]$Path) {
  if (-not (Test-Path $Path)) { return @{} }
  $m = @{}
  foreach ($line in Get-Content $Path) {
    if ($line -match '^[\s]*(#|$)') { continue }
    if ($line -match '^([^=]+)=(.*)$') { $m[$matches[1].Trim()] = $matches[2].Trim() }
  }
  return $m
}

Write-Host "`n=== Ensure resource group $ResourceGroup ==="
$rgExists = & az group exists --name $ResourceGroup --query exists -o tsv 2>$null
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to query resource groups (az exit $LASTEXITCODE)"; exit 1 }
if ($rgExists -ne "true") {
  Write-Host "Creating resource group $ResourceGroup in $Location..."
  az group create --name $ResourceGroup --location $Location --output none
  if ($LASTEXITCODE -ne 0) { Write-Error "Failed to create resource group"; exit 1 }
} else {
  Write-Host "Resource group exists; reusing."
}

Write-Host "`n=== Ensure Key Vault $VaultName ==="
$kvId = az keyvault show --name $VaultName --resource-group $ResourceGroup --query id -o tsv 2>$null
if ($LASTEXITCODE -ne 0 -or -not $kvId) {
  Write-Host "Creating Key Vault $VaultName..."
  az keyvault create --name $VaultName --resource-group $ResourceGroup --location $Location --output none
  if ($LASTEXITCODE -ne 0) { Write-Error "Failed to create Key Vault"; exit 1 }
} else {
  Write-Host "Key Vault exists; reusing."
}

# Read .env overrides if present
$envMap = Read-EnvFile ".env"
$adoOrg = if ($envMap.ContainsKey('AZURE_DEVOPS_ORG')) { $envMap['AZURE_DEVOPS_ORG'] } elseif ($envMap.ContainsKey('AdoOrganization')) { $envMap['AdoOrganization'] } else { 'AzureDevOpsDFW' }
$adoPat = if ($envMap.ContainsKey('ADO_PAT')) { $envMap['ADO_PAT'] } elseif ($envMap.ContainsKey('AZURE_DEVOPS_PAT')) { $envMap['AZURE_DEVOPS_PAT'] } else { '' }
$apiKey = if ($envMap.ContainsKey('INTEGRATION_API_KEY')) { $envMap['INTEGRATION_API_KEY'] } elseif ($envMap.ContainsKey('IntegrationApiKey')) { $envMap['IntegrationApiKey'] } else { 'snowsync-dev-api-key' }

$secrets = @{
  AdoOrganization               = $adoOrg
  AdoPersonalAccessToken       = $adoPat
  AdoCustomIncidentField       = "Custom.ServiceNowIncidentNumber"
  AdoWorkItemType              = "User Story"
  AdoEnableCrossProjectDedupe  = "true"
  IntegrationApiKey            = $apiKey
  ApiKeyHeaderName             = "X-API-Key"
  LoggingVerbosePayload        = "false"
}

Write-Host "`n=== Syncing $($secrets.Count) secrets into $VaultName ==="

foreach ($kvp in $secrets.GetEnumerator()) {
  $name = $kvp.Key
  $value = $kvp.Value
  if (-not $value) { Write-Warning "Skipping $name (no value)"; continue }
  Write-Host "  $name"
  az keyvault secret set --vault-name $VaultName --name $name --value $value --output none 2>$null
  if ($LASTEXITCODE -ne 0) { Write-Error "Failed to set secret $name (az exit $LASTEXITCODE)"; exit 1 }
}

Write-Host "`n=== Updating Function App app settings (if function exists) ==="

$funcId = az functionapp show --name $FuncName --resource-group $ResourceGroup --query id -o tsv 2>$null
if ($LASTEXITCODE -ne 0 -or -not $funcId) {
  Write-Warning "Function $FuncName not found in $ResourceGroup; skipping app settings update."
  Write-Host "Done."
  exit 0
}

$settings = @(
  "Ado__Organization=@Microsoft.KeyVault(VaultName=$VaultName;SecretName=AdoOrganization)"
  "Ado__PersonalAccessToken=@Microsoft.KeyVault(VaultName=$VaultName;SecretName=AdoPersonalAccessToken)"
  "Ado__CustomIncidentField=@Microsoft.KeyVault(VaultName=$VaultName;SecretName=AdoCustomIncidentField)"
  "Ado__WorkItemType=@Microsoft.KeyVault(VaultName=$VaultName;SecretName=AdoWorkItemType)"
  "Ado__EnableCrossProjectDedupe=@Microsoft.KeyVault(VaultName=$VaultName;SecretName=AdoEnableCrossProjectDedupe)"
  "ApiKey__ApiKey=@Microsoft.KeyVault(VaultName=$VaultName;SecretName=IntegrationApiKey)"
  "ApiKey__HeaderName=@Microsoft.KeyVault(VaultName=$VaultName;SecretName=ApiKeyHeaderName)"
  "Logging__VerbosePayload=@Microsoft.KeyVault(VaultName=$VaultName;SecretName=LoggingVerbosePayload)"
)

az functionapp config appsettings set `
  --name $FuncName `
  --resource-group $ResourceGroup `
  --settings $settings `
  --output none 2>&1 | Out-Host

if ($LASTEXITCODE -ne 0) { Write-Error "App settings update failed (az exit $LASTEXITCODE)"; exit 1 } else { Write-Host "Done." }
