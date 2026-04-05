[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot "..\config.yaml"),
    [string]$JobName,
    [switch]$Listen
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\OneShotPrompt.Console"
$resolvedConfigPath = [System.IO.Path]::GetFullPath($ConfigPath)

if ($Listen -and [string]::IsNullOrWhiteSpace($JobName)) {
    throw "JobName is required when -Listen is specified."
}

$commandName = if ($Listen) { "listen" } else { "run" }
$arguments = @("run", "--project", $projectPath, "--", $commandName, "--config", $resolvedConfigPath)

if (-not [string]::IsNullOrWhiteSpace($JobName)) {
    $arguments += @("--job", $JobName)
}

& dotnet @arguments
exit $LASTEXITCODE