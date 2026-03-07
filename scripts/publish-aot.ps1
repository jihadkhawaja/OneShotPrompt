[CmdletBinding()]
param(
    [ValidateSet("win-arm64", "win-x64", "linux-x64", "linux-arm64")]
    [string]$RuntimeIdentifier = "win-arm64",

    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectFile = Join-Path $repoRoot "src\OneShotPrompt.Console\OneShotPrompt.Console.csproj"

& dotnet publish $projectFile -c $Configuration -r $RuntimeIdentifier
exit $LASTEXITCODE