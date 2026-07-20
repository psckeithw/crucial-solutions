# Manual .NET 8.0 DLL Copy - Step-by-step guide
# Since you've done this before, here's the quick reference

Write-Host "=== Manual .NET 8.0 DLL Copy Guide ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Source (portable): C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0" -ForegroundColor Yellow
Write-Host "Target (global):   C:\Program Files\dotnet" -ForegroundColor Yellow
Write-Host ""

Write-Host "FILES TO COPY:" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. SDK (required for build):" -ForegroundColor Green
Write-Host "   FROM: C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0\sdk\8.0.422" -ForegroundColor Gray
Write-Host "   TO:   C:\Program Files\dotnet\sdk\8.0.422" -ForegroundColor Gray
Write-Host ""

Write-Host "2. Core Runtime (required for func tool):" -ForegroundColor Green
Write-Host "   FROM: C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0\shared\Microsoft.NETCore.App\8.0.28" -ForegroundColor Gray
Write-Host "   TO:   C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.28" -ForegroundColor Gray
Write-Host ""

Write-Host "3. ASP.NET Runtime (required for Azure Functions):" -ForegroundColor Green
Write-Host "   FROM: C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0\shared\Microsoft.AspNetCore.App\8.0.28" -ForegroundColor Gray
Write-Host "   TO:   C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\8.0.28" -ForegroundColor Gray
Write-Host ""

Write-Host "4. WindowsDesktop Runtime (for completeness):" -ForegroundColor Green
Write-Host "   FROM: C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0\shared\Microsoft.WindowsDesktop.App\8.0.28" -ForegroundColor Gray
Write-Host "   TO:   C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.28" -ForegroundColor Gray
Write-Host ""

Write-Host "5. Host files (if dotnet.exe missing 8.0 support):" -ForegroundColor Green
Write-Host "   FROM: C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0\host\fxr\8.0.28" -ForegroundColor Gray
Write-Host "   TO:   C:\Program Files\dotnet\host\fxr\8.0.28" -ForegroundColor Gray
Write-Host ""

Write-Host "OPTION 1: Copy via PowerShell (as Admin):" -ForegroundColor Cyan
Write-Host @"
`$portable = "C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0"
`$global = "C:\Program Files\dotnet"

# SDK
Copy-Item "`$portable\sdk\8.0.422" "`$global\sdk\8.0.422" -Recurse -Force

# Runtimes
Copy-Item "`$portable\shared\Microsoft.NETCore.App\8.0.28" "`$global\shared\Microsoft.NETCore.App\8.0.28" -Recurse -Force
Copy-Item "`$portable\shared\Microsoft.AspNetCore.App\8.0.28" "`$global\shared\Microsoft.AspNetCore.App\8.0.28" -Recurse -Force
Copy-Item "`$portable\shared\Microsoft.WindowsDesktop.App\8.0.28" "`$global\shared\Microsoft.WindowsDesktop.App\8.0.28" -Recurse -Force

# Host
Copy-Item "`$portable\host\fxr\8.0.28" "`$global\host\fxr\8.0.28" -Recurse -Force

Write-Host "Done! Verify with: dotnet --list-sdks"
"@ -ForegroundColor Gray
Write-Host ""

Write-Host "OPTION 2: Copy via File Explorer (as Admin):" -ForegroundColor Cyan
Write-Host "  1. Run Explorer as Admin: Start-Process explorer -Verb RunAs" -ForegroundColor Gray
Write-Host "  2. Navigate to both folders in separate windows" -ForegroundColor Gray
Write-Host "  3. Drag/drop or copy-paste the folders listed above" -ForegroundColor Gray
Write-Host ""

Write-Host "After copying, verify:" -ForegroundColor Yellow
Write-Host "  dotnet --list-sdks  # Should show 8.0.422" -ForegroundColor Gray
Write-Host ""

$response = Read-Host "Run automated copy now? (Y/n)"
if ($response -eq '' -or $response -eq 'y' -or $response -eq 'Y') {
    Write-Host ""
    Write-Host "Starting automated copy (requires admin)..." -ForegroundColor Cyan
    Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd '$PSScriptRoot'; .\copy-dotnet8-global.ps1" -Verb RunAs -Wait
} else {
    Write-Host "Copy manually using the commands above." -ForegroundColor Gray
}
