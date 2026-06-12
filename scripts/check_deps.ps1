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
