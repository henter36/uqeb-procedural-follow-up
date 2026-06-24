#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$commonPath = Join-Path $PSScriptRoot '..\deployment\Common.ps1'
. $commonPath

if (-not (Get-Command Get-RelativePathFromDirectory -ErrorAction SilentlyContinue)) {
    throw 'Get-RelativePathFromDirectory is not available after dot-sourcing Common.ps1.'
}

$root = Join-Path $env:TEMP ("uqeb-relative-root-" + [Guid]::NewGuid().ToString('N'))
$file = Join-Path $root 'chromium-1\chrome-win64\chrome.exe'

try {
    New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force | Out-Null
    Set-Content -LiteralPath $file -Value 'fake' -Encoding ASCII

    $result = Get-RelativePathFromDirectory `
        -RootDirectory $root `
        -FullPath $file

    if ($result -ne 'chromium-1/chrome-win64/chrome.exe') {
        throw "Unexpected relative path: $result"
    }

    $rootFile = Join-Path $root 'chrome.exe'
    Set-Content -LiteralPath $rootFile -Value 'fake-root' -Encoding ASCII
    $rootResult = Get-RelativePathFromDirectory `
        -RootDirectory $root `
        -FullPath $rootFile
    if ($rootResult -ne 'chrome.exe') {
        throw "Unexpected root file relative path: $rootResult"
    }

    $siblingRoot = Join-Path $env:TEMP ("uqeb-relative-sibling-" + [Guid]::NewGuid().ToString('N'))
    $siblingDir = "$siblingRoot-extra"
    $siblingFile = Join-Path $siblingDir 'chrome.exe'
    try {
        New-Item -ItemType Directory -Path $siblingRoot -Force | Out-Null
        New-Item -ItemType Directory -Path $siblingDir -Force | Out-Null
        Set-Content -LiteralPath $siblingFile -Value 'fake' -Encoding ASCII
        try {
            $null = Get-RelativePathFromDirectory `
                -RootDirectory $siblingRoot `
                -FullPath $siblingFile
            throw 'Expected sibling-prefix bypass to fail.'
        }
        catch {
            if ($_.Exception.Message -notmatch 'يخرج عن الجذر') {
                throw
            }
        }
    }
    finally {
        Remove-Item -LiteralPath $siblingDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $siblingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host 'Windows PowerShell 5.1 compatibility: PASS'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
