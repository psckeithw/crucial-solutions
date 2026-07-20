# Portable .NET 8.0 SDK Installer (NO admin required)

$installPath = "$env:LOCALAPPDATA\.dotnet\8.0"
$downloadUrl = "https://download.visualstudio.microsoft.com/download/pr/3dc58d4d-2b8f-4d54-a6c0-2c2b4e4e0e2e/3f8e1f39d4a94a7e8d1e9f0b6c8d5a7e/dotnet-sdk-8.0.404-win-x64.zip"

Write-Host "Downloading .NET 8.0 SDK..." -ForegroundColor Yellow
Write-Host "Target: $installPath" -ForegroundColor Gray

# Create directory
New-Item -ItemType Directory -Force -Path $installPath | Out-Null

# Download
$zipPath = "$env:TEMP\dotnet-sdk-8.0.zip"
try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -UseBasicParsing
    Write-Host "✓ Download complete" -ForegroundColor Green
} catch {
    Write-Host "✗ Download failed. Trying alternate method..." -ForegroundColor Red
    # Alternate: Use official script
    $scriptUrl = "https://dot.net/v1/dotnet-install.ps1"
    Invoke-WebRequest -Uri $scriptUrl -OutFile "$env:TEMP\dotnet-install.ps1" -UseBasicParsing
    & "$env:TEMP\dotnet-install.ps1" -InstallDir $installPath -Channel 8.0 -NoPath
    Write-Host "✓ Installed via dotnet-install.ps1" -ForegroundColor Green
    Write-Host ""
    Write-Host "Add to your PATH:" -ForegroundColor Yellow
    Write-Host "  `$env:PATH = `"$installPath;`$env:PATH`"" -ForegroundColor Gray
    return
}

# Extract
Write-Host "Extracting..." -ForegroundColor Yellow
Expand-Archive -Path $zipPath -DestinationPath $installPath -Force
Remove-Item $zipPath

Write-Host "✓ Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Location: $installPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "To use this .NET 8.0 installation:" -ForegroundColor Yellow
Write-Host "  1. Add to PATH for this session:"
Write-Host "     `$env:PATH = `"$installPath;`$env:PATH`""
Write-Host ""
Write-Host "  2. Or create a permanent shortcut:"
Write-Host "     New-Alias -Name dotnet8 -Value `"$installPath\dotnet.exe`""
Write-Host ""
Write-Host "  3. To make permanent, add to PowerShell profile:"
Write-Host "     Add-Content `$PROFILE `"`$env:PATH += ';$installPath'`""
Write-Host ""
Write-Host "Verify:" -ForegroundColor Yellow
Write-Host "  & `"$installPath\dotnet.exe`" --version"
