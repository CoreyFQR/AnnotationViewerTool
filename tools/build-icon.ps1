$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$source = [System.Drawing.Image]::FromFile((Join-Path $root 'assets\logo.png'))
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()

function Set-RoundedCorners([System.Drawing.Bitmap]$bitmap, [double]$radius) {
    $limit = [Math]::Ceiling($radius)
    for ($y = 0; $y -lt $limit; $y++) {
        for ($x = 0; $x -lt $limit; $x++) {
            $distance = [Math]::Sqrt([Math]::Pow($radius - ($x + 0.5), 2) +
                [Math]::Pow($radius - ($y + 0.5), 2))
            $coverage = [Math]::Max(0, [Math]::Min(1, $radius + 0.5 - $distance))
            if ($coverage -ge 1) { continue }
            $points = @(
                @($x, $y), @([int]($bitmap.Width - 1 - $x), $y),
                @($x, [int]($bitmap.Height - 1 - $y)),
                @([int]($bitmap.Width - 1 - $x), [int]($bitmap.Height - 1 - $y))
            )
            foreach ($point in $points) {
                $color = $bitmap.GetPixel($point[0], $point[1])
                $alpha = [Math]::Min($color.A, [Math]::Round($color.A * $coverage))
                $bitmap.SetPixel($point[0], $point[1],
                    [System.Drawing.Color]::FromArgb($alpha, $color.R, $color.G, $color.B))
            }
        }
    }
}

try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size, $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($source, 0, 0, $size, $size)
            Set-RoundedCorners $bitmap ([Math]::Max(3, $size * 160.0 / 1254.0))
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames += ,$stream.ToArray()
        } finally { $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
    }
    $output = [System.IO.File]::Create((Join-Path $root 'assets\AnnotationViewer.ico'))
    $writer = [System.IO.BinaryWriter]::new($output)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose(); $output.Dispose() }
} finally { $source.Dispose() }
