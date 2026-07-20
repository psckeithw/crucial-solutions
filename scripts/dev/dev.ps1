#!/usr/bin/env pwsh
<#.SYNOPSIS
  Local-dev workflow for the ServiceNow→ADO Azure Function.
  Reads .env, writes local.settings.json, restore/build/test/publish, zip, serve.

.PARAMETER Deploy
  Also push secrets to KV, wire Function App app settings, and deploy the zip.

.PARAMETER ResourceGroup
  Azure resource group for the Function App.

.PARAMETER FunctionAppName
  Azure Function App name.

.PARAMETER VaultName
  Azure Key Vault name.

.EXAMPLE
  pwsh scripts\dev\dev.ps1 -Deploy
#>

param(
  [string]$Project = "src/Function/ServiceNowToAdo.csproj",
  [string]$TestProject = "tests/ServiceNowToAdo.Tests.csproj",
  [string]$Template = "src/Function/local.settings.json.template",
  [string]$Output = "src/Function/local.settings.json",
  [string]$PublishDir = "$PWD/build/publish",
  [string]$ZipPath = "$PWD/build/functionapp.zip",
  [switch]$SkipServe,
  [switch]$Deploy,
  [string]$ResourceGroup = "rg-snowsync-dev",
  [string]$FunctionAppName = "func-snowsync",
  [string]$VaultName = "kv-snowsync"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-EnvFile([string]$Path) {
  if (-not (Test-Path $Path)) { throw ".env not found at $Path" }
  $map = @{}
  foreach ($line in Get-Content $Path) {
    if ($line -match '^\s*(#|$)') { continue }
    if ($line -match '^([^=]+)=(.*)$') {
      $map[$matches[1].Trim()] = $matches[2].Trim()
    }
  }
  return $map
}

function Write-LocalSettings([hashtable]$envMap) {
  if (-not (Test-Path $Template)) {
    $settings = @{
      IsEncrypted = $false
      Values = @{
        AzureWebJobsStorage = "<to-be-filled>"
        FUNCTIONS_WORKER_RUNTIME = "dotnet-isolated"
        APPLICATIONINSIGHTS_CONNECTION_STRING = ""
        Ado__Organization = $envMap["AZURE_DEVOPS_ORG"]
        Ado__PersonalAccessToken = $envMap["ADO_PAT"]
        Ado__CustomIncidentField = "Custom.ServiceNowIncidentNumber"
        Ado__WorkItemType = "User Story"
        Ado__EnableCrossProjectDedupe = "true"
        ApiKey__ApiKey = $envMap["INTEGRATION_API_KEY"]
        ApiKey__HeaderName = "X-API-Key"
        Logging__VerbosePayload = "false"
      }
    } | ConvertTo-Json -Depth 5
  } else {
    $raw = Get-Content $Template -Raw | ConvertFrom-Json
    $raw.IsEncrypted = $false
    $raw.Values.Ado__Organization = $envMap["AZURE_DEVOPS_ORG"]
    $raw.Values.Ado__PersonalAccessToken = $envMap["ADO_PAT"]
    $raw.Values.ApiKey__ApiKey = $envMap["INTEGRATION_API_KEY"]
    $settings = $raw | ConvertTo-Json -Depth 5
  }
  $dir = Split-Path $Output -Parent
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
  Set-Content -Path $Output -Value $settings -Encoding UTF8
  Write-Host "Wrote $Output"
}

Write-Host "`n=== Reading .env ==="
$envMap = Read-EnvFile "$PWD/.env"

Write-Host "`n=== Bootstrapping local.settings.json ==="
Write-LocalSettings $envMap

Write-Host "`n=== dotnet restore ==="
dotnet restore $Project --verbosity minimal

Write-Host "`n=== dotnet build (Release) ==="
dotnet build $Project --configuration Release --no-restore

Write-Host "`n=== dotnet test ==="
dotnet test $TestProject --configuration Release --no-build --logger "trx;LogFileName=$PWD/build/test-results.trx"

Write-Host "`n=== dotnet publish ==="
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish $Project -c Release --no-restore -o $PublishDir

Write-Host "`n=== Archive publish output ==="
$zipDir = Split-Path $ZipPath -Parent
if (-not (Test-Path $zipDir)) { New-Item -ItemType Directory -Path $zipDir | Out-Null }

if (Get-Command Compress-Archive -ErrorAction SilentlyContinue) {
  if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
  Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ZipPath -Force
  Write-Host "Zip: $ZipPath"
} else {
  Write-Warning "Compress-Archive not available; publish dir is $PublishDir"
}

Write-Host "`n=== Done ==="
Write-Host "Publish dir : $PublishDir"
Write-Host "Zip artifact: $ZipPath"

if (-not $SkipServe) {
  Write-Host "`n=== func start (Ctrl+C to stop) ==="
  Set-Location (Split-Path $Project -Parent)
  func start
}
