param(
 [ValidateSet('v107.1','v111')][string]$TargetVersion='v111',
 [string]$DataDir,
 [string]$RuntimeDependencyDir,
 [string]$GamePck,
 [string]$ImplementationDll,
 [string]$OutputDir
)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$taskOut=if($OutputDir){[IO.Path]::GetFullPath($OutputDir)}else{Join-Path $taskRoot ('build/bug-report-validation-'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
New-Item -ItemType Directory -Path $taskOut -Force | Out-Null
$taskData=if($DataDir){[IO.Path]::GetFullPath($DataDir)}else{Join-Path $taskRoot $(if($TargetVersion -eq 'v111'){'.tmp/verified-v111-runtime'}else{'.tmp/things-collision-v107-data'})}
$taskRuntime=if($RuntimeDependencyDir){[IO.Path]::GetFullPath($RuntimeDependencyDir)}else{$taskData}
$taskPck=if($GamePck){[IO.Path]::GetFullPath($GamePck)}else{Join-Path (Split-Path $taskData) 'SlayTheSpire2.pck'}
if(-not(Test-Path -LiteralPath $taskPck -PathType Leaf)){throw 'Pass -GamePck for the full matching game version; a reference DLL is not a client resource pack'}
$taskImplementation=if($ImplementationDll){[IO.Path]::GetFullPath($ImplementationDll)}else{Join-Path $taskOut "$TargetVersion/STS2_Things.dll"}
$env:DOTNET_ROOT='C:/Program Files/dotnet'
if(-not $ImplementationDll){
 & 'C:/Program Files/dotnet/dotnet.exe' build (Join-Path $taskRoot 'STS2_Things.csproj') -c Release --nologo -o (Split-Path $taskImplementation) "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$taskData"
 if($LASTEXITCODE -ne 0){throw 'Report implementation build failed'}
}
& 'C:/Program Files/dotnet/dotnet.exe' build (Join-Path $taskRoot 'tools/BugReportProbe/BugReportProbe.csproj') --nologo "/p:Sts2TargetVersion=$TargetVersion" "/p:Sts2DataDir=$taskData" "/p:RuntimeDependencyDir=$taskRuntime" "/p:ImplementationDll=$taskImplementation"
if($LASTEXITCODE -ne 0){throw 'Report native probe build failed'}
$env:DOTNET_ROOT=Join-Path $taskRoot '.tmp/dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP='0'
$env:THINGS_PROBE_GAME_PCK=$taskPck
$env:PATH=$env:DOTNET_ROOT+';'+$env:PATH
$taskGodot='D:/Download/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64_console.exe'
$taskStdout=Join-Path $taskOut "native-$TargetVersion.log"
$taskStderr=Join-Path $taskOut "native-$TargetVersion.stderr.log"
$taskProcess=Start-Process -FilePath $taskGodot -ArgumentList @('--headless','--path',('"'+(Join-Path $taskRoot 'tools/BugReportProbe')+'"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput $taskStdout -RedirectStandardError $taskStderr
try{
  if(-not $taskProcess.WaitForExit(30000)){throw 'Report native probe exceeded 30 seconds'}
  if($taskProcess.ExitCode -ne 0){Get-Content -LiteralPath $taskStderr -Tail 15;throw 'Report native probe failed'}
}finally{if(-not $taskProcess.HasExited){Stop-Process -InputObject $taskProcess};$taskProcess.Dispose()}
$taskErrors=Get-Content -LiteralPath $taskStderr | Where-Object {($_ -match '^ERROR:|SCRIPT ERROR') -and $_ -ne 'ERROR: System.InvalidOperationException: private engine error'}
if($taskErrors){$taskErrors;throw 'Unexpected report native engine errors'}
if(-not(Select-String -LiteralPath $taskStdout -Pattern 'BugReport native probe: PASS' -Quiet)){Get-Content -LiteralPath $taskStdout -Tail 10;throw 'Native report PASS marker missing'}
Get-Content -LiteralPath $taskStdout -Tail 3
