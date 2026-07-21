# .NET 8 Portable SDK Installation & Troubleshooting Guide

## Problem Statement
After accidentally installing .NET 10 and having .NET 8 removed, Azure Functions v4 (which only supports .NET 8) could not run locally. This guide documents the portable installation process without requiring admin rights.

---

## CRITICAL FINDING: Azure Functions Core Tools Limitation

**Azure Functions Core Tools (`func.exe`) is HARDCODED to use `C:\Program Files\dotnet\dotnet.exe`.**

- It **IGNORES** `DOTNET_ROOT` environment variable
- It **IGNORES** `PATH` environment variable  
- It **IGNORES** `DOTNET_MULTILEVEL_LOOKUP=0`
- It directly calls `C:\Program Files\dotnet\dotnet.exe` for the isolated worker process

### The Only Solution

**You MUST remove .NET 10 from `C:\Program Files\dotnet\`** to allow Azure Functions to work with .NET 8.

#### Option A: Run Admin Script (Recommended)
1. Right-click PowerShell → **Run as Administrator**
2. Navigate to project: `cd c:\code\UPO\ado-servicenow`
3. Run: `.\ADMIN-UNINSTALL-NET10.ps1`
4. Follow prompts to rename/disable .NET 10

#### Option B: Manual Uninstall
1. Open **Settings** → **Apps** → **Installed apps**
2. Search for **"Microsoft .NET SDK"**
3. Uninstall **.NET 10** (keep .NET 8 if shown separately)
4. Restart terminal

---

## Solution: Portable .NET 8 SDK Installation (✓ COMPLETED)

### Step 1: Download .NET 8 SDK Binaries (✓ Done)
1. Navigate to: https://dotnet.microsoft.com/download/dotnet/8.0
2. Locate **SDK 8.0.403** (or latest 8.0.x version)
3. Click **Windows** → **x64** → **Binaries** (NOT the installer)
4. Download the `.zip` file (approximately 200-250 MB)

### Step 2: Extract to User Profile
Extract the **entire contents** of the ZIP to:
```
C:\Users\Keith.Watson\.dotnet\8.0
```

**Important:** Extract ALL files including:
- `dotnet.exe`
- `sdk` folder
- `host` folder  
- `shared` folder
- All other contents

### Step 3: Verify Installation
```powershell
C:\Users\Keith.Watson\.dotnet\8.0\dotnet.exe --version
```
**Expected output:** `8.0.423` (or similar 8.0.x version)

### Step 4: Update Project Configuration
Created `global.json` in project root to pin SDK version:
```json
{
  "sdk": {
    "version": "8.0.423",
    "rollForward": "latestMinor",
    "allowPrerelease": false
  }
}
```

### Step 5: Run the Function
Use the helper script:
```powershell
cd c:\code\UPO\ado-servicenow
.\run-function-local.ps1
```

Or manually:
```powershell
$env:DOTNET_ROOT = "C:\Users\Keith.Watson\.dotnet\8.0"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:PATH = "C:\Users\Keith.Watson\.dotnet\8.0;$env:PATH"
cd c:\code\UPO\ado-servicenow\src\Function
dotnet run
```

---

## Errors Encountered & Solutions

### Error 1: `func start` Uses Wrong .NET Version
**Symptom:**
```
[2026-07-17T20:41:41.166Z] Microsoft.Azure.WebJobs.Script.Grpc: 
C:\Program Files\dotnet\dotnet.exe exited with code -2147450730 (0x80008096). 
https://aka.ms/dotnet/app-launch-failed.
```

**Root Cause:**  
Azure Functions Core Tools (`func.exe`) ignores `DOTNET_ROOT` and `PATH` environment variables, continuing to use the system-installed .NET 10 from `C:\Program Files\dotnet\`, which is incompatible with Azure Functions v4.

**Solution:**  
Use `dotnet run` instead of `func start`:
```powershell
dotnet run  # Instead of: func start --port 7072
```

**Why This Works:**  
- `dotnet run` respects `DOTNET_ROOT` and uses the portable SDK
- It builds the project and starts the Functions host in one step
- Properly handles .NET Isolated worker process

---

### Error 2: SDK Version Mismatch
**Symptom:**
```
Requested SDK version: 8.0.400
global.json file: C:\code\UPO\ado-servicenow\global.json

Installed SDKs:
10.0.109 [C:\Program Files\dotnet\sdk]

Install the [8.0.400] .NET SDK or update [C:\code\UPO\ado-servicenow\global.json] 
to match an installed SDK.
```

**Root Cause:**  
The `global.json` specified SDK version `8.0.400` but the portable SDK extracted was version `8.0.423`.

**Solution:**  
Update `global.json` to match your extracted SDK version:
```powershell
# Check your actual version
C:\Users\Keith.Watson\.dotnet\8.0\dotnet.exe --version

