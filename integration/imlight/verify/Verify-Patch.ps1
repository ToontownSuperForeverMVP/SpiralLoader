<#
.SYNOPSIS
  Verify the Imlight integration patches without Wizard101 data or Imcodec type dumps (Windows twin of
  verify-patch.sh). Applies the patches to a fresh Imlight checkout, compiles the real patched Director
  against functional stubs, boots it with test mods, and checks its output.
#>
param(
    [string]$Commit = 'b07987168fcfa2d123a5d78df90ca0c76405e427',
    [string]$ImlightDirectory,
    [string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $ImlightDirectory) { $ImlightDirectory = Join-Path $repoRoot "external\Imlight-verify-$($Commit.Substring(0, 12))" }

& (Join-Path $repoRoot 'integration\imlight\Setup-Imlight.ps1') -Destination $ImlightDirectory -Commit $Commit -NoSubmodules

function Invoke-DotNet { dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet $args failed ($LASTEXITCODE)" } }
Invoke-DotNet build (Join-Path $repoRoot 'examples\HelloSpiral\HelloSpiral.csproj') -c $Configuration -v quiet -nologo
Invoke-DotNet build (Join-Path $PSScriptRoot 'VerifyMod\VerifyMod.csproj') -c $Configuration -v quiet -nologo "-p:ImlightSourceRoot=$ImlightDirectory"

$runDirectory = Join-Path $PSScriptRoot "bin\$Configuration\net10.0"
Remove-Item -Recurse -Force (Join-Path $runDirectory 'mods'), (Join-Path $runDirectory 'moddata') -ErrorAction SilentlyContinue
$mods = New-Item -ItemType Directory -Force (Join-Path $runDirectory 'mods')
Copy-Item -Recurse (Join-Path $repoRoot 'artifacts\mods\hello-spiral') $mods
Copy-Item -Recurse (Join-Path $PSScriptRoot 'bin\mods\imlight-verify') $mods

# Program.Main idles forever once the server is up, so stop it after startup.
$outputFile = New-TemporaryFile
$process = Start-Process dotnet -ArgumentList 'Imlight.Director.Verify.dll' -WorkingDirectory $runDirectory `
    -RedirectStandardOutput $outputFile -NoNewWindow -PassThru
if (-not $process.WaitForExit(60000)) { Stop-Process -Id $process.Id -Force }
$process.WaitForExit()
$output = Get-Content -Raw $outputFile

$expected = Get-Content (Join-Path $PSScriptRoot 'expected-output.txt') | Where-Object { $_.Trim() -ne '' }
$failed = $false
Write-Host "Checking patched Director startup ($Commit):"
foreach ($line in $expected) {
    if ($output.Contains($line)) { Write-Host "  ok   $line" } else { Write-Host "  FAIL $line"; $failed = $true }
}
if (Test-Path (Join-Path $runDirectory 'moddata\default\hello-spiral\started.txt')) {
    Write-Host '  ok   hello-spiral wrote moddata/default/hello-spiral/started.txt'
} else {
    Write-Host '  FAIL hello-spiral wrote moddata/default/hello-spiral/started.txt'; $failed = $true
}
Remove-Item $outputFile
if ($failed) { Write-Host '--- Director output ---'; Write-Host $output; exit 1 }
