#Requires -Version 5.1

BeforeAll {
    $script:HealthScript = Join-Path $PSScriptRoot 'verify-deployment-health.ps1'
    $script:CommonPath = Join-Path $PSScriptRoot 'deployment\Common.ps1'

    function Invoke-TestHealthScript {
        param(
            [string]$ApiBaseUrl,
            [int]$RetryCount = 1,
            [int]$RetryDelaySec = 2
        )

        & $script:HealthScript `
            -ApiBaseUrl $ApiBaseUrl `
            -RetryCount $RetryCount `
            -RetryDelaySec $RetryDelaySec `
            -SkipPlaywrightFilesystemChecks `
            -SkipPlaywrightProcessSmokeTest
    }

    function Test-ValidCorrelationId {
        param([string]$Value)

        if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 64) {
            return $false
        }

        return $Value -match '^[A-Za-z0-9._-]+$'
    }

    function New-HealthResponse {
        param(
            [string]$Status,
            [string]$HealthStatus,
            [hashtable]$Headers = @{ 'X-Correlation-ID' = 'abc123' },
            [string]$DatabaseCheck = 'pass'
        )

        $body = @{ status = $HealthStatus }
        if ($HealthStatus -eq 'healthy') {
            $body.checks = @{
                database = $DatabaseCheck
                playwrightChromium = 'pass'
                reportNumberSequence = 'pass'
                institutionalReporting = 'pass'
            }
        }

        return [pscustomobject]@{
            StatusCode = $Status
            Content = ($body | ConvertTo-Json -Compress)
            Headers = $Headers
        }
    }

    function Mock-HealthyApi {
        Mock Invoke-WebRequest {
            param($Uri)
            $path = ([uri]$Uri).AbsolutePath
            switch ($path) {
                '/health/live' { return (New-HealthResponse -Status 200 -HealthStatus 'live') }
                '/health/ready' { return (New-HealthResponse -Status 200 -HealthStatus 'ready') }
                '/health' { return (New-HealthResponse -Status 200 -HealthStatus 'healthy') }
                default { throw "Unexpected path $path" }
            }
        }
    }
}

Describe 'PowerShell script parse checks' {
    It 'parses verify-deployment-health.ps1' {
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            $script:HealthScript,
            [ref]$null,
            [ref]$errors)

        $errors | Should -BeNullOrEmpty
    }
}

Describe 'Correlation ID validation (deploy script policy)' {
    It 'accepts valid IDs' {
        Test-ValidCorrelationId 'abc-123_X.y' | Should -BeTrue
        Test-ValidCorrelationId ('a' * 64) | Should -BeTrue
    }

    It 'rejects empty, long, spaced, and unicode values' {
        Test-ValidCorrelationId '' | Should -BeFalse
        Test-ValidCorrelationId ('a' * 65) | Should -BeFalse
        Test-ValidCorrelationId 'has space' | Should -BeFalse
        Test-ValidCorrelationId 'unicode-ظ…ط±ط­ط¨ط§' | Should -BeFalse
    }
}

Describe 'verify-deployment-health.ps1 HTTP scenarios' {
    BeforeEach {
        Mock Invoke-WebRequest
        Mock Start-Sleep {}
    }

    It 'PASS: /health/live succeeds on first attempt' {
        Mock-HealthyApi

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } |
            Should -Not -Throw
    }

    It 'PASS: endpoint succeeds after two attempts' {
        Mock Invoke-WebRequest {
            param($Uri)
            $path = ([uri]$Uri).AbsolutePath

            if ($path -eq '/health/live') {
                if (-not $global:liveAttemptCount) {
                    $global:liveAttemptCount = 0
                }

                $global:liveAttemptCount++
                if ($global:liveAttemptCount -lt 2) {
                    throw 'starting'
                }

                return (New-HealthResponse -Status 200 -HealthStatus 'live')
            }

            if ($path -eq '/health/ready') {
                return (New-HealthResponse -Status 200 -HealthStatus 'ready')
            }

            if ($path -eq '/health') {
                return (New-HealthResponse -Status 200 -HealthStatus 'healthy')
            }

            throw "Unexpected path $path"
        }

        $global:liveAttemptCount = 0

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 3 -RetryDelaySec 1 } |
            Should -Not -Throw

        Assert-MockCalled Invoke-WebRequest -ParameterFilter { $Uri -like '*/health/live' } -Times 2 -Exactly
        Assert-MockCalled Invoke-WebRequest -Times 4 -Exactly
    }

    It 'FAIL: endpoint fails through final retry' {
        Mock Invoke-WebRequest { throw 'connection refused' }

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 2 -RetryDelaySec 1 } |
            Should -Throw '*liveness failed after 2 attempts*'
    }

    It 'FAIL: unexpected HTTP status' {
        Mock Invoke-WebRequest {
            return (New-HealthResponse -Status 503 -HealthStatus 'not_ready')
        }

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } |
            Should -Throw '*unexpected status 503*'
    }

    It 'FAIL: invalid JSON body' {
        Mock Invoke-WebRequest {
            return [pscustomobject]@{
                StatusCode = 200
                Content = 'not-json'
                Headers = @{ 'X-Correlation-ID' = 'abc123' }
            }
        }

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } |
            Should -Throw '*invalid JSON*'
    }

    It 'FAIL: incorrect status field' {
        Mock Invoke-WebRequest {
            return (New-HealthResponse -Status 200 -HealthStatus 'degraded')
        }

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } |
            Should -Throw "*instead of 'live'*"
    }

    It 'FAIL: missing correlation header' {
        Mock Invoke-WebRequest {
            return [pscustomobject]@{
                StatusCode = 200
                Content = (@{ status = 'live' } | ConvertTo-Json -Compress)
                Headers = @{}
            }
        }

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } |
            Should -Throw '*did not return X-Correlation-ID header*'
    }

    It 'FAIL: multiple correlation header values' {
        Mock Invoke-WebRequest {
            return [pscustomobject]@{
                StatusCode = 200
                Content = (@{ status = 'live' } | ConvertTo-Json -Compress)
                Headers = @{ 'X-Correlation-ID' = @('first', 'second') }
            }
        }

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } |
            Should -Throw '*multiple X-Correlation-ID header values*'
    }

    It 'FAIL: invalid correlation header value' {
        Mock Invoke-WebRequest {
            return [pscustomobject]@{
                StatusCode = 200
                Content = (@{ status = 'live' } | ConvertTo-Json -Compress)
                Headers = @{ 'X-Correlation-ID' = 'bad header' }
            }
        }

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } |
            Should -Throw '*invalid X-Correlation-ID header*'
    }

    It 'PASS: base URL with trailing slash' {
        Mock Invoke-WebRequest {
            param($Uri)
            ([uri]$Uri).AbsolutePath | Should -Be '/health/live'
            return (New-HealthResponse -Status 200 -HealthStatus 'live')
        }

        Mock Invoke-WebRequest -ParameterFilter { $Uri -like '*/health/ready' } {
            return (New-HealthResponse -Status 200 -HealthStatus 'ready')
        }

        Mock Invoke-WebRequest -ParameterFilter {
            $Uri -match '/health$' -and $Uri -notmatch '/health/'
        } {
            return (New-HealthResponse -Status 200 -HealthStatus 'healthy')
        }

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000/' -RetryCount 1 } |
            Should -Not -Throw
    }

    It 'PASS: base URL without trailing slash' {
        Mock-HealthyApi

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } |
            Should -Not -Throw
    }

    It 'FAIL: ready endpoint returns 503' {
        Mock Invoke-WebRequest {
            param($Uri)
            if (([uri]$Uri).AbsolutePath -eq '/health/live') {
                return (New-HealthResponse -Status 200 -HealthStatus 'live')
            }

            throw [System.Net.WebException]::new('503')
        }

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } |
            Should -Throw
    }

    It 'FAIL: summary degraded' {
        Mock Invoke-WebRequest {
            param($Uri)

            $path = ([uri]$Uri).AbsolutePath

            switch ($path) {
                '/health/live' {
                    return New-HealthResponse `
                        -Status 200 `
                        -HealthStatus 'live'
                }

                '/health/ready' {
                    return New-HealthResponse `
                        -Status 200 `
                        -HealthStatus 'ready'
                }

                '/health' {
                    return New-HealthResponse `
                        -Status 503 `
                        -HealthStatus 'degraded'
                }

                default {
                    throw "Unexpected path $path"
                }
            }
        }

        {
            Invoke-TestHealthScript `
                -ApiBaseUrl 'http://localhost:5000' `
                -RetryCount 1
        } | Should -Throw '*summary failed with unexpected status 503*'

        Assert-MockCalled Invoke-WebRequest `
            -ParameterFilter {
                ([uri]$Uri).AbsolutePath -eq '/health/live'
            } `
            -Times 1 `
            -Exactly

        Assert-MockCalled Invoke-WebRequest `
            -ParameterFilter {
                ([uri]$Uri).AbsolutePath -eq '/health/ready'
            } `
            -Times 1 `
            -Exactly

        Assert-MockCalled Invoke-WebRequest `
            -ParameterFilter {
                ([uri]$Uri).AbsolutePath -eq '/health'
            } `
            -Times 1 `
            -Exactly
    }
    It 'FAIL: summary database check is not pass' {
        Mock Invoke-WebRequest {
            param($Uri)
            $path = ([uri]$Uri).AbsolutePath
            switch ($path) {
                '/health/live' { return (New-HealthResponse -Status 200 -HealthStatus 'live') }
                '/health/ready' { return (New-HealthResponse -Status 200 -HealthStatus 'ready') }
                '/health' {
                    return (New-HealthResponse -Status 200 -HealthStatus 'healthy' -DatabaseCheck 'fail')
                }
                default { throw "Unexpected path $path" }
            }
        }

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } |
            Should -Throw "*database check 'fail' instead of 'pass'*"
    }

    It 'FAIL: summary missing required reporting check' {
        Mock Invoke-WebRequest {
            param($Uri)
            $path = ([uri]$Uri).AbsolutePath
            switch ($path) {
                '/health/live' { return (New-HealthResponse -Status 200 -HealthStatus 'live') }
                '/health/ready' { return (New-HealthResponse -Status 200 -HealthStatus 'ready') }
                '/health' {
                    return [pscustomobject]@{
                        StatusCode = 200
                        Content = (@{
                            status = 'healthy'
                            checks = @{ database = 'pass' }
                        } | ConvertTo-Json -Compress)
                        Headers = @{ 'X-Correlation-ID' = 'abc123' }
                    }
                }
                default { throw "Unexpected path $path" }
            }
        }

        { Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } |
            Should -Throw "*required check 'playwrightChromium'*"
    }
}

