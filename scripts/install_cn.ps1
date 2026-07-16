#requires -Version 5.1
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\HaloCreek"),

    [string]$DownloadsMetadataUrl = "https://xianyu603.github.io/HaloCreek/downloads.json",

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

function Invoke-Download {
    param(
        [string]$Uri,
        [string]$Path
    )

    $response = $null
    $inputStream = $null
    $outputStream = $null

    try {
        $request = [Net.WebRequest]::Create($Uri)
        $request.Method = "GET"
        $request.UserAgent = "HaloCreek-China-Installer"

        $response = $request.GetResponse()
        $totalBytes = [int64]$response.ContentLength
        $inputStream = $response.GetResponseStream()
        $outputStream = [IO.File]::Create($Path)
        $buffer = [byte[]]::new(1MB)
        $downloadedBytes = [int64]0

        while ($true) {
            $read = $inputStream.Read($buffer, 0, $buffer.Length)
            if ($read -le 0) {
                break
            }

            $outputStream.Write($buffer, 0, $read)
            $downloadedBytes += $read

            $downloadedMb = [Math]::Round($downloadedBytes / 1MB, 1)
            if ($totalBytes -gt 0) {
                $totalMb = [Math]::Round($totalBytes / 1MB, 1)
                $percent = [Math]::Min(100, [int](($downloadedBytes * 100) / $totalBytes))
                Write-Progress -Activity "Download" -Status "$downloadedMb MB / $totalMb MB" -PercentComplete $percent
            } else {
                Write-Progress -Activity "Download" -Status "$downloadedMb MB"
            }
        }
    }
    finally {
        Write-Progress -Activity "Download" -Completed
        if ($null -ne $outputStream) {
            $outputStream.Dispose()
        }
        if ($null -ne $inputStream) {
            $inputStream.Dispose()
        }
        if ($null -ne $response) {
            $response.Dispose()
        }
    }
}

function Invoke-DownloadString {
    param([string]$Uri)

    $webClient = New-Object Net.WebClient
    $webClient.Headers.Add("User-Agent", "HaloCreek-China-Installer")
    try {
        $webClient.DownloadString($Uri)
    }
    finally {
        $webClient.Dispose()
    }
}

function Test-ZipHash {
    param(
        [string]$ZipPath,
        [string]$ExpectedHash
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedHash)) {
        throw "Downloads metadata does not contain offlineSha256."
    }

    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipPath).Hash.ToLowerInvariant()
    $expectedHash = $ExpectedHash.ToLowerInvariant()

    if ($actualHash -ne $expectedHash) {
        throw "SHA256 mismatch. Expected $expectedHash, actual $actualHash."
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

function Get-RequiredTextProperty {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object -or -not $Object.PSObject.Properties[$Name] -or [string]::IsNullOrWhiteSpace([string]$Object.$Name)) {
        throw "Downloads metadata does not contain $Name."
    }

    [string]$Object.$Name
}

function Install-HaloCreek {
    Set-NetworkDefaults

    Write-Step "Resolve China download metadata"
    $downloads = Invoke-DownloadString $DownloadsMetadataUrl | ConvertFrom-Json
    $releaseTag = Get-RequiredTextProperty $downloads "releaseTag"
    $offlineZipName = Get-RequiredTextProperty $downloads "offlineZipName"
    $offlineDirectUrl = Get-RequiredTextProperty $downloads "offlineDirectUrl"
    $offlineSha256 = Get-RequiredTextProperty $downloads "offlineSha256"
    Write-Host "Release: $releaseTag"

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("HaloCreek-install-zh-" + [Guid]::NewGuid().ToString("N"))
    $extractDir = Join-Path $tempRoot "offline"
    $offlinePackPath = Join-Path $tempRoot $offlineZipName

    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

    try {
        Write-Step "Download offline pack"
        Invoke-Download $offlineDirectUrl $offlinePackPath

        Write-Step "Verify offline pack"
        Test-ZipHash $offlinePackPath $offlineSha256
        Write-Host "SHA256: $offlineSha256"

        Write-Step "Extract offline pack"
        Expand-Archive -LiteralPath $offlinePackPath -DestinationPath $extractDir -Force
        $offlineInstallerPath = Join-Path $extractDir "install_offline.ps1"
        if (-not (Test-Path -LiteralPath $offlineInstallerPath)) {
            throw "Offline pack does not contain install_offline.ps1 at the archive root."
        }

        Write-Step "Run offline installer"
        Invoke-OfflineInstaller $offlineInstallerPath

        Write-Host ""
        Write-Host "HaloCreek $($releaseTag.TrimStart("v")) installed to $InstallDir"
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
