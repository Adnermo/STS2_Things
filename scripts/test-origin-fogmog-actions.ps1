[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')][string]$TargetVersion = 'v113.1',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [switch]$Preview,
    [string]$OutputDir = 'build\origin-fogmog-impact-20261003',
    [string]$GodotExe = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskProbe = Join-Path $taskRoot 'tools\CaveGodProbe'
$taskEvidence = if ([IO.Path]::IsPathRooted($OutputDir)) { [IO.Path]::GetFullPath($OutputDir) } else { [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDir)) }
$taskOutput = Join-Path $taskEvidence 'actions'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($DataDir)) {
    if ($TargetVersion -eq 'v111.1') {
        $DataDir = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v111.1\b0460083b012\game\data_sts2_windows_x86_64'
    }
    else {
        $v1131Data = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v113.1\76cc75b6f097\game\data_sts2_windows_x86_64'
        if (Test-Path -LiteralPath $v1131Data -PathType Container) {
            $DataDir = $v1131Data
        }
        else {
            $DataDir = 'D:\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64'
        }
    }
}
$DataDir = [IO.Path]::GetFullPath($DataDir)
$candidateGamePck = Join-Path (Split-Path $DataDir -Parent) 'SlayTheSpire2.pck'
if (Test-Path -LiteralPath $candidateGamePck) {
    $env:THINGS_PROBE_GAME_PCK = $candidateGamePck
}

if ([string]::IsNullOrWhiteSpace($RuntimeDependencyDir)) {
    $RuntimeDependencyDir = $DataDir
}
$RuntimeDependencyDir = [IO.Path]::GetFullPath($RuntimeDependencyDir)

if ([string]::IsNullOrWhiteSpace($ImplementationDll)) {
    $ImplementationDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.dll"
}
$taskImplementation = if ([IO.Path]::IsPathRooted($ImplementationDll)) { [IO.Path]::GetFullPath($ImplementationDll) } else { [IO.Path]::GetFullPath((Join-Path $taskRoot $ImplementationDll)) }

if ($PackagePck) {
    $env:THINGS_PROBE_PCK = [IO.Path]::GetFullPath($PackagePck)
}
elseif (-not $env:THINGS_PROBE_PCK) {
    foreach ($candidateModPck in @(
        (Join-Path $taskRoot "build\$TargetVersion\STS2_Things.pck"),
        (Join-Path $taskRoot 'build\unified\STS2_Things.pck')
    )) {
        if (Test-Path -LiteralPath $candidateModPck -PathType Leaf) {
            $env:THINGS_PROBE_PCK = [IO.Path]::GetFullPath($candidateModPck)
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    $GodotExe = $env:GODOT_4_5_1_MONO
}
if ([string]::IsNullOrWhiteSpace($GodotExe) -or -not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) {
    if (Test-Path -LiteralPath 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' -PathType Leaf) {
        $GodotExe = 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
    else {
        $GodotExe = 'D:\Download\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
}
$GodotExe = [IO.Path]::GetFullPath($GodotExe)

Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $taskProbe 'CaveGodProbe.csproj') -c Debug --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$DataDir" "/p:RuntimeDependencyDir=$RuntimeDependencyDir" "/p:ImplementationDll=$taskImplementation"
if ($LASTEXITCODE -ne 0) { throw 'Fogmog action probe compile failed.' }
$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$taskArgs = @('--path', ('"{0}"' -f $taskProbe), '--rendering-method', 'gl_compatibility', '--', '--fogmog-actions')
if ($Preview) {
    $taskOutput = Join-Path $taskEvidence 'preview'
    New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
    $taskArgs = @('--path', ('"{0}"' -f $taskProbe), '--rendering-method', 'gl_compatibility', '--fixed-fps', '30', '--', '--fogmog-preview')
}
$taskArgs += @('--fogmog-output', ('"{0}"' -f $taskOutput))
$taskProcess = Start-Process -FilePath $GodotExe -ArgumentList $taskArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskOutput 'probe.stdout.log') -RedirectStandardError (Join-Path $taskOutput 'probe.stderr.log')
try {
    $taskTimeout = if ($Preview) { 240000 } else { 60000 }
    if (-not $taskProcess.WaitForExit($taskTimeout)) { throw 'Fogmog action probe timed out.' }
    Get-Content -LiteralPath (Join-Path $taskOutput 'probe.stdout.log') -Tail 12
    if ($taskProcess.ExitCode -ne 0) {
        Get-Content -LiteralPath (Join-Path $taskOutput 'probe.stderr.log') -TotalCount 16
        throw "Fogmog action probe failed: $($taskProcess.ExitCode)"
    }
    $taskErrors = Select-String -LiteralPath (Join-Path $taskOutput 'probe.stderr.log') -Pattern '^ERROR:|SCRIPT ERROR'
    if ($taskErrors) { throw ($taskErrors.Line -join [Environment]::NewLine) }
    $taskPass = if ($Preview) { 'OriginFogmog preview: PASS' } else { 'OriginFogmog action regression: PASS' }
    if (-not (Select-String -LiteralPath (Join-Path $taskOutput 'probe.stdout.log') -Pattern $taskPass -Quiet)) { throw 'Fogmog probe did not report PASS.' }
}
finally {
    if (-not $taskProcess.HasExited) { Stop-Process -InputObject $taskProcess }
    $taskProcess.Dispose()
}
