# Checks after every restructure phase (PLAN.md): build, compare world fingerprints with the references, take screenshots.
#   powershell -ExecutionPolicy Bypass -File checks\check.ps1            # compare
#   powershell -ExecutionPolicy Bypass -File checks\check.ps1 -Update    # write new references (only when a change is intended)
#   add -NoShots to skip the screenshots (they need a GPU window for ~10 s each)
param([switch]$Update, [switch]$NoShots)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build -c Release --nologo -v q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
    $exe = Join-Path $root 'bin\Release\net8.0\win-x64\CubePlanet.exe'

    # worlds: small default planet and a big one (different band count / scaling)
    $worlds = @(@('1200', '1337'), @('20000', '42'))
    $lines = foreach ($w in $worlds) { & $exe --fingerprint --radius $w[0] --seed $w[1] | Select-String 'FINGERPRINT' | ForEach-Object { $_.Line } }
    $lines | ForEach-Object { Write-Host $_ }
    $refFile = Join-Path $PSScriptRoot 'fingerprint.txt'
    $now = $lines | ForEach-Object { ($_ -replace '\s*\([\d.]+ s\)$', '') }
    if ($Update -or -not (Test-Path $refFile)) { $now | Set-Content -Encoding ascii $refFile; Write-Host "fingerprint references written" -ForegroundColor Yellow }
    else {
        $ref = Get-Content $refFile
        if (Compare-Object $ref $now) { Write-Host "FINGERPRINT CHANGED" -ForegroundColor Red; Compare-Object $ref $now | Out-Host; exit 1 }
        Write-Host "fingerprints unchanged" -ForegroundColor Green
    }

    # headless rule checks (collision / selection boxes, placement, meshing; later: multiblocks, containers, saving)
    & $exe --selftest | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host "SELFTEST FAILED" -ForegroundColor Red; exit 1 }

    if (-not $NoShots) {
        # clouds drift and the HUD shows FPS, so compare these by eye with checks\ref (not pixel exact)
        $dir = if ($Update) { Join-Path $PSScriptRoot 'ref' } else { Join-Path $PSScriptRoot 'out' }
        New-Item -ItemType Directory -Force $dir | Out-Null
        $shots = @(
            @{ n = 'day';    a = @('--hour', '10') },
            @{ n = 'sunset'; a = @('--hour', '18.3', '--yaw', '120', '--pitch', '-5') },
            @{ n = 'down';   a = @('--hour', '13', '--pitch', '-60', '--up', '-20') },
            @{ n = 'blocks'; a = @('--hour', '10', '--showcase', '--up', '-25', '--pitch', '-30') },
            @{ n = 'ui';     a = @('--hour', '10', '--showcase', '--showui', 'furnace') }
        )
        foreach ($s in $shots) {
            $args2 = @('--bench', '4', '--shot', (Join-Path $dir "$($s.n).png")) + $s.a
            & $exe @args2 | Select-String 'BENCH|PROF total' | ForEach-Object { Write-Host "$($s.n): $($_.Line)" }
        }
        Write-Host "screenshots in $dir (references in checks\ref)"
    }
}
finally { Pop-Location }