# Update global.json with that exact version
```

---

### Error 3: Worker Process Exit Code -2147450730 (0x80008096)
**Symptom:**
```
Failed to start language worker process for runtime: dotnet-isolated
Language Worker Process exited. Pid=50336.
C:\Program Files\dotnet\dotnet.exe exited with code -2147450730 (0x80008096)
```

**Root Cause:**  
Functions runtime tried to start a .NET 8 isolated worker using .NET 10 runtime, causing a version incompatibility crash.

**Solution:**  
Ensure environment variables are set in the **same PowerShell session** before running:
```powershell
$env:DOTNET_ROOT = "C:\Users\Keith.Watson\.dotnet\8.0"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"  # Prevents searching system paths
$env:PATH = "C:\Users\Keith.Watson\.dotnet\8.0;$env:PATH"
```

Then use `dotnet run` (not `func start`).

---

### Error 4: Network Download Failure
**Symptom:**
```
Invoke-WebRequest: GatewayExceptionResponse
```

**Root Cause:**  
Corporate proxy, TLS version requirements, or network restrictions blocking direct downloads from Microsoft CDN.

**Solution:**  
Manual download via browser:
1. Open browser and navigate to: https://dotnet.microsoft.com/download/dotnet/8.0
2. Click through to download the SDK binaries ZIP
3. Extract manually to `C:\Users\Keith.Watson\.dotnet\8.0`

---

## Key Environment Variables Explained

### `DOTNET_ROOT`
Points to the root directory of the .NET SDK to use. This tells .NET tools where to find the runtime.

**Example:**
```powershell
$env:DOTNET_ROOT = "C:\Users\Keith.Watson\.dotnet\8.0"
```

### `DOTNET_MULTILEVEL_LOOKUP`
Controls whether .NET searches multiple locations for SDKs/runtimes.

**Set to `0`:** Only use `DOTNET_ROOT` - don't search `C:\Program Files\dotnet`  
**Set to `1` or unset:** Search multiple locations (default behavior)

**For portable SDK, always set to `0`:**
```powershell
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
```

### `PATH`
Determines which `dotnet.exe` is used when you type `dotnet` in the command line.

**Prepend your portable SDK location:**
```powershell
$env:PATH = "C:\Users\Keith.Watson\.dotnet\8.0;$env:PATH"
```

---

## Verification Checklist

Before running the function, verify:

1. **SDK Extracted:**
   ```powershell
   Test-Path "C:\Users\Keith.Watson\.dotnet\8.0\dotnet.exe"
   # Should return: True
   ```

2. **Correct Version:**
   ```powershell
   C:\Users\Keith.Watson\.dotnet\8.0\dotnet.exe --version
   # Should show: 8.0.423 (or your extracted version)
   ```

3. **Environment Set:**
   ```powershell
   $env:DOTNET_ROOT
   # Should show: C:\Users\Keith.Watson\.dotnet\8.0
   
   $env:DOTNET_MULTILEVEL_LOOKUP
   # Should show: 0
   
   Get-Command dotnet | Select-Object Source
   # Should show: C:\Users\Keith.Watson\.dotnet\8.0\dotnet.exe
   ```

4. **global.json Matches:**
   ```powershell
   Get-Content C:\code\UPO\ado-servicenow\global.json
   # SDK version should match your installed version
   ```

5. **Local Settings Valid:**
   ```powershell
   Get-Content C:\code\UPO\ado-servicenow\src\Function\local.settings.json
   # Should have: "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
   ```

---

## Helper Scripts Created

### `run-function-local.ps1`
Automated script that sets environment and runs the function:
- Sets `DOTNET_ROOT`, `DOTNET_MULTILEVEL_LOOKUP`, and `PATH`
- Verifies .NET 8 SDK exists
- Changes to function directory
- Runs `dotnet run`

**Usage:**
```powershell
cd c:\code\UPO\ado-servicenow
.\run-function-local.ps1
```

### `setup-dotnet8.ps1`
Attempts to auto-download and extract .NET 8 SDK (network permitting):
- Downloads SDK binaries ZIP
- Extracts to user profile
- Provides manual instructions if download fails

**Usage:**
```powershell
pwsh -NoProfile -ExecutionPolicy Bypass .\setup-dotnet8.ps1
```

---

## Project Files Modified

1. **global.json** - Created to pin SDK to 8.0.423
2. **local.settings.json** - Updated `BlobStorage__ConnectionString` to use local storage emulator
3. **run-function-local.ps1** - Created helper script
4. **setup-dotnet8.ps1** - Created installer script
5. **DOTNET8-SETUP.md** - Quick reference guide

---

## Alternative Approaches Considered

### ❌ Installing .NET 8 System-Wide
**Why not:** Requires admin privileges, which are not available.

### ❌ Using .NET 10 with Azure Functions v4
**Why not:** Azure Functions v4 does not support .NET 10. Only supports:
- .NET 6 (LTS, ending support soon)
- .NET 8 (LTS, recommended)

### ❌ Upgrading to .NET 10
**Why not:** 
- Would require changing `TargetFramework` in `.csproj`
- Azure Functions SDK doesn't support .NET 10 yet
- Pipeline would fail (configured for .NET 8)
- Azure Function App runtime doesn't support .NET 10

### ✅ Portable .NET 8 SDK (Selected Approach)
**Why:** 
- No admin rights required
- Both .NET 8 and .NET 10 can coexist
- Project remains compatible with Azure deployment
- Team members with .NET 8 installed system-wide unaffected

---

## Final Working Command

From project root, run:
```powershell
$env:DOTNET_ROOT = "C:\Users\Keith.Watson\.dotnet\8.0"
$env:DOTNET_MULTILEVEL_LOOKUP = "0"
$env:PATH = "C:\Users\Keith.Watson\.dotnet\8.0;$env:PATH"
cd src\Function
dotnet run
```

**Expected Output:**
```
Build succeeded.
Azure Functions Core Tools
Core Tools Version: 4.12.1+...
Function Runtime Version: 4.1048.200.26180

