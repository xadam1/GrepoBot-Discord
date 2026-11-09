<#
.SYNOPSIS
    Tests, builds and installs the GrepoBot Discord bot on the Raspberry Pi.

.EXAMPLE
    ./deploy/deploy.ps1                       # the Pi from deploy/target.local
    ./deploy/deploy.ps1 -Target pi@grepobot-pi.local
#>
param(
    # SSH login of the Pi (user@host). Without it: $env:GREPOBOT_PI, then deploy/target.local (gitignored, one line).
    # Set up key login once with the README steps so no password is asked.
    [string]$Target = "",

    # Skip the tests (not recommended).
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if (-not $Target) { $Target = $env:GREPOBOT_PI }
$targetFile = Join-Path $PSScriptRoot "target.local"
if (-not $Target -and (Test-Path $targetFile)) { $Target = (Get-Content $targetFile -TotalCount 1).Trim() }
if (-not $Target) { throw "Which Pi? Pass -Target user@host, set GREPOBOT_PI, or put user@host into deploy/target.local." }
$out = Join-Path ([System.IO.Path]::GetTempPath()) "grepobot-discord-deploy"

if (-not $SkipTests)
{
    Write-Host "Running tests..." -ForegroundColor Cyan
    dotnet test "$root" --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed, nothing was deployed." }
}

Write-Host "Publishing for linux-arm64..." -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish "$root/src/GrepoBot.Discord" -c Release -r linux-arm64 --self-contained true -o "$out/app" --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# Never ship local secrets.
Remove-Item "$out/app/appsettings.Local.json" -ErrorAction SilentlyContinue
Copy-Item "$PSScriptRoot/install.sh", "$PSScriptRoot/grepobot-discord.service", "$PSScriptRoot/grepobot-discord.env.example" $out

Write-Host "Uploading to $Target..." -ForegroundColor Cyan
ssh $Target "rm -rf ~/grepobot-discord-deploy"
if ($LASTEXITCODE -ne 0) { throw "Cannot reach $Target over SSH." }
scp -r -q "$out" "${Target}:~/grepobot-discord-deploy"
if ($LASTEXITCODE -ne 0) { throw "Upload failed." }

Write-Host "Installing..." -ForegroundColor Cyan
ssh -t $Target "sudo bash ~/grepobot-discord-deploy/install.sh"
if ($LASTEXITCODE -ne 0) { throw "Install failed." }

Remove-Item $out -Recurse -Force
Write-Host "Done." -ForegroundColor Green
