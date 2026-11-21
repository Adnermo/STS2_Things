[CmdletBinding()]
param(
    [string]$LogDirectory = 'build\reverse_salamander',
    [string]$DataDirV1111 = $env:STS2_DATA_DIR_V111_1,
    [string]$DataDirV1131 = $env:STS2_DATA_DIR_V113_1,
    [string]$GodotExe = $env:GODOT_4_5_1_MONO
)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskLog = Join-Path $taskRoot $LogDirectory
$taskDotnet = 'C:\Program Files\dotnet\dotnet.exe'

if ([string]::IsNullOrWhiteSpace($GodotExe)) {
    if (Test-Path -LiteralPath 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' -PathType Leaf) {
        $GodotExe = 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
    else {
        $GodotExe = 'D:\Download\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe'
    }
}

if ([string]::IsNullOrWhiteSpace($DataDirV1111)) {
    $DataDirV1111 = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v111.1\b0460083b012\game\data_sts2_windows_x86_64'
}
if ([string]::IsNullOrWhiteSpace($DataDirV1131)) {
    $candidateV1131 = 'D:\Things\Things-Workspace\STS2-MCP\.state\runtimes\v113.1\76cc75b6f097\game\data_sts2_windows_x86_64'
    if (Test-Path -LiteralPath $candidateV1131 -PathType Container) {
        $DataDirV1131 = $candidateV1131
    }
    else {
        $DataDirV1131 = 'D:\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64'
    }
}

$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $taskLog, (Join-Path $taskRoot 'build\v111.1'), (Join-Path $taskRoot 'build\v113.1'), (Join-Path $taskRoot 'build\unified') -Force | Out-Null

# This request explicitly excludes tests and live probes. These steps only
# compile the deliverable, import assets, and export the distributable package.
& $taskDotnet build (Join-Path $taskRoot 'STS2_Things.csproj') -c Release --nologo -o (Join-Path $taskLog 'v111.1') '/p:Sts2TargetVersion=v111.1' "/p:Sts2DataDir=$DataDirV1111" *> (Join-Path $taskLog 'compile-v111.1.log')
if ($LASTEXITCODE -ne 0) { throw 'v111.1 compilation failed; see compile-v111.1.log' }
Copy-Item -LiteralPath (Join-Path $taskLog 'v111.1\STS2_Things.dll') -Destination (Join-Path $taskRoot 'build\v111.1\STS2_Things.dll') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'manifests\v111.1\STS2_Things.json') -Destination (Join-Path $taskRoot 'build\v111.1\STS2_Things.json') -Force

& $taskDotnet build (Join-Path $taskRoot 'STS2_Things.csproj') -c Release --nologo '/p:Sts2TargetVersion=v113.1' "/p:Sts2DataDir=$DataDirV1131" *> (Join-Path $taskLog 'compile-v113.1.log')
if ($LASTEXITCODE -ne 0) { throw 'v113.1 compilation failed; see compile-v113.1.log' }
Copy-Item -LiteralPath (Join-Path $taskRoot '.godot\mono\temp\bin\Release\STS2_Things.dll') -Destination (Join-Path $taskRoot 'build\v113.1\STS2_Things.dll') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'manifests\v113.1\STS2_Things.json') -Destination (Join-Path $taskRoot 'build\v113.1\STS2_Things.json') -Force

& $GodotExe --headless --editor --path $taskRoot --import --quit --log-file (Join-Path $taskLog 'import.log') *> (Join-Path $taskLog 'import-console.log')
if ($LASTEXITCODE -ne 0) { throw 'Godot asset import failed' }
if (Select-String -LiteralPath (Join-Path $taskLog 'import.log') -Pattern '^ERROR:|SCRIPT ERROR:' -Quiet) { throw 'Godot asset import reported errors' }
& $GodotExe --headless --editor --path $taskRoot --export-pack 'STS2 PCK' (Join-Path $taskRoot 'build\v113.1\STS2_Things.pck') --quit --log-file (Join-Path $taskLog 'export.log') *> (Join-Path $taskLog 'export-console.log')
if ($LASTEXITCODE -ne 0) { throw 'Godot package export failed' }
if (Select-String -LiteralPath (Join-Path $taskLog 'export.log') -Pattern '^ERROR:|SCRIPT ERROR:' -Quiet) { throw 'Godot package export reported errors' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'build\v113.1\STS2_Things.pck') -Destination (Join-Path $taskRoot 'build\v111.1\STS2_Things.pck') -Force

Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
& $taskDotnet build (Join-Path $taskRoot 'bootstrap\STS2_Things.Bootstrap.csproj') -c Release --nologo "/p:Sts2DataDir=$DataDirV1111" "/p:ImplementationV1111=$(Join-Path $taskRoot 'build\v111.1\STS2_Things.dll')" "/p:ImplementationV1131=$(Join-Path $taskRoot 'build\v113.1\STS2_Things.dll')" *> (Join-Path $taskLog 'compile-bootstrap.log')
if ($LASTEXITCODE -ne 0) { throw 'Bootstrap compilation failed' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'bootstrap\bin\Release\net9.0\STS2_Things.Bootstrap.dll') -Destination (Join-Path $taskRoot 'build\unified\STS2_Things.dll') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'build\v113.1\STS2_Things.pck'), (Join-Path $taskRoot 'STS2_Things.json') -Destination (Join-Path $taskRoot 'build\unified') -Force

$taskBridgeRef = 'D:\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib\BaseLib.dll'
if (Test-Path -LiteralPath $taskBridgeRef) {
    Remove-Item Env:DOTNET_ROOT, Env:DOTNET_MULTILEVEL_LOOKUP -ErrorAction SilentlyContinue
    & $taskDotnet build (Join-Path $taskRoot 'bridges\STS2_Things.BaseLibBridge\STS2_Things.BaseLibBridge.csproj') -c Release --nologo "/p:BaseLibRef=$taskBridgeRef" "/p:Sts2DataDir=$DataDirV1131" *> (Join-Path $taskLog 'compile-bridge.log')
    if ($LASTEXITCODE -ne 0) { throw 'Optional bridge compilation failed' }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'bridges\STS2_Things.BaseLibBridge\.godot\mono\temp\bin\Release\STS2_Things.BaseLibBridge.dll') -Destination (Join-Path $taskRoot 'build\unified\STS2_Things.BaseLibBridge.dll') -Force
}
Write-Output 'Compilation and export complete. No tests, probes, or game interactions were run.'
