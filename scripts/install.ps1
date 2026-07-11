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

function Remove-StartMenuShortcut {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "HaloCreek.lnk"

    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
}

function Remove-HaloCreek {
    Write-Step "Uninstall HaloCreek"

    Remove-StartMenuShortcut

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

function Install-HaloCreek {
    Set-NetworkDefaults

    Write-Step "Resolve release"
    $release = Get-Release
    $releaseVersion = $release.tag_name.TrimStart("v")
    Write-Host "Release: $($release.tag_name)"

    $zipAsset = Select-Asset $release "^HaloCreek-.+-win-x64\.zip$" "Windows zip"
    $checksumAsset = Select-Asset $release "^$([Regex]::Escape($zipAsset.name))\.sha256$" "checksum"

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("HaloCreek-install-" + [Guid]::NewGuid().ToString("N"))
    $extractDir = Join-Path $tempRoot "extract"
    $zipPath = Join-Path $tempRoot $zipAsset.name
    $checksumPath = Join-Path $tempRoot $checksumAsset.name
    $backupDir = $null

    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

    try {
        Write-Step "Download package"
        Invoke-Download $zipAsset.browser_download_url $zipPath
        Invoke-Download $checksumAsset.browser_download_url $checksumPath

        Write-Step "Verify SHA256"
        $expectedHash = Get-ExpectedSha256 $checksumPath $zipAsset.name
        Test-ZipHash $zipPath $expectedHash
        Write-Host "SHA256: $expectedHash"

        Write-Step "Extract package"
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force
        $exePath = Join-Path $extractDir "HaloCreek.exe"
        if (-not (Test-Path -LiteralPath $exePath)) {
            throw "Package does not contain HaloCreek.exe at the archive root."
        }

        Write-Step "Install files"
        $installParent = Split-Path -Parent $InstallDir
        New-Item -ItemType Directory -Force -Path $installParent | Out-Null

        if (Test-Path -LiteralPath $InstallDir) {
            $backupDir = "$InstallDir.backup.$([DateTime]::UtcNow.ToString("yyyyMMddHHmmss"))"
            Move-Item -LiteralPath $InstallDir -Destination $backupDir
        }

        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
        Copy-Item -Path (Join-Path $extractDir "*") -Destination $InstallDir -Recurse -Force

        if ($backupDir -and (Test-Path -LiteralPath $backupDir)) {
            Remove-Item -LiteralPath $backupDir -Recurse -Force
        }

        $installedExe = Join-Path $InstallDir "HaloCreek.exe"

        if (-not $NoShortcut) {
            Write-Step "Create Start Menu shortcut"
            $shortcutPath = New-StartMenuShortcut $installedExe
            Write-Host "Shortcut: $shortcutPath"
        }

        if (-not $NoInstallDependencies) {
            Write-Step "Install runtime dependencies"
            $dependencyScript = Join-Path $InstallDir "scripts\install_deps.ps1"
            if (-not (Test-Path -LiteralPath $dependencyScript)) {
                throw "Dependency installer is missing: $dependencyScript"
            }

            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $dependencyScript
            if ($LASTEXITCODE -ne 0) {
                throw "Dependency installer failed with exit code $LASTEXITCODE."
            }
        }

        if ($Launch) {
            Write-Step "Launch HaloCreek"
            Start-Process -FilePath $installedExe
        }

        Write-Host ""
        Write-Host "HaloCreek $releaseVersion installed to $InstallDir"
    }
    catch {
        if ($backupDir -and (Test-Path -LiteralPath $backupDir)) {
            if (Test-Path -LiteralPath $InstallDir) {
                Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
            }

            Move-Item -LiteralPath $backupDir -Destination $InstallDir
        }

        throw
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
