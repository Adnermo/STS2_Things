[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$NativeGameDir,
    [Parameter(Mandatory)][string]$ImplementationDll,
    [Parameter(Mandatory)][string]$PackagePck,
    [Parameter(Mandatory)][string]$ForeignDll,
    [string]$SecondForeignDll='',
    [string]$ThirdForeignDll='',
    [Parameter(Mandatory)][string]$OutputDir,
    [ValidateSet('before','after')][string]$ForeignLoadOrder='before',
    [switch]$Baseline,
    [switch]$Cards,
    [string]$GodotExe='D:/Download/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64_console.exe'
)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskGame=[IO.Path]::GetFullPath($NativeGameDir)
$taskOutput=[IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$env:DOTNET_ROOT='C:/Program Files/dotnet'
$env:PATH='C:/Program Files/dotnet;'+$env:PATH
& dotnet build (Join-Path $taskRoot 'tools/PortraitCompatibilityProbe/PortraitCompatibilityProbe.csproj') --nologo "-p:Sts2DataDir=$taskGame/data_sts2_windows_x86_64" *> (Join-Path $taskOutput 'build.log')
if($LASTEXITCODE -ne 0){Get-Content -LiteralPath (Join-Path $taskOutput 'build.log') -Tail 15;throw 'Portrait probe build failed'}
$taskArgs=@('--headless','--path',('"'+(Join-Path $taskRoot 'tools/PortraitCompatibilityProbe')+'"'),'--rendering-method','gl_compatibility','--',('"'+(Join-Path $taskGame 'SlayTheSpire2.pck')+'"'),('"'+[IO.Path]::GetFullPath($PackagePck)+'"'),('"'+[IO.Path]::GetFullPath($ForeignDll)+'"'),('"'+[IO.Path]::GetFullPath($ImplementationDll)+'"'))
if($Baseline){$taskArgs+='--baseline'}
if($ForeignLoadOrder -eq 'after'){$taskArgs+='--early'}
if($Cards){$taskArgs+='--cards'}
if($SecondForeignDll){$taskArgs+=@('--second-foreign',('"'+[IO.Path]::GetFullPath($SecondForeignDll)+'"'))}
if($ThirdForeignDll){$taskArgs+=@('--third-foreign',('"'+[IO.Path]::GetFullPath($ThirdForeignDll)+'"'))}
$env:DOTNET_ROOT=Join-Path $taskRoot '.tmp/dotnet9'
$env:DOTNET_MULTILEVEL_LOOKUP='0'
$taskLog=Join-Path $taskOutput 'stdout.log'
$taskError=Join-Path $taskOutput 'stderr.log'
$taskProcess=Start-Process -FilePath $GodotExe -ArgumentList $taskArgs -WindowStyle Hidden -PassThru -RedirectStandardOutput $taskLog -RedirectStandardError $taskError
try{
    $taskStart=Get-Date
    while(-not $taskProcess.WaitForExit(30000)){
        Write-Output 'Headless portrait lifecycle checks are running...'
        if(((Get-Date)-$taskStart).TotalSeconds -gt 120){throw 'Portrait checks timed out'}
    }
    Get-Content -LiteralPath $taskLog -Tail 10
    if($taskProcess.ExitCode -ne 0){Get-Content -LiteralPath $taskError -Tail 8;throw "Portrait lifecycle check failed (exit $($taskProcess.ExitCode))"}
    if(Select-String -LiteralPath $taskError -Pattern '^ERROR:|SCRIPT ERROR' -Quiet){throw 'Portrait checks logged engine errors'}
    if(-not(Select-String -LiteralPath $taskLog -Pattern 'PORTRAIT_COMPAT_PASS' -Quiet)){throw 'Missing portrait PASS marker'}
}finally{
    if(-not $taskProcess.HasExited){Stop-Process -InputObject $taskProcess}
    $taskProcess.Dispose()
}
