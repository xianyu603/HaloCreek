#requires -Version 5.1
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$Repository = "xianyu603/HaloCreek",

    [string]$PsmuxRepository = "marlocarlo/psmux",

    [string]$PsmuxAssetPattern = "^psmux-v.+-windows-x64\.zip$",

    [string]$CodexRepository = "openai/codex",

    [string]$CodexAssetPattern = "^codex-package-x86_64-pc-windows-msvc\.tar\.gz$",

    [string]$TortoiseGitDownloadPage = "https://tortoisegit.org/download/",

    [string]$TortoiseGitAssetPattern = "^TortoiseGit-.+-64bit\.msi$",

    [string]$HaloCreekZipPath,

    [string]$HaloCreekChecksumPath,

    [string]$HaloCreekVersion,

    [string]$HaloCreekSource,

    [string]$WorkDir,

    [switch]$KeepWorkDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Utf8NoBom = New-Object System.Text.UTF8Encoding $false
[Console]::InputEncoding = $Utf8NoBom
[Console]::OutputEncoding = $Utf8NoBom
$OutputEncoding = $Utf8NoBom
chcp.com 65001 | Out-Null

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
        "User-Agent"           = "HaloCreek-Offline-Pack-Builder"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    try {
        Invoke-RestMethod -Uri $Uri -Headers $headers
    }
    catch {
        throw "GitHub API request failed. Uri=$Uri Error=$($_.Exception.Message)"
    }
}

function Invoke-Download {
    param(
        [string]$Uri,
        [string]$Path
    )

    $webClient = New-Object Net.WebClient
    $webClient.Headers.Add("User-Agent", "HaloCreek-Offline-Pack-Builder")
    try {
        $webClient.DownloadFile($Uri, $Path)
    }
    finally {
        $webClient.Dispose()
    }
}

function Invoke-DownloadString {
    param([string]$Uri)

    $webClient = New-Object Net.WebClient
    $webClient.Headers.Add("User-Agent", "HaloCreek-Offline-Pack-Builder")
    try {
        $webClient.DownloadString($Uri)
    }
    finally {
        $webClient.Dispose()
    }
}

function Select-ReleaseAsset {
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

function Get-LatestGitHubRelease {
    param([string]$Repo)

    Invoke-GitHubApi "https://api.github.com/repos/$Repo/releases/latest"
}

function Resolve-TortoiseGitInstaller {
    param(
        [string]$DownloadPage,
        [string]$AssetPattern
    )

    $html = Invoke-DownloadString $DownloadPage
    $baseUri = [Uri]$DownloadPage
    $linkMatches = [Regex]::Matches(
        $html,
        '(?<href>(?:https?:)?//download\.tortoisegit\.org/tgit/[^"'' <]+/TortoiseGit-[^"'' <]+-64bit\.msi)',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)

    $candidates = foreach ($match in $linkMatches) {
        $href = $match.Groups["href"].Value
        $uri = [Uri]::new($baseUri, $href)
        $fileName = [IO.Path]::GetFileName($uri.AbsolutePath)
        if ($fileName -match $AssetPattern) {
            [pscustomobject]@{
                Name = $fileName
                Uri = $uri.AbsoluteUri
            }
        }
    }

    $candidates = @($candidates | Sort-Object Uri -Unique)
    if ($candidates.Count -ne 1) {
        $names = @($candidates | ForEach-Object { $_.Name }) -join ", "
        throw "Expected exactly one TortoiseGit installer matching '$AssetPattern' from $DownloadPage. Found $($candidates.Count). Candidates: $names"
    }

    $candidates[0]
}

function Add-PackFile {
    param(
        [System.Collections.ArrayList]$Artifacts,
        [string]$Name,
        [string]$Kind,
        [string]$Source,
        [string]$Version,
        [string]$RelativePath,
        [string]$DownloadUrl,
        [string]$LocalPath
    )

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $LocalPath).Hash.ToLowerInvariant()
    $fileInfo = Get-Item -LiteralPath $LocalPath

    [void]$Artifacts.Add([ordered]@{
        name         = $Name
        kind         = $Kind
        source       = $Source
        version      = $Version
        path         = $RelativePath.Replace("\", "/")
        downloadUrl  = $DownloadUrl
        sha256       = $hash
        sizeBytes    = $fileInfo.Length
    })
}

function New-PackDirectory {
    param([string]$Root)

    New-Item -ItemType Directory -Force -Path $Root | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $Root "app") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $Root "dependencies\psmux") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $Root "dependencies\codex") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $Root "dependencies\git") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $Root "dependencies\tortoisegit") | Out-Null
}

