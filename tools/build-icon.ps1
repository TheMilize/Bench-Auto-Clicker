param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePng,

    [Parameter(Mandatory = $true)]
    [string]$OutputIco
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$sourceImage = [System.Drawing.Image]::FromFile($SourcePng)
$pngStreams = New-Object System.Collections.Generic.List[System.IO.MemoryStream]

try {
    foreach ($iconSize in $iconSizes) {
        $bitmap = New-Object System.Drawing.Bitmap(
            $iconSize,
            $iconSize,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($sourceImage, 0, 0, $iconSize, $iconSize)

            $pngStream = New-Object System.IO.MemoryStream
            $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
            $pngStreams.Add($pngStream)
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }

    $outputDirectory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($OutputIco))
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    $fileStream = [System.IO.File]::Create($OutputIco)
    $writer = New-Object System.IO.BinaryWriter($fileStream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$iconSizes.Count)

        $imageOffset = 6 + (16 * $iconSizes.Count)
        for ($index = 0; $index -lt $iconSizes.Count; $index++) {
            $iconSize = $iconSizes[$index]
            $pngLength = [uint32]$pngStreams[$index].Length
            $writer.Write([byte]$(if ($iconSize -eq 256) { 0 } else { $iconSize }))
            $writer.Write([byte]$(if ($iconSize -eq 256) { 0 } else { $iconSize }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write($pngLength)
            $writer.Write([uint32]$imageOffset)
            $imageOffset += $pngLength
        }

        foreach ($pngStream in $pngStreams) {
            $writer.Write($pngStream.ToArray())
        }
    }
    finally {
        $writer.Dispose()
        $fileStream.Dispose()
    }
}
finally {
    foreach ($pngStream in $pngStreams) {
        $pngStream.Dispose()
    }
    $sourceImage.Dispose()
}

Write-Output "Created $OutputIco with $($iconSizes.Count) embedded sizes."
