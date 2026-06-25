param(
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,

    [ValidateRange(1, 120)]
    [int]$TimeoutSec = 20,

    [ValidateRange(1, 20)]
    [int]$RetryCount = 5,

    [ValidateRange(1, 30)]
    [int]$RetryDelaySec = 2,

    [string]$PlaywrightBrowsersPath = "C:\Uqeb\tools\ms-playwright",
    [string]$ExpectedBrowserExecutableSha256 = "",
    [string]$ToolsRoot = "C:\UqebTools",
    [switch]$SkipPlaywrightProcessSmokeTest,
    [switch]$SkipPlaywrightFilesystemChecks,
    [switch]$SkipInvalidLoginProbe
)

$ErrorActionPreference = "Stop"

$commonPath = Join-Path $PSScriptRoot "deployment\Common.ps1"
if (-not (Test-Path -LiteralPath $commonPath)) {
    $commonPath = Join-Path $ToolsRoot "deployment\Common.ps1"
}

if (-not (Test-Path -LiteralPath $commonPath)) {
    throw "سكربت deployment\Common.ps1 المطلوب غير موجود."
}

. $commonPath

foreach ($requiredCommand in @(
    "Test-PlaywrightBrowserPayload",
    "Get-FileSha256Hex"
)) {
    if (-not (Get-Command $requiredCommand -ErrorAction SilentlyContinue)) {
        throw "الدالة المطلوبة غير متاحة: $requiredCommand"
    }
}

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

function Assert-SummaryChecks {
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

    foreach ($checkName in @('playwrightChromium', 'reportNumberSequence', 'institutionalReporting')) {
        if ($payload.checks.PSObject.Properties.Name -notcontains $checkName) {
            throw "$Label did not return required check '$checkName'."
        }

        $value = [string]$payload.checks.$checkName
        if ($value -notin @('pass', 'not_applicable')) {
            throw "$Label reported $checkName='$value'."
        }
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
        [string]$ExpectedStatus,
        [switch]$ValidateSummaryChecks
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

    if ($ValidateSummaryChecks) {
        Assert-SummaryChecks `
            -Content $Response.Content `
            -Label $Label
    }
}

function Invoke-HealthEndpoint {
    param(
        [string]$Path,
        [string]$Label,
        [int[]]$AllowedStatusCodes,
        [string]$ExpectedStatus,
        [switch]$ValidateSummaryChecks
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
                -ExpectedStatus $ExpectedStatus `
                -ValidateSummaryChecks:$ValidateSummaryChecks

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

function Invoke-InvalidLoginProbe {
    $uri = Get-HealthUri -BaseUrl $ApiBaseUrl -Path '/api/auth/login'
    $body = @{
        username = '__deployment_probe__'
        password = '__deployment_probe__'
    } | ConvertTo-Json

    Write-Output "Checking invalid-login probe => $uri"
    $lastFailure = $null

    for ($attempt = 1; $attempt -le $RetryCount; $attempt++) {
        try {
            $response = Invoke-WebRequest `
                -UseBasicParsing `
                -Uri $uri `
                -Method Post `
                -ContentType 'application/json' `
                -Body $body `
                -TimeoutSec $TimeoutSec

            $statusCode = [int]$response.StatusCode
            if ($statusCode -eq 401) {
                return
            }

            $lastFailure = "unexpected status $statusCode"
        }
        catch {
            if ($_.Exception.PSObject.Properties.Name -contains 'Response' -and $_.Exception.Response) {
                $statusCode = [int]$_.Exception.Response.StatusCode
                if ($statusCode -eq 401) {
                    return
                }

                $lastFailure = "unexpected status $statusCode"
            }
            else {
                $lastFailure = $_.Exception.Message
            }
        }

        if ($attempt -eq $RetryCount) {
            break
        }

        Write-Output "Retry $attempt/$RetryCount for invalid-login probe after failure: $lastFailure"
        Start-Sleep -Seconds $RetryDelaySec
    }

    throw "invalid-login probe failed after $RetryCount attempts at $uri. Last failure: $lastFailure"
}

if (-not $SkipPlaywrightFilesystemChecks) {
    Write-Output "Checking local Playwright browser payload => $PlaywrightBrowsersPath"
    $manifestPath = Join-Path $PlaywrightBrowsersPath "playwright-browser-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Browser metadata missing: $manifestPath"
    }

    Test-PlaywrightBrowserPayload `
        -BrowsersRoot $PlaywrightBrowsersPath `
        -ExpectedExecutableSha256 $ExpectedBrowserExecutableSha256 | Out-Null
}

if (-not $SkipPlaywrightProcessSmokeTest) {
    $readinessScript = Join-Path $PSScriptRoot "verify-playwright-readiness.ps1"
    if (-not (Test-Path -LiteralPath $readinessScript)) {
        throw "verify-playwright-readiness.ps1 is required but missing."
    }

    & $readinessScript `
        -PlaywrightBrowsersPath $PlaywrightBrowsersPath `
        -ExpectedBrowserExecutableSha256 $ExpectedBrowserExecutableSha256 `
        -SkipProcessSmokeTest:$false
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
    -ExpectedStatus 'healthy' `
    -ValidateSummaryChecks | Out-Null

if (-not $SkipInvalidLoginProbe) {
    Invoke-InvalidLoginProbe
}

Write-Output 'Health verification passed.'
exit 0