function Resolve-FullPath {
    param([string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

function Resolve-OutputArchivePath {
    param([string]$Path)

    $fullPath = Resolve-FullPath $Path

    if (Test-Path -LiteralPath $fullPath -PathType Container) {
        return Join-Path $fullPath "HaloCreekOfflinePack.zip"
    }

    $extension = [IO.Path]::GetExtension($fullPath)
    if ([string]::IsNullOrWhiteSpace($extension)) {
        return Join-Path $fullPath "HaloCreekOfflinePack.zip"
    }

    if ($extension -ne ".zip") {
        throw "OutputPath must be a .zip file path or a directory path. Actual path: $Path"
    }

    $fullPath
}

function Compress-PackRoot {
    param(
        [string]$SourceRoot,
        [string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $destinationParent = Split-Path -Parent $DestinationPath
    $tempArchivePath = Join-Path $destinationParent ("HaloCreekOfflinePack-" + [Guid]::NewGuid().ToString("N") + ".tmp.zip")

    if (Test-Path -LiteralPath $tempArchivePath) {
        Remove-Item -LiteralPath $tempArchivePath -Force
    }

    try {
        [IO.Compression.ZipFile]::CreateFromDirectory($SourceRoot, $tempArchivePath, [IO.Compression.CompressionLevel]::Optimal, $false)

        if (Test-Path -LiteralPath $DestinationPath) {
            if (Test-Path -LiteralPath $DestinationPath -PathType Container) {
                throw "Output archive path points to a directory and will not be removed: $DestinationPath"
            }

            Remove-Item -LiteralPath $DestinationPath -Force
        }

        Move-Item -LiteralPath $tempArchivePath -Destination $DestinationPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $tempArchivePath) {
            Remove-Item -LiteralPath $tempArchivePath -Force -ErrorAction SilentlyContinue
        }
    }
}

Set-NetworkDefaults

$outputFullPath = Resolve-OutputArchivePath $OutputPath
$outputParent = Split-Path -Parent $outputFullPath
if ([string]::IsNullOrWhiteSpace($outputParent)) {
    $outputParent = (Get-Location).Path
    $outputFullPath = Join-Path $outputParent $OutputPath
}

New-Item -ItemType Directory -Force -Path $outputParent | Out-Null

$useLocalHaloCreekPackage = -not [string]::IsNullOrWhiteSpace($HaloCreekZipPath) -or
    -not [string]::IsNullOrWhiteSpace($HaloCreekChecksumPath)

if ($useLocalHaloCreekPackage) {
    if ([string]::IsNullOrWhiteSpace($HaloCreekZipPath) -or [string]::IsNullOrWhiteSpace($HaloCreekChecksumPath)) {
        throw "HaloCreekZipPath and HaloCreekChecksumPath must be provided together."
    }
}

$tempRoot = if ([string]::IsNullOrWhiteSpace($WorkDir)) {
    Join-Path ([IO.Path]::GetTempPath()) ("HaloCreek-offline-pack-" + [Guid]::NewGuid().ToString("N"))
} else {
    Resolve-FullPath $WorkDir
}

$packRoot = Join-Path $tempRoot "HaloCreekOfflinePack"
$artifacts = New-Object System.Collections.ArrayList

if (Test-Path -LiteralPath $packRoot) {
    Remove-Item -LiteralPath $packRoot -Recurse -Force
}

New-PackDirectory $packRoot

try {
    if ($useLocalHaloCreekPackage) {
        Write-Step "Add local HaloCreek package"
        $haloZipSourcePath = Resolve-FullPath $HaloCreekZipPath
        $haloChecksumSourcePath = Resolve-FullPath $HaloCreekChecksumPath
        if (-not (Test-Path -LiteralPath $haloZipSourcePath -PathType Leaf)) {
            throw "HaloCreek zip does not exist: $haloZipSourcePath"
        }

        if (-not (Test-Path -LiteralPath $haloChecksumSourcePath -PathType Leaf)) {
            throw "HaloCreek checksum does not exist: $haloChecksumSourcePath"
        }

        $haloVersion = if ([string]::IsNullOrWhiteSpace($HaloCreekVersion)) { "" } else { $HaloCreekVersion }
        $haloSource = if ([string]::IsNullOrWhiteSpace($HaloCreekSource)) { "" } else { $HaloCreekSource }
        $haloZipName = Split-Path -Leaf $haloZipSourcePath
        $haloChecksumName = Split-Path -Leaf $haloChecksumSourcePath
        $haloZipPath = Join-Path $packRoot ("app\" + $haloZipName)
        $haloChecksumPath = Join-Path $packRoot ("app\" + $haloChecksumName)

        Copy-Item -LiteralPath $haloZipSourcePath -Destination $haloZipPath -Force
        Copy-Item -LiteralPath $haloChecksumSourcePath -Destination $haloChecksumPath -Force

        Add-PackFile $artifacts "HaloCreek" "halocreek-zip" $haloSource $haloVersion ("app/" + $haloZipName) "" $haloZipPath
        Add-PackFile $artifacts "HaloCreek checksum" "halocreek-sha256" $haloSource $haloVersion ("app/" + $haloChecksumName) "" $haloChecksumPath
    } else {
        Write-Step "Download HaloCreek release"
        $haloRelease = Get-LatestGitHubRelease $Repository
        $haloVersion = [string]$haloRelease.tag_name
        $haloZipAsset = Select-ReleaseAsset $haloRelease "^HaloCreek-.+-win-x64\.zip$" "HaloCreek Windows zip"
        $haloChecksumAsset = Select-ReleaseAsset $haloRelease "^$([Regex]::Escape($haloZipAsset.name))\.sha256$" "HaloCreek checksum"
        $haloZipPath = Join-Path $packRoot ("app\" + $haloZipAsset.name)
        $haloChecksumPath = Join-Path $packRoot ("app\" + $haloChecksumAsset.name)
        Invoke-Download $haloZipAsset.browser_download_url $haloZipPath
        Invoke-Download $haloChecksumAsset.browser_download_url $haloChecksumPath
        Add-PackFile $artifacts "HaloCreek" "halocreek-zip" "https://github.com/$Repository/releases/latest" $haloVersion ("app/" + $haloZipAsset.name) $haloZipAsset.browser_download_url $haloZipPath
        Add-PackFile $artifacts "HaloCreek checksum" "halocreek-sha256" "https://github.com/$Repository/releases/latest" $haloVersion ("app/" + $haloChecksumAsset.name) $haloChecksumAsset.browser_download_url $haloChecksumPath
    }

    Write-Step "Download psmux release"
    $psmuxRelease = Get-LatestGitHubRelease $PsmuxRepository
    $psmuxVersion = [string]$psmuxRelease.tag_name
    $psmuxAsset = Select-ReleaseAsset $psmuxRelease $PsmuxAssetPattern "psmux Windows package"
    $psmuxPath = Join-Path $packRoot ("dependencies\psmux\" + $psmuxAsset.name)
    Invoke-Download $psmuxAsset.browser_download_url $psmuxPath
    Add-PackFile $artifacts "psmux" "psmux-package" "https://github.com/$PsmuxRepository/releases/latest" $psmuxVersion ("dependencies/psmux/" + $psmuxAsset.name) $psmuxAsset.browser_download_url $psmuxPath

    Write-Step "Download Codex CLI release"
    $codexRelease = Get-LatestGitHubRelease $CodexRepository
    $codexVersion = [string]$codexRelease.tag_name
    $codexAsset = Select-ReleaseAsset $codexRelease $CodexAssetPattern "Codex CLI Windows package"
    $codexPath = Join-Path $packRoot ("dependencies\codex\" + $codexAsset.name)
    Invoke-Download $codexAsset.browser_download_url $codexPath
    Add-PackFile $artifacts "Codex CLI" "codex-package" "https://github.com/$CodexRepository/releases/latest" $codexVersion ("dependencies/codex/" + $codexAsset.name) $codexAsset.browser_download_url $codexPath

    Write-Step "Download Git for Windows"
    $gitRelease = Get-LatestGitHubRelease "git-for-windows/git"
    $gitVersion = [string]$gitRelease.tag_name
    $gitAsset = Select-ReleaseAsset $gitRelease "^Git-.+-64-bit\.exe$" "Git for Windows installer"
    $gitPath = Join-Path $packRoot ("dependencies\git\" + $gitAsset.name)
    Invoke-Download $gitAsset.browser_download_url $gitPath
    Add-PackFile $artifacts "Git for Windows" "git-installer" "https://github.com/git-for-windows/git/releases/latest" $gitVersion ("dependencies/git/" + $gitAsset.name) $gitAsset.browser_download_url $gitPath

    Write-Step "Download TortoiseGit"
    $tortoiseInstaller = Resolve-TortoiseGitInstaller $TortoiseGitDownloadPage $TortoiseGitAssetPattern
    $tortoiseVersion = if ($tortoiseInstaller.Name -match "^TortoiseGit-(.+)-64bit\.msi$") {
        $matches[1]
    } else {
        ""
    }
    $tortoisePath = Join-Path $packRoot ("dependencies\tortoisegit\" + $tortoiseInstaller.Name)
    Invoke-Download $tortoiseInstaller.Uri $tortoisePath
    Add-PackFile $artifacts "TortoiseGit" "tortoisegit-installer" $TortoiseGitDownloadPage $tortoiseVersion ("dependencies/tortoisegit/" + $tortoiseInstaller.Name) $tortoiseInstaller.Uri $tortoisePath

    Write-Step "Add offline installer scripts"
    $scriptRoot = Split-Path -Parent $PSCommandPath
    Copy-Item -LiteralPath (Join-Path $scriptRoot "install_offline.ps1") -Destination (Join-Path $packRoot "install_offline.ps1") -Force
    Copy-Item -LiteralPath (Join-Path $scriptRoot "install_offline.bat") -Destination (Join-Path $packRoot "install_offline.bat") -Force

    $manifest = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
        packageName = "HaloCreek Offline Pack"
        repository = $Repository
        artifacts = $artifacts
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText((Join-Path $packRoot "offline-pack.json"), $manifestJson, $Utf8NoBom)

    Write-Step "Pack archive"
    Compress-PackRoot $packRoot $outputFullPath
    Write-Host "Offline pack: $outputFullPath"
}
finally {
    if (-not $KeepWorkDir -and [string]::IsNullOrWhiteSpace($WorkDir) -and (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
