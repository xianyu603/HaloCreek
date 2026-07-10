#requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)

    Write-Host ""
    Write-Host "==> $Message"
}

function Test-CommandAvailable {
    param([string]$Name)

    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Invoke-ExternalCommand {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$Description
    )

    Write-Host $Description
    & $FilePath @ArgumentList
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }
}

function Install-WinGetPackage {
    param(
        [string]$PackageId,
        [string]$DisplayName
    )

    if (-not (Test-CommandAvailable "winget.exe")) {
        throw "winget.exe is required. Install or update Windows Package Manager first."
    }

    $arguments = @(
        "install",
        "--id", $PackageId,
        "--exact",
        "--accept-source-agreements",
        "--accept-package-agreements"
    )

    Invoke-ExternalCommand "winget.exe" $arguments "Installing $DisplayName with WinGet ($PackageId)."
}

function Ensure-WinGetPackage {
    param(
        [string]$CommandName,
        [string]$PackageId,
        [string]$DisplayName
    )

    if (Test-CommandAvailable $CommandName) {
        Write-Host "$DisplayName is already available on PATH."
        return
    }

    Install-WinGetPackage $PackageId $DisplayName
}

function Ensure-CodexCli {
    if (Test-CommandAvailable "codex.exe") {
        Write-Host "Codex CLI is already available on PATH."
        return
    }

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-Command", "irm https://chatgpt.com/codex/install.ps1 | iex"
    )

    Invoke-ExternalCommand "powershell.exe" $arguments "Installing Codex CLI with the official Windows installer."
}

function Ensure-TortoiseGit {
    if (Test-CommandAvailable "TortoiseGitProc.exe") {
        Write-Host "TortoiseGitProc.exe is already available on PATH."
        return
    }

    Install-WinGetPackage "TortoiseGit.TortoiseGit" "TortoiseGit"
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

Write-Host "HaloCreek Windows dependency installer"
Write-Host "This script installs missing dependencies. Git and TortoiseGit installers may show setup UI."

Write-Step "Install psmux"
Ensure-WinGetPackage "psmux.exe" "marlocarlo.psmux" "psmux"

Write-Step "Install Codex CLI"
Ensure-CodexCli

Write-Step "Install Git for Windows"
Ensure-WinGetPackage "git.exe" "Git.Git" "Git for Windows"

Write-Step "Install TortoiseGit"
Ensure-TortoiseGit

Write-Step "Verify dependencies"
$results = @(
    (Test-Dependency "psmux" { psmux.exe --version } { param($Output) $Output -match "\d+\.\d+" }),
    (Test-Dependency "Codex CLI" { cmd.exe /d /c "codex.exe --version 2>&1" } { param($Output) $Output -match "codex-cli\s+\d+\." }),
    (Test-Dependency "Git for Windows" { git.exe --version } { param($Output) $Output -match "^git version \d+\." }),
    (Test-Dependency "TortoiseGitProc on PATH" { where.exe TortoiseGitProc.exe } { param($Output) $Output -match "TortoiseGitProc\.exe" })
)

if ($results -contains $false) {
    Write-Host ""
    Write-Host "Some dependencies are still unavailable on PATH."
    Write-Host "If an installer just finished, open a new PowerShell and run this script again."
    Write-Host "If it still fails, check whether the installed tool added its executable directory to PATH."
    exit 1
}

Write-Host ""
Write-Host "All HaloCreek Windows dependencies are installed."
exit 0
