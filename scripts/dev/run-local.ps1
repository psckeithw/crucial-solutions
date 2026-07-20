# Run Azure Function Locally with .NET 10 (Rollforward)
# Alternative to 'func start' when .NET 8.0 is missing

Write-Host "=== Starting Function with .NET 10 Rollforward ===" -ForegroundColor Cyan
Write-Host ""

# Load .env if exists
if (Test-Path .env) {
    Get-Content .env | ForEach-Object {
        if ($_ -match '^\s*([^#][^=]+)=(.*)$') {
            $key = $matches[1].Trim()
            $value = $matches[2].Trim()
            [System.Environment]::SetEnvironmentVariable($key, $value)
            Write-Host "  $key" -ForegroundColor Gray
        }
    }
    Write-Host ""
}

# Enable rollforward for runtime
$env:DOTNET_ROLL_FORWARD = "Major"

# Navigate to function project
Push-Location src/Function

try {
    Write-Host "Building and starting function..." -ForegroundColor Green
    Write-Host ""
    
    # dotnet run builds and starts the function host
    # Uses .NET 10 with rollforward to run .NET 8 project
    dotnet run
    
} finally {
    Pop-Location
}
