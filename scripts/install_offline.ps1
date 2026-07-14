#requires -Version 5.1
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\HaloCreek"),

    [string]$PsmuxInstallDir = (Join-Path $env:LOCALAPPDATA "Programs\psmux"),

    [string]$CodexInstallDir = (Join-Path $env:LOCALAPPDATA "Programs\OpenAI\Codex\offline"),

    [switch]$NoInstallDependencies,

    [switch]$NoShortcut,

    [switch]$Launch,

    [switch]$DependenciesOnly,

    [switch]$Uninstall,

    [switch]$RemoveUserData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Utf8NoBom = New-Object System.Text.UTF8Encoding $false
[Console]::InputEncoding = $Utf8NoBom
[Console]::OutputEncoding = $Utf8NoBom
$OutputEncoding = $Utf8NoBom
chcp.com 65001 | Out-Null

$DiagnosticsDir = Join-Path $PSScriptRoot "diagnostics"
$InstallRunId = [DateTime]::Now.ToString("yyyyMMdd-HHmmss")
$InstallTranscriptPath = Join-Path $DiagnosticsDir "$InstallRunId-install-transcript.log"
$TranscriptStarted = $false

function Write-Step {
    param([string]$Message)

    Write-Host ""
    Write-Host "==> $Message"
}

function Initialize-Diagnostics {
    New-Item -ItemType Directory -Force -Path $DiagnosticsDir | Out-Null
    try {
        Start-Transcript -LiteralPath $InstallTranscriptPath -Force | Out-Null
        $script:TranscriptStarted = $true
    }
    catch {
        Write-Warning "Failed to start installer transcript: $($_.Exception.Message)"
    }

    Write-Host "Diagnostics directory: $DiagnosticsDir"
    Write-Host "Installer transcript: $InstallTranscriptPath"
}

function Stop-Diagnostics {
    if ($script:TranscriptStarted) {
        try {
            Stop-Transcript | Out-Null
        }
        catch {
            Write-Warning "Failed to stop installer transcript: $($_.Exception.Message)"
        }
    }
}

