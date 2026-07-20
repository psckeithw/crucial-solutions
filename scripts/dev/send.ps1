<#
.SYNOPSIS
  Postman-style sender for local or deployed /api/servicenow/userstory.
  Reads ADO org/PAT from .env (local testing only), sends a sample JSON payload,
  prints the response.

.PARAMETER Sample
  Path to JSON payload. Default: samples/valid-payload.json

.PARAMETER Endpoint
  Function URL. Default: http://localhost:7071/api/servicenow/userstory

.PARAMETER ApiKey
  X-API-Key value. Required unless endpoint allows anonymous.

.EXAMPLE
  pwsh scripts\dev\send.ps1 -Sample samples/valid-payload.json
  API_KEY="mykey" ENDPOINT="https://<app>.azurewebsites.net/api/servicenow/userstory" pwsh scripts\dev\send.ps1 -Sample samples/duplicate-same-project.json
#>

param(
  [string]$Sample = "samples/valid-payload.json",
  [string]$Endpoint = "http://localhost:7071/api/servicenow/userstory",
  [string]$ApiKey = $env:API_KEY
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path .env)) { throw ".env not found in $(Get-Location)" }
if (-not (Test-Path $Sample)) { throw "Sample not found: $Sample" }

$m = @{}
foreach ($line in Get-Content .env) {
  if ($line -match '^\s*(#|$)') { continue }
  if ($line -match '^([^=]+)=(.*)$') { $m[$matches[1].Trim()] = $matches[2].Trim() }
}

$body = Get-Content $Sample -Raw

$headers = @{
  "Content-Type" = "application/json"
}
if ($ApiKey) {
  $headers["X-API-Key"] = $ApiKey
} else {
  Write-Warning "API_KEY not set; request may return 401"
}

Write-Host "POST $Endpoint"
Write-Host "Body:  $Sample"
Write-Host "Org:   $($m['AZURE_DEVOPS_ORG'])"
Write-Host "Key:   $(if ($ApiKey) { '<set>' } else { '<none>' })"
Write-Host ""

try {
  $resp = Invoke-RestMethod -Uri $Endpoint -Method Post -Headers $headers -Body $body -ContentType "application/json"
  $resp | ConvertTo-Json -Depth 5
} catch {
  if ($_.Exception.Response) {
    $sr = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
    Write-Host $sr.ReadToEnd() -ForegroundColor Red
    $sr.Close()
  } else {
    Write-Host $_.Exception.Message -ForegroundColor Red
  }
  exit 1
}
