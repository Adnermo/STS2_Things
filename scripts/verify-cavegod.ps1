[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')]
    [string]$TargetVersion = 'v113.1',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$PackagePck = '',
    [string]$DotnetExe = 'C:\Program Files\dotnet\dotnet.exe',
    [string]$GodotExe = '',
    [string]$OutputDirectory = 'build/cavegod-refactor-20260919',
    [switch]$Render
)

$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))
$implementation = Join-Path $outputRoot 'candidate/STS2_Things.dll'
$probeRoot = Join-Path $taskRoot 'tools/CaveGodProbe'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

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
& $DotnetExe build (Join-Path $taskRoot 'STS2_Things.csproj') -c Release --nologo `
    "-p:Sts2TargetVersion=$TargetVersion" "-p:Sts2DataDir=$DataDir" -o (Join-Path $outputRoot 'candidate') |
    Tee-Object -FilePath (Join-Path $outputRoot 'release-build.log')
if ($LASTEXITCODE -ne 0) { throw "Release build failed: $LASTEXITCODE" }

Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& $DotnetExe build (Join-Path $probeRoot 'CaveGodProbe.csproj') -c Debug --nologo `
    "-p:Sts2TargetVersion=$TargetVersion" `
    "-p:Sts2DataDir=$DataDir" `
    "-p:RuntimeDependencyDir=$RuntimeDependencyDir" `
    "-p:ImplementationDll=$implementation"
if ($LASTEXITCODE -ne 0) { throw "Probe build failed: $LASTEXITCODE" }

$portableDotnet = Join-Path $taskRoot '.tmp\dotnet9'
if (Test-Path -LiteralPath (Join-Path $portableDotnet 'dotnet.exe') -PathType Leaf) {
    $env:DOTNET_ROOT = $portableDotnet
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_ROLL_FORWARD = 'Major'
}

& $GodotExe --headless --path $probeRoot --log-file (Join-Path $outputRoot 'probe.log')
if ($LASTEXITCODE -ne 0) { throw "CaveGod behavior probe failed: $LASTEXITCODE" }

if ($Render) {
    . (Join-Path $PSScriptRoot 'Invoke-GodotProcess.ps1')
    $visualLog = Join-Path $outputRoot 'visual-probe.log'
    $arguments = @('--path', $probeRoot, '--rendering-method', 'gl_compatibility',
        '--resolution', '1920x1080', '--log-file', $visualLog,
        '--', '--visual')
    $result = Start-HeadlessSafeProcess -FilePath $GodotExe -ArgumentList $arguments -TimeoutMs 60000
    if ($result.TimedOut) { throw "CaveGod renderer timed out after 60s." }
    if ($result.ExitCode -ne 0) { throw "CaveGod renderer failed: $($result.ExitCode)" }
}

Write-Host "CaveGod verification complete: $outputRoot"
