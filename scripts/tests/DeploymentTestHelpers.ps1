#Requires -Version 5.1

function Initialize-DeploymentTestSession {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptsRoot
    )

    return [pscustomobject]@{
        ScriptsRoot = $ScriptsRoot
        CommonPath = Join-Path $ScriptsRoot 'deployment\Common.ps1'
        InstallScript = Join-Path $ScriptsRoot 'install-production-package.ps1'
    }
}

function New-DeploymentTestTempDirectory {
    param(
        [string]$Prefix = 'uqeb-deploy-test-'
    )

    $path = Join-Path ([System.IO.Path]::GetTempPath()) ($Prefix + [guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

function New-TestProductionSettingsJson {
    param(
        [string]$ConfigMarker = ''
    )

    $settings = [ordered]@{
        ConnectionStrings = @{ DefaultConnection = 'Server=.;Database=UqebDb;Integrated Security=True;TrustServerCertificate=True' }
        Jwt = @{ Key = '12345678901234567890123456789012' }
    }
    if ($ConfigMarker) {
        $settings['ConfigMarker'] = $ConfigMarker
    }

    return ($settings | ConvertTo-Json -Compress)
}

function New-AuthoritativeProductionConfig {
    param(
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [string]$ConfigMarker = 'authoritative-config'
    )

    $configPath = Join-Path $InstallRoot 'config\appsettings.Production.json'
    Ensure-Directory (Split-Path -Parent $configPath)
    New-TestProductionSettingsJson -ConfigMarker $ConfigMarker |
        Set-Content -LiteralPath $configPath -Encoding UTF8
    return $configPath
}

function Initialize-ApiWebPayload {
    param(
        [Parameter(Mandatory = $true)][string]$ApiPath,
        [Parameter(Mandatory = $true)][string]$WebPath,
        [Parameter(Mandatory = $true)][string]$Marker,
        [string]$ExtraApiFile = ''
    )

    Ensure-Directory $ApiPath
    Ensure-Directory $WebPath
    Set-Content -LiteralPath (Join-Path $ApiPath 'Uqeb.Api.dll') -Value $Marker -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $WebPath 'index.html') -Value "<html>$Marker</html>" -Encoding ASCII
    if ($ExtraApiFile) {
        Set-Content -LiteralPath (Join-Path $ApiPath $ExtraApiFile) -Value 'stale' -Encoding ASCII
    }
}

function New-TestPackage {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$CommonPath,
        [string]$Version = 'test-001'
    )

    $api = Join-Path $Root 'api'
    $web = Join-Path $Root 'web'
    $database = Join-Path $Root 'database'
    $scripts = Join-Path $Root 'scripts'
    $browsers = Join-Path $Root 'browsers'
    $deployment = Join-Path $scripts 'deployment'
    Ensure-Directory $api
    Ensure-Directory $web
    Ensure-Directory $database
    Ensure-Directory $browsers
    Ensure-Directory $deployment

    Set-Content (Join-Path $api 'Uqeb.Api.dll') 'dll' -Encoding ASCII
    Set-Content (Join-Path $api 'playwright.ps1') 'exit 0' -Encoding ASCII
    Set-Content (Join-Path $web 'index.html') '<html></html>' -Encoding ASCII
    Set-Content (Join-Path $database 'migrations-idempotent.sql') 'SELECT 1;' -Encoding ASCII
    'exit 0' | Set-Content (Join-Path $scripts 'apply-migrations.ps1') -Encoding ASCII
    'exit 0' | Set-Content (Join-Path $scripts 'verify-deployment-health.ps1') -Encoding ASCII
    Copy-Item $CommonPath (Join-Path $deployment 'Common.ps1') -Force

    $executableDir = Join-Path $browsers 'chromium-1\chrome-win64'
    Ensure-Directory $executableDir
    $executablePath = Join-Path $executableDir 'chrome.exe'
    Set-Content -LiteralPath $executablePath -Value 'fake-chromium' -Encoding ASCII

    $playwrightScriptPath = Join-Path $api 'playwright.ps1'
    $browserManifest = New-PlaywrightBrowserManifest `
        -BrowsersRoot $browsers `
        -PlaywrightScriptPath $playwrightScriptPath
    $browserManifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $browsers 'playwright-browser-manifest.json') -Encoding UTF8

    $files = [ordered]@{}
    foreach ($relative in @(
        'api/Uqeb.Api.dll',
        'api/playwright.ps1',
        'web/index.html',
        'database/migrations-idempotent.sql',
        'browsers/playwright-browser-manifest.json',
        ('browsers/' + ($browserManifest.executableRelativePath -replace '\\', '/'))
    )) {
        $full = Join-Path $Root ($relative -replace '/', '\')
        $files[($relative -replace '\\', '/')] = Get-FileSha256Hex -Path $full
    }

    $manifest = [ordered]@{
        applicationName = 'Uqeb'
        version = $Version
        buildTimestampUtc = (Get-Date).ToUniversalTime().ToString('o')
        commitSha = 'testsha'
        minimumDatabaseMigration = '20260622062754_AddReferenceDataNormalizedNames'
        playwright = [ordered]@{
            required = $true
            browser = 'chromium'
            browserPayloadIncluded = $true
            browserRoot = 'browsers'
            browserExecutableRelativePath = $browserManifest.executableRelativePath
            browserExecutableSha256 = $browserManifest.browserExecutableSha256
            playwrightScriptSha256 = $browserManifest.playwrightScriptSha256
        }
        files = $files
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Root 'manifest.json') -Encoding UTF8
}

function New-ZipFromDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$ZipPath
    )

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($Source, $ZipPath)
}

