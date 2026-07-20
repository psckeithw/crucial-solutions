# Copy .NET 8.0 from portable to global with elevation prompt
# Requests admin only for the copy operation

$portable = "C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0"
$global = "C:\Program Files\dotnet"

Write-Host "=== Copy .NET 8.0 to Global Location ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "This will prompt for admin elevation to copy .NET 8.0 files." -ForegroundColor Yellow
Write-Host ""
Write-Host "From: $portable" -ForegroundColor Gray
Write-Host "To:   $global" -ForegroundColor Gray
Write-Host ""

# Create PowerShell script for elevated copy
$script = @"
Write-Host 'Copying .NET 8.0 SDK and Runtimes...' -ForegroundColor Green
Write-Host ''

`$portable = '$portable'
`$global = '$global'

# Copy SDK
Write-Host 'SDK 8.0.422...' -ForegroundColor Cyan
Copy-Item "`$portable\sdk\8.0.422" "`$global\sdk\8.0.422" -Recurse -Force

# Copy Core Runtime
Write-Host 'Microsoft.NETCore.App 8.0.28...' -ForegroundColor Cyan
Copy-Item "`$portable\shared\Microsoft.NETCore.App\8.0.28" "`$global\shared\Microsoft.NETCore.App\8.0.28" -Recurse -Force

# Copy ASP.NET Core Runtime
Write-Host 'Microsoft.AspNetCore.App 8.0.28...' -ForegroundColor Cyan
Copy-Item "`$portable\shared\Microsoft.AspNetCore.App\8.0.28" "`$global\shared\Microsoft.AspNetCore.App\8.0.28" -Recurse -Force

# Copy Windows Desktop Runtime
Write-Host 'Microsoft.WindowsDesktop.App 8.0.28...' -ForegroundColor Cyan
Copy-Item "`$portable\shared\Microsoft.WindowsDesktop.App\8.0.28" "`$global\shared\Microsoft.WindowsDesktop.App\8.0.28" -Recurse -Force

# Copy Host FXR
Write-Host 'Host FXR 8.0.28...' -ForegroundColor Cyan
Copy-Item "`$portable\host\fxr\8.0.28" "`$global\host\fxr\8.0.28" -Recurse -Force

Write-Host ''
Write-Host 'Done! .NET 8.0 installed globally.' -ForegroundColor Green
Write-Host ''
Write-Host 'Verification:' -ForegroundColor Yellow
& 'C:\Program Files\dotnet\dotnet.exe' --list-sdks
& 'C:\Program Files\dotnet\dotnet.exe' --list-runtimes

Write-Host ''
Write-Host 'Press any key to close...' -ForegroundColor Gray
`$null = `$Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
"@

# Save to temp
$tempScript = [System.IO.Path]::GetTempFileName() + ".ps1"
$script | Out-File $tempScript -Encoding UTF8

try {
    # Start elevated PowerShell with the copy script
    Start-Process powershell.exe -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $tempScript -Verb RunAs -Wait
    
    Write-Host ""
    Write-Host "✓ Copy complete. Verifying..." -ForegroundColor Green
    Write-Host ""
    
    dotnet --list-sdks
    dotnet --list-runtimes
    
} catch {
    Write-Host "✗ Elevation denied or failed" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
} finally {
    if (Test-Path $tempScript) {
        Remove-Item $tempScript -Force
    }
}
