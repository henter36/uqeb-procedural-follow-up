#Requires -Version 5.1

BeforeAll {
    $script:HelpersPath = Join-Path $PSScriptRoot 'DeploymentTestHelpers.ps1'
    . $script:HelpersPath
}

Describe 'Invoke-TestInstallScript exit code handling' {
    It 'ignores stale LASTEXITCODE when the install script succeeds' {
        $scriptPath = Join-Path $TestDrive 'install-success.ps1'
        'Write-Output "ok"' | Set-Content -LiteralPath $scriptPath -Encoding ASCII

        $global:LASTEXITCODE = 1
        $env = [pscustomobject]@{
            ZipPath = 'C:\fake\Uqeb-test.zip'
            InstallRoot = 'C:\fake\install'
            ToolsRoot = 'C:\fake\tools'
            ApiPath = 'C:\fake\api'
            WebPath = 'C:\fake\web'
            ConfigPath = 'C:\fake\config\appsettings.Production.json'
            PlaywrightBrowsersPath = 'C:\fake\tools\ms-playwright'
        }

        { Invoke-TestInstallScript -InstallScript $scriptPath -Environment $env } | Should -Not -Throw
    }

    It 'fails when the install script exits with code 1' {
        $scriptPath = Join-Path $TestDrive 'install-fail.ps1'
        @'
Write-Output "failed"
exit 1
'@ | Set-Content -LiteralPath $scriptPath -Encoding ASCII

        $global:LASTEXITCODE = 0
        $env = [pscustomobject]@{
            ZipPath = 'C:\fake\Uqeb-test.zip'
            InstallRoot = 'C:\fake\install'
            ToolsRoot = 'C:\fake\tools'
            ApiPath = 'C:\fake\api'
            WebPath = 'C:\fake\web'
            ConfigPath = 'C:\fake\config\appsettings.Production.json'
            PlaywrightBrowsersPath = 'C:\fake\tools\ms-playwright'
        }

        { Invoke-TestInstallScript -InstallScript $scriptPath -Environment $env } | Should -Throw '*failed*'
    }

    It 'preserves collected output when the install script fails' {
        $scriptPath = Join-Path $TestDrive 'install-output-fail.ps1'
        @'
Write-Output "line-one"
Write-Output "line-two"
exit 1
'@ | Set-Content -LiteralPath $scriptPath -Encoding ASCII

        $env = [pscustomobject]@{
            ZipPath = 'C:\fake\Uqeb-test.zip'
            InstallRoot = 'C:\fake\install'
            ToolsRoot = 'C:\fake\tools'
            ApiPath = 'C:\fake\api'
            WebPath = 'C:\fake\web'
            ConfigPath = 'C:\fake\config\appsettings.Production.json'
            PlaywrightBrowsersPath = 'C:\fake\tools\ms-playwright'
        }

        try {
            Invoke-TestInstallScript -InstallScript $scriptPath -Environment $env | Out-Null
            throw 'expected failure'
        }
        catch {
            $_.Exception.Message | Should -Match 'line-one'
            $_.Exception.Message | Should -Match 'line-two'
        }
    }
}

Describe 'Get-InstallScriptOutput exit code handling' {
    It 'ignores stale LASTEXITCODE when the script block succeeds' {
        $global:LASTEXITCODE = 1

        $result = Get-InstallScriptOutput {
            'success'
        }

        $result.Threw | Should -BeFalse
        $result.ExitCode | Should -Be 0
        $result.Text | Should -Be 'success'
    }

    It 'marks failure when the script block exits with code 1' {
        $scriptPath = Join-Path $TestDrive 'block-exit.ps1'
        'exit 1' | Set-Content -LiteralPath $scriptPath -Encoding ASCII
        $global:LASTEXITCODE = 0

        $result = Get-InstallScriptOutput {
            & $scriptPath
        }

        $result.Threw | Should -BeTrue
        $result.ExitCode | Should -Be 1
    }

    It 'marks failure when the script block throws' {
        $result = Get-InstallScriptOutput {
            throw 'boom'
        }

        $result.Threw | Should -BeTrue
        $result.Text | Should -Match 'boom'
    }
}
