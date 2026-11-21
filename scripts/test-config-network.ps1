[CmdletBinding()]
param(
    [ValidateSet('v111.1', 'v113.1')][string]$TargetVersion = 'v113.1',
    [string]$DataDir = '',
    [string]$ImplementationDll = '',
    [string]$Pck = '',
    [string]$RuntimeDependencyDir = '',
    [string]$GodotExe = '',
    [string]$OutputDirectory = 'build\multiplayer-config-20261002\enet',
    [ushort]$Port = 29342
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskProbe = Join-Path $taskRoot 'tools\ThingsConfigProbe'
$taskOutput = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))
if (-not $taskOutput.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must stay inside the project.' }
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
$taskData = [IO.Path]::GetFullPath($DataDir)
$candidateGamePck = Join-Path (Split-Path $taskData -Parent) 'SlayTheSpire2.pck'
if (Test-Path -LiteralPath $candidateGamePck) {
    $env:THINGS_PROBE_GAME_PCK = $candidateGamePck
}

if ([string]::IsNullOrWhiteSpace($RuntimeDependencyDir)) {
    $RuntimeDependencyDir = $taskData
}
$taskRuntime = [IO.Path]::GetFullPath($RuntimeDependencyDir)

if ([string]::IsNullOrWhiteSpace($ImplementationDll)) {
    $ImplementationDll = Join-Path $taskRoot "build\$TargetVersion\STS2_Things.dll"
}
$taskDll = [IO.Path]::GetFullPath($ImplementationDll)

if ([string]::IsNullOrWhiteSpace($Pck)) {
    foreach ($candidateModPck in @(
        (Join-Path $taskRoot "build\$TargetVersion\STS2_Things.pck"),
        (Join-Path $taskRoot 'build\unified\STS2_Things.pck')
    )) {
        if (Test-Path -LiteralPath $candidateModPck -PathType Leaf) {
            $Pck = $candidateModPck
            break
        }
    }
}
$taskPack = [IO.Path]::GetFullPath($Pck)
if (-not $env:THINGS_PROBE_PCK) {
    $env:THINGS_PROBE_PCK = $taskPack
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
& 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $taskProbe 'ThingsConfigProbe.csproj') -t:Rebuild -c Debug --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$taskData" "/p:RuntimeDependencyDir=$taskRuntime" "/p:ImplementationDll=$taskDll"
if ($LASTEXITCODE -ne 0) { throw 'Network probe build failed.' }

$env:DOTNET_ROOT = Join-Path $taskRoot '.tmp\dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$taskProcesses = @()
$taskRunners = @()
try {
    foreach ($taskRole in @('host', 'client1', 'client2')) {
        $taskRunner = Join-Path $taskOutput $taskRole
        if (Test-Path -LiteralPath $taskRunner) { throw "Runner already exists: $taskRunner" }
        New-Item -ItemType Directory -Path $taskRunner | Out-Null
        $taskRunners += $taskRunner
        Get-ChildItem -LiteralPath $taskProbe -File | Where-Object { $_.Extension -in '.cs', '.tscn' } | Copy-Item -Destination $taskRunner
        $taskSettings = (Get-Content -LiteralPath (Join-Path $taskProbe 'project.godot') -Raw).Replace('Things Config Probe', 'Things Config Probe ENet ' + $taskRole)
        $taskSettings | Set-Content -LiteralPath (Join-Path $taskRunner 'project.godot') -Encoding utf8
        $taskBin = Join-Path $taskRunner '.godot\mono\temp\bin'
        New-Item -ItemType Directory -Path $taskBin -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $taskProbe '.godot\mono\temp\bin\Debug') -Destination $taskBin -Recurse
        foreach ($taskName in @($taskRole + '.result', 'host-ready')) {
            $taskOld = Join-Path $taskOutput $taskName
            if (Test-Path -LiteralPath $taskOld) { Remove-Item -LiteralPath $taskOld }
        }
        $taskArguments = @('--headless', '--path', ('"{0}"' -f $taskRunner), '--', '"' + $taskPack + '"', $taskRole, $Port, ('"{0}"' -f $taskOutput))
        $taskProcess = Start-Process -FilePath $GodotExe -ArgumentList $taskArguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskOutput ($taskRole + '.stdout.log')) -RedirectStandardError (Join-Path $taskOutput ($taskRole + '.stderr.log'))
        $taskProcesses += $taskProcess
        if ($taskRole -eq 'host') {
            $taskDeadline = [DateTime]::UtcNow.AddSeconds(15)
            while (-not (Test-Path -LiteralPath (Join-Path $taskOutput 'host-ready'))) {
                if ($taskProcess.HasExited -or [DateTime]::UtcNow -gt $taskDeadline) { throw 'Host did not become ready. Inspect host logs.' }
                Start-Sleep -Milliseconds 200
            }
        }
    }
    $taskDeadline = [DateTime]::UtcNow.AddSeconds(40)
    while (@($taskProcesses | Where-Object { -not $_.HasExited }).Count -gt 0) {
        if ([DateTime]::UtcNow -gt $taskDeadline) { throw 'Network probe timed out.' }
        Start-Sleep -Milliseconds 300
    }
    foreach ($taskRole in @('host', 'client1', 'client2')) {
        $taskResult = Join-Path $taskOutput ($taskRole + '.result')
        if (-not (Test-Path -LiteralPath $taskResult)) { throw "Missing success report: $taskRole" }
        Get-Content -LiteralPath $taskResult
    }
    $taskReports = @('host', 'client1', 'client2') | ForEach-Object { Get-Content -LiteralPath (Join-Path $taskOutput ($_ + '.result')) }
    if (@($taskReports | Select-Object -Unique).Count -ne 1) { throw 'Room digests differed between processes.' }
    Write-Host 'Three-process ENet config probe: PASS'
}
finally {
    foreach ($taskProcess in $taskProcesses) {
        if (-not $taskProcess.HasExited) { Stop-Process -InputObject $taskProcess }
        $taskProcess.Dispose()
    }
    foreach ($taskRunner in $taskRunners) {
        $taskResolved = [IO.Path]::GetFullPath($taskRunner)
        if ((Split-Path -Parent $taskResolved) -ne $taskOutput -or (Split-Path -Leaf $taskResolved) -notin @('host', 'client1', 'client2')) { throw 'Unsafe runner cleanup target.' }
        Remove-Item -LiteralPath $taskResolved -Recurse -Force
    }
}
