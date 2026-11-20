# Draws the YTLite app icon (white play triangle on a red rounded rectangle) at every
# standard Windows icon size and packs them into one .ico (PNG-compressed entries).
# Usage: powershell -File tools\make-icon.ps1 -Out YTLite\app.ico
param([string]$Out = (Join-Path $PSScriptRoot '..\YTLite\app.ico'), [string]$Preview = '')

. (Join-Path $PSScriptRoot 'icon-draw.ps1')

function New-IconPng([int]$size) {
    $bmp = New-YtIconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        # 256 px entry as PNG (standard; keeps the file small).
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        if ($Preview) { $bmp.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png) }
    } else {
        # Smaller entries as classic 32-bit DIBs, which every icon reader understands.
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([UInt32]40); $bw.Write([Int32]$size); $bw.Write([Int32]($size * 2))  # BITMAPINFOHEADER, height = XOR + AND
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]($size * $size * 4)); $bw.Write([Int32]0); $bw.Write([Int32]0)
        $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($yy = $size - 1; $yy -ge 0; $yy--) {                                       # BGRA pixels, bottom-up
            for ($xx = 0; $xx -lt $size; $xx++) {
                $c = $bmp.GetPixel($xx, $yy)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)                              # AND mask (unused with alpha)
        $bw.Write((New-Object byte[] ($maskRow * $size)))
        $bw.Flush()
    }
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { ,(New-IconPng $s) }

$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $Out) | Join-Path -ChildPath (Split-Path $Out -Leaf)))
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {                                      # ICONDIRENTRY
    $s = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256))                   # 256 is stored as 0
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
"Wrote $Out ($((Get-Item $Out).Length) bytes, sizes: $($sizes -join ', '))"
