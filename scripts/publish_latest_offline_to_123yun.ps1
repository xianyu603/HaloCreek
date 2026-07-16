#requires -Version 7.0
param(
    [string]$Repository = "xianyu603/HaloCreek",

    [string]$ConfigPath = "..\123yun.txt",

    [long]$ParentFileID = 0,

    [string]$FolderPath = "HaloCreek/releases",

    [int]$ShareExpire = 0,

    [string]$SharePassword = "",

    [string]$GitHubToken = "",

    [string]$WorkDir = ".HaloCreek/latest-offline-release",

    [string]$PagesDownloadsPath = "pages/downloads.json"
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

function Resolve-FullPath {
    param([string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

function Read-123YunConfig {
    param([string]$Path)

    $fullPath = Resolve-FullPath $Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "123 Cloud config file does not exist: $fullPath"
    }

    $lines = @(Get-Content -LiteralPath $fullPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count % 2 -ne 0) {
        throw "123 Cloud config file must use alternating key/value lines: $fullPath"
    }

    $config = @{}
    for ($i = 0; $i -lt $lines.Count; $i += 2) {
        $key = $lines[$i].Trim()
        $value = $lines[$i + 1].Trim()
        if ([string]::IsNullOrWhiteSpace($key)) {
            throw "123 Cloud config contains an empty key: $fullPath"
        }

        $config[$key] = $value
    }

    foreach ($requiredKey in @("client_id", "client_secret")) {
        if (-not $config.ContainsKey($requiredKey) -or [string]::IsNullOrWhiteSpace($config[$requiredKey])) {
            throw "123 Cloud config is missing required key: $requiredKey"
        }
    }

    $config
}

function Get-ExceptionDetails {
    param([Exception]$Exception)

    $messages = New-Object System.Collections.ArrayList
    $current = $Exception
    while ($null -ne $current) {
        if (-not [string]::IsNullOrWhiteSpace($current.Message)) {
            [void]$messages.Add($current.Message)
        }

        $current = $current.InnerException
    }

    $messages -join " InnerException="
}

function New-GitHubProxy {
    if (-not [string]::IsNullOrWhiteSpace($script:GitHubProxy)) {
        return [Net.WebProxy]::new($script:GitHubProxy)
    }

    [Net.WebRequest]::GetSystemWebProxy()
}

function Get-GitHubProxyUri {
    param([string]$Uri)

    if (-not [string]::IsNullOrWhiteSpace($script:GitHubProxy)) {
        return [Uri]$script:GitHubProxy
    }

    $targetUri = [Uri]$Uri
    $systemProxy = [Net.WebRequest]::GetSystemWebProxy()
    if ($null -eq $systemProxy) {
        return $null
    }

    $proxyUri = $systemProxy.GetProxy($targetUri)
    if ($null -eq $proxyUri -or $proxyUri.AbsoluteUri -eq $targetUri.AbsoluteUri) {
        return $null
    }

    $proxyUri
}

function Invoke-GitHubApi {
    param([string]$Uri)

    $headers = @{
        "Accept"               = "application/vnd.github+json"
        "User-Agent"           = "HaloCreek-Offline-123yun-Publisher"
        "X-GitHub-Api-Version" = "2022-11-28"
    }
    if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
        $headers["Authorization"] = "Bearer $GitHubToken"
    }

    $invokeArgs = @{
        Uri     = $Uri
        Headers = $headers
    }
    $proxyUri = Get-GitHubProxyUri $Uri
    if ($null -ne $proxyUri) {
        $invokeArgs["Proxy"] = $proxyUri
        $invokeArgs["ProxyUseDefaultCredentials"] = $true
    }

    try {
        Invoke-RestMethod @invokeArgs
    }
    catch {
        throw "GitHub API request failed. Uri=$Uri Error=$(Get-ExceptionDetails $_.Exception)"
    }
}

function Invoke-Download {
    param(
        [string]$Uri,
        [string]$Path,
        [int]$MaxAttempts = 3
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $response = $null
        $inputStream = $null
        $outputStream = $null

        try {
            $request = [Net.WebRequest]::Create($Uri)
            $request.Method = "GET"
            $request.UserAgent = "HaloCreek-Offline-123yun-Publisher"
            $request.Proxy = New-GitHubProxy
            if ($null -ne $request.Proxy) {
                $request.Proxy.Credentials = [Net.CredentialCache]::DefaultCredentials
            }

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

            Write-Progress -Activity "Download" -Completed
            return
        }
        catch {
            Write-Progress -Activity "Download" -Completed
            $message = Get-ExceptionDetails $_.Exception

            if ($attempt -ge $MaxAttempts) {
                throw "Download failed. Uri=$Uri Path=$Path Attempts=$attempt Error=$message"
            }

            Write-Warning "Download failed. Attempt=$attempt Uri=$Uri Error=$message"
            Start-Sleep -Seconds (5 * $attempt)
        }
        finally {
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

if ($ShareExpire -notin @(0, 1, 7, 30)) {
    throw "ShareExpire must be one of 0, 1, 7, or 30. Actual: $ShareExpire"
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$workDirFullPath = Resolve-FullPath $WorkDir
$pagesDownloadsFullPath = Resolve-FullPath $PagesDownloadsPath
$yun123Config = Read-123YunConfig $ConfigPath
if ([string]::IsNullOrWhiteSpace($GitHubToken) -and $yun123Config.ContainsKey("github_token")) {
    $GitHubToken = $yun123Config["github_token"]
}
$script:GitHubProxy = if ($yun123Config.ContainsKey("github_proxy")) {
    $yun123Config["github_proxy"]
} else {
    ""
}
$scriptRoot = Split-Path -Parent $PSCommandPath
$publishScriptPath = Join-Path $scriptRoot "publish_to_123yun.ps1"
if (-not (Test-Path -LiteralPath $publishScriptPath -PathType Leaf)) {
    throw "123 Cloud publish script does not exist: $publishScriptPath"
}

Write-Step "Resolve latest GitHub release"
$release = Invoke-GitHubApi "https://api.github.com/repos/$Repository/releases/latest"
$releaseTag = [string]$release.tag_name
if ([string]::IsNullOrWhiteSpace($releaseTag)) {
    throw "Latest GitHub release response does not contain tag_name."
}
Write-Host "Release: $releaseTag"

$offlinePackAsset = Select-ReleaseAsset $release "^HaloCreek-.+-win-x64-offline\.zip$" "offline pack"
$checksumAsset = Select-ReleaseAsset $release "^$([Regex]::Escape($offlinePackAsset.name))\.sha256$" "offline pack checksum"

$releaseWorkDir = Join-Path $workDirFullPath $releaseTag
New-Item -ItemType Directory -Force -Path $releaseWorkDir | Out-Null

$offlinePackPath = Join-Path $releaseWorkDir $offlinePackAsset.name
$checksumPath = Join-Path $releaseWorkDir $checksumAsset.name
$uploadMetadataPath = Join-Path $releaseWorkDir "123-cloud-upload.json"

Write-Step "Download offline release assets"
Invoke-Download $offlinePackAsset.browser_download_url $offlinePackPath
Invoke-Download $checksumAsset.browser_download_url $checksumPath

Write-Step "Verify offline pack"
$expectedHash = Get-ExpectedSha256 $checksumPath $offlinePackAsset.name
Test-ZipHash $offlinePackPath $expectedHash
Write-Host "SHA256: $expectedHash"

Write-Step "Upload offline pack to 123 Cloud without proxy"
& $publishScriptPath `
    -PackagePath $offlinePackPath `
    -ChecksumPath $checksumPath `
    -ClientID $yun123Config["client_id"] `
    -ClientSecret $yun123Config["client_secret"] `
    -ReleaseTag $releaseTag `
    -ParentFileID $ParentFileID `
    -FolderPath $FolderPath `
    -ShareExpire $ShareExpire `
    -SharePassword $SharePassword `
    -NoProxy `
    -MetadataOutputPath $uploadMetadataPath

$uploadMetadata = Get-Content -LiteralPath $uploadMetadataPath -Raw | ConvertFrom-Json

Write-Step "Write Pages download metadata"
$pagesDownloadsParent = Split-Path -Parent $pagesDownloadsFullPath
if (-not [string]::IsNullOrWhiteSpace($pagesDownloadsParent)) {
    New-Item -ItemType Directory -Force -Path $pagesDownloadsParent | Out-Null
}

$downloads = [ordered]@{
    schemaVersion           = 1
    releaseTag              = $releaseTag
    offlineZipName          = $offlinePackAsset.name
    offlineChecksumName     = $checksumAsset.name
    offlineSha256           = $expectedHash
    yun123ShareUrl          = [string]$uploadMetadata.shareUrl
    yun123FolderId          = [string]$uploadMetadata.folderId
    yun123PackageFileId     = [string]$uploadMetadata.packageFileId
    yun123ChecksumFileId    = [string]$uploadMetadata.checksumFileId
    githubReleaseUrl        = [string]$release.html_url
    generatedAtUtc          = [DateTime]::UtcNow.ToString("o")
}

$downloadsJson = $downloads | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($pagesDownloadsFullPath, $downloadsJson + [Environment]::NewLine, $Utf8NoBom)

Write-Host "Pages downloads metadata: $pagesDownloadsFullPath"
Write-Host "123 Cloud share URL: $($uploadMetadata.shareUrl)"
