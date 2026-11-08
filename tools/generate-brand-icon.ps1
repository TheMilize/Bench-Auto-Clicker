param(
    [string]$AssetsDirectory = (Join-Path $PSScriptRoot '..\BenchAutoClicker\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = [System.IO.Path]::GetFullPath($AssetsDirectory)
$sourcePath = Join-Path $assets 'bench-auto-clicker-icon.svg'
$pngPath = Join-Path $assets 'bench-auto-clicker-icon.png'
$icoPath = Join-Path $assets 'BenchAutoClicker.ico'
[xml]$svg = Get-Content -LiteralPath $sourcePath -Raw
$group = $svg.DocumentElement.SelectSingleNode('*[local-name()="g"]')

function ConvertTo-GraphicsPath([string]$Data) {
    # Explicit absolute SVG M/L/C/Z commands; Alternate fill keeps the wheel transparent.
    $tokens = [regex]::Matches($Data, '[MLCZ]|-?\d*\.?\d+(?:[eE][+-]?\d+)?')
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new([System.Drawing.Drawing2D.FillMode]::Alternate)
    $index = 0
    $current = [System.Drawing.PointF]::Empty
    $start = $current
    while ($index -lt $tokens.Count) {
        $command = $tokens[$index++].Value
        if ($command -eq 'Z') {
            $path.CloseFigure()
            $current = $start
            continue
        }
        $count = switch ($command) { 'M' { 2 } 'L' { 2 } 'C' { 6 } default { throw "Unsupported SVG command: $command" } }
        $values = [single[]]::new($count)
        for ($n = 0; $n -lt $count; $n++) {
            $values[$n] = [single]::Parse($tokens[$index++].Value, [System.Globalization.CultureInfo]::InvariantCulture)
        }
        switch ($command) {
            'M' {
                $path.StartFigure()
                $current = [System.Drawing.PointF]::new($values[0], $values[1])
                $start = $current
            }
            'L' {
                $next = [System.Drawing.PointF]::new($values[0], $values[1])
                $path.AddLine($current, $next)
                $current = $next
            }
            'C' {
                $next = [System.Drawing.PointF]::new($values[4], $values[5])
                $path.AddBezier($current, [System.Drawing.PointF]::new($values[0], $values[1]),
                    [System.Drawing.PointF]::new($values[2], $values[3]), $next)
                $current = $next
            }
        }
    }
    return ,$path
}

$bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.ScaleTransform(4, 4)
    foreach ($element in $group.ChildNodes) {
        if ($element -isnot [System.Xml.XmlElement]) { continue }
        if ($element.LocalName -ne 'path') { throw "Unsupported SVG element: $($element.LocalName)" }
        $path = ConvertTo-GraphicsPath $element.GetAttribute('d')
        $fillColor = $element.GetAttribute('fill')
        if (-not $fillColor) { $fillColor = $group.GetAttribute('fill') }
        $brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($fillColor))
        try { $graphics.FillPath($brush, $path) }
        finally { $brush.Dispose(); $path.Dispose() }
    }
    $bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}
& (Join-Path $PSScriptRoot 'build-icon.ps1') -SourcePng $pngPath -OutputIco $icoPath
Write-Output "Generated transparent brand assets from $sourcePath"
