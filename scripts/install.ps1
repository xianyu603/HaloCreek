#requires -Version 5.1
param(
    [string]$Version,

    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\HaloCreek"),

    [string]$Repository = "xianyu603/HaloCreek",

    [switch]$NoInstallDependencies,

    [switch]$NoShortcut,

    [switch]$Launch,

    [switch]$Uninstall,

    [switch]$RemoveUserData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)

    Write-Host ""
    Write-Host "==> $Message"
}

function Set-NetworkDefaults {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
}

function Invoke-GitHubApi {
    param([string]$Uri)

    $headers = @{
        "Accept"               = "application/vnd.github+json"
        "User-Agent"           = "HaloCreek-Installer"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    Invoke-RestMethod -Uri $Uri -Headers $headers
}

function Get-Release {
    $apiBase = "https://api.github.com/repos/$Repository/releases"

    if ([string]::IsNullOrWhiteSpace($Version)) {
        return Invoke-GitHubApi "$apiBase/latest"
    }

    $tag = if ($Version.StartsWith("v", [StringComparison]::OrdinalIgnoreCase)) {
        $Version
    } else {
        "v$Version"
    }

    Invoke-GitHubApi "$apiBase/tags/$tag"
}

function Select-Asset {
    param(
        [object]$Release,
        [string]$NamePattern,
        [string]$Description
    )

    $assets = @($Release.assets | Where-Object { $_.name -match $NamePattern })

    if ($assets.Count -ne 1) {
        $names = @($Release.assets | ForEach-Object { $_.name }) -join ", "
        throw "Expected exactly one $Description asset matching '$NamePattern'. Found $($assets.Count). Release assets: $names"
    }

    $assets[0]
}

function Invoke-Download {
    param(
        [string]$Uri,
        [string]$Path
    )

    $webClient = New-Object Net.WebClient
    $webClient.Headers.Add("User-Agent", "HaloCreek-Installer")
    try {
        $webClient.DownloadFile($Uri, $Path)
    }
    finally {
        $webClient.Dispose()
    }
}

function Get-ExpectedSha256 {
    param(
        [string]$ChecksumFile,
        [string]$ZipName
    )

    $content = Get-Content -LiteralPath $ChecksumFile -Raw
    $escapedZipName = [Regex]::Escape($ZipName)

    if ($content -notmatch "(?im)^\s*([a-f0-9]{64})\s+\*?$escapedZipName\s*$") {
        throw "Checksum file does not contain a SHA256 entry for $ZipName."
    }

    $matches[1].ToLowerInvariant()
}

function Test-ZipHash {
    param(
        [string]$ZipPath,
        [string]$ExpectedHash
    )

    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipPath).Hash.ToLowerInvariant()

    if ($actualHash -ne $ExpectedHash) {
        throw "SHA256 mismatch. Expected $ExpectedHash, actual $actualHash."
    }
}

function Invoke-WithRetry {
    param(
        [scriptblock]$Action,
        [string]$Description,
        [int]$MaxAttempts = 5,
        [int]$DelaySeconds = 3
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            if ($attempt -gt 1) {
                Write-Host "Retrying $Description ($attempt/$MaxAttempts)."
            }

            & $Action
            return
        }
        catch {
            if ($attempt -ge $MaxAttempts) {
                throw
            }

            Write-Warning "$Description failed: $($_.Exception.Message)"
            Write-Host "Waiting $DelaySeconds seconds before retry."
            Start-Sleep -Seconds $DelaySeconds
        }
    }
}

function New-StartMenuShortcut {
    param([string]$TargetPath)

    $programsDir = [Environment]::GetFolderPath("Programs")
    $shortcutPath = Join-Path $programsDir "HaloCreek.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = Split-Path -Parent $TargetPath
    $shortcut.Description = "HaloCreek"
    $shortcut.Save()

    $shortcutPath
}

function New-DesktopShortcut {
    param([string]$TargetPath)

    $desktopDir = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktopDir "HaloCreek.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = Split-Path -Parent $TargetPath
    $shortcut.Description = "HaloCreek"
    $shortcut.Save()

    $shortcutPath
}

function Remove-StartMenuShortcut {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "HaloCreek.lnk"

    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
}

function Remove-DesktopShortcut {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "HaloCreek.lnk"

    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
}

function Remove-HaloCreek {
    Write-Step "Uninstall HaloCreek"

    Remove-StartMenuShortcut
    Remove-DesktopShortcut

    if (Test-Path -LiteralPath $InstallDir) {
        Remove-Item -LiteralPath $InstallDir -Recurse -Force
        Write-Host "Removed $InstallDir"
    } else {
        Write-Host "Install directory does not exist: $InstallDir"
    }

    if ($RemoveUserData) {
        $userDataDir = Join-Path $env:APPDATA "HaloCreek"
        if (Test-Path -LiteralPath $userDataDir) {
            Remove-Item -LiteralPath $userDataDir -Recurse -Force
            Write-Host "Removed $userDataDir"
        }
    }
}

function Invoke-OfflineInstaller {
    param([string]$InstallerPath)

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $InstallerPath,
        "-InstallDir", $InstallDir
    )

    if ($NoInstallDependencies) {
        $arguments += "-NoInstallDependencies"
    }

    if ($NoShortcut) {
        $arguments += "-NoShortcut"
    }

    if ($Launch) {
        $arguments += "-Launch"
    }

    & powershell.exe @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Offline installer failed with exit code $LASTEXITCODE."
    }
}

function Install-HaloCreek {
    Set-NetworkDefaults

    Write-Step "Resolve release"
    $release = Get-Release
    $releaseVersion = $release.tag_name.TrimStart("v")
    Write-Host "Release: $($release.tag_name)"

    $offlinePackAsset = Select-Asset $release "^HaloCreek-.+-win-x64-offline\.zip$" "offline pack"
    $checksumAsset = Select-Asset $release "^$([Regex]::Escape($offlinePackAsset.name))\.sha256$" "offline pack checksum"

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("HaloCreek-install-" + [Guid]::NewGuid().ToString("N"))
    $extractDir = Join-Path $tempRoot "offline"
    $offlinePackPath = Join-Path $tempRoot $offlinePackAsset.name
    $checksumPath = Join-Path $tempRoot $checksumAsset.name

    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

    try {
        Write-Step "Download offline pack"
        Invoke-Download $offlinePackAsset.browser_download_url $offlinePackPath
        Invoke-Download $checksumAsset.browser_download_url $checksumPath

        Write-Step "Verify offline pack"
        $expectedHash = Get-ExpectedSha256 $checksumPath $offlinePackAsset.name
        Test-ZipHash $offlinePackPath $expectedHash
        Write-Host "SHA256: $expectedHash"

        Write-Step "Extract offline pack"
        Expand-Archive -LiteralPath $offlinePackPath -DestinationPath $extractDir -Force
        $offlineInstallerPath = Join-Path $extractDir "install_offline.ps1"
        if (-not (Test-Path -LiteralPath $offlineInstallerPath)) {
            throw "Offline pack does not contain install_offline.ps1 at the archive root."
        }

        Write-Step "Run offline installer"
        Invoke-OfflineInstaller $offlineInstallerPath

        Write-Host ""
        Write-Host "HaloCreek $releaseVersion installed to $InstallDir"
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($Uninstall) {
    Remove-HaloCreek
    exit 0
}

Install-HaloCreek
