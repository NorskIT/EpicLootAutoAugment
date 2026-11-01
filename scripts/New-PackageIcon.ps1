$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = [Drawing.Image]::FromFile((Join-Path $root 'images/epicloot-auto-augment.png'))
$icon = New-Object Drawing.Bitmap 256,256
$graphics = [Drawing.Graphics]::FromImage($icon)
try {
    $graphics.Clear([Drawing.Color]::FromArgb(6, 23, 37))
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    # Preserve the complete supplied artwork and its aspect ratio.
    $scale = [Math]::Min(256.0 / $source.Width, 256.0 / $source.Height)
    $width = [int][Math]::Round($source.Width * $scale)
    $height = [int][Math]::Round($source.Height * $scale)
    $graphics.DrawImage($source, [int]((256 - $width) / 2), [int]((256 - $height) / 2), $width, $height)
    $icon.Save((Join-Path $root 'packaging/icon.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $icon.Dispose(); $source.Dispose() }