function New-ZipWithSha256Sidecar {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$ZipPath
    )

    New-ZipFromDirectory -Source $SourceDirectory -ZipPath $ZipPath
    $hash = Get-FileSha256Hex -Path $ZipPath
    $directory = Split-Path -Parent $ZipPath
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($ZipPath)
    $sidecarPath = Join-Path $directory ($baseName + '.sha256.txt')
    Set-Content -LiteralPath $sidecarPath -Value ("$hash  " + [System.IO.Path]::GetFileName($ZipPath)) -Encoding ASCII
    return [pscustomobject]@{
        ZipPath = $ZipPath
        Sha256Path = $sidecarPath
        Sha256 = $hash
    }
}

function New-InstallTestEnvironment {
    param(
        [Parameter(Mandatory = $true)][string]$CommonPath,
        [scriptblock]$PackageMutator,
        [string]$PackageMigrationContent = 'exit 0',
        [string]$PackageVersion = 'test-001'
    )

    $root = New-DeploymentTestTempDirectory
    $installRoot = Join-Path $root 'install'
    $tools = Join-Path $root 'tools'
    $incoming = Join-Path $installRoot 'incoming'
    Ensure-Directory $incoming
    Ensure-Directory $tools
    Ensure-Directory (Join-Path $tools 'deployment')
    Copy-Item $CommonPath (Join-Path $tools 'deployment\Common.ps1') -Force

    $pkgRoot = Join-Path $root 'package'
    Ensure-Directory $pkgRoot
    New-TestPackage -Root $pkgRoot -CommonPath $CommonPath -Version $PackageVersion
    if ($PackageMutator) {
        & $PackageMutator $pkgRoot
    }
    $PackageMigrationContent | Set-Content (Join-Path $pkgRoot 'scripts\apply-migrations.ps1') -Encoding ASCII

    $zip = Join-Path $incoming 'Uqeb-test.zip'
    New-ZipWithSha256Sidecar -SourceDirectory $pkgRoot -ZipPath $zip | Out-Null

    $configPath = New-AuthoritativeProductionConfig -InstallRoot $installRoot

    return [pscustomobject]@{
        Root = $root
        InstallRoot = $installRoot
        ToolsRoot = $tools
        ZipPath = $zip
        ConfigPath = $configPath
        ApiPath = Join-Path $installRoot 'publish\api'
        WebPath = Join-Path $installRoot 'publish\web'
        PlaywrightBrowsersPath = Join-Path $installRoot 'tools\ms-playwright'
    }
}

