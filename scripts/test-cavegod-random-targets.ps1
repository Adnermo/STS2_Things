[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ImplementationDll,
    [Parameter(Mandatory = $true)][string]$DataDir,
    [Parameter(Mandatory = $true)][string]$GamePck,
    [Parameter(Mandatory = $true)][string]$PackagePck,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$GodotExe = 'D:\Download\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe'
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskProbe = Join-Path $taskRoot 'tools\CaveGodTargetProbe'
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$env:DOTNET_ROOT = 'C:\Program Files\dotnet'
& 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $taskProbe 'CaveGodTargetProbe.csproj') -t:Rebuild -c Debug --nologo "/p:ImplementationDll=$([IO.Path]::GetFullPath($ImplementationDll))" "/p:Sts2DataDir=$([IO.Path]::GetFullPath($DataDir))" *> (Join-Path $taskOutput 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $taskOutput 'build.log') -Tail 15; throw 'Random target probe compile failed.' }
$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:THINGS_PROBE_GAME_PCK = [IO.Path]::GetFullPath($GamePck)
$env:THINGS_PROBE_PCK = [IO.Path]::GetFullPath($PackagePck)
$env:THINGS_PROBE_RUNTIME_DIR = [IO.Path]::GetFullPath($DataDir)
$taskArgs = @('--headless', '--path', ('"{0}"' -f $taskProbe), '--', '--random-target-regression')
$taskProcess = Start-Process -FilePath $GodotExe -ArgumentList $taskArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskOutput 'stdout.log') -RedirectStandardError (Join-Path $taskOutput 'stderr.log')
try {
    if (-not $taskProcess.WaitForExit(45000)) { throw 'Random target probe timed out.' }
    Get-Content -LiteralPath (Join-Path $taskOutput 'stdout.log') -Tail 5
    if ($taskProcess.ExitCode -ne 0) {
        Get-Content -LiteralPath (Join-Path $taskOutput 'stderr.log') -TotalCount 12
        throw "Random target probe failed: $($taskProcess.ExitCode)"
    }
    if (Select-String -LiteralPath (Join-Path $taskOutput 'stderr.log') -Pattern '^ERROR:|SCRIPT ERROR' -Quiet) { throw 'Native errors in random target probe.' }
    if (-not (Select-String -LiteralPath (Join-Path $taskOutput 'stdout.log') -Pattern 'CaveGod random target regression: PASS' -Quiet)) { throw 'Random target probe did not report PASS.' }
}
finally {
    if (-not $taskProcess.HasExited) { Stop-Process -InputObject $taskProcess }
    $taskProcess.Dispose()
}
