<#
.SYNOPSIS
    Publishes an STS2_Things release (unified ZIP + unpacked mod files + release notes + SHA256SUMS)
    to FlareDrive (Cloudflare Worker + R2 bucket `adnermo-pan`) via WebDAV + R2 Multipart Upload,
    and updates `STS2_Things/_latest.json`.

.EXAMPLE
    .\scripts\publish-r2-release.ps1 -Version 2.0.2 -ZipPath .\dist\v2.0.2\STS2_Things-2.0.2-unified.zip
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$ZipPath,

    [string]$ReleaseNotesPath,

    [string]$Sha256SumsPath,

    [string]$UnpackedDir,

    [string]$BaseUrl = 'https://pan.adnermo.online',

    [string]$Username,

    [string]$Password,

    [int]$ChunkSizeMB = 24,

    [switch]$NoSetLatest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression.FileSystem

try {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072 -bor 12288
} catch {
    try {
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
    } catch {}
}
[System.Net.ServicePointManager]::DefaultConnectionLimit = 16

if ($ChunkSizeMB -lt 5) {
    throw "ChunkSizeMB must be at least 5 (Cloudflare R2 minimum multipart part size is 5 MiB)."
}

# Normalize Version -> semver + v-prefixed tag
$cleanVer = $Version.Trim()
if ($cleanVer -match '^[vV](.+)$') {
    $semver = $Matches[1]
} else {
    $semver = $cleanVer
}
if ($semver -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid semantic version '$Version'. Expected format like '2.0.2' or 'v2.0.2'."
}
$tag = "v$semver"
$BaseUrl = $BaseUrl.TrimEnd('/')
$RepoRoot = Split-Path -Parent $PSScriptRoot

function Read-DevVars {
    param([string]$FilePath)
    $map = @{}
    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        return $map
    }
    $lines = [System.IO.File]::ReadAllLines($FilePath, [System.Text.Encoding]::UTF8)
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#')) {
            continue
        }
        if ($trimmed -match '^([A-Za-z0-9_]+)\s*=\s*(.*)$') {
            $k = $Matches[1].Trim()
            $v = $Matches[2].Trim()
            if (($v.StartsWith('"') -and $v.EndsWith('"')) -or ($v.StartsWith("'") -and $v.EndsWith("'"))) {
                $v = $v.Substring(1, $v.Length - 2)
            }
            $map[$k] = $v
        }
    }
    return $map
}

function Resolve-WebDavCredentials {
    param(
        [string]$ExplicitUser,
        [string]$ExplicitPass,
        [string]$Root
    )
    $devVarsPath = Join-Path $Root 'services\flaredrive\.dev.vars'
    $devVars = Read-DevVars -FilePath $devVarsPath

    $user = $ExplicitUser
    if ([string]::IsNullOrWhiteSpace($user)) { $user = $env:WEBDAV_USERNAME }
    if ([string]::IsNullOrWhiteSpace($user) -and $devVars.ContainsKey('WEBDAV_USERNAME')) { $user = $devVars['WEBDAV_USERNAME'] }

    $pass = $ExplicitPass
    if ([string]::IsNullOrWhiteSpace($pass)) { $pass = $env:WEBDAV_PASSWORD }
    if ([string]::IsNullOrWhiteSpace($pass) -and $devVars.ContainsKey('WEBDAV_PASSWORD')) { $pass = $devVars['WEBDAV_PASSWORD'] }

    if ([string]::IsNullOrWhiteSpace($user) -or [string]::IsNullOrWhiteSpace($pass)) {
        throw "WebDAV credentials not found. Provide -Username/-Password, set WEBDAV_USERNAME/WEBDAV_PASSWORD env vars, or configure services\flaredrive\.dev.vars."
    }

    $raw = "${user}:${pass}"
    $b64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($raw))
    return @{
        Username   = $user
        AuthHeader = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Basic', $b64)
    }
}

function Get-FileSha256Hex {
    param([string]$FilePath)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $fs = [System.IO.File]::Open($FilePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        $hash = $sha.ComputeHash($fs)
        return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
    } finally {
        $fs.Dispose()
        $sha.Dispose()
    }
}

function Encode-KeyPath {
    param([string]$Key)
    $segments = $Key.Trim('/').Split('/') | Where-Object { $_ -ne '' } | ForEach-Object { [System.Uri]::EscapeDataString($_) }
    return ($segments -join '/')
}

