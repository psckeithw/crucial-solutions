# Setup portable .NET 8 SDK without admin rights
# Run this script after downloading and extracting the SDK

$dotnetDir = "$env:USERPROFILE\.dotnet\8.0"
$sdkUrl = "https://download.visualstudio.microsoft.com/download/pr/34f8a6de-276b-44e7-b9e5-a8f8cc6ff769/01c3c013e45e9a7a9ddc8e07b39e4e29/dotnet-sdk-8.0.403-win-x64.zip"
$zipPath = "$env:TEMP\dotnet-sdk-8.0.403.zip"

Write-Host "=== .NET 8 SDK Portable Setup ===" -ForegroundColor Cyan
Write-Host ""

# Check if already extracted
if (Test-Path "$dotnetDir\dotnet.exe") {
    Write-Host "✓ .NET 8 SDK already found at: $dotnetDir" -ForegroundColor Green
    & "$dotnetDir\dotnet.exe" --version
} else {
    Write-Host "Downloading .NET 8 SDK..." -ForegroundColor Yellow
    Write-Host "URL: $sdkUrl"
    Write-Host "To: $zipPath"
    Write-Host ""
    
    try {
        # Download
        Invoke-WebRequest -Uri $sdkUrl -OutFile $zipPath -UseBasicParsing
        Write-Host "✓ Downloaded" -ForegroundColor Green
        
        # Extract
        Write-Host "Extracting to: $dotnetDir" -ForegroundColor Yellow
        Expand-Archive -Path $zipPath -DestinationPath $dotnetDir -Force
        Write-Host "✓ Extracted" -ForegroundColor Green
        
        # Cleanup
        Remove-Item $zipPath -Force
        
        # Verify
        if (Test-Path "$dotnetDir\dotnet.exe") {
            Write-Host "✓ .NET 8 SDK installed successfully!" -ForegroundColor Green
            & "$dotnetDir\dotnet.exe" --version
        }
    }
    catch {
        Write-Host "✗ Download failed. Please manually:" -ForegroundColor Red
        Write-Host "  1. Download from: https://dotnet.microsoft.com/download/dotnet/8.0"
        Write-Host "  2. Choose: SDK 8.0.403 - Windows x64 Binaries"
        Write-Host "  3. Extract all contents to: $dotnetDir"
    }
}

Write-Host ""
Write-Host "=== To use with Azure Functions ===" -ForegroundColor Cyan
Write-Host "Run this before 'func start':" -ForegroundColor Yellow
Write-Host ""
Write-Host "`$env:DOTNET_ROOT = `"$dotnetDir`"" -ForegroundColor White
Write-Host "`$env:PATH = `"$dotnetDir;`$env:PATH`"" -ForegroundColor White
Write-Host "cd src/Function" -ForegroundColor White
Write-Host "func start --port 7072" -ForegroundColor White