function New-DiagnosticLogPath {
    param(
        [string]$Name,
        [string]$Extension = "log"
    )

    New-Item -ItemType Directory -Force -Path $DiagnosticsDir | Out-Null
    $safeName = ($Name -replace '[^A-Za-z0-9._-]+', "-").Trim("-")
    if ([string]::IsNullOrWhiteSpace($safeName)) {
        $safeName = "diagnostic"
    }

    Join-Path $DiagnosticsDir "$InstallRunId-$safeName.$Extension"
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-DiagnosticsSnapshot {
    param([object]$Manifest)

    $path = New-DiagnosticLogPath "environment" "txt"
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("HaloCreek offline installer diagnostics")
    $lines.Add("RunId: $InstallRunId")
    $lines.Add("Timestamp: $([DateTime]::Now.ToString("o"))")
    $lines.Add("ScriptRoot: $PSScriptRoot")
    $lines.Add("CurrentDirectory: $((Get-Location).Path)")
    $lines.Add("UserName: $env:USERNAME")
    $lines.Add("UserDomain: $env:USERDOMAIN")
    $lines.Add("ComputerName: $env:COMPUTERNAME")
    $lines.Add("IsAdministrator: $(Test-IsAdministrator)")
    $lines.Add("PowerShellVersion: $($PSVersionTable.PSVersion)")
    $lines.Add("ProcessArchitecture: $env:PROCESSOR_ARCHITECTURE")
    $lines.Add("OSArchitecture: $env:PROCESSOR_ARCHITEW6432")
    $lines.Add("ProgramFiles: $env:ProgramFiles")
    $lines.Add("LocalAppData: $env:LOCALAPPDATA")
    $lines.Add("Temp: $env:TEMP")
    $lines.Add("")
    $lines.Add("PATH:")
    $lines.Add($env:Path)
    $lines.Add("")
    $lines.Add("Manifest artifacts:")
    foreach ($artifact in @($Manifest.artifacts)) {
        $lines.Add("kind=$($artifact.kind) name=$($artifact.name) version=$($artifact.version) path=$($artifact.path) sha256=$($artifact.sha256) sizeBytes=$($artifact.sizeBytes)")
    }

    $commands = @("psmux.exe", "codex.exe", "git.exe", "TortoiseGitProc.exe")
    foreach ($command in $commands) {
        $lines.Add("")
        $lines.Add("where.exe ${command}:")
        try {
            $whereOutput = (& where.exe $command 2>&1 | Out-String).Trim()
            if ([string]::IsNullOrWhiteSpace($whereOutput)) {
                $whereOutput = "(no output)"
            }
            $lines.Add($whereOutput)
        }
        catch {
            $lines.Add($_.Exception.Message)
        }
    }

    [IO.File]::WriteAllLines($path, $lines, $Utf8NoBom)
    Write-Host "Environment diagnostics: $path"
}

function Test-CommandAvailable {
    param([string]$Name)

    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Join-ProcessArguments {
    param([string[]]$ArgumentList)

    $arguments = foreach ($argument in @($ArgumentList)) {
        if ($null -eq $argument) {
            continue
        }

        if ($argument -notmatch '[\s"]') {
            $argument
            continue
        }

        '"' + $argument.Replace('"', '\"') + '"'
    }

    $arguments -join " "
}

function Invoke-ExternalCommand {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$Description,
        [int[]]$SuccessExitCodes = @(0),
        [string]$LogPath
    )

    Write-Host $Description
    if (-not [string]::IsNullOrWhiteSpace($LogPath)) {
        Write-Host "Installer log: $LogPath"
    }

    $startProcessArguments = @{
        FilePath = $FilePath
        Wait     = $true
        PassThru = $true
    }
    $argumentLine = Join-ProcessArguments $ArgumentList
    if (-not [string]::IsNullOrWhiteSpace($argumentLine)) {
        $startProcessArguments.ArgumentList = $argumentLine
    }

    $process = Start-Process @startProcessArguments
    $exitCode = $process.ExitCode

    if ($SuccessExitCodes -notcontains $exitCode) {
        $logMessage = if ([string]::IsNullOrWhiteSpace($LogPath)) { "" } else { " See log: $LogPath" }
        throw "$Description failed with exit code $exitCode.$logMessage"
    }

    if ($exitCode -eq 3010) {
        Write-Warning "$Description completed and requested a restart."
    }
}

function Update-ProcessPath {
    $pathSeparator = [IO.Path]::PathSeparator
    $paths = @(
        [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::Machine),
        [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::User),
        [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::Process)
    )

    $seen = @{}
    $mergedPaths = foreach ($path in $paths) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        foreach ($entry in $path.Split($pathSeparator)) {
            if ([string]::IsNullOrWhiteSpace($entry)) {
                continue
            }

            $trimmedEntry = $entry.Trim()
            $normalizedEntry = $trimmedEntry.TrimEnd("\").ToUpperInvariant()
            if ($seen.ContainsKey($normalizedEntry)) {
                continue
            }

            $seen[$normalizedEntry] = $true
            $trimmedEntry
        }
    }

    $env:Path = $mergedPaths -join $pathSeparator
}

function Add-UserPathEntry {
    param([string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    New-Item -ItemType Directory -Force -Path $fullPath | Out-Null

    $pathSeparator = [IO.Path]::PathSeparator
    $userPath = [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::User)
    $entries = @()
    if (-not [string]::IsNullOrWhiteSpace($userPath)) {
        $entries = @($userPath.Split($pathSeparator) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    $normalizedFullPath = $fullPath.TrimEnd("\").ToUpperInvariant()
    $alreadyPresent = $false
    foreach ($entry in $entries) {
        if ($entry.Trim().TrimEnd("\").ToUpperInvariant() -eq $normalizedFullPath) {
            $alreadyPresent = $true
            break
        }
    }

    if (-not $alreadyPresent) {
        $entries += $fullPath
        [Environment]::SetEnvironmentVariable("Path", ($entries -join $pathSeparator), [EnvironmentVariableTarget]::User)
        Write-Host "Added to user PATH: $fullPath"
    } else {
        Write-Host "User PATH already contains: $fullPath"
    }

    Update-ProcessPath
}

function Wait-PathExists {
    param(
        [string]$LiteralPath,
        [string]$DisplayName,
        [int]$TimeoutSeconds = 120,
        [int]$IntervalSeconds = 2
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -le $deadline) {
        if (Test-Path -LiteralPath $LiteralPath) {
            return $true
        }

        Start-Sleep -Seconds $IntervalSeconds
    }

    Write-Warning "$DisplayName was not found before timeout: $LiteralPath"
    return $false
}

function Wait-CommandAvailable {
    param(
        [string]$Name,
        [int]$TimeoutSeconds = 180,
        [int]$IntervalSeconds = 3
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    Write-Host "Waiting for $Name to become available on PATH."

    while ([DateTime]::UtcNow -le $deadline) {
        Update-ProcessPath
        if (Test-CommandAvailable $Name) {
            Write-Host "$Name is available on PATH."
            return $true
        }

        Start-Sleep -Seconds $IntervalSeconds
    }

    return $false
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

function Read-PackManifest {
    $manifestPath = Join-Path $PSScriptRoot "offline-pack.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Offline pack manifest is missing: $manifestPath"
    }

    Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
}

function Get-PackArtifact {
    param(
        [object]$Manifest,
        [string]$Kind
    )

    $matches = @($Manifest.artifacts | Where-Object { $_.kind -eq $Kind })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one artifact of kind '$Kind'. Found $($matches.Count)."
    }

    $matches[0]
}

function Resolve-PackPath {
    param([object]$Artifact)

    Join-Path $PSScriptRoot ([string]$Artifact.path)
}

function Test-PackArtifactHash {
    param([object]$Artifact)

    $path = Resolve-PackPath $Artifact
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Offline pack artifact is missing: $($Artifact.path)"
    }

    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
    $expectedHash = ([string]$Artifact.sha256).ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "SHA256 mismatch for $($Artifact.path). Expected $expectedHash, actual $actualHash."
    }
}

function Test-PackHashes {
    param([object]$Manifest)

    foreach ($artifact in $Manifest.artifacts) {
        Test-PackArtifactHash $artifact
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

function Install-HaloCreekApp {
    param([object]$Manifest)

    $zipArtifact = Get-PackArtifact $Manifest "halocreek-zip"
    $checksumArtifact = Get-PackArtifact $Manifest "halocreek-sha256"
    $zipPath = Resolve-PackPath $zipArtifact
    $checksumPath = Resolve-PackPath $checksumArtifact
    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("HaloCreek-offline-install-" + [Guid]::NewGuid().ToString("N"))
    $extractDir = Join-Path $tempRoot "extract"
    $backupDir = $null

    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

    try {
        Write-Step "Verify HaloCreek package"
        $expectedHash = Get-ExpectedSha256 $checksumPath (Split-Path -Leaf $zipPath)
        Test-ZipHash $zipPath $expectedHash
        Write-Host "SHA256: $expectedHash"

        Write-Step "Extract HaloCreek"
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force
        $exePath = Join-Path $extractDir "HaloCreek.exe"
        if (-not (Test-Path -LiteralPath $exePath)) {
            throw "Package does not contain HaloCreek.exe at the archive root."
        }

        Write-Step "Install HaloCreek files"
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
            Write-Step "Create shortcuts"
            $startMenuShortcutPath = New-StartMenuShortcut $installedExe
            Write-Host "Start Menu shortcut: $startMenuShortcutPath"

            $desktopShortcutPath = New-DesktopShortcut $installedExe
            Write-Host "Desktop shortcut: $desktopShortcutPath"
        }

        if ($Launch) {
            Write-Step "Launch HaloCreek"
            Start-Process -FilePath $installedExe
        }
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

function Install-ZipTool {
    param(
        [string]$ZipPath,
        [string]$TargetDir,
        [string]$ExecutableName,
        [string]$DisplayName
    )

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("HaloCreek-tool-install-" + [Guid]::NewGuid().ToString("N"))
    $extractDir = Join-Path $tempRoot "extract"
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

    try {
        Expand-Archive -LiteralPath $ZipPath -DestinationPath $extractDir -Force
        $exe = Get-ChildItem -LiteralPath $extractDir -Recurse -File -Filter $ExecutableName |
            Select-Object -First 1
        if ($null -eq $exe) {
            throw "$DisplayName package does not contain $ExecutableName."
        }

        $sourceDir = $exe.DirectoryName
        if (Test-Path -LiteralPath $TargetDir) {
            Remove-Item -LiteralPath $TargetDir -Recurse -Force
        }

        New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
        Copy-Item -Path (Join-Path $sourceDir "*") -Destination $TargetDir -Recurse -Force
        Add-UserPathEntry $TargetDir
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Expand-ToolArchive {
    param(
        [string]$ArchivePath,
        [string]$DestinationPath,
        [string]$DisplayName
    )

    $fileName = Split-Path -Leaf $ArchivePath
    $extension = [IO.Path]::GetExtension($ArchivePath).ToLowerInvariant()

    if ($extension -eq ".zip") {
        Expand-Archive -LiteralPath $ArchivePath -DestinationPath $DestinationPath -Force
        return
    }

    if ($fileName.EndsWith(".tar.gz", [StringComparison]::OrdinalIgnoreCase)) {
        Invoke-ExternalCommand "tar.exe" @("-xzf", $ArchivePath, "-C", $DestinationPath) "Extracting $DisplayName archive."
        return
    }

    throw "Unsupported $DisplayName archive extension: $fileName"
}

function Install-Psmux {
    param([object]$Manifest)

    $artifact = Get-PackArtifact $Manifest "psmux-package"
    $path = Resolve-PackPath $artifact
    $extension = [IO.Path]::GetExtension($path).ToLowerInvariant()

    if ($extension -eq ".zip") {
        Install-ZipTool $path $PsmuxInstallDir "psmux.exe" "psmux"
        return
    }

    if ($extension -eq ".msi") {
        $logPath = New-DiagnosticLogPath "psmux-msi"
        Invoke-ExternalCommand -FilePath "msiexec.exe" -ArgumentList @("/i", $path, "/passive", "/norestart", "/L*V", $logPath) -Description "Installing psmux from local MSI." -SuccessExitCodes @(0, 3010) -LogPath $logPath
        Update-ProcessPath
        return
    }

    if ($extension -eq ".exe") {
        Invoke-ExternalCommand $path @("/quiet", "/norestart") "Installing psmux from local installer."
        Update-ProcessPath
        return
    }

    throw "Unsupported psmux package extension: $extension"
}

function Install-VcRedistX64 {
    param([object]$Manifest)

    $artifact = Get-PackArtifact $Manifest "vc-redist-x64-installer"
    $path = Resolve-PackPath $artifact
    $logPath = New-DiagnosticLogPath "vc-redist-x64"
    Invoke-ExternalCommand -FilePath $path -ArgumentList @("/install", "/passive", "/norestart", "/log", $logPath) -Description "Installing Microsoft Visual C++ Redistributable x64 from local installer." -SuccessExitCodes @(0, 3010, 1638) -LogPath $logPath
}

function Install-CodexCli {
    param([object]$Manifest)

    $artifact = Get-PackArtifact $Manifest "codex-package"
    $path = Resolve-PackPath $artifact
    $versionPart = [string]$artifact.version
    if ([string]::IsNullOrWhiteSpace($versionPart)) {
        $versionPart = "offline"
    }

    $targetDir = Join-Path $CodexInstallDir $versionPart
    if (Test-Path -LiteralPath $targetDir) {
        Remove-Item -LiteralPath $targetDir -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    $extension = [IO.Path]::GetExtension($path).ToLowerInvariant()

    if ($extension -eq ".exe") {
        Copy-Item -LiteralPath $path -Destination (Join-Path $targetDir "codex.exe") -Force
        Add-UserPathEntry $targetDir
        return
    }

    $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("HaloCreek-codex-install-" + [Guid]::NewGuid().ToString("N"))
    $extractDir = Join-Path $tempRoot "extract"
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

    try {
        Expand-ToolArchive $path $extractDir "Codex CLI"

        $codexExe = Get-ChildItem -LiteralPath $extractDir -Recurse -File -Filter "codex.exe" |
            Select-Object -First 1
        if ($null -eq $codexExe) {
            $codexExe = Get-ChildItem -LiteralPath $extractDir -Recurse -File -Filter "codex*.exe" |
                Select-Object -First 1
        }

        if ($null -eq $codexExe) {
            throw "Codex CLI package does not contain a Windows executable."
        }

        $packageJson = Get-ChildItem -LiteralPath $extractDir -Recurse -File -Filter "codex-package.json" |
            Select-Object -First 1
        $sourceDir = if ($null -ne $packageJson) {
            $packageJson.DirectoryName
        } else {
            $codexExe.DirectoryName
        }

        Copy-Item -Path (Join-Path $sourceDir "*") -Destination $targetDir -Recurse -Force

        $installedExe = Get-ChildItem -LiteralPath $targetDir -Recurse -File -Filter "codex.exe" |
            Select-Object -First 1
        if ($null -eq $installedExe) {
            $installedExe = Join-Path $targetDir "codex.exe"
            Copy-Item -LiteralPath $codexExe.FullName -Destination $installedExe -Force
        }
    }
    finally {
        if (Test-Path -LiteralPath $tempRoot) {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    $installedCodexExe = Get-ChildItem -LiteralPath $targetDir -Recurse -File -Filter "codex.exe" |
        Select-Object -First 1
    if ($null -eq $installedCodexExe) {
        throw "Codex CLI installation did not produce codex.exe."
    }

    Add-UserPathEntry $installedCodexExe.DirectoryName
}

function Install-GitForWindows {
    param([object]$Manifest)

    $artifact = Get-PackArtifact $Manifest "git-installer"
    $path = Resolve-PackPath $artifact
    $logPath = New-DiagnosticLogPath "git-for-windows"
    $arguments = @(
        "/VERYSILENT",
        "/NORESTART",
        "/NOCANCEL",
        "/SP-",
        "/CLOSEAPPLICATIONS",
        "/RESTARTAPPLICATIONS",
        "/LOG=$logPath"
    )

    Invoke-ExternalCommand -FilePath $path -ArgumentList $arguments -Description "Installing Git for Windows from local installer." -SuccessExitCodes @(0) -LogPath $logPath
    $gitCmdPath = Join-Path $env:ProgramFiles "Git\cmd"
    Wait-PathExists (Join-Path $gitCmdPath "git.exe") "Git for Windows" | Out-Null
    if (Test-Path -LiteralPath (Join-Path $gitCmdPath "git.exe")) {
        Add-UserPathEntry $gitCmdPath
    }

    Update-ProcessPath
}

function Install-TortoiseGit {
    param([object]$Manifest)

    $artifact = Get-PackArtifact $Manifest "tortoisegit-installer"
    $path = Resolve-PackPath $artifact
    $logPath = New-DiagnosticLogPath "tortoisegit-msi"
    Invoke-ExternalCommand -FilePath "msiexec.exe" -ArgumentList @("/i", $path, "/passive", "/norestart", "/L*V", $logPath) -Description "Installing TortoiseGit from local MSI." -SuccessExitCodes @(0, 3010) -LogPath $logPath
    $tortoiseGitPath = Join-Path $env:ProgramFiles "TortoiseGit\bin"
    Wait-PathExists (Join-Path $tortoiseGitPath "TortoiseGitProc.exe") "TortoiseGit" | Out-Null
    if (Test-Path -LiteralPath (Join-Path $tortoiseGitPath "TortoiseGitProc.exe")) {
        Add-UserPathEntry $tortoiseGitPath
    }

    Update-ProcessPath
}

function Test-Dependency {
    param(
        [string]$Name,
        [scriptblock]$Probe,
        [scriptblock]$Assert
    )

    try {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $output = (& $Probe 2>&1 | Out-String).Trim()
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        $passed = [bool](& $Assert $output)
    }
    catch {
        $output = $_.Exception.Message
        $passed = $false
    }

    $status = if ($passed) { "PASS" } else { "FAIL" }
    Write-Host "[$status] $Name - $output"

    return $passed
}

function Install-OfflineDependency {
    param(
        [string]$DisplayName,
        [string]$CommandName,
        [scriptblock]$Install
    )

    Update-ProcessPath
    if (Test-CommandAvailable $CommandName) {
        Write-Host "$DisplayName is already available on PATH: $CommandName"
        return
    }

    & $Install

    if (-not (Wait-CommandAvailable $CommandName)) {
        throw "$DisplayName is still unavailable on PATH after installation: $CommandName"
    }
}

function Install-OfflineDependencies {
    param([object]$Manifest)

    Write-Step "Install psmux"
    Install-OfflineDependency "psmux" "psmux.exe" { Install-Psmux $Manifest }

    Write-Step "Install Codex CLI"
    Install-OfflineDependency "Codex CLI" "codex.exe" { Install-CodexCli $Manifest }

    Write-Step "Install Git for Windows"
    Install-OfflineDependency "Git for Windows" "git.exe" { Install-GitForWindows $Manifest }

    Write-Step "Install Microsoft Visual C++ Redistributable x64"
    Install-VcRedistX64 $Manifest

    Write-Step "Install TortoiseGit"
    Install-OfflineDependency "TortoiseGit" "TortoiseGitProc.exe" { Install-TortoiseGit $Manifest }

    Write-Step "Verify dependencies"
    Update-ProcessPath
    $results = @(
        (Test-Dependency "psmux" { psmux.exe --version } { param($Output) $Output -match "\d+\.\d+" }),
        (Test-Dependency "Codex CLI" { cmd.exe /d /c "codex.exe --version 2>&1" } { param($Output) $Output -match "codex-cli\s+\d+\." }),
        (Test-Dependency "Git for Windows" { git.exe --version } { param($Output) $Output -match "^git version \d+\." }),
        (Test-Dependency "TortoiseGitProc on PATH" { where.exe TortoiseGitProc.exe } { param($Output) $Output -match "TortoiseGitProc\.exe" })
    )

    if ($results -contains $false) {
        throw "Some dependencies are still unavailable on PATH."
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

Initialize-Diagnostics
$exitCode = 0

try {
    if ($Uninstall) {
        Remove-HaloCreek
    }
    else {
        if ($DependenciesOnly -and $NoInstallDependencies) {
            throw "DependenciesOnly and NoInstallDependencies cannot be used together."
        }

        Write-Host "HaloCreek offline installer"
        Write-Host "Offline pack root: $PSScriptRoot"

        $manifest = Read-PackManifest
        Write-DiagnosticsSnapshot $manifest

        Write-Step "Verify offline pack files"
        Test-PackHashes $manifest

        if (-not $NoInstallDependencies) {
            Install-OfflineDependencies $manifest
        }

        if (-not $DependenciesOnly) {
            Install-HaloCreekApp $manifest
        }

        Write-Host ""
        Write-Host "HaloCreek offline installation completed."
    }
}
catch {
    $exitCode = 1
    $errorPath = New-DiagnosticLogPath "error" "txt"
    $errorText = @(
        "HaloCreek offline installation failed.",
        "RunId: $InstallRunId",
        "Timestamp: $([DateTime]::Now.ToString("o"))",
        "Message: $($_.Exception.Message)",
        "",
        "Script stack trace:",
        $_.ScriptStackTrace,
        "",
        "Full error:",
        ($_ | Out-String)
    )
    [IO.File]::WriteAllLines($errorPath, $errorText, $Utf8NoBom)
    Write-Host ""
    Write-Host "HaloCreek offline installation failed."
    Write-Host "Error: $($_.Exception.Message)"
    Write-Host "Error diagnostics: $errorPath"
    Write-Host "Diagnostics directory: $DiagnosticsDir"
}
finally {
    Stop-Diagnostics
}

exit $exitCode