function Get-ContentTypeForName {
    param([string]$Name)
    $ext = [System.IO.Path]::GetExtension($Name).ToLowerInvariant()
    switch ($ext) {
        '.zip'  { return 'application/zip' }
        '.json' { return 'application/json; charset=utf-8' }
        '.md'   { return 'text/markdown; charset=utf-8' }
        '.txt'  { return 'text/plain; charset=utf-8' }
        default { return 'application/octet-stream' }
    }
}

function Format-ByteSize {
    param([long]$Bytes)
    if ($Bytes -ge 1GB) { return ('{0:N2} GB' -f ($Bytes / 1GB)) }
    if ($Bytes -ge 1MB) { return ('{0:N2} MB' -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ('{0:N1} KB' -f ($Bytes / 1KB)) }
    return "$Bytes B"
}

function Invoke-HttpWithRetry {
    param(
        [System.Net.Http.HttpClient]$Client,
        [scriptblock]$RequestFactory,
        [string]$ActionLabel,
        [int[]]$AcceptedStatuses = @(),
        [int]$MaxAttempts = 4
    )
    $lastErr = $null
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $req = & $RequestFactory
        $resp = $null
        try {
            $resp = $Client.SendAsync($req, [System.Net.Http.HttpCompletionOption]::ResponseContentRead).GetAwaiter().GetResult()
            $status = [int]$resp.StatusCode
            $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $isAccepted = ($status -ge 200 -and $status -lt 300) -or ($AcceptedStatuses -contains $status)
            if ($isAccepted) {
                $etagVal = $null
                $vals = $null
                if ($resp.Headers.TryGetValues('etag', [ref]$vals)) {
                    $etagVal = ($vals | Select-Object -First 1).Trim()
                } elseif ($null -ne $resp.Headers.ETag) {
                    $etagVal = $resp.Headers.ETag.Tag
                }
                return @{
                    Status        = $status
                    Body          = $body
                    ETag          = $etagVal
                    ContentLength = $resp.Content.Headers.ContentLength
                }
            }
            $snippet = if ($body.Length -gt 300) { $body.Substring(0, 300) } else { $body }
            $lastErr = "${ActionLabel} failed with HTTP ${status}: ${snippet}"
            if ($status -lt 500 -and $status -ne 429 -and $status -ne 408) {
                throw $lastErr
            }
        } catch {
            $lastErr = $_.Exception.Message
            if ($attempt -eq $MaxAttempts) {
                throw
            }
        } finally {
            if ($null -ne $resp) { $resp.Dispose() }
            if ($null -ne $req) { $req.Dispose() }
        }
        $delaySec = [Math]::Min(16, [Math]::Pow(2, $attempt - 1))
        Write-Warning "${ActionLabel} attempt $attempt/$MaxAttempts failed ($lastErr). Retrying in ${delaySec}s..."
        Start-Sleep -Seconds $delaySec
    }
    throw "${ActionLabel} failed after $MaxAttempts attempts: $lastErr"
}

function Ensure-WebDavFolder {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Base,
        [System.Net.Http.Headers.AuthenticationHeaderValue]$AuthHeader,
        [string]$FolderKey
    )
    $encoded = Encode-KeyPath $FolderKey
    $url = "$Base/webdav/$encoded"
    $mkcolMethod = New-Object System.Net.Http.HttpMethod('MKCOL')
    $res = Invoke-HttpWithRetry `
        -Client $Client `
        -ActionLabel "MKCOL $FolderKey" `
        -AcceptedStatuses @(405, 409) `
        -RequestFactory {
            $req = New-Object System.Net.Http.HttpRequestMessage($mkcolMethod, $url)
            $req.Headers.Authorization = $AuthHeader
            return $req
        }
    if ($res.Status -eq 201) {
        Write-Host "[DIR ] Created folder /webdav/$FolderKey"
    } else {
        Write-Host "[DIR ] Folder /webdav/$FolderKey exists (HTTP $($res.Status))"
    }
}

