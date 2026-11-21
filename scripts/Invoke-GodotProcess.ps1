function Format-SafeProcessArgument([string]$Value) {
    if ($null -eq $Value) { return '""' }
    $clean = $Value
    if ($clean.Length -ge 2 -and $clean.StartsWith('"') -and $clean.EndsWith('"')) {
        $clean = $clean.Substring(1, $clean.Length - 2).Replace('\"', '"')
    }
    if ($clean.Length -eq 0) { return '""' }
    if ($clean -notmatch '[\s"]') { return $clean }
    return '"' + ($clean -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Start-HeadlessSafeProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [int]$TimeoutMs = 60000,
        [string]$StdoutPath = '',
        [string]$StderrPath = ''
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    if ($null -ne $psi.PSObject.Properties['ArgumentList']) {
        foreach ($arg in $ArgumentList) {
            $clean = $arg
            if ($clean.Length -ge 2 -and $clean.StartsWith('"') -and $clean.EndsWith('"')) {
                $clean = $clean.Substring(1, $clean.Length - 2).Replace('\"', '"')
            }
            [void]$psi.ArgumentList.Add($clean)
        }
    }
    else {
        $psi.Arguments = (($ArgumentList | ForEach-Object { Format-SafeProcessArgument $_ }) -join ' ')
    }

    $redirectOut = -not [string]::IsNullOrWhiteSpace($StdoutPath)
    $redirectErr = -not [string]::IsNullOrWhiteSpace($StderrPath)
    $psi.RedirectStandardOutput = $redirectOut
    $psi.RedirectStandardError = $redirectErr

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    $outTask = $null
    $errTask = $null

    try {
        [void]$proc.Start()
        if ($redirectOut) { $outTask = $proc.StandardOutput.ReadToEndAsync() }
        if ($redirectErr) { $proc.StandardError.ReadToEndAsync() | ForEach-Object { $errTask = $_ } }

        $exited = $proc.WaitForExit($TimeoutMs)
        if (-not $exited) {
            try {
                if ($PSVersionTable.PSVersion.Major -ge 7) { $proc.Kill($true) }
                else { & taskkill.exe /PID $proc.Id /T /F *> $null }
            }
            catch {}
            return [pscustomobject]@{
                ExitCode = -1
                TimedOut = $true
            }
        }
        $proc.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $proc.ExitCode
            TimedOut = $false
        }
    }
    finally {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        if ($redirectOut) {
            $fullOut = [IO.Path]::GetFullPath($StdoutPath)
            $outDir = Split-Path -Parent $fullOut
            if ($outDir) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
            $outText = ''
            if ($null -ne $outTask -and $outTask.Wait(2000)) { $outText = $outTask.Result }
            [IO.File]::WriteAllText($fullOut, $outText, $utf8NoBom)
        }
        if ($redirectErr) {
            $fullErr = [IO.Path]::GetFullPath($StderrPath)
            $errDir = Split-Path -Parent $fullErr
            if ($errDir) { New-Item -ItemType Directory -Path $errDir -Force | Out-Null }
            $errText = ''
            if ($null -ne $errTask -and $errTask.Wait(2000)) { $errText = $errTask.Result }
            [IO.File]::WriteAllText($fullErr, $errText, $utf8NoBom)
        }
        $proc.Dispose()
    }
}
