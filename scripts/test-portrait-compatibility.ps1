[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v114.0')][string]$TargetVersion = 'v114.0',
    [string]$NativeGameDir = '',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [Parameter(Mandatory)][string]$ForeignDll,
    [string]$SecondForeignDll = '',
    [string]$ThirdForeignDll = '',
    [Parameter(Mandatory)][string]$OutputDir,
    [ValidateSet('before', 'after')][string]$ForeignLoadOrder = 'before',
    [switch]$Baseline,
    [switch]$Cards,
    [string]$GodotExe = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

if ($NativeGameDir) {
    $taskGame = [IO.Path]::GetFullPath($NativeGameDir)
    if ([string]::IsNullOrWhiteSpace($DataDir)) {
        $DataDir = Join-Path $taskGame 'data_sts2_windows_x86_64'
    }
}
elseif ([string]::IsNullOrWhiteSpace($DataDir)) {
    if ($TargetVersion -eq 'v111.1') {
        $DataDir = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v111.1\b0460083b012\game\data_sts2_windows_x86_64'
    }
    else {
        $v1140Data = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v114\0e151996c7aa\game\data_sts2_windows_x86_64'
        if (Test-Path -LiteralPath $v1140Data -PathType Container) {
            $DataDir = $v1140Data
        }
        else {
            $DataDir = 'D:\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64'
        }
    }
}
$DataDir = [IO.Path]::GetFullPath($DataDir)
$taskGame = if ($NativeGameDir) { [IO.Path]::GetFullPath($NativeGameDir) } else { Split-Path $DataDir -Parent }
$candidateGamePck = Join-Path $taskGame 'SlayTheSpire2.pck'
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
if ([string]::IsNullOrWhiteSpace($PackagePck)) {
    foreach ($candidateModPck in @(
        (Join-Path $taskRoot "build\$TargetVersion\STS2_Things.pck"),
        (Join-Path $taskRoot 'build\unified\STS2_Things.pck')
    )) {
        if (Test-Path -LiteralPath $candidateModPck -PathType Leaf) {
            $PackagePck = $candidateModPck
            break
        }
    }
}
if ($PackagePck) {
    $env:THINGS_PROBE_PCK = [IO.Path]::GetFullPath($PackagePck)
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

$taskOutput = [IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$env:PATH = 'C:/Program Files/dotnet;' + $env:PATH
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& dotnet build (Join-Path $taskRoot 'tools/PortraitCompatibilityProbe/PortraitCompatibilityProbe.csproj') --nologo "-p:Sts2TargetVersion=$TargetVersion" "-p:Sts2DataDir=$DataDir" "-p:RuntimeDependencyDir=$RuntimeDependencyDir" *> (Join-Path $taskOutput 'build.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $taskOutput 'build.log') -Tail 15; throw 'Portrait probe build failed' }
. (Join-Path $PSScriptRoot 'Invoke-GodotProcess.ps1')
$taskArgs = @('--headless', '--path', (Join-Path $taskRoot 'tools/PortraitCompatibilityProbe'), '--rendering-method', 'gl_compatibility', '--', $candidateGamePck, ([IO.Path]::GetFullPath($PackagePck)), ([IO.Path]::GetFullPath($ForeignDll)), ([IO.Path]::GetFullPath($ImplementationDll)))
if ($Baseline) { $taskArgs += '--baseline' }
if ($ForeignLoadOrder -eq 'after') { $taskArgs += '--early' }
if ($Cards) { $taskArgs += '--cards' }
if ($SecondForeignDll) { $taskArgs += @('--second-foreign', ([IO.Path]::GetFullPath($SecondForeignDll))) }
if ($ThirdForeignDll) { $taskArgs += @('--third-foreign', ([IO.Path]::GetFullPath($ThirdForeignDll))) }
$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$taskLog = Join-Path $taskOutput 'stdout.log'
$taskError = Join-Path $taskOutput 'stderr.log'
$taskRun = Start-HeadlessSafeProcess -FilePath $GodotExe -ArgumentList $taskArgs -TimeoutMs 120000 -StdoutPath $taskLog -StderrPath $taskError
if ($taskRun.TimedOut) { throw 'Portrait checks timed out' }
Get-Content -LiteralPath $taskLog -Tail 10
if ($taskRun.ExitCode -ne 0) { Get-Content -LiteralPath $taskError -Tail 8; throw "Portrait lifecycle check failed (exit $($taskRun.ExitCode))" }
if (Select-String -LiteralPath $taskError -Pattern '^ERROR:|SCRIPT ERROR' -Quiet) { throw 'Portrait checks logged engine errors' }
if (-not (Select-String -LiteralPath $taskLog -Pattern 'PORTRAIT_COMPAT_PASS' -Quiet)) { throw 'Missing portrait PASS marker' }