function Send-SmallBytesWebDav {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Base,
        [System.Net.Http.Headers.AuthenticationHeaderValue]$AuthHeader,
        [string]$Key,
        [byte[]]$Bytes,
        [string]$ContentType
    )
    $url = "$Base/webdav/$(Encode-KeyPath $Key)"
    $null = Invoke-HttpWithRetry -Client $Client -ActionLabel "PUT $Key" -RequestFactory {
        $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Put, $url)
        $req.Headers.Authorization = $AuthHeader
        $content = New-Object System.Net.Http.ByteArrayContent($Bytes, 0, $Bytes.Length)
        $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ContentType)
        $req.Content = $content
        return $req
    }
}

function Send-FileViaFlareDrive {
    param(
        [System.Net.Http.HttpClient]$Client,
        [string]$Base,
        [System.Net.Http.Headers.AuthenticationHeaderValue]$AuthHeader,
        [string]$Key,
        [string]$FilePath,
        [long]$Size,
        [string]$ContentType,
        [int]$ChunkBytes
    )
    $encodedKey = Encode-KeyPath $Key
    $objectUrl = "$Base/webdav/$encodedKey"

    if ($Size -le $ChunkBytes) {
        Write-Host ("[PUT ] {0} ({1})..." -f $Key, (Format-ByteSize $Size))
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $null = Invoke-HttpWithRetry -Client $Client -ActionLabel "PUT $Key" -RequestFactory {
            $fs = [System.IO.File]::Open($FilePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
            $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Put, $objectUrl)
            $req.Headers.Authorization = $AuthHeader
            $content = New-Object System.Net.Http.StreamContent($fs)
            $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ContentType)
            $content.Headers.ContentLength = $Size
            $req.Content = $content
            return $req
        }
        $sw.Stop()
        Write-Host ("[ OK ] {0} uploaded in {1:N1}s" -f $Key, $sw.Elapsed.TotalSeconds)
        return
    }

    # FlareDrive native R2 Multipart Upload:
    # 1. POST /webdav/<key>?uploads -> { key, uploadId }
    # 2. PUT  /webdav/<key>?uploadId=<id>&partNumber=<n> -> header etag
    # 3. POST /webdav/<key>?uploadId=<id> with JSON { parts: [{ partNumber, etag }, ...] }
    $totalParts = [int][Math]::Ceiling($Size / [double]$ChunkBytes)
    Write-Host ("[INIT] {0} ({1}, {2} parts x {3} MiB)..." -f $Key, (Format-ByteSize $Size), $totalParts, [int]($ChunkBytes / 1MB))

    $initRes = Invoke-HttpWithRetry -Client $Client -ActionLabel "POST ?uploads ($Key)" -RequestFactory {
        $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "${objectUrl}?uploads")
        $req.Headers.Authorization = $AuthHeader
        $emptyBytes = New-Object byte[] 0
        $content = New-Object System.Net.Http.ByteArrayContent($emptyBytes, 0, 0)
        $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ContentType)
        $req.Content = $content
        return $req
    }
    $initJson = $initRes.Body | ConvertFrom-Json
    $uploadId = [string]$initJson.uploadId
    if ([string]::IsNullOrWhiteSpace($uploadId)) {
        throw "Multipart init returned empty uploadId for $Key"
    }

    $encodedUploadId = [System.Uri]::EscapeDataString($uploadId)
    $parts = New-Object System.Collections.Generic.List[object]
    $buffer = New-Object byte[] $ChunkBytes
    $fs = [System.IO.File]::Open($FilePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    $totalSw = [System.Diagnostics.Stopwatch]::StartNew()
    $uploadedBytes = [long]0

    try {
        for ($partNumber = 1; $partNumber -le $totalParts; $partNumber++) {
            $filled = 0
            while ($filled -lt $ChunkBytes) {
                $read = $fs.Read($buffer, $filled, $ChunkBytes - $filled)
                if ($read -le 0) { break }
                $filled += $read
            }
            if ($filled -le 0) { break }

            $partSw = [System.Diagnostics.Stopwatch]::StartNew()
            $partUrl = "${objectUrl}?uploadId=${encodedUploadId}&partNumber=${partNumber}"
            $partBytesCount = $filled

            $partRes = Invoke-HttpWithRetry -Client $Client -ActionLabel "PART $partNumber/$totalParts ($Key)" -RequestFactory {
                $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Put, $partUrl)
                $req.Headers.Authorization = $AuthHeader
                $content = New-Object System.Net.Http.ByteArrayContent($buffer, 0, $partBytesCount)
                $content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/octet-stream')
                $req.Content = $content
                return $req
            }
            $partSw.Stop()

            if ([string]::IsNullOrWhiteSpace($partRes.ETag)) {
                throw "Part $partNumber for $Key did not return an ETag header."
            }
            $parts.Add([PSCustomObject]@{
                partNumber = [int]$partNumber
                etag       = [string]$partRes.ETag
            })

            $uploadedBytes += $partBytesCount
            $pct = [Math]::Min(100.0, ($uploadedBytes * 100.0) / $Size)
            $speedMBs = if ($partSw.Elapsed.TotalSeconds -gt 0.01) { ($partBytesCount / 1MB) / $partSw.Elapsed.TotalSeconds } else { 0 }
            Write-Host ("  [PART {0,2}/{1}] {2} / {3} ({4:N1}%, {5:N1} MB/s)" -f $partNumber, $totalParts, (Format-ByteSize $uploadedBytes), (Format-ByteSize $Size), $pct, $speedMBs)
        }

        $completePayload = ([PSCustomObject]@{
            parts = @($parts.ToArray())
        } | ConvertTo-Json -Depth 5 -Compress)

        $null = Invoke-HttpWithRetry -Client $Client -ActionLabel "COMPLETE ($Key)" -RequestFactory {
            $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "${objectUrl}?uploadId=${encodedUploadId}")
            $req.Headers.Authorization = $AuthHeader
            $req.Content = New-Object System.Net.Http.StringContent($completePayload, [System.Text.Encoding]::UTF8, 'application/json')
            return $req
        }
        $totalSw.Stop()
        $avgMBs = if ($totalSw.Elapsed.TotalSeconds -gt 0.01) { ($Size / 1MB) / $totalSw.Elapsed.TotalSeconds } else { 0 }
        Write-Host ("[ OK ] {0} completed in {1:N1}s (avg {2:N1} MB/s)" -f $Key, $totalSw.Elapsed.TotalSeconds, $avgMBs)
    } catch {
        Write-Warning "Multipart upload failed for $Key; aborting uploadId=$uploadId..."
        try {
            $abortReq = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Delete, "${objectUrl}?uploadId=${encodedUploadId}")
            $abortReq.Headers.Authorization = $AuthHeader
            $abortResp = $Client.SendAsync($abortReq).GetAwaiter().GetResult()
            $abortResp.Dispose()
            $abortReq.Dispose()
        } catch {}
        throw
    } finally {
        $fs.Dispose()
    }
}

