# Builds assets\app.ico: the Enshrouded game icon with an orange "update" badge, in every Windows size.
# Source: assets\src\enshrouded_game.ico (Steam client icon of app 1203620).
param([switch]$NoBadge)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$source = New-Object Drawing.Icon "$here\src\enshrouded_game.ico", 256, 256
$master = $source.ToBitmap()

function Draw-Badge([Drawing.Graphics]$g, [int]$size) {
    # Badge: orange disc with two white circular arrows, bottom-right corner.
    # Small sizes need a relatively bigger badge to stay readable.
    $d = [Math]::Round($size * $(if ($size -le 24) { 0.5 } else { 0.4 }))
    $x = $size - $d; $y = $size - $d
    $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 16, 20, 28))), $x - $size * 0.03, $y - $size * 0.03, $d + $size * 0.03, $d + $size * 0.03)
    $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 240, 160, 75))), $x, $y, $d - 1, $d - 1)
    $penW = [Math]::Max(1.5, $d * 0.12)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), $penW
    $m = $d * 0.25; $r = New-Object Drawing.RectangleF ($x + $m), ($y + $m), ($d - 2 * $m - 1), ($d - 2 * $m - 1)
    $g.DrawArc($pen, $r, 200, 120)
    $g.DrawArc($pen, $r, 20, 120)
    # Arrow heads at the end of each arc.
    $cx = $r.X + $r.Width / 2; $cy = $r.Y + $r.Height / 2; $rad = $r.Width / 2; $h = $d * 0.17
    foreach ($a in 320, 140) {
        $t = $a * [Math]::PI / 180
        $px = $cx + $rad * [Math]::Cos($t); $py = $cy + $rad * [Math]::Sin($t)
        $tx = -[Math]::Sin($t); $ty = [Math]::Cos($t)   # tangent (direction of travel)
        $nx = [Math]::Cos($t); $ny = [Math]::Sin($t)     # normal
        $pts = [Drawing.PointF[]]@(
            (New-Object Drawing.PointF ($px + $tx * $h), ($py + $ty * $h)),
            (New-Object Drawing.PointF ($px + $nx * $h * 0.8), ($py + $ny * $h * 0.8)),
            (New-Object Drawing.PointF ($px - $nx * $h * 0.8), ($py - $ny * $h * 0.8)))
        $g.FillPolygon([Drawing.Brushes]::White, $pts)
    }
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
    $bmp = New-Object Drawing.Bitmap $s, $s
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"; $g.InterpolationMode = "HighQualityBicubic"; $g.PixelOffsetMode = "HighQuality"
    $g.DrawImage($master, 0, 0, $s, $s)
    if (-not $NoBadge) { Draw-Badge $g $s }
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    if ($s -eq 256) { $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png) }
    else {
        # Classic 32-bit DIB frame: System.Drawing.Icon (WinForms, tray) renders small PNG frames as noise.
        $w = New-Object IO.BinaryWriter $ms
        $w.Write([UInt32]40); $w.Write([Int32]$s); $w.Write([Int32]($s * 2)); $w.Write([UInt16]1); $w.Write([UInt16]32)
        $w.Write([UInt32]0); $w.Write([UInt32]($s * $s * 4)); $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
        for ($yy = $s - 1; $yy -ge 0; $yy--) {
            for ($xx = 0; $xx -lt $s; $xx++) { $c = $bmp.GetPixel($xx, $yy); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) }
        }
        $maskRow = [Math]::Ceiling($s / 32) * 4
        $w.Write((New-Object byte[] ($maskRow * $s)))   # AND mask unused with alpha
        $w.Flush()
    }
    if ($s -eq 256) {
        $bmp.Save("$here\app-256.png", [Drawing.Imaging.ImageFormat]::Png)
        $bmp.Save("$here\..\src\EnshroudedManager.Service\wwwroot\icon.png", [Drawing.Imaging.ImageFormat]::Png)
    }
    ,@($s, $ms.ToArray())
}

# ICO container with PNG frames (supported since Windows Vista).
$out = New-Object IO.BinaryWriter ([IO.File]::Create("$here\app.ico"))
$out.Write([UInt16]0); $out.Write([UInt16]1); $out.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $data = $f[1]
    $out.Write([byte]($s % 256)); $out.Write([byte]($s % 256)); $out.Write([byte]0); $out.Write([byte]0)
    $out.Write([UInt16]1); $out.Write([UInt16]32); $out.Write([UInt32]$data.Length); $out.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $out.Write($f[1]) }
$out.Close()
"app.ico : $($frames.Count) tailles"
