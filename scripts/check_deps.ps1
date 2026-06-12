#requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-CheckCommand {
    param(
        [string]$Command,

        [string[]]$ArgumentList = @()
    )

    $output = & $Command @ArgumentList 2>&1

    [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output   = ($output | Out-String).Trim()
    }
}

$Checks = @(
    [pscustomobject]@{
        Name            = "WSL default distribution"
        Probe           = { Invoke-CheckCommand "wsl.exe" @("--exec", "bash", "-ic", "printf halocreek-wsl-ok") }
        Assert          = { param($Actual) $Actual.ExitCode -eq 0 -and $Actual.Output -eq "halocreek-wsl-ok" }
        ExpectedMessage = "wsl.exe can run bash in the default distribution."
    },
    [pscustomobject]@{
        Name            = "WSL tmux"
        Probe           = { Invoke-CheckCommand "wsl.exe" @("--exec", "bash", "-ic", "tmux -V") }
        Assert          = { param($Actual) $Actual.ExitCode -eq 0 -and $Actual.Output -match "^tmux \d+\." }
        ExpectedMessage = "tmux is installed and tmux -V succeeds in the default WSL distribution."
    },
    [pscustomobject]@{
        Name            = "WSL Codex CLI"
        Probe           = { Invoke-CheckCommand "wsl.exe" @("--exec", "bash", "-ic", "codex --version") }
        Assert          = { param($Actual) $Actual.ExitCode -eq 0 -and $Actual.Output -match "^codex-cli \d+\." }
        ExpectedMessage = "codex is installed and codex --version succeeds in the default WSL distribution."
    },
    [pscustomobject]@{
        Name            = "Windows Git on PATH"
        Probe           = { Invoke-CheckCommand "git.exe" @("--version") }
        Assert          = { param($Actual) $Actual.ExitCode -eq 0 -and $Actual.Output -match "^git version \d+\." }
        ExpectedMessage = "git.exe is on PATH and git.exe --version succeeds."
    },
    [pscustomobject]@{
        Name            = "TortoiseGitProc on PATH"
        Probe           = { Invoke-CheckCommand "where.exe" @("TortoiseGitProc.exe") }
        Assert          = { param($Actual) $Actual.ExitCode -eq 0 -and $Actual.Output -match "TortoiseGitProc\.exe$" }
        ExpectedMessage = "TortoiseGitProc.exe is on PATH."
    }
)

if ($Checks.Count -eq 0) {
    Write-Host "No runtime environment checks are defined."
    exit 0
}

$failedCount = 0

foreach ($check in $Checks) {
    try {
        $actual = & $check.Probe
        $passed = [bool](& $check.Assert $actual)
        $message = if ($passed) {
            "OK"
        } else {
            "Expected $($check.ExpectedMessage) Actual exit code $($actual.ExitCode), output: $($actual.Output)"
        }
    }
    catch {
        $actual = $null
        $passed = $false
        $message = $_.Exception.Message
    }

    if (-not $passed) {
        $failedCount++
    }

    $status = if ($passed) { "PASS" } else { "FAIL" }
    Write-Host "[$status] $($check.Name) - $message"
}

if ($failedCount -gt 0) {
    exit 1
}

exit 0
