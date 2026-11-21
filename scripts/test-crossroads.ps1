[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')][string]$TargetVersion = 'v113.1',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [switch]$Ui,
    [string]$GodotExe = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskOutput = Join-Path $taskRoot 'build\crossroads-20261003'
$taskProbe = Join-Path $taskRoot 'tools\CaveGodProbe'
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
if ($LASTEXITCODE -ne 0) { throw 'Crossroad probe compilation failed.' }
$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
. (Join-Path $PSScriptRoot 'Invoke-GodotProcess.ps1')
$taskMode = if ($Ui) { 'ui' } else { 'rules' }
$taskFlag = if ($Ui) { '--crossroads-ui' } else { '--crossroads' }
$taskArgs = @('--path', $taskProbe, '--rendering-method', 'gl_compatibility', '--fixed-fps', '60', '--', $taskFlag)
if (-not $Ui) { $taskArgs = @('--headless') + $taskArgs }
$taskStdout = Join-Path $taskOutput "$taskMode.stdout.log"
$taskStderr = Join-Path $taskOutput "$taskMode.stderr.log"
$taskRun = Start-HeadlessSafeProcess -FilePath $GodotExe -ArgumentList $taskArgs -TimeoutMs 60000 -StdoutPath $taskStdout -StderrPath $taskStderr
if ($taskRun.TimedOut) { throw 'Crossroad probe timed out.' }
Get-Content -LiteralPath $taskStdout -Tail 5
$taskErrors = Select-String -LiteralPath $taskStderr -Pattern 'ERROR:|SCRIPT ERROR'
if ($taskRun.ExitCode -ne 0 -or $taskErrors) {
    Get-Content -LiteralPath $taskStderr -First 12
    throw 'Crossroad probe failed; see the evidence logs.'
}
$taskPass = if ($Ui) { 'CROSSROADS_UI_PASS' } else { 'CROSSROADS_RULES_PASS' }
if (-not (Select-String -LiteralPath $taskStdout -Pattern $taskPass -Quiet)) { throw 'Crossroad probe did not report PASS.' }