function Invoke-TestInstallScript {
    param(
        [Parameter(Mandatory = $true)][string]$InstallScript,
        [Parameter(Mandatory = $true)][psobject]$Environment,
        [hashtable]$AdditionalParameters = @{}
    )

    $params = @{
        PackagePath = $Environment.ZipPath
        InstallRoot = $Environment.InstallRoot
        ToolsRoot = $Environment.ToolsRoot
        ApiPath = $Environment.ApiPath
        WebPath = $Environment.WebPath
        ConfigPath = $Environment.ConfigPath
        PlaywrightBrowsersPath = $Environment.PlaywrightBrowsersPath
    }
    foreach ($key in $AdditionalParameters.Keys) {
        $params[$key] = $AdditionalParameters[$key]
    }

    $output = @()
    & $InstallScript @params 6>&1 | ForEach-Object { $output += $_.ToString() }
    if ($LASTEXITCODE -ne 0) {
        $text = ($output -join [Environment]::NewLine)
        if ([string]::IsNullOrWhiteSpace($text)) {
            throw "install script failed with exit code $LASTEXITCODE"
        }

        throw $text
    }
}

function Ensure-ScheduledTaskCommandStubs {
    if (-not (Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue)) {
        function script:Get-ScheduledTask {
            param([string]$TaskName)
            return [pscustomobject]@{ TaskName = $TaskName }
        }
    }
    if (-not (Get-Command Stop-ScheduledTask -ErrorAction SilentlyContinue)) {
        function script:Stop-ScheduledTask { param([string]$TaskName) }
    }
    if (-not (Get-Command Start-ScheduledTask -ErrorAction SilentlyContinue)) {
        function script:Start-ScheduledTask { param([string]$TaskName) }
    }
    if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
        function script:Get-NetTCPConnection { return @() }
    }
}

function Mock-RobocopyAsCopyOnNonWindows {
    if (Test-IsWindowsPlatform) {
        return
    }

    Mock Invoke-RobocopySafe {
        param(
            [string]$Source,
            [string]$Destination
        )

        Ensure-Directory $Destination
        Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
        }
    }
}

