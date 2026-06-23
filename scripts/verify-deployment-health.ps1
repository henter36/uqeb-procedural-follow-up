param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"

function Invoke-HealthCheck {
    param([string]$Path, [string]$Label)
    $uri = ($ApiBaseUrl.TrimEnd("/") + $Path)
    Write-Output ("Checking " + $Label + " => " + $uri)
    $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -TimeoutSec $TimeoutSeconds
    if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
        throw ($Label + " failed with status " + $response.StatusCode)
    }
    if (-not $response.Headers["X-Correlation-ID"]) {
        throw ($Label + " did not return X-Correlation-ID header.")
    }
    return $response.Content
}

Invoke-HealthCheck "/health/live" "liveness" | Out-Null
Invoke-HealthCheck "/health/ready" "readiness" | Out-Null
Invoke-HealthCheck "/health" "summary" | Out-Null

Write-Output "Health verification passed."
