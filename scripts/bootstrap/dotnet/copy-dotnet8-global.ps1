# Copy .NET 8.0 from portable to global install
# Run as Administrator

#Requires -RunAsAdministrator

$portableRoot = "C:\Users\Keith.Watson\AppData\Local\.dotnet\8.0"
$globalRoot = "C:\Program Files\dotnet"

Write-Host "=== Manual .NET 8.0 Global Installation ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "This script copies .NET 8.0 from portable install to global location" -ForegroundColor Yellow
Write-Host "Source: $portableRoot" -ForegroundColor Gray
Write-Host "Target: $globalRoot" -ForegroundColor Gray
Write-Host ""

if (-not (Test-Path $portableRoot)) {
    Write-Host "ERROR: Portable .NET 8.0 not found at: $portableRoot" -ForegroundColor Red
    Write-Host "Run: pwsh scripts\bootstrap\dotnet\install-dotnet8-portable.ps1" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $globalRoot)) {
    Write-Host "ERROR: Global dotnet folder not found at: $globalRoot" -ForegroundColor Red
    exit 1
}

Write-Host "Checking what needs to be copied..." -ForegroundColor Yellow
Write-Host ""

# Check SDK
$sdk8 = Get-ChildItem "$portableRoot\sdk" -Directory | Select-Object -First 1
if ($sdk8) {
    Write-Host "✓ Found SDK: $($sdk8.Name)" -ForegroundColor Green
} else {
    Write-Host "✗ No SDK found in portable install" -ForegroundColor Red
    exit 1
}

# Check runtimes
$runtimeCore = Get-ChildItem "$portableRoot\shared\Microsoft.NETCore.App" -Directory | Select-Object -First 1
$runtimeAspNet = Get-ChildItem "$portableRoot\shared\Microsoft.AspNetCore.App" -Directory | Select-Object -First 1
$runtimeDesktop = Get-ChildItem "$portableRoot\shared\Microsoft.WindowsDesktop.App" -Directory | Select-Object -First 1

Write-Host "✓ Found Microsoft.NETCore.App: $($runtimeCore.Name)" -ForegroundColor Green
Write-Host "✓ Found Microsoft.AspNetCore.App: $($runtimeAspNet.Name)" -ForegroundColor Green
Write-Host "✓ Found Microsoft.WindowsDesktop.App: $($runtimeDesktop.Name)" -ForegroundColor Green
Write-Host ""

$confirmation = Read-Host "Copy .NET 8.0 to global location? (y/N)"
if ($confirmation -ne 'y' -and $confirmation -ne 'Y') {
    Write-Host "Cancelled." -ForegroundColor Gray
    exit 0
}

Write-Host ""
Write-Host "Copying files..." -ForegroundColor Yellow
Write-Host ""

try {
    # Copy SDK
    Write-Host "Copying SDK $($sdk8.Name)..." -ForegroundColor Cyan
    $targetSdk = Join-Path $globalRoot "sdk\$($sdk8.Name)"
    if (Test-Path $targetSdk) {
        Write-Host "  SDK already exists, skipping..." -ForegroundColor Yellow
    } else {
        Copy-Item -Path $sdk8.FullName -Destination $targetSdk -Recurse -Force
        Write-Host "  ✓ SDK copied" -ForegroundColor Green
    }

    # Copy host (dotnet.exe and related)
    Write-Host "Copying host files..." -ForegroundColor Cyan
    $hostFiles = Get-ChildItem "$portableRoot\host" -Recurse
    foreach ($file in $hostFiles) {
        $relativePath = $file.FullName.Substring($portableRoot.Length + 1)
        $targetPath = Join-Path $globalRoot $relativePath
        
        if ($file.PSIsContainer) {
            if (-not (Test-Path $targetPath)) {
                New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
            }
        } else {
            $targetDir = Split-Path $targetPath
            if (-not (Test-Path $targetDir)) {
                New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
            }
            Copy-Item -Path $file.FullName -Destination $targetPath -Force
        }
    }
    Write-Host "  ✓ Host files copied" -ForegroundColor Green

    # Copy runtimes
    Write-Host "Copying Microsoft.NETCore.App $($runtimeCore.Name)..." -ForegroundColor Cyan
    $targetRuntime = Join-Path $globalRoot "shared\Microsoft.NETCore.App\$($runtimeCore.Name)"
    if (Test-Path $targetRuntime) {
        Write-Host "  Runtime already exists, skipping..." -ForegroundColor Yellow
    } else {
        Copy-Item -Path $runtimeCore.FullName -Destination $targetRuntime -Recurse -Force
        Write-Host "  ✓ Runtime copied" -ForegroundColor Green
    }

    Write-Host "Copying Microsoft.AspNetCore.App $($runtimeAspNet.Name)..." -ForegroundColor Cyan
    $targetAspNet = Join-Path $globalRoot "shared\Microsoft.AspNetCore.App\$($runtimeAspNet.Name)"
    if (Test-Path $targetAspNet) {
        Write-Host "  Runtime already exists, skipping..." -ForegroundColor Yellow
    } else {
        Copy-Item -Path $runtimeAspNet.FullName -Destination $targetAspNet -Recurse -Force
        Write-Host "  ✓ Runtime copied" -ForegroundColor Green
    }

    Write-Host "Copying Microsoft.WindowsDesktop.App $($runtimeDesktop.Name)..." -ForegroundColor Cyan
    $targetDesktop = Join-Path $globalRoot "shared\Microsoft.WindowsDesktop.App\$($runtimeDesktop.Name)"
    if (Test-Path $targetDesktop) {
        Write-Host "  Runtime already exists, skipping..." -ForegroundColor Yellow
    } else {
        Copy-Item -Path $runtimeDesktop.FullName -Destination $targetDesktop -Recurse -Force
        Write-Host "  ✓ Runtime copied" -ForegroundColor Green
    }

    # Copy packs if they exist
    if (Test-Path "$portableRoot\packs") {
        Write-Host "Copying targeting packs..." -ForegroundColor Cyan
        Copy-Item -Path "$portableRoot\packs\*" -Destination "$globalRoot\packs\" -Recurse -Force
        Write-Host "  ✓ Packs copied" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "✓ .NET 8.0 successfully installed globally!" -ForegroundColor Green
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""
    Write-Host "Verifying installation..." -ForegroundColor Yellow
    Write-Host ""
    
    & "$globalRoot\dotnet.exe" --list-sdks
    Write-Host ""
    & "$globalRoot\dotnet.exe" --list-runtimes

    Write-Host ""
    Write-Host "✓ .NET 8.0 is now available globally" -ForegroundColor Green
    Write-Host ""
    Write-Host "You can now run:" -ForegroundColor Cyan
    Write-Host "  pwsh scripts\dev\demo.ps1" -ForegroundColor Gray
    Write-Host ""

} catch {
    Write-Host ""
    Write-Host "✗ Copy failed: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "Try running as Administrator" -ForegroundColor Yellow
    exit 1
}