function Register-StandardDeploymentInstallMocks {
    param(
        [switch]$IncludePromotionMock,
        [switch]$IncludeHealthMocks
    )

    Ensure-ScheduledTaskCommandStubs

    Mock Test-IsAdministrator { $true }
    Mock Get-ScheduledTask { return [pscustomobject]@{ TaskName = 'UqebApi' } }
    Mock Stop-ScheduledTask {}
    Mock Start-ScheduledTask {}
    Mock Stop-ApiListenersOnPort {}
    Mock Wait-PortReleased { $true }
    Mock Test-PortListener { $true }
    Mock Stop-Process {}
    Mock Get-NetTCPConnection { return @() }
    Mock Test-RecentLogErrors { return @() }
    Mock Test-RequiredMigrationApplied {}
    Mock Copy-ApplicationPayload {}
    Mock Test-PackageManifestHashes {}
    Mock Invoke-DatabaseBackupRetentionPolicy { return @() }
    Mock Get-SqlConnectionInfoFromSettings {
        return [pscustomobject]@{
            Server = '.'
            Database = 'UqebDb'
            ConnectionString = 'Server=.;Database=UqebDb;Integrated Security=True;TrustServerCertificate=True'
        }
    }
    Mock Invoke-ProductionDatabaseBackup {
        $backupFile = Join-Path $TestDrive ("UqebDb-before-mock.bak")
        Ensure-Directory (Split-Path -Parent $backupFile)
        Set-Content -LiteralPath $backupFile -Value ('x' * 64) -Encoding ASCII
        return [pscustomobject]@{
            Path = $backupFile
            SizeBytes = (Get-Item -LiteralPath $backupFile).Length
            CreatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
            Sha256 = ('a' * 64)
            DatabaseName = 'UqebDb'
        }
    }
    Mock Install-PlaywrightBrowserToProduction { return '' }
    Mock Test-PlaywrightBrowserPayload {
        $executablePath = Join-Path $TestDrive ('chrome-' + [guid]::NewGuid().ToString('N') + '.exe')
        Ensure-Directory (Split-Path -Parent $executablePath)
        Set-Content -LiteralPath $executablePath -Value 'fake-chromium' -Encoding ASCII
        return [pscustomobject]@{ FullPath = $executablePath }
    }
    Mock Test-PlaywrightPackagePreflight {
        return [pscustomobject]@{
            ExecutableRelativePath = 'chromium-1\chrome-win64\chrome.exe'
        }
    }

    if ($IncludePromotionMock) {
        Mock Install-StagedReleaseToProduction {
            param(
                [string]$InstallRoot,
                [string]$Version
            )

            return [pscustomobject]@{
                Paths = [pscustomobject]@{
                    ReleaseRoot = Join-Path $InstallRoot ("releases\" + $Version)
                    CurrentApi = Join-Path $InstallRoot 'current\api'
                    CurrentWeb = Join-Path $InstallRoot 'current\web'
                }
                ConfigTarget = Join-Path $InstallRoot 'current\api\appsettings.Production.json'
                PreviousRelease = ''
            }
        }
    }

    if ($IncludeHealthMocks) {
        Mock Invoke-RestartCurrentReleaseService {}
        Mock Invoke-ReleaseRollbackFromState { return $true }
    }

    Mock-RobocopyAsCopyOnNonWindows
}

function Initialize-PromotionFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$ApiMarker,
        [string]$WebMarker = $ApiMarker,
        [string]$ExtraApiFile = ''
    )

    $staging = Join-Path $Root ("staging-$Version")
    Initialize-ApiWebPayload `
        -ApiPath (Join-Path $staging 'api') `
        -WebPath (Join-Path $staging 'web') `
        -Marker $ApiMarker `
        -ExtraApiFile $ExtraApiFile
    if ($WebMarker -ne $ApiMarker) {
        Set-Content -LiteralPath (Join-Path $staging 'web\index.html') -Value "<html>$WebMarker</html>" -Encoding ASCII
    }

    return $staging
}

function Invoke-TestReleasePromotion {
    param(
        [Parameter(Mandatory = $true)][string]$InstallRoot,
        [Parameter(Mandatory = $true)][string]$StagingPath,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$ConfigPath
    )

    Install-StagedReleaseToProduction `
        -StagingPath $StagingPath `
        -InstallRoot $InstallRoot `
        -Version $Version `
        -ConfigPath $ConfigPath `
        -PackagePath 'Uqeb-test.zip' `
        -PackageCommit 'testsha' | Out-Null
}

function Get-CurrentApiMarker {
    param([Parameter(Mandatory = $true)][string]$InstallRoot)

    return (Get-Content -LiteralPath (Join-Path $InstallRoot 'current\api\Uqeb.Api.dll') -Raw).Trim()
}

function Get-CurrentWebMarker {
    param([Parameter(Mandatory = $true)][string]$InstallRoot)

    $html = Get-Content -LiteralPath (Join-Path $InstallRoot 'current\web\index.html') -Raw
    if ($html -match '<html>(.*)</html>') {
        return $Matches[1]
    }

    return $html.Trim()
}

function Get-InstallScriptOutput {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$ScriptBlock
    )

    $output = @()
    $threw = $false
    try {
        & $ScriptBlock 6>&1 | ForEach-Object { $output += $_.ToString() }
    }
    catch {
        $threw = $true
        $output += $_.Exception.Message
    }

    if ($LASTEXITCODE -ne 0) {
        $threw = $true
    }

    return [pscustomobject]@{
        Output = $output
        Text = ($output -join [Environment]::NewLine)
        Threw = $threw
    }
}
