param(
    [ValidateSet('v111.1', 'v113.1')][string]$TargetVersion = 'v113.1',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$GamePck = '',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [string]$OutputDir = '',
    [switch]$Performance,
    [switch]$Measure,
    [string]$HexDll = '',
    [string]$GodotExe = $env:GODOT_4_5_1_MONO
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskOut = if ($OutputDir) { [IO.Path]::GetFullPath($OutputDir) } else { Join-Path $taskRoot ('build/bug-report-validation-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)) }
New-Item -ItemType Directory -Path $taskOut -Force | Out-Null

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

$taskRuntime = if ($RuntimeDependencyDir) { [IO.Path]::GetFullPath($RuntimeDependencyDir) } else { $DataDir }
$taskPck = if ($GamePck) { [IO.Path]::GetFullPath($GamePck) } else { $candidateGamePck }
if (-not (Test-Path -LiteralPath $taskPck -PathType Leaf)) {
    throw 'Pass -GamePck for the full matching game version; a reference DLL is not a client resource pack'
}
$env:THINGS_PROBE_GAME_PCK = $taskPck

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

if ($ImplementationDll) {
    $taskImplementation = [IO.Path]::GetFullPath($ImplementationDll)
}
else {
    $builtDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.dll"
    if (Test-Path -LiteralPath $builtDll -PathType Leaf) {
        $taskImplementation = $builtDll
    }
    else {
        $taskImplementation = Join-Path $taskOut "$TargetVersion/STS2_Things.dll"
        Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
        & 'C:/Program Files/dotnet/dotnet.exe' build (Join-Path $taskRoot 'STS2_Things.csproj') -c Release --nologo -o (Split-Path $taskImplementation) "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$DataDir"
        if ($LASTEXITCODE -ne 0) { throw 'Report implementation build failed' }
    }
}

Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& 'C:/Program Files/dotnet/dotnet.exe' build (Join-Path $taskRoot 'tools/BugReportProbe/BugReportProbe.csproj') --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$DataDir" "/p:RuntimeDependencyDir=$taskRuntime" "/p:ImplementationDll=$taskImplementation"
if ($LASTEXITCODE -ne 0) { throw 'Report native probe build failed' }

$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:THINGS_PERF_HEX_DLL = if ($HexDll) { [IO.Path]::GetFullPath($HexDll) } else { '' }

if ([string]::IsNullOrWhiteSpace($GodotExe) -or -not (Test-Path -LiteralPath $GodotExe -PathType Leaf)) {
    if (Test-Path -LiteralPath 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' -PathType Leaf) {
        $GodotExe = 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
    else {
        $GodotExe = 'D:\Download\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
}
$taskGodot = [IO.Path]::GetFullPath($GodotExe)
$taskStdout = Join-Path $taskOut "native-$TargetVersion.log"
$taskStderr = Join-Path $taskOut "native-$TargetVersion.stderr.log"
. (Join-Path $PSScriptRoot 'Invoke-GodotProcess.ps1')
$taskArguments = @('--headless', '--path', (Join-Path $taskRoot 'tools/BugReportProbe'))
if ($Performance) { $taskArguments += @('--', '--performance'); if ($Measure) { $taskArguments += '--measure' } }
$taskRun = Start-HeadlessSafeProcess -FilePath $taskGodot -ArgumentList $taskArguments -TimeoutMs 30000 -StdoutPath $taskStdout -StderrPath $taskStderr
if ($taskRun.TimedOut) { throw 'Report native probe exceeded 30 seconds' }
if ($taskRun.ExitCode -ne 0) { Get-Content -LiteralPath $taskStderr -Tail 15; throw 'Report native probe failed' }
$taskErrors = Get-Content -LiteralPath $taskStderr | Where-Object { ($_ -match '^ERROR:|SCRIPT ERROR') -and $_ -ne 'ERROR: System.InvalidOperationException: private engine error' }
if ($taskErrors) { $taskErrors; throw 'Unexpected report native engine errors' }
$taskMarker = if ($Performance) { 'REPORT_PERF' } else { 'BugReport native probe: PASS' }
if (-not (Select-String -LiteralPath $taskStdout -Pattern $taskMarker -Quiet)) { Get-Content -LiteralPath $taskStdout -Tail 10; throw 'Native report PASS marker missing' }
Get-Content -LiteralPath $taskStdout -Tail 3
