#requires -Version 7.0
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$ChecksumPath,

    [Parameter(Mandatory = $true)]
    [string]$ClientID,

    [Parameter(Mandatory = $true)]
    [string]$ClientSecret,

    [string]$ReleaseTag,

    [long]$ParentFileID = 0,

    [string]$FolderPath = "HaloCreek/releases",

    [int]$ShareExpire = 0,

    [string]$SharePassword = "",

    [switch]$NoProxy,

    [string]$MetadataOutputPath
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

function Assert-CloudName {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw "123 Cloud name must not be empty."
    }

    if ($Name.Length -ge 256) {
        throw "123 Cloud name is too long: $Name"
    }

    if ($Name -match '[\\\"/:*?|><]') {
        throw "123 Cloud name contains unsupported characters: $Name"
    }
}

function Invoke-123Api {
    param(
        [ValidateSet("GET", "POST", "PUT", "DELETE")]
        [string]$Method,

        [string]$Path,

        [string]$AccessToken,

        [object]$Body,

        [hashtable]$Query,

        [int[]]$RetryableCodes = @()
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
    }
    if ($NoProxy) {
        $invokeArgs["NoProxy"] = $true
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
        if ($null -ne $response -and [int]$response.code -in $RetryableCodes) {
            return $response
        }

        throw "123 Cloud API returned an error. Method=$Method Path=$Path Code=$code Message=$message TraceID=$traceID"
    }

    $response.data
}

