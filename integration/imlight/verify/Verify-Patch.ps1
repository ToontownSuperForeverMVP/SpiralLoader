<#
.SYNOPSIS
  Verify the Imlight integration patches without Wizard101 data or Imcodec type dumps (Windows twin of
  verify-patch.sh). Applies the patches to a fresh Imlight checkout, compiles the real patched Director, modding
  files and session plumbing against functional stubs, boots it twice (with every mod, then with the
  client-process mods disabled) while Scenarios.cs drives real sessions, and checks the output.
#>
param(
    [string]$Commit = 'b07987168fcfa2d123a5d78df90ca0c76405e427',
    [string]$ImlightDirectory,
    [string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $ImlightDirectory) { $ImlightDirectory = Join-Path $repoRoot "external\Imlight-verify-$($Commit.Substring(0, 12))" }
$verifyMods = @('VerifyMod', 'ClientProcessMod', 'IsolationMod', 'DuplicateClaimMod')
# Disabled for the vanilla run: the client-process mods, and the data mod that needs a newer integration API than the
# Director provides (under the Abort policy a rejected mod would stop startup; disabling it must not).
$clientProcessMods = 'client-process-verify, isolation-verify, duplicate-claim-verify, future-api-verify'
# The newest recorded baseline of the public Imlight.CoreLib.Modding surface.
$apiBaseline = Join-Path (Get-ChildItem (Join-Path $repoRoot 'api-baseline') -Directory | Sort-Object { [version]$_.Name } | Select-Object -Last 1).FullName 'Imlight.CoreLib.Modding.txt'

& (Join-Path $repoRoot 'integration\imlight\Setup-Imlight.ps1') -Destination $ImlightDirectory -Commit $Commit -NoSubmodules

function Invoke-DotNet { dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet $args failed ($LASTEXITCODE)" } }
Invoke-DotNet build (Join-Path $repoRoot 'examples\HelloSpiral\HelloSpiral.csproj') -c $Configuration -v quiet -nologo
foreach ($mod in $verifyMods) {
    Invoke-DotNet build (Join-Path $PSScriptRoot "$mod\$mod.csproj") -c $Configuration -v quiet -nologo "-p:ImlightSourceRoot=$ImlightDirectory"
}

$runDirectory = Join-Path $PSScriptRoot "bin\$Configuration\net10.0"
$ini = Join-Path $runDirectory 'Config\Imlight.ini'
$originalIni = [IO.File]::ReadAllText((Join-Path $ImlightDirectory 'src\Imlight.Director\Config\Imlight.ini'))

# Boots the Director with the given [SpiralLoader] FailurePolicy and DisabledMods and returns its output.
function Invoke-Director([string]$Policy, [string]$Disabled) {
    Remove-Item -Recurse -Force (Join-Path $runDirectory 'mods'), (Join-Path $runDirectory 'moddata') -ErrorAction SilentlyContinue
    $mods = New-Item -ItemType Directory -Force (Join-Path $runDirectory 'mods')
    Copy-Item -Recurse (Join-Path $repoRoot 'artifacts\mods\hello-spiral') $mods
    Get-ChildItem (Join-Path $PSScriptRoot 'bin\mods') -Directory | ForEach-Object { Copy-Item -Recurse $_.FullName $mods }
    Get-ChildItem (Join-Path $PSScriptRoot 'DataMods') -Directory | ForEach-Object { Copy-Item -Recurse $_.FullName $mods }
    $text = $originalIni -replace '(?m)^FailurePolicy = .*$', "FailurePolicy = $Policy" `
                         -replace '(?m)^DisabledMods =.*$', "DisabledMods = $Disabled"
    [IO.File]::WriteAllText($ini, $text)

    # Program.Main idles forever once the server is up: stop it shortly after the scenarios finish (the game
    # servers report right after them), or after 90 s.
    $outputFile = New-TemporaryFile
    $process = Start-Process dotnet -ArgumentList 'Imlight.Director.Verify.dll' -WorkingDirectory $runDirectory `
        -RedirectStandardOutput $outputFile -NoNewWindow -PassThru
    $deadline = (Get-Date).AddSeconds(90)
    while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        # The redirected file may be locked while dotnet writes to it (Windows); try again next second.
        try { $sofar = [IO.File]::ReadAllText($outputFile.FullName) } catch { $sofar = '' }
        if ($sofar.Contains('VERIFY: scenarios done')) { Start-Sleep -Seconds 3; break }
    }
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $process.WaitForExit()
    $output = [IO.File]::ReadAllText($outputFile.FullName)
    Remove-Item $outputFile
    return $output
}

# Every non-empty line of the expectation files must appear in the output; a line starting with "!" must not.
function Test-Output([string]$Output, [string[]]$Files) {
    $ok = $true
    foreach ($file in $Files) {
        if (-not (Test-Path $file)) { continue }
        foreach ($line in (Get-Content $file | Where-Object { $_.Trim() -ne '' })) {
            if ($line.StartsWith('!')) {
                $text = $line.Substring(1)
                if ($Output.Contains($text)) { Write-Host "  FAIL absent: $text"; $ok = $false } else { Write-Host "  ok   absent: $text" }
            } elseif ($Output.Contains($line)) {
                Write-Host "  ok   $line"
            } else {
                Write-Host "  FAIL $line"; $ok = $false
            }
        }
    }
    return $ok
}

$failed = $false
$modded = Invoke-Director 'Continue' ''
Write-Host "Checking patched Director with mods ($Commit):"
$revisionExpectations = Join-Path $PSScriptRoot "expected-output-$($Commit.Substring(0, 12)).txt"
if (-not (Test-Output $modded @((Join-Path $PSScriptRoot 'expected-output.txt'), $revisionExpectations, $apiBaseline))) { $failed = $true }
if (Test-Path (Join-Path $runDirectory 'moddata\default\hello-spiral\started.txt')) {
    Write-Host '  ok   hello-spiral wrote moddata/default/hello-spiral/started.txt'
} else {
    Write-Host '  FAIL hello-spiral wrote moddata/default/hello-spiral/started.txt'; $failed = $true
}
$moddedFailed = $failed

$vanilla = Invoke-Director 'Abort' $clientProcessMods
Write-Host "Checking patched Director with the client-process mods disabled ($Commit):"
if (-not (Test-Output $vanilla @((Join-Path $PSScriptRoot 'expected-vanilla-output.txt')))) { $failed = $true }

[IO.File]::WriteAllText($ini, $originalIni)
if ($moddedFailed) { Write-Host '--- Director output (mods) ---'; Write-Host $modded }
elseif ($failed) { Write-Host '--- Director output (client-process mods disabled) ---'; Write-Host $vanilla }
if ($failed) { exit 1 }
