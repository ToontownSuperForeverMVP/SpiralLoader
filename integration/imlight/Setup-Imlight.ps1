<#
.SYNOPSIS
  Clone Imlight at a pinned commit and apply the SpiralLoader integration patches.
.EXAMPLE
  .\integration\imlight\Setup-Imlight.ps1
  .\integration\imlight\Setup-Imlight.ps1 -Destination C:\src\Imlight -Commit 2a16010e...
#>
param(
    [string]$Destination,
    [string]$Commit = 'b07987168fcfa2d123a5d78df90ca0c76405e427',
    [switch]$NoSubmodules
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Destination) { $Destination = Join-Path $repoRoot 'external\Imlight' }

function Invoke-Git { git @args; if ($LASTEXITCODE -ne 0) { throw "git $args failed ($LASTEXITCODE)" } }

if (-not (Test-Path (Join-Path $Destination '.git'))) {
    Invoke-Git clone https://github.com/Revive101/Imlight.git $Destination
}
# Windows PowerShell 5.1 turns redirected native stderr into terminating errors under 'Stop'.
$ErrorActionPreference = 'Continue'
git -C $Destination diff --quiet; $dirty = $LASTEXITCODE -ne 0
git -C $Destination diff --cached --quiet; $dirty = $dirty -or ($LASTEXITCODE -ne 0)
git -C $Destination cat-file -e "$Commit^{commit}" 2>$null; $haveCommit = $LASTEXITCODE -eq 0
$ErrorActionPreference = 'Stop'
if ($dirty) { throw "$Destination has uncommitted changes; refusing to reset it." }
if (-not $haveCommit) { Invoke-Git -C $Destination fetch origin }

Invoke-Git -C $Destination checkout -q -B spiralloader $Commit
if (-not $NoSubmodules) { Invoke-Git -C $Destination submodule update --init }
$patches = Get-ChildItem (Join-Path $PSScriptRoot 'patches\*.patch') | Sort-Object Name | ForEach-Object FullName
Invoke-Git -C $Destination -c user.name=SpiralLoader -c user.email=spiralloader@localhost am --3way --quiet @patches
Write-Host "Imlight $Commit + $($patches.Count) SpiralLoader patches -> $Destination"
