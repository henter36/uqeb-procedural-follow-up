#Requires -Version 5.1
<#
.SYNOPSIS
  Shared helpers for the deployment/windows Windows Service scripts. Dot-source
  this file; it defines functions only and has no side effects.
#>

# Write-Output (not Write-Host) so callers can redirect/capture this output
# like any other stream, and so PSScriptAnalyzer's PSAvoidUsingWriteHost rule
# doesn't fire. Kept as two thin wrappers purely to give step/info messages a
# consistent, greppable prefix across every script in this folder.
function Write-Step {
    param([string]$Message)
    Write-Output ""
    Write-Output ("==> " + $Message)
}

function Write-Info {
    param([string]$Message)
    Write-Output ("[info] " + $Message)
}

# Resolves the host to use for a local health probe from the address the API
# is actually bound to. Binding to a wildcard address (0.0.0.0, *, +, or no
# address at all) still answers on the loopback interface, so "localhost" is
# correct there. Binding to one specific IP (e.g. 10.0.177.17) means Kestrel
# is NOT listening on loopback, so the probe must target that IP directly -
# hardcoding "localhost" in that case would produce a false-negative health
# check against an otherwise-healthy service.
function Get-UqebHealthHost {
    param([string]$ApiBindAddress)

    if ([string]::IsNullOrWhiteSpace($ApiBindAddress)) {
        return 'localhost'
    }

    if ($ApiBindAddress -in @('0.0.0.0', '*', '+')) {
        return 'localhost'
    }

    return $ApiBindAddress
}
