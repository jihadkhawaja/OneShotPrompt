[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot "..\config.yaml"),
    [string]$JobName
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\OneShotPrompt.Console"
$resolvedConfigPath = [System.IO.Path]::GetFullPath($ConfigPath)

$arguments = @("run", "--project", $projectPath, "--", "run", "--config", $resolvedConfigPath)

if (-not [string]::IsNullOrWhiteSpace($JobName)) {
    $arguments += @("--job", $JobName)
}

& dotnet @arguments
exit $LASTEXITCODE