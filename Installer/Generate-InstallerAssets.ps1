[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

function New-LimelightBitmap {
    param(
        [int]$Width,
        [int]$Height,
        [string]$Path,
        [switch]$Compact
    )

    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    try {
        $background = [System.Drawing.Color]::FromArgb(8, 9, 18)
        $panel = [System.Drawing.Color]::FromArgb(19, 22, 41)
        $pink = [System.Drawing.Color]::FromArgb(255, 60, 172)
        $cyan = [System.Drawing.Color]::FromArgb(53, 231, 255)
        $white = [System.Drawing.Color]::FromArgb(247, 245, 255)

        $graphics.Clear($background)

        if ($Compact) {
            $designScale = [Math]::Min($Width / 55.0, $Height / 58.0)
            $outerSize = [Math]::Min($Width, $Height) - (12 * $designScale)
            $outerX = ($Width - $outerSize) / 2
            $outerY = ($Height - $outerSize) / 2
            $ringWidth = 2.0 * $designScale
        }
        else {
            # I draw the artwork at a larger resolution so Windows can scale it down cleanly.
            $designScale = $Width / 164.0
            $gradientRect = [System.Drawing.Rectangle]::new(0, 0, $Width, $Height)
            $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $gradientRect,
                [System.Drawing.Color]::FromArgb(41, 18, 54),
                [System.Drawing.Color]::FromArgb(8, 34, 49),
                62.0)
            $graphics.FillRectangle($gradient, $gradientRect)
            $gradient.Dispose()

            $accentBrush = [System.Drawing.SolidBrush]::new($pink)
            $graphics.FillRectangle($accentBrush, 0, 0, 5 * $designScale, $Height)
            $accentBrush.Dispose()

            $outerSize = [Math]::Min($Width - (26 * $designScale), 126 * $designScale)
            $outerX = ($Width - $outerSize) / 2
            $outerY = 43 * $designScale
            $ringWidth = 2.0 * $designScale
        }

        $panelBrush = [System.Drawing.SolidBrush]::new($panel)
        $graphics.FillEllipse($panelBrush, $outerX, $outerY, $outerSize, $outerSize)
        $panelBrush.Dispose()

        $cyanPen = [System.Drawing.Pen]::new($cyan, $ringWidth)
        $pinkPen = [System.Drawing.Pen]::new($pink, $ringWidth + 1)
        $graphics.DrawEllipse($cyanPen, $outerX, $outerY, $outerSize, $outerSize)
        $innerInset = [Math]::Max(8 * $designScale, [int]($outerSize * 0.16))
        $graphics.DrawEllipse(
            $pinkPen,
            $outerX + $innerInset,
            $outerY + $innerInset,
            $outerSize - ($innerInset * 2),
            $outerSize - ($innerInset * 2))
        $cyanPen.Dispose()
        $pinkPen.Dispose()

        $logoFontSize = if ($Compact) { 18 * $designScale } else { 43 * $designScale }
        $logoFont = [System.Drawing.Font]::new(
            "Bahnschrift",
            $logoFontSize,
            [System.Drawing.FontStyle]::Bold -bor [System.Drawing.FontStyle]::Italic,
            [System.Drawing.GraphicsUnit]::Pixel)
        $logoBrush = [System.Drawing.SolidBrush]::new($white)
        $logoFormat = [System.Drawing.StringFormat]::new()
        $logoFormat.Alignment = [System.Drawing.StringAlignment]::Center
        $logoFormat.LineAlignment = [System.Drawing.StringAlignment]::Center
        $graphics.DrawString(
            "L",
            $logoFont,
            $logoBrush,
            [System.Drawing.RectangleF]::new($outerX, $outerY, $outerSize, $outerSize),
            $logoFormat)
        $logoFont.Dispose()
        $logoBrush.Dispose()
        $logoFormat.Dispose()

        if (-not $Compact) {
            $headingFont = [System.Drawing.Font]::new(
                "Bahnschrift",
                18 * $designScale,
                [System.Drawing.FontStyle]::Bold -bor [System.Drawing.FontStyle]::Italic,
                [System.Drawing.GraphicsUnit]::Pixel)
            $smallFont = [System.Drawing.Font]::new(
                "Bahnschrift",
                9 * $designScale,
                [System.Drawing.FontStyle]::Bold,
                [System.Drawing.GraphicsUnit]::Pixel)
            $watermarkFont = [System.Drawing.Font]::new(
                "Bahnschrift",
                18 * $designScale,
                [System.Drawing.FontStyle]::Bold,
                [System.Drawing.GraphicsUnit]::Pixel)
            $headingBrush = [System.Drawing.SolidBrush]::new($white)
            $cyanBrush = [System.Drawing.SolidBrush]::new($cyan)
            $watermarkBrush = [System.Drawing.SolidBrush]::new(
                [System.Drawing.Color]::FromArgb(140, 255, 60, 172))

            $format = [System.Drawing.StringFormat]::new()
            $format.Alignment = [System.Drawing.StringAlignment]::Center
            $graphics.DrawString(
                "LIMELIGHT",
                $headingFont,
                $headingBrush,
                [System.Drawing.RectangleF]::new(
                    4 * $designScale,
                    181 * $designScale,
                    $Width - (8 * $designScale),
                    32 * $designScale),
                $format)

            $headingAccentBrush = [System.Drawing.SolidBrush]::new($pink)
            $headingAccentWidth = 56 * $designScale
            $graphics.FillRectangle(
                $headingAccentBrush,
                ($Width - $headingAccentWidth) / 2,
                216 * $designScale,
                $headingAccentWidth,
                3 * $designScale)
            $headingAccentBrush.Dispose()

            $graphics.DrawString(
                "DEAD AS DISCO`nMOD MANAGER",
                $smallFont,
                $cyanBrush,
                [System.Drawing.RectangleF]::new(
                    4 * $designScale,
                    228 * $designScale,
                    $Width - (8 * $designScale),
                    34 * $designScale),
                $format)

            $watermarkFormat = [System.Drawing.StringFormat]::new()
            $watermarkFormat.Alignment = [System.Drawing.StringAlignment]::Center
            $watermarkFormat.LineAlignment = [System.Drawing.StringAlignment]::Center
            $graphics.DrawString(
                "PRIVATE`nTEST BUILD",
                $watermarkFont,
                $watermarkBrush,
                [System.Drawing.RectangleF]::new(
                    4 * $designScale,
                    265 * $designScale,
                    $Width - (8 * $designScale),
                    $Height - (269 * $designScale)),
                $watermarkFormat)

            $headingFont.Dispose()
            $smallFont.Dispose()
            $watermarkFont.Dispose()
            $headingBrush.Dispose()
            $cyanBrush.Dispose()
            $watermarkBrush.Dispose()
            $format.Dispose()
            $watermarkFormat.Dispose()
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

# I generate these from code so the installer artwork always stays aligned with Limelight's palette.
New-LimelightBitmap `
    -Width 656 `
    -Height 1256 `
    -Path (Join-Path $resolvedOutput "LimelightWizard.bmp")

New-LimelightBitmap `
    -Width 220 `
    -Height 232 `
    -Path (Join-Path $resolvedOutput "LimelightWizardSmall.bmp") `
    -Compact
