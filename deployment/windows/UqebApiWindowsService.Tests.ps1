#Requires -Version 5.1
<#
.SYNOPSIS
  Pester tests for the deployment/windows Windows Service scripts.
  Uses Windows-only cmdlets (Get-Service, sc.exe, Get-NetFirewallRule,
  Get-NetTCPConnection, Get-CimInstance, etc.) and therefore only runs on
  Windows (see .github/workflows/deployment-package.yml, windows-latest).
#>

BeforeAll {
    $script:InstallScript = Join-Path $PSScriptRoot 'install-uqeb-api-service.ps1'
    $script:UpdateScript = Join-Path $PSScriptRoot 'update-uqeb-api-service.ps1'
    $script:RemoveScript = Join-Path $PSScriptRoot 'remove-uqeb-api-service.ps1'
    $script:VerifyScript = Join-Path $PSScriptRoot 'verify-uqeb-api-service.ps1'
    $script:RotateScript = Join-Path $PSScriptRoot 'rotate-uqeb-api-log.ps1'

    function New-FakeServiceObject {
        param([string]$Status = 'Running')
        $obj = [pscustomobject]@{ Name = 'UqebApi'; Status = $Status }
        $obj | Add-Member -MemberType ScriptMethod -Name WaitForStatus -Value { param($TargetStatus, $Timeout) } -Force
        return $obj
    }
}

Describe 'PowerShell script parse checks' {
    It 'parses install-uqeb-api-service.ps1' {
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($script:InstallScript, [ref]$null, [ref]$errors)
        $errors | Should -BeNullOrEmpty
    }

    It 'parses update-uqeb-api-service.ps1' {
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($script:UpdateScript, [ref]$null, [ref]$errors)
        $errors | Should -BeNullOrEmpty
    }

    It 'parses remove-uqeb-api-service.ps1' {
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($script:RemoveScript, [ref]$null, [ref]$errors)
        $errors | Should -BeNullOrEmpty
    }

    It 'parses verify-uqeb-api-service.ps1' {
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($script:VerifyScript, [ref]$null, [ref]$errors)
        $errors | Should -BeNullOrEmpty
    }

    It 'parses rotate-uqeb-api-log.ps1' {
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($script:RotateScript, [ref]$null, [ref]$errors)
        $errors | Should -BeNullOrEmpty
    }
}

Describe 'verify-uqeb-api-service.ps1 exit codes' {
    BeforeEach {
        $script:FakeBinaryPath = Join-Path ([System.IO.Path]::GetTempPath()) 'Uqeb.Api.exe'
        Set-Content -LiteralPath $script:FakeBinaryPath -Value 'stub' -Force

        Mock Get-Service { New-FakeServiceObject -Status 'Running' }
        Mock Get-CimInstance { [pscustomobject]@{ ProcessId = 4242 } }
        Mock Get-Process { [pscustomobject]@{ Path = $script:FakeBinaryPath } }
        Mock Get-NetTCPConnection { [pscustomobject]@{ LocalPort = 5000; State = 'Listen' } }
    }

    AfterEach {
        Remove-Item -LiteralPath $script:FakeBinaryPath -Force -ErrorAction SilentlyContinue
    }

    It 'exits non-zero when health/live does not return 200' {
        Mock Invoke-WebRequest {
            param($Uri)
            $path = ([uri]$Uri).AbsolutePath
            if ($path -eq '/health/live') {
                throw 'Connection refused'
            }
            return [pscustomobject]@{ StatusCode = 200 }
        }

        $missingLogPath = Join-Path ([System.IO.Path]::GetTempPath()) ("uqeb-verify-test-" + [Guid]::NewGuid().ToString('N') + ".log")
        & $script:VerifyScript -ExpectedBinaryPath $script:FakeBinaryPath -ApiPort 5000 -LogPath $missingLogPath *> $null

        $LASTEXITCODE | Should -Be 1
    }

    It 'exits zero when every check passes' {
        Mock Invoke-WebRequest { return [pscustomobject]@{ StatusCode = 200 } }

        $missingLogPath = Join-Path ([System.IO.Path]::GetTempPath()) ("uqeb-verify-test-" + [Guid]::NewGuid().ToString('N') + ".log")
        & $script:VerifyScript -ExpectedBinaryPath $script:FakeBinaryPath -ApiPort 5000 -LogPath $missingLogPath *> $null

        $LASTEXITCODE | Should -Be 0
    }
}

Describe 'install-uqeb-api-service.ps1 idempotency' {
    BeforeAll {
        $script:InstallFakeBinaryPath = Join-Path ([System.IO.Path]::GetTempPath()) 'Uqeb.Api.Install.Test.exe'
        Set-Content -LiteralPath $script:InstallFakeBinaryPath -Value 'stub' -Force
    }

    AfterAll {
        Remove-Item -LiteralPath $script:InstallFakeBinaryPath -Force -ErrorAction SilentlyContinue
    }

    BeforeEach {
        Mock Test-Path { return $true }
        Mock New-ItemProperty { }
        Mock sc.exe { $global:LASTEXITCODE = 0; return 'ok' }
        Mock Get-Service { New-FakeServiceObject -Status 'Running' }
        Mock Stop-Service { }
        Mock Start-Service { }
        Mock Get-NetFirewallRule { return $null }
        Mock New-NetFirewallRule { }
    }

    It 'reconfigures an existing service instead of calling New-Service (idempotent)' {
        & $script:InstallScript `
            -BinaryPath $script:InstallFakeBinaryPath `
            -SkipHealthCheck `
            -SkipLogRotationTask *> $null

        $LASTEXITCODE | Should -Be 0
        Should -Invoke New-Service -Times 0 -Exactly
        # sc.exe config + sc.exe description (reconfiguration) + sc.exe failure (recovery policy, always set).
        Should -Invoke sc.exe -Times 3 -Exactly
        Should -Invoke Stop-Service -Times 1 -Exactly
    }

    It 'can be run twice in a row without failing' {
        & $script:InstallScript -BinaryPath $script:InstallFakeBinaryPath -SkipHealthCheck -SkipLogRotationTask *> $null
        $firstExitCode = $LASTEXITCODE

        & $script:InstallScript -BinaryPath $script:InstallFakeBinaryPath -SkipHealthCheck -SkipLogRotationTask *> $null
        $secondExitCode = $LASTEXITCODE

        $firstExitCode | Should -Be 0
        $secondExitCode | Should -Be 0
    }
}

Describe 'remove-uqeb-api-service.ps1 when service is absent' {
    It 'is a no-op and exits 0 without calling Stop-Service or deleting anything' {
        Mock Get-Service { return $null }
        Mock Stop-Service { }
        Mock sc.exe { $global:LASTEXITCODE = 0; return 'ok' }

        & $script:RemoveScript *> $null

        $LASTEXITCODE | Should -Be 0
        Should -Invoke Stop-Service -Times 0 -Exactly
        Should -Invoke sc.exe -Times 0 -Exactly
    }
}

Describe 'Production logging configuration' {
    It 'does not leave Microsoft.EntityFrameworkCore.Database.Command at Information or more verbose in Production' {
        $settingsPath = Resolve-Path (Join-Path $PSScriptRoot '..\..\backend\Uqeb.Api\appsettings.Production.json')
        $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json

        $verboseLevels = @('Trace', 'Debug', 'Information')
        $commandLogLevel = $settings.Logging.LogLevel.'Microsoft.EntityFrameworkCore.Database.Command'

        $commandLogLevel | Should -Not -BeNullOrEmpty
        $verboseLevels | Should -Not -Contain $commandLogLevel
    }
}
