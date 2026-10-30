# Renders AppIcon.xaml into QuoteSnap.ico. Run it after changing the drawings:
#   powershell -NoProfile -File src/QuoteSnap.Desktop/Assets/build-icon.ps1 [-PreviewDirectory <dir>]
param([string]$PreviewDirectory)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$assets = $PSScriptRoot
$resources = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $assets 'AppIcon.xaml')))

function Render([Windows.Media.ImageSource]$image, [int]$size) {
    $visual = New-Object Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.DrawImage($image, (New-Object Windows.Rect 0, 0, $size, $size))
    $context.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    return , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = foreach ($size in $sizes) {
    $source = if ($size -le 24) { $resources['AppIconSmallImage'] } else { $resources['AppIconImage'] }
    [pscustomobject]@{ Size = $size; Png = (Render $source $size) }
}

# ICO: a 6-byte header, a 16-byte directory entry per image, then the PNG images.
$out = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $out
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$image.Png.Length); $writer.Write([uint32]$offset)
    $offset += $image.Png.Length
}
foreach ($image in $images) { $writer.Write($image.Png) }
$writer.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'QuoteSnap.ico'), $out.ToArray())
"Wrote QuoteSnap.ico ($($sizes -join ', ') px)"

if ($PreviewDirectory) {
    New-Item -ItemType Directory -Force $PreviewDirectory | Out-Null
    foreach ($image in $images) { [IO.File]::WriteAllBytes((Join-Path $PreviewDirectory "icon-$($image.Size).png"), $image.Png) }
}
