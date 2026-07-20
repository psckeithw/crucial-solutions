[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SubscriptionId,

    [string]$ResourceGroup = "rg-common-poc-01",

    [string]$ResourceName = "oai-common-poc-01",

    [string]$Location = "eastus2",

    [string[]]$DeployModels = @(
        "gpt-4.1-mini",
        "gpt-4.1",
        "gpt-4o-mini",
        "gpt-4o",
        "o3-mini",
        "text-embedding-3-large"
    ),

    [switch]$SkipModelDeployment
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-AzCli {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw "Azure CLI (az) was not found on PATH. Install it first."
    }
}

function Get-ModelVersionFromCatalog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ModelName,

        [Parameter(Mandatory = $true)]
        [object[]]$Catalog
    )

    $matches = $Catalog | Where-Object {
        ($_.name -eq $ModelName) -or ($_.modelName -eq $ModelName) -or ($_.model -eq $ModelName)
    }

    if (-not $matches) {
        return $null
    }

    $sorted = $matches | Sort-Object {
        if ($_.version) { $_.version } else { "0" }
    } -Descending

    return $sorted | Select-Object -First 1
}

Assert-AzCli

Write-Host "Setting subscription to $SubscriptionId..."
az account set --subscription $SubscriptionId | Out-Null

Write-Host "Ensuring resource group $ResourceGroup exists in $Location..."
az group create --name $ResourceGroup --location $Location --output none | Out-Null

Write-Host "Creating Azure OpenAI resource $ResourceName..."
az cognitiveservices account create `
    --name $ResourceName `
    --resource-group $ResourceGroup `
    --location $Location `
    --kind OpenAI `
    --sku s0 `
    --custom-domain $ResourceName `
    --yes `
    --output none | Out-Null

Write-Host "Resource created. Listing available models for this account..."
$catalogJson = az cognitiveservices account list-models `
    --name $ResourceName `
    --resource-group $ResourceGroup `
    -o json

$catalog = $catalogJson | ConvertFrom-Json
if (-not $catalog) {
    Write-Warning "No models were returned by the catalog. Check region availability and quotas."
    exit 0
}

Write-Host "Available models:"
$catalog | Select-Object name, version, format, skuName | Sort-Object name, version | Format-Table -AutoSize

if ($SkipModelDeployment) {
    Write-Host "Skipping deployment as requested."
    exit 0
}

foreach ($modelName in $DeployModels) {
    $selected = Get-ModelVersionFromCatalog -ModelName $modelName -Catalog $catalog
    if (-not $selected) {
        Write-Warning "Model '$modelName' is not available in this region/account, skipping."
        continue
    }

    $deploymentName = $modelName.Replace('.', '-')
    $skuName = if ($selected.skuName) { $selected.skuName } else { "Standard" }
    $modelVersion = if ($selected.version) { $selected.version } else { "latest" }
    $modelFormat = if ($selected.format) { $selected.format } else { "OpenAI" }

    Write-Host "Deploying $modelName as $deploymentName (version $modelVersion, format $modelFormat, sku $skuName)..."
    az cognitiveservices account deployment create `
        --name $ResourceName `
        --resource-group $ResourceGroup `
        --deployment-name $deploymentName `
        --model-name $modelName `
        --model-version $modelVersion `
        --model-format $modelFormat `
        --sku-capacity "1" `
        --sku-name $skuName `
        --output none | Out-Null
}

Write-Host "\nDone. Retrieve your endpoint and keys with:"
Write-Host "az cognitiveservices account show -n $ResourceName -g $ResourceGroup --query properties.endpoint -o tsv"
Write-Host "az cognitiveservices account keys list -n $ResourceName -g $ResourceGroup -o table"
