param(
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,
    [string]$Username = "admin"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    throw "ApiBaseUrl is required for reporting production smoke test."
}

$Password = if ($env:UQEB_PASSWORD) { $env:UQEB_PASSWORD } else { "" }

function Invoke-UqebApi {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [string]$Token = $null
    )

    $headers = @{}
    if ($Token) { $headers.Authorization = "Bearer $Token" }

    $params = @{
        Uri = "$ApiBaseUrl$Path"
        Method = $Method
        Headers = $headers
    }
    if ($Body) {
        $params.ContentType = "application/json"
        $params.Body = ($Body | ConvertTo-Json -Depth 8 -Compress)
    }

    return Invoke-WebRequest @params
}

Write-Host "Reporting production smoke test starting against $ApiBaseUrl"

$login = Invoke-UqebApi -Method Post -Path "/auth/login" -Body @{
    username = $Username
    password = $Password
}
$token = ($login.Content | ConvertFrom-Json).token
if (-not $token) { throw "Login failed." }

$config = Invoke-UqebApi -Method Get -Path "/institutional-reports/configuration" -Token $token
if ($config.StatusCode -ne 200) { throw "Configuration endpoint failed." }

$ready = Invoke-UqebApi -Method Get -Path "/institutional-reports/readiness" -Token $token
if ($ready.StatusCode -ne 200) { throw "Readiness endpoint failed." }

$preview = Invoke-UqebApi -Method Post -Path "/institutional-reports/preview" -Token $token -Body @{
    reportType = 1
    sectionIds = @(1, 2)
    filters = @{ dateFrom = "2025-01-01"; dateTo = "2025-12-31" }
}
if ($preview.StatusCode -ne 200) { throw "Preview failed." }

Write-Host "Smoke test passed."
