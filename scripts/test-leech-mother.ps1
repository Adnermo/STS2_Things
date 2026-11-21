[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')][string]$TargetVersion = 'v113.1',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [string]$OutputDir = '',
    [string]$GodotExe = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskOut = if ($OutputDir) { [IO.Path]::GetFullPath($OutputDir) } else { Join-Path $taskRoot 'build/leech_mother' }
New-Item -ItemType Directory -Force -Path $taskOut | Out-Null

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

if (-not $ImplementationDll) {
    $legacyImpl = Join-Path $taskRoot "build/leech_mother/$TargetVersion/STS2_Things.dll"
    if (Test-Path -LiteralPath $legacyImpl -PathType Leaf) {
        $ImplementationDll = $legacyImpl
    }
    else {
        $ImplementationDll = Join-Path $taskRoot "build/$TargetVersion/STS2_Things.dll"
    }
}
$ImplementationDll = [IO.Path]::GetFullPath($ImplementationDll)

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

$env:PATH = 'C:/Program Files/dotnet;' + $env:PATH
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& dotnet build (Join-Path $taskRoot 'tools/LeechMotherProbe/LeechMotherProbe.csproj') --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$DataDir" "/p:RuntimeDependencyDir=$RuntimeDependencyDir" "/p:ImplementationDll=$ImplementationDll"
if ($LASTEXITCODE -ne 0) { throw 'Leech Mother probe compile failed' }

$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$taskLog = Join-Path $taskOut "probe-$TargetVersion.stdout.log"
$taskErr = Join-Path $taskOut "probe-$TargetVersion.stderr.log"
$taskProc = Start-Process -FilePath $GodotExe -ArgumentList @('--headless', '--path', ('"' + (Join-Path $taskRoot 'tools/LeechMotherProbe') + '"'), '--rendering-method', 'gl_compatibility') -WindowStyle Hidden -PassThru -RedirectStandardOutput $taskLog -RedirectStandardError $taskErr
try {
    $taskStart = Get-Date
    while (-not $taskProc.WaitForExit(30000)) {
        Write-Output 'Native mother checks are running...'
        if (((Get-Date) - $taskStart).TotalSeconds -gt 120) { throw 'Leech Mother probe timed out' }
    }
    if ($taskProc.ExitCode -ne 0) { Get-Content -LiteralPath $taskErr -TotalCount 12; throw 'Leech Mother native probe failed' }
}
finally {
    if (-not $taskProc.HasExited) { Stop-Process -InputObject $taskProc }
    $taskProc.Dispose()
}
if (Select-String -LiteralPath $taskErr -Pattern '^ERROR:|SCRIPT ERROR' -Quiet) { throw 'Leech Mother engine errors' }
if (-not (Select-String -LiteralPath $taskLog -Pattern 'Leech Mother probe: PASS' -Quiet)) { throw 'Missing native PASS marker' }
Get-Content -LiteralPath $taskLog -Tail 3