# Main execution
$creds = Resolve-WebDavCredentials -ExplicitUser $Username -ExplicitPass $Password -Root $RepoRoot

$resolvedZip = (Resolve-Path -LiteralPath $ZipPath).Path
if (-not (Test-Path -LiteralPath $resolvedZip -PathType Leaf)) {
    throw "ZipPath not found: $ZipPath"
}
$zipDir = Split-Path -Parent $resolvedZip

if ([string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
    $candidate = Join-Path $zipDir 'RELEASE_NOTES.md'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { $ReleaseNotesPath = $candidate }
} elseif (-not (Test-Path -LiteralPath $ReleaseNotesPath -PathType Leaf)) {
    throw "ReleaseNotesPath not found: $ReleaseNotesPath"
}

if ([string]::IsNullOrWhiteSpace($Sha256SumsPath)) {
    $candidate = Join-Path $zipDir 'SHA256SUMS.txt'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) { $Sha256SumsPath = $candidate }
} elseif (-not (Test-Path -LiteralPath $Sha256SumsPath -PathType Leaf)) {
    throw "Sha256SumsPath not found: $Sha256SumsPath"
}

$tempExtractDir = $null
$httpHandler = New-Object System.Net.Http.HttpClientHandler
$httpHandler.AutomaticDecompression = [System.Net.DecompressionMethods]::None
$httpClient = New-Object System.Net.Http.HttpClient($httpHandler)
$httpClient.Timeout = [TimeSpan]::FromMinutes(15)

