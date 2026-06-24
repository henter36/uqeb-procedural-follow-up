BeforeAll {
    $script:CommonPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'deployment\Common.ps1'
    . $script:CommonPath

    function script:New-FakeProcess {
        param(
            [bool]$HasExited,
            [int]$ExitCode = 0
        )

        $process = [pscustomobject]@{
            Id = 424242
            HasExited = $HasExited
            ExitCode = $ExitCode
        }

        $process | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {
            param([int]$TimeoutMs)
            return $true
        }

        return $process
    }
}

Describe 'Invoke-PlaywrightExecutableSmokeTest' {
    It 'propagates Start-Process failures' {
        Mock Start-Process {
            throw [System.InvalidOperationException]::new('launch failed')
        }

        {
            Invoke-PlaywrightExecutableSmokeTest -ExecutablePath 'C:\fake\chrome.exe'
        } | Should -Throw 'launch failed'
    }

    It 'does not call Stop-Process when process already exited' {
        $script:stopCalled = $false
        Mock Start-Process {
            return New-FakeProcess -HasExited $true -ExitCode 0
        }
        Mock Stop-Process {
            $script:stopCalled = $true
        }

        Invoke-PlaywrightExecutableSmokeTest -ExecutablePath 'C:\fake\chrome.exe'

        $script:stopCalled | Should -BeFalse
    }

    It 'stops a running process in finally' {
        $script:stopCalled = $false
        Mock Start-Process {
            return New-FakeProcess -HasExited $false -ExitCode 0
        }
        Mock Stop-Process {
            param($Id, [switch]$Force)
            $script:stopCalled = $true
            $Force | Should -BeTrue
        }

        Invoke-PlaywrightExecutableSmokeTest -ExecutablePath 'C:\fake\chrome.exe'

        $script:stopCalled | Should -BeTrue
    }

    It 'does not throw in finally when Start-Process fails' {
        $script:stopCalled = $false
        Mock Start-Process {
            throw [System.InvalidOperationException]::new('start failed')
        }
        Mock Stop-Process {
            $script:stopCalled = $true
        }

        {
            Invoke-PlaywrightExecutableSmokeTest -ExecutablePath 'C:\fake\chrome.exe'
        } | Should -Throw 'start failed'

        $script:stopCalled | Should -BeFalse
    }
}
