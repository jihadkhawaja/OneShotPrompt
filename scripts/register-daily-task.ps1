[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TaskName,

    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,

    [Parameter(Mandatory = $true)]
    [string]$JobName,

    [ValidatePattern("^([01]\d|2[0-3]):[0-5]\d$")]
    [string]$Time = "00:00",

    [string]$Description = "OneShotPrompt scheduled job"
)

$ErrorActionPreference = "Stop"

$resolvedExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)
$resolvedConfigPath = [System.IO.Path]::GetFullPath($ConfigPath)

if (-not (Test-Path -LiteralPath $resolvedExecutablePath)) {
    throw "Executable was not found: $resolvedExecutablePath"
}

if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
    throw "Config file was not found: $resolvedConfigPath"
}

$workingDirectory = Split-Path -Path $resolvedExecutablePath -Parent
$runTime = [DateTime]::ParseExact($Time, "HH:mm", [System.Globalization.CultureInfo]::InvariantCulture)
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

$arguments = @(
    "run",
    "--config",
    ('"{0}"' -f $resolvedConfigPath),
    "--job",
    ('"{0}"' -f $JobName)
) -join " "

$action = New-ScheduledTaskAction -Execute $resolvedExecutablePath -Argument $arguments -WorkingDirectory $workingDirectory
$trigger = New-ScheduledTaskTrigger -Daily -At $runTime
$principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Limited

Register-ScheduledTask `
    -TaskName $TaskName `
    -Description $Description `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Force | Out-Null

Write-Host "Scheduled task '$TaskName' registered for $currentUser at $Time."