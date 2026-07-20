$pat = (Get-Content .env | Where-Object { $_ -match '^ADO_PAT=' }).Replace('ADO_PAT=','')
$basic = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$pat"))
$headers = @{ Authorization = "Basic $basic" }
$wi = Invoke-RestMethod -Uri "https://dev.azure.com/aha-bt/_apis/wit/workitems/232381?api-version=7.1" -Headers $headers
Write-Host "`nWork Item 232381:"
Write-Host "Title: $($wi.fields.'System.Title')"
Write-Host "ServiceNow ID: $($wi.fields.'Custom.ServiceNowID')"
Write-Host "`nAll Custom fields:"
$wi.fields.PSObject.Properties | Where-Object { $_.Name -like 'Custom.*' } | ForEach-Object { "  $($_.Name) = $($_.Value)" }
