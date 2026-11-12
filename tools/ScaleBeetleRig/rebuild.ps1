[CmdletBinding()]
param([string]$PythonExe = 'python', [string]$NodeExe = 'node', [switch]$Deploy)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path -LiteralPath 'node_modules\@esotericsoftware\spine-core')) {
        & npm ci --ignore-scripts --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Spine runtime dependency install failed' }
    }
    & $PythonExe parts.py
    if ($LASTEXITCODE -ne 0) { throw 'Part restoration failed' }
    & $PythonExe -m rigkit build
    if ($LASTEXITCODE -ne 0) { throw 'Mesh/atlas build failed' }
    & $PythonExe -m rigkit export
    if ($LASTEXITCODE -ne 0) { throw 'Animation bake failed' }
    & $PythonExe package.py
    if ($LASTEXITCODE -ne 0) { throw 'Spine packaging failed' }
    & $NodeExe verify.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Runtime geometry regression failed' }
    & $NodeExe seam_dump.mjs pkg\scale_beetle.spjson pkg\scale_beetle.atlas out\seam-bones.json 60
    if ($LASTEXITCODE -ne 0) { throw 'Carapace seam sampling failed' }
    & $PythonExe seams.py check out\seam-bones.json 4.0
    if ($LASTEXITCODE -ne 0) { throw 'A hidden carapace cut edge shows during an animation' }
    if ($Deploy) {
        & $PythonExe package.py --deploy
        if ($LASTEXITCODE -ne 0) { throw 'Project asset deployment failed' }
    }
}
finally { Pop-Location }
