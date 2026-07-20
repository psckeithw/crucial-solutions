# .NET 8.0 Installation Guide for Windows
# Multiple methods provided - choose based on your permissions/preferences

Write-Host "=== .NET 8.0 Installation Options ===" -ForegroundColor Cyan
Write-Host ""

# Check current .NET installations
Write-Host "Current .NET installations:" -ForegroundColor Yellow
dotnet --list-sdks
dotnet --list-runtimes
Write-Host ""

Write-Host "Choose installation method:" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. WinGet (Quick, requires admin)" -ForegroundColor Green
Write-Host "   winget install Microsoft.DotNet.SDK.8"
Write-Host ""
Write-Host "2. Portable Install (NO admin needed - your past method)" -ForegroundColor Green
Write-Host "   Downloads to: $env:LOCALAPPDATA\.dotnet\8.0"
Write-Host "   Run: pwsh scripts\bootstrap\dotnet\install-dotnet8-portable.ps1"
Write-Host ""
Write-Host "3. Official Installer (GUI, requires admin)" -ForegroundColor Green
Write-Host "   Download: https://dotnet.microsoft.com/download/dotnet/8.0"
Write-Host "   Then run the .exe installer"
Write-Host ""
Write-Host "4. Manual Extract (NO admin, advanced)" -ForegroundColor Green
Write-Host "   1. Download SDK from: https://dotnet.microsoft.com/download/dotnet/8.0"
Write-Host "   2. Choose: .NET SDK 8.0.x - Windows x64 Binary"
Write-Host "   3. Extract ZIP to: C:\dotnet8 (or any folder)"
Write-Host "   4. Add to PATH: `$env:PATH += ';C:\dotnet8'"
Write-Host ""

$choice = Read-Host "Enter choice (1-4) or Q to quit"

switch ($choice) {
    "1" {
        Write-Host "Installing via WinGet..." -ForegroundColor Green
        winget install Microsoft.DotNet.SDK.8
    }
    "2" {
        Write-Host "Creating portable installer script..." -ForegroundColor Green
        
        # Create the portable installer script
        $portableScript = @'
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
'@

        Set-Content -Path (Join-Path $PSScriptRoot "install-dotnet8-portable.ps1") -Value $portableScript
        Write-Host "✓ Created: install-dotnet8-portable.ps1" -ForegroundColor Green
        Write-Host ""
        Write-Host "Run: pwsh scripts\bootstrap\dotnet\install-dotnet8-portable.ps1" -ForegroundColor Cyan
    }
    "3" {
        Write-Host "Opening download page..." -ForegroundColor Green
        Start-Process "https://dotnet.microsoft.com/download/dotnet/8.0"
        Write-Host "Download the Windows x64 installer and run it."
    }
    "4" {
        Write-Host "Manual extraction steps:" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "1. Download binary (NOT installer):" -ForegroundColor Cyan
        Write-Host "   https://dotnet.microsoft.com/download/dotnet/8.0"
        Write-Host "   Look for: '.NET SDK 8.0.x - Windows x64 Binary (zip)'"
        Write-Host ""
        Write-Host "2. Extract to your preferred location:" -ForegroundColor Cyan
        Write-Host "   Example: C:\dotnet8 or $env:USERPROFILE\dotnet8"
        Write-Host ""
        Write-Host "3. Add to PATH (this session):" -ForegroundColor Cyan
        Write-Host "   `$env:PATH = `"C:\dotnet8;`$env:PATH`""
        Write-Host ""
        Write-Host "4. Test:" -ForegroundColor Cyan
        Write-Host "   dotnet --version"
        Write-Host ""
        Write-Host "5. Make permanent (add to PowerShell profile):" -ForegroundColor Cyan
        Write-Host "   Add-Content `$PROFILE `"`$env:PATH += ';C:\dotnet8'`""
    }
    default {
        Write-Host "Cancelled." -ForegroundColor Gray
    }
}
