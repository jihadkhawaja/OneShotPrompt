[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot "..\config.yaml")
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\OneShotPrompt.Console"
$resolvedConfigPath = [System.IO.Path]::GetFullPath($ConfigPath)

& dotnet run --project $projectPath -- jobs --config $resolvedConfigPath
exit $LASTEXITCODE