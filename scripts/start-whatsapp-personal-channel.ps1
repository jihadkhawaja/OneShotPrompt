param(
    [switch]$SkipInstall
)

$toolPath = Join-Path $PSScriptRoot "..\tools\whatsapp-personal-channel"

Push-Location $toolPath

try
{
    if (-not $SkipInstall -and -not (Test-Path (Join-Path $toolPath "node_modules")))
    {
        $env:PUPPETEER_SKIP_DOWNLOAD = "1"
        $env:PUPPETEER_CHROME_SKIP_DOWNLOAD = "1"
        npm install
        Remove-Item Env:PUPPETEER_SKIP_DOWNLOAD -ErrorAction SilentlyContinue
        Remove-Item Env:PUPPETEER_CHROME_SKIP_DOWNLOAD -ErrorAction SilentlyContinue
    }

    node --disable-warning=DEP0040 channel.mjs serve
}
finally
{
    Pop-Location
}