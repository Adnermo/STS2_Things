[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')][string]$TargetVersion = 'v113.1',
    [string]$GameRoot = '',
    [string]$DataDir = '',
    [string]$RuntimeDependencyDir = '',
    [string]$ImplementationDll = '',
    [string]$PackagePck = '',
    [string]$OutputDir = '',
    [string]$GodotExe = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

if ($GameRoot -and [string]::IsNullOrWhiteSpace($DataDir)) {
    $DataDir = Join-Path ([IO.Path]::GetFullPath($GameRoot)) 'data_sts2_windows_x86_64'
}
elseif ([string]::IsNullOrWhiteSpace($DataDir)) {
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
    $env:THINGS_VANILLA_PCK = $candidateGamePck
}

if ([string]::IsNullOrWhiteSpace($RuntimeDependencyDir)) {
    $RuntimeDependencyDir = $DataDir
}
$RuntimeDependencyDir = [IO.Path]::GetFullPath($RuntimeDependencyDir)

if ([string]::IsNullOrWhiteSpace($ImplementationDll)) {
    $ImplementationDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.dll"
}
$ImplementationDll = [IO.Path]::GetFullPath($ImplementationDll)

if ($PackagePck) {
    $env:THINGS_PROBE_PCK = [IO.Path]::GetFullPath($PackagePck)
}
else {
    foreach ($candidateModPck in @(
        (Join-Path $taskRoot 'build\merchant-pair-attack-split-20261010\package\STS2_Things.pck'),
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
& 'C:/Program Files/dotnet/dotnet.exe' build (Join-Path $taskRoot 'tools/MerchantPairProbe/MerchantPairProbe.csproj') -c Debug --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$DataDir" "/p:RuntimeDependencyDir=$RuntimeDependencyDir" "/p:ImplementationDll=$ImplementationDll"
if ($LASTEXITCODE -ne 0) { throw 'Merchant probe compilation failed' }

$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
if (-not $OutputDir) { $OutputDir = Join-Path $taskRoot "build/merchant-pair-attack-split-20261010/native/$TargetVersion" }
$env:THINGS_PROBE_OUTPUT = [IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Path $env:THINGS_PROBE_OUTPUT -Force | Out-Null
$stdout = Join-Path $env:THINGS_PROBE_OUTPUT 'probe.stdout.log'
$stderr = Join-Path $env:THINGS_PROBE_OUTPUT 'probe.stderr.log'
$arguments = @('--headless', '--path', ('"' + (Join-Path $taskRoot 'tools/MerchantPairProbe') + '"'))
$process = Start-Process -FilePath $GodotExe -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
try {
    if (-not $process.WaitForExit(45000)) { throw 'Merchant probe timeout' }
    if ($process.ExitCode -ne 0 -or (Select-String -LiteralPath $stderr -Pattern '^ERROR:|SCRIPT ERROR:' -Quiet)) {
        Get-Content -LiteralPath $stderr -TotalCount 25
        throw 'Merchant probe failed'
    }
    if (-not (Select-String -LiteralPath $stdout -Pattern 'Merchant pair probe: PASS' -Quiet)) { throw 'Missing merchant probe result' }
    Get-Content -LiteralPath $stdout -Tail 4
}
finally {
    if (-not $process.HasExited) { Stop-Process -InputObject $process }
    $process.Dispose()
}