Describe 'verify-deployment-health Common.ps1 requirements' {
    It 'fails when Common.ps1 is missing from script and tools roots' {
        $root = Join-Path $env:TEMP ("uqeb-health-common-" + [Guid]::NewGuid().ToString('N'))
        $isolatedScript = Join-Path $root 'verify-deployment-health.ps1'
        $sourceScript = Join-Path $PSScriptRoot 'verify-deployment-health.ps1'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        Copy-Item -LiteralPath $sourceScript -Destination $isolatedScript -Force

        {
            & $isolatedScript `
                -ApiBaseUrl 'http://localhost:5000' `
                -ToolsRoot (Join-Path $root 'missing-tools') `
                -SkipPlaywrightFilesystemChecks `
                -SkipPlaywrightProcessSmokeTest `
                -RetryCount 1
        } | Should -Throw '*Common.ps1*'
    }

    It 'loads Common.ps1 from deployment folder beside script' {
        $root = Join-Path $env:TEMP ("uqeb-health-common-ok-" + [Guid]::NewGuid().ToString('N'))
        $deploymentDir = Join-Path $root 'deployment'
        $isolatedScript = Join-Path $root 'verify-deployment-health.ps1'
        $sourceScript = Join-Path $PSScriptRoot 'verify-deployment-health.ps1'
        New-Item -ItemType Directory -Path $deploymentDir -Force | Out-Null
        Copy-Item -LiteralPath $script:CommonPath -Destination (Join-Path $deploymentDir 'Common.ps1') -Force
        Copy-Item -LiteralPath $sourceScript -Destination $isolatedScript -Force

        Mock Invoke-WebRequest {
            param($Uri)
            $path = ([uri]$Uri).AbsolutePath
            switch ($path) {
                '/health/live' { return (New-HealthResponse -Status 200 -HealthStatus 'live') }
                '/health/ready' { return (New-HealthResponse -Status 200 -HealthStatus 'ready') }
                '/health' {
                    return (New-HealthResponse -Status 200 -HealthStatus 'healthy')
                }
                default { throw "Unexpected path $path" }
            }
        }

        {
            & $isolatedScript `
                -ApiBaseUrl 'http://localhost:5000' `
                -SkipPlaywrightFilesystemChecks `
                -SkipPlaywrightProcessSmokeTest `
                -RetryCount 1
        } | Should -Not -Throw
    }
}

Describe 'deploy script health verifier integration pattern' {
    It 'propagates verifier failure to caller catch block' {
        Mock Invoke-WebRequest { throw 'connection refused' }

        {
            try {
                Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1
            }
            catch {
                throw "Post-deploy health verification failed. Details: $($_.Exception.Message)"
            }
        } | Should -Throw '*Post-deploy health verification failed*'
    }

    It 'allows deploy flow to continue when verifier succeeds' {
        Mock-HealthyApi

        $deploySucceeded = $false
        try {
            Invoke-TestHealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1
            $deploySucceeded = $true
        }
        catch {
            throw "Post-deploy health verification failed. Details: $($_.Exception.Message)"
        }

        $deploySucceeded | Should -BeTrue
    }
}
