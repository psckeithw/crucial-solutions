$pat = (Get-Content .env | Where-Object { $_ -match '^ADO_PAT=' }).Replace('ADO_PAT=','')
$basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$pat"))
$headers = @{ Authorization = "Basic $basic" }
$fields = Invoke-RestMethod -Uri "https://dev.azure.com/aha-bt/_apis/wit/fields?api-version=7.1" -Headers $headers
$snow = $fields.value | Where-Object { $_.name -like '*ServiceNow*' }
if ($snow) {
    $snow | Select-Object name, referenceName, type | Format-Table -AutoSize
} else {
    Write-Host "No ServiceNow fields found yet. Create 'ServiceNow ID' in the process template, then re-run this script."
}
