<#
.SYNOPSIS
  Creates an Azure Resource Manager service connection in Azure DevOps.

.DESCRIPTION
  This script uses an Azure service principal and the Azure DevOps CLI to
  create an ARM service connection that can be used by pipelines.

  Recommendation for enterprise tenants:
  - Prefer Workload Identity Federation when your org allows it.
  - Use this script when you need a fully scriptable fallback today.
  - Scope the connection to a single project and authorize only the
    pipeline that needs it.

.EXAMPLE
  pwsh .\scripts\bootstrap\azure-devops\new-azure-devops-service-connection.ps1 -OrganizationUrl https://dev.azure.com/contoso -ProjectName MyProject -ServiceConnectionName sc-contoso-prod -SubscriptionId 00000000-0000-0000-0000-000000000000 -SubscriptionName "Contoso Production" -TenantId 11111111-1111-1111-1111-111111111111

.NOTES
  Prerequisites:
  - Azure CLI installed and signed in (`az login`)
  - Azure DevOps CLI extension installed (`az extension add --name azure-devops`)
  - Azure DevOps PAT with permission to create service connections
  - Existing Azure service principal secret, or let the script create one
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory)]
  [string]$OrganizationUrl,

  [Parameter(Mandatory)]
  [string]$ProjectName,

  [Parameter(Mandatory)]
  [string]$ServiceConnectionName,

  [Parameter(Mandatory)]
  [string]$SubscriptionId,

  [Parameter(Mandatory)]
  [string]$SubscriptionName,

  [Parameter(Mandatory)]
  [string]$TenantId,

  [string]$ServicePrincipalId,

  [string]$ServicePrincipalSecret,

  [switch]$CreateServicePrincipal,

  [string]$ServicePrincipalName = "sp-$ServiceConnectionName"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Ensure-AzDevOpsCli {
  $extension = az extension show --name azure-devops --only-show-errors 2>$null
  if (-not $extension) {
    az extension add --name azure-devops --only-show-errors | Out-Null
  }
}

function Ensure-AzLogin {
  $account = az account show --query id -o tsv 2>$null
  if (-not $account) {
    throw 'Run az login first and try again.'
  }
}

Ensure-AzLogin
Ensure-AzDevOpsCli

if ($CreateServicePrincipal) {
  if ($ServicePrincipalId -or $ServicePrincipalSecret) {
    throw 'Do not combine -CreateServicePrincipal with pre-supplied ServicePrincipalId or ServicePrincipalSecret.'
  }

  $sp = az ad sp create-for-rbac `
    --name $ServicePrincipalName `
    --role Contributor `
    --scopes "/subscriptions/$SubscriptionId" `
    --output json | ConvertFrom-Json

  $ServicePrincipalId = $sp.appId
  $ServicePrincipalSecret = $sp.password
}

if (-not $ServicePrincipalId -or -not $ServicePrincipalSecret) {
  throw 'Provide -ServicePrincipalId and -ServicePrincipalSecret, or use -CreateServicePrincipal.'
}

$env:AZURE_DEVOPS_EXT_AZURE_RM_SERVICE_PRINCIPAL_KEY = $ServicePrincipalSecret

az devops configure --defaults organization=$OrganizationUrl project=$ProjectName | Out-Null

$result = az devops service-endpoint azurerm create `
  --azure-rm-service-principal-id $ServicePrincipalId `
  --azure-rm-subscription-id $SubscriptionId `
  --azure-rm-subscription-name $SubscriptionName `
  --azure-rm-tenant-id $TenantId `
  --name $ServiceConnectionName `
  --output json | ConvertFrom-Json

Write-Host ''
Write-Host 'Created service connection:'
Write-Host "Name: $($result.name)"
Write-Host "Id:   $($result.id)"
Write-Host ''
Write-Host 'Recommendation: keep this connection scoped to the target project and authorize only the pipeline that deploys the function app.'