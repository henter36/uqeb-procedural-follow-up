param(
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,

    [ValidateRange(1, 120)]
    [int]$TimeoutSec = 20,

    [ValidateRange(1, 20)]
    [int]$RetryCount = 5,

    [ValidateRange(1, 30)]
    [int]$RetryDelaySec = 2
)

$ErrorActionPreference = "Stop"

function Test-ValidCorrelationId {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 64) {
        return $false
    }

    return $Value -match '^[A-Za-z0-9._-]+$'
}

function Invoke-HealthEndpoint {
    param(
        [string]$Path,
        [string]$Label,
        [int[]]$AllowedStatusCodes,
        [string]$ExpectedStatusToken
    )

    $base = $ApiBaseUrl.TrimEnd('/')
    $uri = "$base$Path"
    Write-Output ("Checking $Label => $uri")

  $attempt = 0
  while ($true) {
    $attempt++
    try {
      $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -TimeoutSec $TimeoutSec
      $statusCode = [int]$response.StatusCode

      if ($AllowedStatusCodes -notcontains $statusCode) {
        throw "$Label failed with unexpected status $statusCode."
      }

      if (-not $response.Headers['X-Correlation-ID']) {
        throw "$Label did not return X-Correlation-ID header."
      }

      $correlationValues = @($response.Headers['X-Correlation-ID'])
      if ($correlationValues.Count -ne 1) {
        throw "$Label returned multiple X-Correlation-ID header values."
      }

      if (-not (Test-ValidCorrelationId -Value $correlationValues[0])) {
        throw "$Label returned an invalid X-Correlation-ID header."
      }

      $body = $response.Content
      if ($ExpectedStatusToken -and $body -notmatch $ExpectedStatusToken) {
        throw "$Label response did not contain expected status token '$ExpectedStatusToken'."
      }

      return $response
    }
    catch {
      if ($attempt -ge $RetryCount) {
        throw
      }

      Write-Output ("Retry $attempt/$RetryCount for $Label after failure: $($_.Exception.Message)")
      Start-Sleep -Seconds $RetryDelaySec
    }
  }
}

try {
  Invoke-HealthEndpoint `
    -Path '/health/live' `
    -Label 'liveness' `
    -AllowedStatusCodes @(200) `
    -ExpectedStatusToken '"status":"live"' | Out-Null

  Invoke-HealthEndpoint `
    -Path '/health/ready' `
    -Label 'readiness' `
    -AllowedStatusCodes @(200) `
    -ExpectedStatusToken '"status":"ready"' | Out-Null

  Invoke-HealthEndpoint `
    -Path '/health' `
    -Label 'summary' `
    -AllowedStatusCodes @(200) `
    -ExpectedStatusToken '"status":"healthy"' | Out-Null

  Write-Output 'Health verification passed.'
  exit 0
}
catch {
  Write-Error $_.Exception.Message
  exit 1
}
