# Generates the Android launcher icons (mipmap-*) and the Android TV launcher banner (320x180)
# from the same drawing as the Windows icon.
# Usage: powershell -File tools\make-android-assets.ps1
param([string]$Res = (Join-Path $PSScriptRoot '..\android\app\src\main\res'))

. (Join-Path $PSScriptRoot 'icon-draw.ps1')

$densities = @{ 'mdpi' = 48; 'hdpi' = 72; 'xhdpi' = 96; 'xxhdpi' = 144; 'xxxhdpi' = 192 }
foreach ($d in $densities.Keys) {
    $dir = Join-Path $Res "mipmap-$d"; New-Item -ItemType Directory -Force $dir | Out-Null
    $bmp = New-YtIconBitmap $densities[$d]
    $bmp.Save((Join-Path $dir 'ic_launcher.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

# TV banner: dark tile, icon on the left, app name on the right (xhdpi = 320x180 px).
$dir = Join-Path $Res 'drawable-xhdpi'; New-Item -ItemType Directory -Force $dir | Out-Null
$banner = New-Object System.Drawing.Bitmap 320, 180, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($banner)
$g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.TextRenderingHint = 'AntiAliasGridFit'
$g.Clear([System.Drawing.Color]::FromArgb(255, 24, 24, 24))
Add-YtIcon $g 22 40 100
$font = New-Object System.Drawing.Font 'Segoe UI Semibold', 34, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$fmt = New-Object System.Drawing.StringFormat; $fmt.LineAlignment = 'Center'
$g.DrawString('YTLite', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 132, 0, 188, 180), $fmt)
$g.Dispose()
$banner.Save((Join-Path $dir 'banner.png'), [System.Drawing.Imaging.ImageFormat]::Png); $banner.Dispose()

Get-ChildItem $Res -Recurse -Filter *.png | ForEach-Object { "{0}  {1} bytes" -f $_.FullName.Substring($Res.Length), $_.Length }
