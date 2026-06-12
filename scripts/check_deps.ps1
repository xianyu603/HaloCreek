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
    # Example:
    # [pscustomobject]@{
    #     Name            = "dotnet SDK"
    #     Probe           = { Invoke-CheckCommand "dotnet" @("--version") }
    #     Assert          = { param($Actual) $Actual.ExitCode -eq 0 -and $Actual.Output -match "^10\." }
    #     ExpectedMessage = "exit code 0, version starts with 10."
    # }
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
        $message = if ($passed) { "OK" } else { "Expected $($check.ExpectedMessage)" }
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
