#!/usr/bin/env pwsh
<#.SYNOPSIS
  Build and deploy the Function App directly from the command line.

.DESCRIPTION
  This script keeps the subscription roles explicit:
  - HomeSubscriptionId: your normal Azure CLI context (informational/sanity check)
  - AhaSubscriptionId: the target subscription that actually receives the deploy

  The deployment will always switch Azure CLI to the Aha subscription before
  uploading the zip package.

.EXAMPLE
  pwsh scripts\dev\deploy.ps1 -HomeSubscriptionId <home-sub-id> -AhaSubscriptionId <aha-sub-id> -ResourceGroup rg-ado-snowsync-poc-01 -FunctionAppName fun-ado-snowsync-poc-01
#>

[CmdletBinding()]
param(
  [string]$Project = "src/Function/ServiceNowToAdo.csproj",
  [string]$PublishDir = "$PWD/build/publish",
  [string]$ZipPath = "$PWD/build/functionapp.zip",
  [string]$ResourceGroup = "rg-ado-snowsync-poc-01",
  [string]$FunctionAppName = "fun-ado-snowsync-poc-01",
  [Parameter(Mandatory)]
  [string]$AhaSubscriptionId,
  [string]$HomeSubscriptionId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-AzLogin {
  $accountId = az account show --query id -o tsv 2>$null
  if (-not $accountId) {
    throw "Run 'az login' first."
  }
  return $accountId.Trim()
}

Write-Host "`n=== Azure CLI context ==="
$currentSubscriptionId = Assert-AzLogin
Write-Host "Current subscription : $currentSubscriptionId"
if ($HomeSubscriptionId) {
  Write-Host "Home subscription    : $HomeSubscriptionId"
}
Write-Host "AHA target subscription: $AhaSubscriptionId"

if ($HomeSubscriptionId -and $currentSubscriptionId -ne $HomeSubscriptionId) {
  Write-Warning "Your current Azure CLI subscription does not match -HomeSubscriptionId. The script will still deploy to -AhaSubscriptionId."
}

Write-Host "`n=== Switching to AHA subscription ==="
az account set --subscription $AhaSubscriptionId | Out-Null

Write-Host "`n=== dotnet publish ==="
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish $Project -c Release --no-restore -o $PublishDir

Write-Host "`n=== Archive publish output ==="
$zipDir = Split-Path $ZipPath -Parent
if (-not (Test-Path $zipDir)) { New-Item -ItemType Directory -Path $zipDir | Out-Null }
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ZipPath -Force

Write-Host "`n=== Deploy zip to Azure Function App ==="
az functionapp deploy `
  --resource-group $ResourceGroup `
  --name $FunctionAppName `
  --src-path $ZipPath `
  --type zip `
  --clean true

Write-Host "`n=== Done ==="
Write-Host "Home subscription : $HomeSubscriptionId"
Write-Host "Target subscription: $AhaSubscriptionId"
Write-Host "Function app       : $FunctionAppName"
Write-Host "Resource group     : $ResourceGroup"
Write-Host "Zip artifact       : $ZipPath"