#Requires -Version 5.1

BeforeAll {
    $script:HealthScript = Join-Path $PSScriptRoot 'verify-deployment-health.ps1'
    $script:DeployScript = Join-Path $PSScriptRoot 'deploy-production-v2.ps1'

    function Test-ValidCorrelationId {
        param([string]$Value)

        if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 64) {
            return $false
        }

        return $Value -match '^[A-Za-z0-9._-]+$'
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

    It 'parses deploy-production-v2.ps1' {
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            $script:DeployScript,
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
        Test-ValidCorrelationId 'unicode-مرحبا' | Should -BeFalse
    }
}

Describe 'verify-deployment-health.ps1 HTTP scenarios' {
    BeforeEach {
        Mock Invoke-WebRequest
    }

    It 'PASS: healthy API' {
        Mock Invoke-WebRequest {
            param($Uri)
            $path = ([uri]$Uri).AbsolutePath
            $status = switch ($path) {
                '/health/live' { 200; '"status":"live"' }
                '/health/ready' { 200; '"status":"ready"' }
                '/health' { 200; '"status":"healthy"' }
                default { throw "Unexpected path $path" }
            }

            return [pscustomobject]@{
                StatusCode = $status[0]
                Content = $status[1]
                Headers = @{ 'X-Correlation-ID' = 'abc123' }
            }
        }

        & $script:HealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1
        $LASTEXITCODE | Should -Be 0
    }

    It 'FAIL: live endpoint down' {
        Mock Invoke-WebRequest { throw 'connection refused' }

        { & $script:HealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } | Should -Throw
        $LASTEXITCODE | Should -Be 1
    }

    It 'FAIL: ready returns 503' {
        Mock Invoke-WebRequest {
            param($Uri)
            if (([uri]$Uri).AbsolutePath -eq '/health/live') {
                return [pscustomobject]@{
                    StatusCode = 200
                    Content = '"status":"live"'
                    Headers = @{ 'X-Correlation-ID' = 'live-id' }
                }
            }

            throw [System.Net.WebException]::new('503')
        }

        { & $script:HealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } | Should -Throw
    }

    It 'FAIL: summary degraded' {
        Mock Invoke-WebRequest {
            param($Uri)
            $path = ([uri]$Uri).AbsolutePath
            if ($path -eq '/health') {
                return [pscustomobject]@{
                    StatusCode = 503
                    Content = '"status":"degraded"'
                    Headers = @{ 'X-Correlation-ID' = 'summary-id' }
                }
            }

            return [pscustomobject]@{
                StatusCode = 200
                Content = '"status":"ready"'
                Headers = @{ 'X-Correlation-ID' = 'ok-id' }
            }
        }

        { & $script:HealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } | Should -Throw
    }

    It 'FAIL: missing correlation header' {
        Mock Invoke-WebRequest {
            return [pscustomobject]@{
                StatusCode = 200
                Content = '"status":"live"'
                Headers = @{}
            }
        }

        { & $script:HealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 1 } | Should -Throw
    }

    It 'PASS: API starts after two attempts' {
        $script:callCount = 0
        Mock Invoke-WebRequest {
            $script:callCount++
            if ($script:callCount -lt 2) {
                throw 'starting'
            }

            param($Uri)
            $path = ([uri]$Uri).AbsolutePath
            $content = switch ($path) {
                '/health/live' { '"status":"live"' }
                '/health/ready' { '"status":"ready"' }
                '/health' { '"status":"healthy"' }
            }

            return [pscustomobject]@{
                StatusCode = 200
                Content = $content
                Headers = @{ 'X-Correlation-ID' = 'retry-id' }
            }
        }

        & $script:HealthScript -ApiBaseUrl 'http://localhost:5000' -RetryCount 3 -RetryDelaySec 0
        $LASTEXITCODE | Should -Be 0
        $script:callCount | Should -BeGreaterOrEqual 2
    }

    It 'PASS: trailing slash base URL' {
        Mock Invoke-WebRequest {
            param($Uri)
            ([uri]$Uri).AbsolutePath | Should -Be '/health/live'
            return [pscustomobject]@{
                StatusCode = 200
                Content = '"status":"live"'
                Headers = @{ 'X-Correlation-ID' = 'slash-id' }
            }
        }

        Mock Invoke-WebRequest -ParameterFilter { $Uri -like '*/health/ready*' } {
            return [pscustomobject]@{
                StatusCode = 200
                Content = '"status":"ready"'
                Headers = @{ 'X-Correlation-ID' = 'slash-id' }
            }
        }

        Mock Invoke-WebRequest -ParameterFilter { $Uri -like '*/health' -and $Uri -notlike '*/health/*' } {
            return [pscustomobject]@{
                StatusCode = 200
                Content = '"status":"healthy"'
                Headers = @{ 'X-Correlation-ID' = 'slash-id' }
            }
        }

        & $script:HealthScript -ApiBaseUrl 'http://localhost:5000/' -RetryCount 1
        $LASTEXITCODE | Should -Be 0
    }
}
