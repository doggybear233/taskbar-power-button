# Build power.ico from the Material Rounded "power_settings_new" glyph.
#
# Each size is rendered natively by headless Chrome (one 256px calibration pass
# decides the glyph scale and centering), then every size is written as a
# single-size icon by System.Drawing and the parts are merged into one
# multi-size ICO: 16/20/24/32/40/48/64/128/256.
#
# Usage:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
# Requires Google Chrome. No other dependency.

[CmdletBinding()]
param(
    [string]$GlyphSvg,
    [string]$OutIcon,
    [string]$Work,
    [double]$GlyphRatio = 0.56   # visible ring width / icon size
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not bound while parameters are evaluated, so resolve here
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $GlyphSvg) { $GlyphSvg = Join-Path $root '..\icons\raw\02-ms-rounded.svg' }
if (-not $OutIcon)  { $OutIcon  = Join-Path $root '..\icons\power.ico' }
if (-not $Work)     { $Work     = Join-Path $root '..\icons\build' }

$chrome = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
if (-not (Test-Path $chrome)) {
    $chrome = (Get-Command chrome.exe -ErrorAction SilentlyContinue).Source
}
if (-not $chrome) { throw 'Chrome not found' }

Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
New-Item -ItemType Directory -Force -Path $Work | Out-Null

# ---------------------------------------------------------------- glyph path
$raw = [System.IO.File]::ReadAllText((Resolve-Path $GlyphSvg), [System.Text.Encoding]::UTF8)
$pathData = [regex]::Match($raw, 'd="([^"]+)"').Groups[1].Value
if (-not $pathData) { throw 'no path data in glyph svg' }

$inv = [System.Globalization.CultureInfo]::InvariantCulture

# ---------------------------------------------------------------- svg builder
function New-IconSvg {
    param([double]$Scale, [double]$Tx, [double]$Ty, [int]$Size, [double]$Stroke)

    $strokeAttr = ''
    if ($Stroke -gt 0) {
        $strokeAttr = '<circle cx="128" cy="128" r="126" fill="none" stroke="#c9c9c9" stroke-width="' +
                      ($Stroke.ToString($inv)) + '"/>'
    }
    $s  = $Scale.ToString('0.#####', $inv)
    $tx = $Tx.ToString('0.##', $inv)
    $ty = $Ty.ToString('0.##', $inv)

    @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="$Size" height="$Size">
<circle cx="128" cy="128" r="127" fill="#ffffff"/>
$strokeAttr
<g transform="translate($tx,$ty) scale($s)" fill="#1a1a1a"><path d="$pathData"/></g>
</svg>
"@
}

# ---------------------------------------------------------------- rendering
function Invoke-Render {
    param([string]$SvgText, [int]$Size, [string]$Png)

    $html = @"
<!doctype html><html><head><meta charset="utf-8"><style>
html,body{margin:0;padding:0;background:transparent;}
svg{display:block;width:${Size}px;height:${Size}px;}
</style></head><body>$SvgText</body></html>
"@
    $htmlPath = Join-Path $Work 'render.html'
    [System.IO.File]::WriteAllText($htmlPath, $html, (New-Object System.Text.UTF8Encoding($false)))
    if (Test-Path $Png) { Remove-Item $Png -Force }

    $chromeArgs = @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars',
        '--default-background-color=00000000', '--force-device-scale-factor=1',
        "--window-size=$Size,$Size", "--screenshot=$Png",
        ('file:///' + ($htmlPath -replace '\\', '/'))
    )
    Start-Process -FilePath $chrome -ArgumentList $chromeArgs -NoNewWindow -Wait | Out-Null
    for ($i = 0; $i -lt 40 -and -not (Test-Path $Png); $i++) { Start-Sleep -Milliseconds 50 }
    if (-not (Test-Path $Png)) { throw "render failed for size $Size" }
}

# ---------------------------------------------------------------- measure
function Measure-DarkBox {
    param([string]$Png)

    $bmp = [System.Drawing.Bitmap]::FromFile($Png)
    $minX = $bmp.Width; $minY = $bmp.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            $c = $bmp.GetPixel($x, $y)
            if ($c.A -gt 40 -and $c.R -lt 120 -and $c.G -lt 120 -and $c.B -lt 120) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    $bmp.Dispose()
    if ($maxX -lt 0) { throw "no dark pixels in $Png" }
    [pscustomobject]@{
        W  = ($maxX - $minX + 1)
        H  = ($maxY - $minY + 1)
        Cx = (($minX + $maxX) / 2.0)
        Cy = (($minY + $maxY) / 2.0)
    }
}

