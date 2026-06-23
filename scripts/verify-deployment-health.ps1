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

function Get-HealthUri {
    param(
        [string]$BaseUrl,
        [string]$Path
    )

    return ($BaseUrl.TrimEnd('/') + $Path)
}

function Assert-AllowedStatusCode {
    param(
        [int]$StatusCode,
        [int[]]$AllowedStatusCodes,
        [string]$Label
    )

    if ($AllowedStatusCodes -notcontains $StatusCode) {
        throw "$Label failed with unexpected status $StatusCode."
    }
}

function Get-ValidCorrelationId {
    param(
        $Response,
        [string]$Label
    )

    $header = $Response.Headers['X-Correlation-ID']

    if (-not $header) {
        throw "$Label did not return X-Correlation-ID header."
    }

    $values = @($header)

    if ($values.Count -ne 1) {
        throw "$Label returned multiple X-Correlation-ID header values."
    }

    $value = [string]$values[0]

    if (-not (Test-ValidCorrelationId -Value $value)) {
        throw "$Label returned an invalid X-Correlation-ID header."
    }

    return $value
}

function Assert-SummaryDatabasePass {
    param(
        [string]$Content,
        [string]$Label
    )

    try {
        $payload = $Content | ConvertFrom-Json
    }
    catch {
        throw "$Label returned invalid JSON."
    }

    if (-not $payload.checks) {
        throw "$Label did not return checks object."
    }

    $databaseCheck = [string]$payload.checks.database
    if ($databaseCheck -ne 'pass') {
        throw "$Label reported database check '$databaseCheck' instead of 'pass'."
    }
}

function Assert-ExpectedHealthStatus {
    param(
        [string]$Content,
        [string]$ExpectedStatus,
        [string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedStatus)) {
        return
    }

    try {
        $payload = $Content | ConvertFrom-Json
    }
    catch {
        throw "$Label returned invalid JSON."
    }

    if ($payload.status -ne $ExpectedStatus) {
        throw "$Label returned status '$($payload.status)' instead of '$ExpectedStatus'."
    }
}

function Assert-HealthResponse {
    param(
        $Response,
        [string]$Label,
        [int[]]$AllowedStatusCodes,
        [string]$ExpectedStatus
    )

    Assert-AllowedStatusCode `
        -StatusCode ([int]$Response.StatusCode) `
        -AllowedStatusCodes $AllowedStatusCodes `
        -Label $Label

    Get-ValidCorrelationId `
        -Response $Response `
        -Label $Label | Out-Null

    Assert-ExpectedHealthStatus `
        -Content $Response.Content `
        -ExpectedStatus $ExpectedStatus `
        -Label $Label

    if ($Label -eq 'summary') {
        Assert-SummaryDatabasePass `
            -Content $Response.Content `
            -Label $Label
    }
}

function Invoke-HealthEndpoint {
    param(
        [string]$Path,
        [string]$Label,
        [int[]]$AllowedStatusCodes,
        [string]$ExpectedStatus
    )

    $uri = Get-HealthUri -BaseUrl $ApiBaseUrl -Path $Path
    Write-Output "Checking $Label => $uri"

    for ($attempt = 1; $attempt -le $RetryCount; $attempt++) {
        try {
            $response = Invoke-WebRequest `
                -UseBasicParsing `
                -Uri $uri `
                -TimeoutSec $TimeoutSec

            Assert-HealthResponse `
                -Response $response `
                -Label $Label `
                -AllowedStatusCodes $AllowedStatusCodes `
                -ExpectedStatus $ExpectedStatus

            return $response
        }
        catch {
            if ($attempt -eq $RetryCount) {
                throw "$Label failed after $RetryCount attempts. Details: $($_.Exception.Message)"
            }

            Write-Output "Retry $attempt/$RetryCount for $Label after failure: $($_.Exception.Message)"
            Start-Sleep -Seconds $RetryDelaySec
        }
    }
}

Invoke-HealthEndpoint `
    -Path '/health/live' `
    -Label 'liveness' `
    -AllowedStatusCodes @(200) `
    -ExpectedStatus 'live' | Out-Null

Invoke-HealthEndpoint `
    -Path '/health/ready' `
    -Label 'readiness' `
    -AllowedStatusCodes @(200) `
    -ExpectedStatus 'ready' | Out-Null

Invoke-HealthEndpoint `
    -Path '/health' `
    -Label 'summary' `
    -AllowedStatusCodes @(200) `
    -ExpectedStatus 'healthy' | Out-Null

Write-Output 'Health verification passed.'
exit 0
