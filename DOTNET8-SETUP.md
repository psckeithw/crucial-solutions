# Portable .NET 8 SDK Setup - Quick Reference

## Download Location
https://dotnet.microsoft.com/download/dotnet/8.0
- Choose: **SDK 8.0.403**
- Platform: **Windows x64**  
- Type: **Binaries** (NOT installer)

## Extract To
```
C:\Users\Keith.Watson\.dotnet\8.0
```

## Verify Installation
After extracting, check:
```powershell
C:\Users\Keith.Watson\.dotnet\8.0\dotnet.exe --version
```
Should show: `8.0.403`

## Run Function Locally

### Option 1: Use Helper Script (Easiest)
```powershell
cd c:\code\UPO\ado-servicenow
.\run-function-local.ps1
```

### Option 2: Manual Commands
```powershell
$env:DOTNET_ROOT = "C:\Users\Keith.Watson\.dotnet\8.0"
$env:PATH = "C:\Users\Keith.Watson\.dotnet\8.0;$env:PATH"
cd c:\code\UPO\ado-servicenow\src\Function
dotnet run
```

**Note:** Use `dotnet run` instead of `func start` - it properly handles .NET Isolated projects.

## Files Created
- `setup-dotnet8.ps1` - Auto-download script (if network allows)
- `run-function-local.ps1` - Quick start script for local testing
- `global.json` - Pins project to .NET 8.x SDK

## Troubleshooting
If `func start` says "Worker runtime cannot be 'None'":
- Ensure you're in `src/Function` directory
- Check `local.settings.json` has `"FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"`