try {
    # 1. Verify write auth and ensure parent folders exist via MKCOL
    Write-Host "Ensuring FlareDrive folders on $BaseUrl (user: $($creds.Username))..."
    Ensure-WebDavFolder -Client $httpClient -Base $BaseUrl -AuthHeader $creds.AuthHeader -FolderKey 'STS2_Things'
    Ensure-WebDavFolder -Client $httpClient -Base $BaseUrl -AuthHeader $creds.AuthHeader -FolderKey "STS2_Things/$tag"

    # 2. Resolve unpacked directory (or extract zip into temp staging dir)
    $sourceUnpackedDir = $null
    if (-not [string]::IsNullOrWhiteSpace($UnpackedDir)) {
        $sourceUnpackedDir = (Resolve-Path -LiteralPath $UnpackedDir).Path
        if (-not (Test-Path -LiteralPath $sourceUnpackedDir -PathType Container)) {
            throw "UnpackedDir not found: $UnpackedDir"
        }
    } else {
        $tempExtractDir = Join-Path ([System.IO.Path]::GetTempPath()) ("sts2_r2_pub_" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tempExtractDir -Force | Out-Null
        Write-Host "Extracting $resolvedZip to temporary staging directory..."
        [System.IO.Compression.ZipFile]::ExtractToDirectory($resolvedZip, $tempExtractDir)
        $sourceUnpackedDir = $tempExtractDir
    }

    # 3. Build upload plan under STS2_Things/$tag/
    $uploadPlan = New-Object System.Collections.Generic.List[object]
    $seenNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

    $addFileToPlan = {
        param([string]$LocalPath)
        $fi = Get-Item -LiteralPath $LocalPath
        if (-not $seenNames.Add($fi.Name)) { return }
        Write-Host ("Hashing {0} ({1})..." -f $fi.Name, (Format-ByteSize $fi.Length))
        $sha = Get-FileSha256Hex -FilePath $fi.FullName
        $uploadPlan.Add([PSCustomObject]@{
            Name        = $fi.Name
            LocalPath   = $fi.FullName
            Key         = "STS2_Things/$tag/$($fi.Name)"
            Size        = [long]$fi.Length
            Sha256      = $sha
            ContentType = Get-ContentTypeForName $fi.Name
        })
    }

    & $addFileToPlan $resolvedZip

    $unpackedFiles = @(Get-ChildItem -LiteralPath $sourceUnpackedDir -File -Recurse | Sort-Object Name)
    if ($unpackedFiles.Count -eq 0) {
        throw "No unpacked files found in $sourceUnpackedDir"
    }
    foreach ($uf in $unpackedFiles) {
        & $addFileToPlan $uf.FullName
    }

    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
        & $addFileToPlan ((Resolve-Path -LiteralPath $ReleaseNotesPath).Path)
    }
    if (-not [string]::IsNullOrWhiteSpace($Sha256SumsPath)) {
        & $addFileToPlan ((Resolve-Path -LiteralPath $Sha256SumsPath).Path)
    }

    # Cross-check against SHA256SUMS.txt if present
    if (-not [string]::IsNullOrWhiteSpace($Sha256SumsPath)) {
        $sumLines = [System.IO.File]::ReadAllLines((Resolve-Path -LiteralPath $Sha256SumsPath).Path, [System.Text.Encoding]::UTF8)
        $expectedSums = @{}
        foreach ($line in $sumLines) {
            if ($line.Trim() -match '^([0-9a-fA-F]{64})\s+\*?(.+)$') {
                $expectedSums[$Matches[2].Trim()] = $Matches[1].ToLowerInvariant()
            }
        }
        foreach ($item in $uploadPlan) {
            if ($expectedSums.ContainsKey($item.Name)) {
                if ($expectedSums[$item.Name] -ne $item.Sha256) {
                    throw "SHA256 mismatch for $($item.Name)! Local=$($item.Sha256), SHA256SUMS.txt=$($expectedSums[$item.Name])"
                }
            }
        }
        Write-Host "SHA256SUMS.txt verified against all local files."
    }

    # 4. Upload all files via FlareDrive WebDAV / Multipart API
    $chunkBytes = $ChunkSizeMB * 1024 * 1024
    foreach ($item in $uploadPlan) {
        Send-FileViaFlareDrive `
            -Client $httpClient `
            -Base $BaseUrl `
            -AuthHeader $creds.AuthHeader `
            -Key $item.Key `
            -FilePath $item.LocalPath `
            -Size $item.Size `
            -ContentType $item.ContentType `
            -ChunkBytes $chunkBytes
    }

    $zipEntry = $uploadPlan | Where-Object { $_.Name.ToLowerInvariant().EndsWith('.zip') } | Select-Object -First 1

    # 5. Update STS2_Things/_latest.json (unless -NoSetLatest)
    if (-not $NoSetLatest) {
        Write-Host "Updating STS2_Things/_latest.json -> $tag..."
        $latestDoc = [PSCustomObject]@{
            version   = $semver
            tag       = $tag
            zip       = $zipEntry.Name
            updatedAt = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
        }
        $latestJson = ($latestDoc | ConvertTo-Json -Depth 4)
        $latestBytes = [System.Text.Encoding]::UTF8.GetBytes($latestJson)
        Send-SmallBytesWebDav `
            -Client $httpClient `
            -Base $BaseUrl `
            -AuthHeader $creds.AuthHeader `
            -Key 'STS2_Things/_latest.json' `
            -Bytes $latestBytes `
            -ContentType 'application/json; charset=utf-8'
    }

    # 6. Verify public unauthenticated HEAD 200 OK + exact Content-Length on every uploaded file
    Write-Host "Verifying public unauthenticated HEAD responses and exact Content-Length..."
    foreach ($item in $uploadPlan) {
        $headUrl = "$BaseUrl/webdav/$(Encode-KeyPath $item.Key)"
        $req = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Head, $headUrl)
        $null = $req.Headers.TryAddWithoutValidation('Accept-Encoding', 'identity')
        $resp = $null
        try {
            $resp = $httpClient.SendAsync($req).GetAwaiter().GetResult()
            $code = [int]$resp.StatusCode
            $remoteLen = $resp.Content.Headers.ContentLength
            if ($code -ne 200) {
                throw "Public HEAD verification failed for $($item.Key): expected HTTP 200, got $code"
            }
            if ($null -eq $remoteLen -or [long]$remoteLen -ne $item.Size) {
                throw "Remote Content-Length mismatch for $($item.Key): expected $($item.Size), got $remoteLen"
            }
            Write-Host ("  [VERIFIED] {0} -> HTTP 200, {1} bytes" -f $item.Key, $remoteLen)
        } finally {
            if ($null -ne $resp) { $resp.Dispose() }
            if ($null -ne $req) { $req.Dispose() }
        }
    }

    if (-not $NoSetLatest) {
        $latestHeadReq = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Head, "$BaseUrl/webdav/STS2_Things/_latest.json")
        $null = $latestHeadReq.Headers.TryAddWithoutValidation('Accept-Encoding', 'identity')
        $latestHeadResp = $null
        try {
            $latestHeadResp = $httpClient.SendAsync($latestHeadReq).GetAwaiter().GetResult()
            if ([int]$latestHeadResp.StatusCode -ne 200) {
                throw "Public HEAD verification failed for STS2_Things/_latest.json: HTTP $([int]$latestHeadResp.StatusCode)"
            }
            Write-Host ("  [VERIFIED] STS2_Things/_latest.json -> HTTP 200, {0} bytes" -f $latestHeadResp.Content.Headers.ContentLength)
        } finally {
            if ($null -ne $latestHeadResp) { $latestHeadResp.Dispose() }
            if ($null -ne $latestHeadReq) { $latestHeadReq.Dispose() }
        }
    }

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host " STS2_Things $tag published to FlareDrive!" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host "  Drive folder URL   : $BaseUrl/STS2_Things/$tag/"
    Write-Host "  Latest redirect    : $BaseUrl/STS2_Things/latest"
    Write-Host "  Latest ZIP redirect: $BaseUrl/STS2_Things/latest.zip"
    Write-Host "  Direct ZIP link    : $BaseUrl/webdav/STS2_Things/$tag/$([System.Uri]::EscapeDataString($zipEntry.Name))"
    Write-Host "  ZIP SHA256         : $($zipEntry.Sha256)"
    Write-Host "  Individual files   :"
    foreach ($item in $uploadPlan) {
        Write-Host ("    - {0} ({1}): {2}/webdav/{3}" -f $item.Name, (Format-ByteSize $item.Size), $BaseUrl, (Encode-KeyPath $item.Key))
    }
    Write-Host "============================================================" -ForegroundColor Green
} finally {
    if ($null -ne $httpClient) { $httpClient.Dispose() }
    if ($null -ne $httpHandler) { $httpHandler.Dispose() }
    if ($null -ne $tempExtractDir -and (Test-Path -LiteralPath $tempExtractDir)) {
        Remove-Item -LiteralPath $tempExtractDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