function Invoke-123UploadComplete {
    param(
        [string]$AccessToken,
        [string]$PreuploadID,
        [string]$FileName,
        [int]$MaxAttempts = 120
    )

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $completeResult = Invoke-123Api `
            -Method POST `
            -Path "/upload/v2/file/upload_complete" `
            -AccessToken $AccessToken `
            -Body @{
                preuploadID = $PreuploadID
            } `
            -RetryableCodes @(20103)

        if ($completeResult.PSObject.Properties["code"] -and [int]$completeResult.code -eq 20103) {
            Write-Host "123 Cloud is verifying $FileName. Retry $attempt/$MaxAttempts."
            Start-Sleep -Seconds 1
            continue
        }

        return $completeResult
    }

    throw "123 Cloud upload did not complete because the file is still verifying. File=$FileName Attempts=$MaxAttempts"
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

function Get-123ChildFolderID {
    param(
        [string]$AccessToken,
        [long]$ParentID,
        [string]$Name
    )

    $lastFileID = $null
    do {
        $query = @{
            parentFileId = $ParentID
            limit        = 100
        }
        if ($null -ne $lastFileID) {
            $query["lastFileId"] = $lastFileID
        }

        $data = Invoke-123Api -Method GET -Path "/api/v2/file/list" -AccessToken $AccessToken -Query $query
        foreach ($item in @($data.fileList)) {
            if ($item.filename -eq $Name -and $item.type -eq 1 -and $item.trashed -eq 0) {
                return [long]$item.fileId
            }
        }

        $lastFileID = $data.lastFileId
    } while ($null -ne $lastFileID -and [long]$lastFileID -ne -1)

    $null
}

function New-123Folder {
    param(
        [string]$AccessToken,
        [long]$ParentID,
        [string]$Name
    )

    $data = Invoke-123Api `
        -Method POST `
        -Path "/upload/v1/file/mkdir" `
        -AccessToken $AccessToken `
        -Body @{
            name     = $Name
            parentID = $ParentID
        }

    if ($null -eq $data.dirID -or [long]$data.dirID -le 0) {
        throw "123 Cloud mkdir response does not contain a valid data.dirID. Folder=$Name"
    }

    [long]$data.dirID
}

function Resolve-123FolderPath {
    param(
        [string]$AccessToken,
        [long]$ParentID,
        [string]$Path
    )

    $currentID = $ParentID
    $parts = @($Path -split "[/\\]" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    foreach ($part in $parts) {
        Assert-CloudName $part
        $existingID = Get-123ChildFolderID -AccessToken $AccessToken -ParentID $currentID -Name $part
        if ($null -ne $existingID) {
            $currentID = [long]$existingID
            continue
        }

        $currentID = New-123Folder -AccessToken $AccessToken -ParentID $currentID -Name $part
        Write-Host "Created 123 Cloud folder: $part ($currentID)"
    }

    $currentID
}

function Get-UploadBase {
    param([object]$CreateData)

    foreach ($server in @($CreateData.servers)) {
        if (-not [string]::IsNullOrWhiteSpace($server)) {
            return $server.TrimEnd("/")
        }
    }

    $domains = Invoke-123Api -Method GET -Path "/upload/v2/file/domain" -AccessToken $script:AccessToken
    foreach ($domain in @($domains)) {
        if (-not [string]::IsNullOrWhiteSpace($domain)) {
            return $domain.TrimEnd("/")
        }
    }

    throw "123 Cloud upload server list is empty."
}

function Invoke-123Multipart {
    param(
        [string]$Uri,
        [string]$AccessToken,
        [hashtable]$Form
    )

    $headers = $HeadersBase.Clone()
    $headers["Authorization"] = "Bearer $AccessToken"

    try {
        $invokeArgs = @{
            Method  = "POST"
            Uri     = $Uri
            Headers = $headers
            Form    = $Form
        }
        if ($NoProxy) {
            $invokeArgs["NoProxy"] = $true
        }

        $response = Invoke-RestMethod @invokeArgs
    }
    catch {
        $details = if ($_.ErrorDetails -and -not [string]::IsNullOrWhiteSpace($_.ErrorDetails.Message)) {
            $_.ErrorDetails.Message
        } else {
            $_.Exception.Message
        }
        throw "123 Cloud multipart request failed. Uri=$Uri Error=$details"
    }

    if ($null -eq $response -or $response.code -ne 0) {
        $code = if ($null -eq $response) { "<null>" } else { $response.code }
        $message = if ($null -eq $response) { "<null>" } else { $response.message }
        $traceID = if ($null -eq $response) { "" } else { $response."x-traceID" }
        throw "123 Cloud multipart request returned an error. Uri=$Uri Code=$code Message=$message TraceID=$traceID"
    }

    $response.data
}

function Send-123File {
    param(
        [string]$AccessToken,
        [long]$ParentID,
        [string]$Path
    )

    $item = Get-Item -LiteralPath $Path
    Assert-CloudName $item.Name

    Write-Step "Create upload task for $($item.Name)"
    $fileMD5 = (Get-FileHash -Algorithm MD5 -LiteralPath $item.FullName).Hash.ToLowerInvariant()
    $createData = Invoke-123Api `
        -Method POST `
        -Path "/upload/v2/file/create" `
        -AccessToken $AccessToken `
        -Body @{
            parentFileID = $ParentID
            filename     = $item.Name
            etag         = $fileMD5
            size         = $item.Length
            duplicate    = 1
        }

    if ($createData.reuse) {
        if ($null -eq $createData.fileID -or [long]$createData.fileID -le 0) {
            throw "123 Cloud reuse response does not contain a valid data.fileID. File=$($item.Name)"
        }

        Write-Host "Reused existing 123 Cloud file content: $($item.Name) ($($createData.fileID))"
        return [long]$createData.fileID
    }

    if ([string]::IsNullOrWhiteSpace($createData.preuploadID)) {
        throw "123 Cloud create upload response does not contain data.preuploadID. File=$($item.Name)"
    }

    $sliceSize = [int64]$createData.sliceSize
    if ($sliceSize -le 0) {
        throw "123 Cloud create upload response contains invalid data.sliceSize. File=$($item.Name) SliceSize=$sliceSize"
    }

    $uploadBase = Get-UploadBase $createData
    $sliceUri = "$uploadBase/upload/v2/file/slice"
    if ($sliceSize -gt [int]::MaxValue) {
        throw "123 Cloud slice size is too large for PowerShell upload buffer. File=$($item.Name) SliceSize=$sliceSize"
    }

    $buffer = [byte[]]::new([int]$sliceSize)
    $sliceNo = 1
    $tempSlice = Join-Path ([IO.Path]::GetTempPath()) ("HaloCreek-123yun-slice-" + [Guid]::NewGuid().ToString("N") + ".tmp")

    Write-Step "Upload slices for $($item.Name)"
    $stream = [IO.File]::OpenRead($item.FullName)
    try {
        while ($true) {
            $offset = 0
            while ($offset -lt $sliceSize) {
                $read = $stream.Read($buffer, $offset, [int]($sliceSize - $offset))
                if ($read -le 0) {
                    break
                }
                $offset += $read
            }

            if ($offset -le 0) {
                break
            }

            $sliceStream = [IO.File]::Create($tempSlice)
            try {
                $sliceStream.Write($buffer, 0, [int]$offset)
            }
            finally {
                $sliceStream.Dispose()
            }
            $sliceMD5 = (Get-FileHash -Algorithm MD5 -LiteralPath $tempSlice).Hash.ToLowerInvariant()
            Invoke-123Multipart `
                -Uri $sliceUri `
                -AccessToken $AccessToken `
                -Form @{
                    preuploadID = $createData.preuploadID
                    sliceNo     = "$sliceNo"
                    sliceMD5    = $sliceMD5
                    slice       = Get-Item -LiteralPath $tempSlice
                } | Out-Null

            Write-Host "Uploaded slice $sliceNo"
            $sliceNo++
        }
    }
    finally {
        $stream.Dispose()
        if (Test-Path -LiteralPath $tempSlice) {
            Remove-Item -LiteralPath $tempSlice -Force -ErrorAction SilentlyContinue
        }
    }

    Write-Step "Complete upload for $($item.Name)"
    $completeData = Invoke-123UploadComplete `
        -AccessToken $AccessToken `
        -PreuploadID $createData.preuploadID `
        -FileName $item.Name

    if (-not $completeData.completed) {
        throw "123 Cloud upload did not complete. File=$($item.Name)"
    }

    if ($null -eq $completeData.fileID -or [long]$completeData.fileID -le 0) {
        throw "123 Cloud complete upload response does not contain a valid data.fileID. File=$($item.Name)"
    }

    [long]$completeData.fileID
}

function New-123Share {
    param(
        [string]$AccessToken,
        [string]$ShareName,
        [long[]]$FileIDs,
        [int]$ShareExpire,
        [string]$SharePassword
    )

    Assert-CloudName $ShareName
    $body = @{
        shareName   = $ShareName
        shareExpire = $ShareExpire
        fileIDList  = ($FileIDs -join ",")
    }
    if (-not [string]::IsNullOrWhiteSpace($SharePassword)) {
        $body["sharePwd"] = $SharePassword
    }

    $data = Invoke-123Api -Method POST -Path "/api/v1/share/create" -AccessToken $AccessToken -Body $body
    if ([string]::IsNullOrWhiteSpace($data.shareKey)) {
        throw "123 Cloud share response does not contain data.shareKey."
    }

    "https://www.123pan.com/s/$($data.shareKey)"
}

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "PackagePath does not exist: $PackagePath"
}
if (-not (Test-Path -LiteralPath $ChecksumPath -PathType Leaf)) {
    throw "ChecksumPath does not exist: $ChecksumPath"
}
if ($ShareExpire -notin @(0, 1, 7, 30)) {
    throw "ShareExpire must be one of 0, 1, 7, or 30. Actual: $ShareExpire"
}

$packageItem = Get-Item -LiteralPath $PackagePath
$checksumItem = Get-Item -LiteralPath $ChecksumPath
$releaseFolderPath = $FolderPath
if (-not [string]::IsNullOrWhiteSpace($ReleaseTag)) {
    Assert-CloudName $ReleaseTag
    $releaseFolderPath = ($FolderPath.TrimEnd("/", "\") + "/" + $ReleaseTag)
}

Write-Step "Get 123 Cloud access token"
$script:AccessToken = Get-123AccessToken -ClientID $ClientID -ClientSecret $ClientSecret

Write-Step "Resolve 123 Cloud release folder"
$releaseFolderID = Resolve-123FolderPath -AccessToken $script:AccessToken -ParentID $ParentFileID -Path $releaseFolderPath
Write-Host "Release folder ID: $releaseFolderID"

$packageFileID = Send-123File -AccessToken $script:AccessToken -ParentID $releaseFolderID -Path $packageItem.FullName
$checksumFileID = Send-123File -AccessToken $script:AccessToken -ParentID $releaseFolderID -Path $checksumItem.FullName

Write-Step "Create 123 Cloud share"
$shareName = if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
    "HaloCreek offline package"
} else {
    "HaloCreek $ReleaseTag offline package"
}
$shareUrl = New-123Share `
    -AccessToken $script:AccessToken `
    -ShareName $shareName `
    -FileIDs @($packageFileID, $checksumFileID) `
    -ShareExpire $ShareExpire `
    -SharePassword $SharePassword