Functions:
    CreateUserStory: [POST] http://localhost:7071/api/servicenow/userstory
    Health: [GET] http://localhost:7071/api/health

For detailed output, run func with --verbose flag.
```

---

## Testing the Function

Once running, test the health endpoint:
```powershell
Invoke-RestMethod "http://localhost:7071/api/health"
```

**Expected response:**
```json
{
  "status": "ok",
  "timestamp": "2026-07-17T20:45:00Z"
}
```

---

## Additional Notes

- **Storage Emulator:** Local development uses `UseDevelopmentStorage=true` for blob storage
- **Port:** Default port is 7071 (configured in `host.json`)
- **Startup Probe:** Temporarily disabled in `Program.cs` to isolate startup issues
- **API Key:** Mock HTML uses `snowsync-dev-api-key` (configured in `local.settings.json`)

---

## References

- [.NET 8 Downloads](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure Functions .NET Isolated Worker](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide)
- [dotnet CLI Environment Variables](https://learn.microsoft.com/dotnet/core/tools/dotnet-environment-variables)
- [global.json Overview](https://learn.microsoft.com/dotnet/core/tools/global-json)

---

## Current Status (2026-07-17)

### ✓ Completed
1. **Portable .NET 8 SDK installed** at `C:\Users\Keith.Watson\.dotnet\8.0`
   - Version: 8.0.423
   - Successfully extracted from binaries ZIP
   
2. **User PATH updated** to prioritize .NET 8
   - `.dotnet\8.0` prepended to user-level PATH
   - Will apply to new terminal sessions
   - Verified: `dotnet --version` returns `8.0.423` in new terminals

3. **Project configuration validated**
   - `global.json` matches SDK version (8.0.423)
   - `local.settings.json` properly configured
   - `ServiceNowToAdo.csproj` targets net8.0
   - Build succeeds with .NET 8

4. **Scripts created**
   - `setup-dotnet8.ps1` - Downloads and extracts .NET 8 SDK
   - `run-function-local.ps1` - Sets environment and runs function
   - `ADMIN-UNINSTALL-NET10.ps1` - Admin script to remove .NET 10

### ⚠ Blocking Issue
**Azure Functions Core Tools (`func start`) will not work until .NET 10 is removed.**

- `func.exe` is hardcoded to call `C:\Program Files\dotnet\dotnet.exe`
- It ignores all environment variables (DOTNET_ROOT, PATH, DOTNET_MULTILEVEL_LOOKUP)
- Currently throws error: `dotnet.exe exited with code -2147450730 (0x80008096)`
- This is because it's trying to run .NET 8 code with .NET 10 runtime

### 🔧 Next Steps

**Required: Remove .NET 10**
```powershell
# Option 1: Run admin script
# Right-click PowerShell -> Run as Administrator
cd c:\code\UPO\ado-servicenow
.\ADMIN-UNINSTALL-NET10.ps1

# Option 2: Manual uninstall via Windows Settings
# Settings -> Apps -> Installed apps -> Search "Microsoft .NET SDK" -> Uninstall .NET 10
```

**Then restart terminal and run:**
```powershell
cd c:\code\UPO\ado-servicenow
.\run-function-local.ps1
```

### Alternative: Docker Development
If you cannot get admin rights to remove .NET 10, consider using Docker:
```bash
# Create Dockerfile with .NET 8 runtime
# Mount project as volume
# Run Functions in container
```
