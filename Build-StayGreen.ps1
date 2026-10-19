#requires -Version 5.1
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$pngPath = Join-Path $root 'Icon.png'
$icoPath = Join-Path $root 'StayGreen.ico'
$sourcePath = Join-Path $root 'StayGreen.cs'
$exePath = Join-Path $root 'StayGreen.exe'

Add-Type -AssemblyName System.Drawing

function Convert-ToIcon {
    param([string]$ImagePath, [string]$OutputPath)

    $source = [System.Drawing.Bitmap]::FromFile($ImagePath)
    try {
        $left = $source.Width; $top = $source.Height
        $right = -1; $bottom = -1
        for ($y = 0; $y -lt $source.Height; $y++) {
            for ($x = 0; $x -lt $source.Width; $x++) {
                if ($source.GetPixel($x, $y).A -gt 16) {
                    if ($x -lt $left) { $left = $x }
                    if ($x -gt $right) { $right = $x }
                    if ($y -lt $top) { $top = $y }
                    if ($y -gt $bottom) { $bottom = $y }
                }
            }
        }
        if ($right -lt $left) { throw 'Icon.png has no visible pixels.' }

        $crop = [System.Drawing.Rectangle]::FromLTRB($left, $top, ($right + 1), ($bottom + 1))
        $entries = @()
        foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
            $bitmap = New-Object System.Drawing.Bitmap -ArgumentList $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $margin = [Math]::Max(1, [Math]::Round($size * 0.04))
                    $available = $size - 2 * $margin
                    $scale = [Math]::Min($available / $crop.Width, $available / $crop.Height)
                    $width = [int][Math]::Round($crop.Width * $scale)
                    $height = [int][Math]::Round($crop.Height * $scale)
                    $destination = New-Object System.Drawing.Rectangle -ArgumentList ([int][Math]::Floor(($size - $width) / 2)), ([int][Math]::Floor(($size - $height) / 2)), $width, $height
                    $graphics.DrawImage($source, $destination, $crop, [System.Drawing.GraphicsUnit]::Pixel)
                }
                finally { $graphics.Dispose() }

                $memory = New-Object System.IO.MemoryStream
                try {
                    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
                    $entries += [pscustomobject]@{ Size = $size; Bytes = $memory.ToArray() }
                }
                finally { $memory.Dispose() }
            }
            finally { $bitmap.Dispose() }
        }

        $stream = [System.IO.File]::Create($OutputPath)
        $writer = New-Object System.IO.BinaryWriter $stream
        try {
            $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$entries.Count)
            $offset = 6 + 16 * $entries.Count
            foreach ($entry in $entries) {
                $dimension = if ($entry.Size -eq 256) { 0 } else { $entry.Size }
                $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
                $writer.Write([byte]0); $writer.Write([byte]0)
                $writer.Write([uint16]1); $writer.Write([uint16]32)
                $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
                $offset += $entry.Bytes.Length
            }
            foreach ($entry in $entries) { $writer.Write([byte[]]$entry.Bytes) }
        }
        finally { $writer.Dispose(); $stream.Dispose() }
    }
    finally { $source.Dispose() }
}

$compiler = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw 'The .NET Framework C# compiler (csc.exe) was not found.' }

Convert-ToIcon -ImagePath $pngPath -OutputPath $icoPath
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/win32icon:$icoPath" "/out:$exePath" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $sourcePath
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE." }
Write-Host "Built $exePath"
