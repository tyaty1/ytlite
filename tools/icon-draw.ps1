# Shared drawing for the YTLite icon: white play triangle on a red rounded rectangle.
# Dot-source it:  . (Join-Path $PSScriptRoot 'icon-draw.ps1')
Add-Type -AssemblyName System.Drawing

# Draws the icon into an existing Graphics, inside the square (left, top, size).
function Add-YtIcon([System.Drawing.Graphics]$g, [single]$left, [single]$top, [single]$size) {
    $w = $size * 0.94; $h = $w / 1.42
    $x = $left + ($size - $w) / 2; $y = $top + ($size - $h) / 2
    $r = $h * 0.46  # corner diameter
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $r, $r, 180, 90)
    $path.AddArc($x + $w - $r, $y, $r, $r, 270, 90)
    $path.AddArc($x + $w - $r, $y + $h - $r, $r, $r, 0, 90)
    $path.AddArc($x, $y + $h - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $red = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 0, 0))
    $g.FillPath($red, $path)

    # Play triangle, optically centered (nudged right).
    $th = $h * 0.44; $tw = $th * 0.87
    $cx = $left + $size / 2 + $tw * 0.08; $cy = $top + $size / 2
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($cx - $tw / 2), ($cy - $th / 2)),
        (New-Object System.Drawing.PointF ($cx + $tw / 2), $cy),
        (New-Object System.Drawing.PointF ($cx - $tw / 2), ($cy + $th / 2)))
    $g.FillPolygon([System.Drawing.Brushes]::White, $pts)
}

# New transparent square bitmap with the icon on it.
function New-YtIconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    Add-YtIcon $g 0 0 $size
    $g.Dispose()
    return $bmp
}
