[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$size = 256
$bitmap = New-Object System.Drawing.Bitmap($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

$rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
$gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect,
    [System.Drawing.Color]::FromArgb(16, 35, 62),
    [System.Drawing.Color]::FromArgb(37, 99, 235),
    45)
$graphics.FillRectangle($gradient, $rect)

$softWhite = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(22, 255, 255, 255))
$cyan = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(90, 34, 211, 238))
$graphics.FillEllipse($softWhite, -40, 120, 220, 220)
$graphics.FillEllipse($cyan, 140, -60, 220, 220)

$font = New-Object System.Drawing.Font('Segoe UI Semibold', 116, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
$graphics.DrawString('W', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, 10, $size, $size)), $format)

$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create($Path)
try
{
    $icon.Save($stream)
}
finally
{
    $stream.Dispose()
    $icon.Dispose()
    $font.Dispose()
    $format.Dispose()
    $softWhite.Dispose()
    $cyan.Dispose()
    $gradient.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Host "Created icon: $Path"
