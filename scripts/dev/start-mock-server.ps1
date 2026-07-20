# Lightweight local mock server for ServiceNow → ADO demo
# Listens on http://localhost:7071 and handles POST /api/servicenow/userstory

$prefix = 'http://localhost:7071/'

$listener = New-Object System.Net.HttpListener
try {
    $listener.Prefixes.Add($prefix)
    $listener.Start()
} catch {
    Write-Host "Failed to start HttpListener: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "Mock server listening on $prefix" -ForegroundColor Green

# Open demo page (best-effort)
$demoPath = Join-Path $PSScriptRoot 'samples\service-now-mockup.html'
if (Test-Path $demoPath) {
    try { Start-Process $demoPath } catch { }
}

while ($listener.IsListening) {
    try {
        $context = $listener.GetContext()
    } catch {
        break
    }

    Start-Job -ArgumentList $context -ScriptBlock {
        param($context)
        try {
            $req = $context.Request
            $resp = $context.Response

            # CORS headers
            $resp.Headers.Add('Access-Control-Allow-Origin','*')
            $resp.Headers.Add('Access-Control-Allow-Methods','GET, POST, OPTIONS')
            $resp.Headers.Add('Access-Control-Allow-Headers','Content-Type, X-API-Key')

            $path = $req.Url.AbsolutePath.TrimEnd('/')
            Write-Host "[$([DateTime]::UtcNow.ToString('o'))] $($req.HttpMethod) $path" -ForegroundColor Cyan

            if ($req.HttpMethod -eq 'OPTIONS') {
                $resp.StatusCode = 204
                $resp.Close()
                return
            }

            if ($req.HttpMethod -eq 'POST' -and $path -eq '/api/servicenow/userstory') {
                $apiKey = $req.Headers['X-API-Key']
                if (-not $apiKey -or $apiKey -ne 'snowsync-dev-api-key') {
                    $resp.StatusCode = 401
                    $resp.Close()
                    return
                }

                $reader = New-Object System.IO.StreamReader($req.InputStream, $req.ContentEncoding)
                $body = $reader.ReadToEnd()
                $reader.Close()

                try {
                    $payload = $body | ConvertFrom-Json -ErrorAction Stop
                } catch {
                    $resp.StatusCode = 400
                    $buf = [System.Text.Encoding]::UTF8.GetBytes('{"error":"invalid json"}')
                    $resp.ContentType = 'application/json'
                    $resp.ContentLength64 = $buf.Length
                    $resp.OutputStream.Write($buf,0,$buf.Length)
                    $resp.Close()
                    return
                }

                # Simulate creating a work item
                $id = Get-Random -Minimum 100000 -Maximum 999999
                $result = @{ workItemId = $id; workItemUrl = "http://localhost:8080/workitems/$id" }
                $json = $result | ConvertTo-Json

                $resp.StatusCode = 201
                $resp.ContentType = 'application/json'
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
                $resp.ContentLength64 = $bytes.Length
                $resp.OutputStream.Write($bytes,0,$bytes.Length)
                $resp.OutputStream.Flush()
                $resp.Close()
                return
            }

            # Default: 404
            $resp.StatusCode = 404
            $resp.Close()
        } catch {
            Write-Host "Handler error: $($_.Exception.Message)" -ForegroundColor Red
        }
    } | Out-Null
}

Write-Host "Listener stopped." -ForegroundColor Yellow