# ---------------------------------------------------------------- calibrate
Write-Host 'calibrating glyph scale...'
$calSize = 256
# glyph coordinates live in a 0..960 / -960..0 box, so shift it into view first
$s0 = 0.15; $tx0 = 40.0; $ty0 = 150.0
$tmpPng = Join-Path $Work 'calibrate.png'

Invoke-Render -SvgText (New-IconSvg -Scale $s0 -Tx $tx0 -Ty $ty0 -Size $calSize -Stroke 0) -Size $calSize -Png $tmpPng
$m = Measure-DarkBox -Png $tmpPng

$targetPx = $GlyphRatio * $calSize
$s1  = $s0 * ($targetPx / $m.W)
$cx  = ($m.Cx - $tx0) / $s0
$cy  = ($m.Cy - $ty0) / $s0
$tx1 = 128 - $s1 * $cx
$ty1 = 128 - $s1 * $cy
Write-Host ('  scale {0:0.####} -> {1:0.####}' -f $s0, $s1)

Invoke-Render -SvgText (New-IconSvg -Scale $s1 -Tx $tx1 -Ty $ty1 -Size $calSize -Stroke 0) -Size $calSize -Png $tmpPng
$m2 = Measure-DarkBox -Png $tmpPng
Write-Host ('  check: {0:0.#}px wide ({1:0.#}% of 256), center ({2:0.#},{3:0.#})' -f $m2.W, (100 * $m2.W / 256), $m2.Cx, $m2.Cy)

# ---------------------------------------------------------------- render set
Write-Host 'rendering sizes...'
foreach ($size in $sizes) {
    # a thin light ring keeps the white disc readable on a light taskbar
    $stroke = 0.0
    if ($size -ge 24) { $stroke = [Math]::Round([Math]::Max(1.0, $size * 0.012), 2) }
    $png = Join-Path $Work ("icon-$size.png")
    Invoke-Render -SvgText (New-IconSvg -Scale $s1 -Tx $tx1 -Ty $ty1 -Size $size -Stroke $stroke) -Size $size -Png $png
}
# ---------------------------------------------------------------- pack ICO
# Entries are hand-packed 32bpp DIBs (BITMAPINFOHEADER + bottom-up BGRA + an
# all-zero AND mask). PNG-compressed entries and icons rebuilt from HICONs both
# lose the alpha channel, which shows up as a black square in the taskbar.
function Get-IconDib {
    param([string]$Png, [int]$Size)

    $bmp  = [System.Drawing.Bitmap]::FromFile($Png)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $raw = New-Object byte[] ($stride * $Size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length)
    $bmp.UnlockBits($data)
    $bmp.Dispose()

    $xorLen  = $Size * $Size * 4
    $maskRow = [Math]::Floor(($Size + 31) / 32) * 4
    $andLen  = $maskRow * $Size

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint32]40)
    $bw.Write([int32]$Size)
    $bw.Write([int32]($Size * 2))
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]0)
    $bw.Write([uint32]($xorLen + $andLen))
    $bw.Write([int32]0); $bw.Write([int32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0)
    $bw.Flush()
    $header = $ms.ToArray()
    $bw.Close(); $ms.Close()

    $dib = New-Object byte[] ($header.Length + $xorLen + $andLen)
    [Array]::Copy($header, 0, $dib, 0, $header.Length)

    $dst = $header.Length
    for ($y = $Size - 1; $y -ge 0; $y--) {
        [Array]::Copy($raw, ($y * $stride), $dib, $dst, ($Size * 4))
        $dst += ($Size * 4)
    }
    ,$dib
}

$entries = @()
foreach ($size in $sizes) {
    $entries += ,(Get-IconDib -Png (Join-Path $Work ("icon-$size.png")) -Size $size)
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    if ($size -ge 256) { $dim = [byte]0 } else { $dim = [byte]$size }
    $bw.Write($dim)
    $bw.Write($dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$entries[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $entries[$i].Length
}
foreach ($e in $entries) { $bw.Write($e) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($OutIcon, $ms.ToArray())
$bw.Close(); $ms.Close()

# ---------------------------------------------------------------- verify
# GDI+ never selects the 256 entry (it falls back to 128); Explorer reads it
# directly, so only require 128+ for that one size.
foreach ($size in $sizes) {
    $probe = New-Object System.Drawing.Icon($OutIcon, $size, $size)
    $want = if ($size -ge 256) { 128 } else { $size }
    if ($probe.Width -lt $want) { throw "size $size does not resolve (got $($probe.Width))" }
    $probe.Dispose()
}
Write-Host ("wrote {0} ({1} bytes, {2} sizes, all resolve)" -f $OutIcon, (Get-Item $OutIcon).Length, $sizes.Count)