Write-Host "123 Cloud share URL: $shareUrl"

$metadata = [ordered]@{
    schemaVersion       = 1
    releaseTag          = $ReleaseTag
    packageName         = $packageItem.Name
    checksumName        = $checksumItem.Name
    shareUrl            = $shareUrl
    folderId            = $releaseFolderID
    packageFileId       = $packageFileID
    checksumFileId      = $checksumFileID
    generatedAtUtc      = [DateTime]::UtcNow.ToString("o")
}

if (-not [string]::IsNullOrWhiteSpace($MetadataOutputPath)) {
    $metadataOutputParent = Split-Path -Parent $MetadataOutputPath
    if (-not [string]::IsNullOrWhiteSpace($metadataOutputParent)) {
        New-Item -ItemType Directory -Force -Path $metadataOutputParent | Out-Null
    }

    $metadataJson = $metadata | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText($MetadataOutputPath, $metadataJson + [Environment]::NewLine, $Utf8NoBom)
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "folder_id=$releaseFolderID" >> $env:GITHUB_OUTPUT
    "package_file_id=$packageFileID" >> $env:GITHUB_OUTPUT
    "checksum_file_id=$checksumFileID" >> $env:GITHUB_OUTPUT
    "share_url=$shareUrl" >> $env:GITHUB_OUTPUT
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    @"
## 123 Cloud

- Folder ID: $releaseFolderID
- Package file ID: $packageFileID
- Checksum file ID: $checksumFileID
- Share URL: $shareUrl
"@ | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
}
