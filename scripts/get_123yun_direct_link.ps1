#requires -Version 7.0
param(
    [string]$ConfigPath = "..\123yun.txt",

    [string]$PagesDownloadsPath = "pages/downloads.json",

    [long]$FolderID = 0,

    [long]$FileID = 0,

    [switch]$NoUpdatePages
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Utf8NoBom = New-Object System.Text.UTF8Encoding $false
[Console]::InputEncoding = $Utf8NoBom
[Console]::OutputEncoding = $Utf8NoBom
$OutputEncoding = $Utf8NoBom
chcp.com 65001 | Out-Null

$ApiBase = "https://open-api.123pan.com"
$HeadersBase = @{
    "Platform" = "open_platform"
}

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

function Invoke-123Api {
    param(
        [ValidateSet("GET", "POST")]
        [string]$Method,

        [string]$Path,

        [string]$AccessToken,

        [object]$Body,

        [hashtable]$Query
    )

    $uriBuilder = [UriBuilder]::new(($ApiBase.TrimEnd("/") + "/" + $Path.TrimStart("/")))
    if ($Query -and $Query.Count -gt 0) {
        $queryParts = foreach ($entry in $Query.GetEnumerator()) {
            if ($null -ne $entry.Value -and "$($entry.Value)" -ne "") {
                "{0}={1}" -f [Uri]::EscapeDataString([string]$entry.Key), [Uri]::EscapeDataString([string]$entry.Value)
            }
        }
        $uriBuilder.Query = ($queryParts -join "&")
    }

    $headers = $HeadersBase.Clone()
    if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
        $headers["Authorization"] = "Bearer $AccessToken"
    }

    $invokeArgs = @{
        Method  = $Method
        Uri     = $uriBuilder.Uri.AbsoluteUri
        Headers = $headers
        NoProxy = $true
    }

    if ($PSBoundParameters.ContainsKey("Body") -and $null -ne $Body) {
        $invokeArgs["ContentType"] = "application/json"
        $invokeArgs["Body"] = ($Body | ConvertTo-Json -Depth 8 -Compress)
    }

    try {
        $response = Invoke-RestMethod @invokeArgs
    }
    catch {
        $details = if ($_.ErrorDetails -and -not [string]::IsNullOrWhiteSpace($_.ErrorDetails.Message)) {
            $_.ErrorDetails.Message
        } else {
            $_.Exception.Message
        }
        throw "123 Cloud API request failed. Method=$Method Path=$Path Error=$details"
    }

    if ($null -eq $response -or $response.code -ne 0) {
        $code = if ($null -eq $response) { "<null>" } else { $response.code }
        $message = if ($null -eq $response) { "<null>" } else { $response.message }
        $traceID = if ($null -eq $response) { "" } else { $response."x-traceID" }
        throw "123 Cloud API returned an error. Method=$Method Path=$Path Code=$code Message=$message TraceID=$traceID"
    }

    $response.data
}

function Get-123AccessToken {
    param(
        [string]$ClientID,
        [string]$ClientSecret
    )

    $data = Invoke-123Api `
        -Method POST `
        -Path "/api/v1/access_token" `
        -Body @{
            clientID     = $ClientID
            clientSecret = $ClientSecret
        }

    if ([string]::IsNullOrWhiteSpace($data.accessToken)) {
        throw "123 Cloud access token response does not contain data.accessToken."
    }

    $data.accessToken
}

function Get-LongProperty {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object -or -not $Object.PSObject.Properties[$Name]) {
        return 0
    }

    if ([string]::IsNullOrWhiteSpace([string]$Object.$Name)) {
        return 0
    }

    [long]$Object.$Name
}

function Set-JsonProperty {
    param(
        [object]$Object,
        [string]$Name,
        [object]$Value
    )

    if ($Object.PSObject.Properties[$Name]) {
        $Object.$Name = $Value
    } else {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$pagesDownloadsFullPath = Resolve-FullPath $PagesDownloadsPath
$downloads = $null
if (Test-Path -LiteralPath $pagesDownloadsFullPath -PathType Leaf) {
    $downloads = Get-Content -LiteralPath $pagesDownloadsFullPath -Raw | ConvertFrom-Json
}

if ($FolderID -le 0) {
    $FolderID = Get-LongProperty $downloads "yun123FolderId"
}
if ($FileID -le 0) {
    $FileID = Get-LongProperty $downloads "yun123PackageFileId"
}
if ($FolderID -le 0) {
    throw "FolderID is required. Pass -FolderID or run the upload script first so pages/downloads.json contains yun123FolderId."
}
if ($FileID -le 0) {
    throw "FileID is required. Pass -FileID or run the upload script first so pages/downloads.json contains yun123PackageFileId."
}

$config = Read-123YunConfig $ConfigPath

Write-Step "Get 123 Cloud access token"
$accessToken = Get-123AccessToken -ClientID $config["client_id"] -ClientSecret $config["client_secret"]

Write-Step "Enable 123 Cloud direct link space"
Invoke-123Api `
    -Method POST `
    -Path "/api/v1/direct-link/enable" `
    -AccessToken $accessToken `
    -Body @{
        fileID = $FolderID
    } | Out-Null
Write-Host "Direct link space folder ID: $FolderID"

Write-Step "Get 123 Cloud direct link URL"
$directLinkData = Invoke-123Api `
    -Method GET `
    -Path "/api/v1/direct-link/url" `
    -AccessToken $accessToken `
    -Query @{
        fileID = $FileID
    }

if ([string]::IsNullOrWhiteSpace($directLinkData.url)) {
    throw "123 Cloud direct link response does not contain data.url."
}

Write-Host "Direct link URL: $($directLinkData.url)"

if (-not $NoUpdatePages) {
    if ($null -eq $downloads) {
        throw "Pages downloads metadata does not exist: $pagesDownloadsFullPath"
    }

    Set-JsonProperty $downloads "offlineDirectUrl" ([string]$directLinkData.url)
    Set-JsonProperty $downloads "offlineDirectGeneratedAtUtc" ([DateTime]::UtcNow.ToString("o"))

    $downloadsJson = $downloads | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($pagesDownloadsFullPath, $downloadsJson + [Environment]::NewLine, $Utf8NoBom)
    Write-Host "Updated Pages downloads metadata: $pagesDownloadsFullPath"
}
