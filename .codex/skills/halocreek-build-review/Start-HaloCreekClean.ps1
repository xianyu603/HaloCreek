[CmdletBinding()]
param(
    [string]$WorkspaceRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$TargetFramework = 'net10.0-windows10.0.18362.0',
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'

$workspace = (Resolve-Path -LiteralPath $WorkspaceRoot).Path
$appPath = Join-Path $workspace "HaloCreek\bin\$Configuration\$TargetFramework\HaloCreek.exe"

if (-not (Test-Path -LiteralPath $appPath)) {
    throw "HaloCreek executable was not found: $appPath. Build the project before launching."
}

$removedNames = [System.Collections.Generic.List[string]]::new()
$environmentNames = [Environment]::GetEnvironmentVariables('Process').Keys

foreach ($environmentName in $environmentNames) {
    $name = [string]$environmentName
    if ($name -like '*PSMUX*' -or $name -eq 'TMUX' -or $name -eq 'TMUX_PANE') {
        Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
        $removedNames.Add($name)
    }
}

$startArguments = @{
    FilePath = $appPath
    WorkingDirectory = (Split-Path -Parent $appPath)
    PassThru = $true
}

if ($Wait) {
    $startArguments['Wait'] = $true
}

$process = Start-Process @startArguments

if ($removedNames.Count -gt 0) {
    Write-Host "Removed environment variables before launch: $($removedNames -join ', ')"
}
else {
    Write-Host 'No psmux environment variables were present before launch.'
}

Write-Host "Started HaloCreek. PID: $($process.Id)"
