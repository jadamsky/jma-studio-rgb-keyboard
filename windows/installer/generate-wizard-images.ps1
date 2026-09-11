# Generates the installer's branded wizard images from the existing
# app logo (gui/logo.png, 512x512) -- a dark background matching the
# app's own BgBrush (#0B0B12) with the logo centered, since the raw
# square logo alone doesn't fit Inno Setup's tall vertical side-panel
# image shape. Run once; re-run only if the logo itself changes.

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$logoPath = Join-Path $root "..\gui\logo.png"
$outDir = Join-Path $PSScriptRoot "assets"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$bgColor = [System.Drawing.Color]::FromArgb(255, 0x0B, 0x0B, 0x12)
$logo = [System.Drawing.Image]::FromFile((Resolve-Path $logoPath))

function New-WizardImage([string]$path, [int]$w, [int]$h, [int]$logoSize) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear($bgColor)
    $x = [int](($w - $logoSize) / 2)
    $y = [int](($h - $logoSize) / 2)
    $g.DrawImage($logo, $x, $y, $logoSize, $logoSize)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Wrote $path ($w x $h)"
}

# Classic Inno Setup wizard image sizes -- see installer's .iss comments.
New-WizardImage (Join-Path $outDir "WizardImage.png") 164 314 120
New-WizardImage (Join-Path $outDir "WizardSmallImage.png") 55 58 40

$logo.Dispose()
Write-Host "Done."
