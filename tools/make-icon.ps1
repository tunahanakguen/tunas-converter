# Erzeugt aus dem TUNAS-Logo (branding/tunas-logo-1024.png, weiß auf schwarz) die Programmdateien:
#   src/TuNAS.Converter/Resources/TunasLogo.png      – Logo für Kopfzeile und Startfläche (512 px, zugeschnitten)
#   src/TuNAS.Converter/Resources/TunasConverter.ico – Programmsymbol (16–256 px)
# Mit -Source wird zuerst eine neue Vorlage übernommen (z. B. ein KI-Bild); der „Made with AI“-Hinweis
# oben rechts wird dabei schwarz übermalt. Aufruf:
#   powershell -STA -ExecutionPolicy Bypass -File tools\make-icon.ps1 [-Source bild.webp] [-PreviewDir ordner]
param([string]$Source = '', [string]$PreviewDir = '')

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$root = Split-Path $PSScriptRoot -Parent
$master = Join-Path $root 'branding\tunas-logo-1024.png'
$logoPng = Join-Path $root 'src\TuNAS.Converter\Resources\TunasLogo.png'
$icoPath = Join-Path $root 'src\TuNAS.Converter\Resources\TunasConverter.ico'

function Load($path) {
    $img = New-Object System.Windows.Media.Imaging.BitmapImage
    $img.BeginInit(); $img.CacheOption = 'OnLoad'; $img.UriSource = New-Object Uri $path; $img.EndInit(); $img.Freeze()
    return $img
}
function SavePng($bitmap, $path) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $ms = New-Object IO.MemoryStream; $enc.Save($ms)
    if ($path) { [IO.File]::WriteAllBytes($path, $ms.ToArray()) }
    return , $ms.ToArray()
}
# Zeichnet einen Bildausschnitt (crop in Pixeln der Vorlage) hochwertig verkleinert auf size×size.
function Render($image, [int]$size, $crop, [bool]$hideLabel) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, 'HighQuality')
    $ctx = $visual.RenderOpen()
    $k = $size / $crop.Width
    $ctx.DrawRectangle([System.Windows.Media.Brushes]::Black, $null, (New-Object System.Windows.Rect 0, 0, $size, $size))
    $ctx.PushTransform((New-Object System.Windows.Media.MatrixTransform $k, 0, 0, $k, (-$crop.X * $k), (-$crop.Y * $k)))
    $ctx.DrawImage($image, (New-Object System.Windows.Rect 0, 0, $image.PixelWidth, $image.PixelHeight))
    if ($hideLabel) {
        # „Made with AI“-Hinweis oben rechts liegt auf schwarzem Grund – schwarz übermalen.
        $ctx.DrawRectangle([System.Windows.Media.Brushes]::Black, $null, (New-Object System.Windows.Rect 840, 0, 184, 72))
    }
    $ctx.Pop(); $ctx.Close()
    $bmp = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($visual)
    return $bmp
}

if ($Source) {
    New-Item -ItemType Directory -Force (Split-Path $master) | Out-Null
    $src = Load $Source
    [void](SavePng (Render $src 1024 (New-Object System.Windows.Rect 0, 0, $src.PixelWidth, $src.PixelHeight) $true) $master)
    "Vorlage übernommen: $master"
}

$image = Load $master
# Ausschnitt um den Ring (Mitte ca. 512/490), damit das Zeichen das Symbol ausfüllt.
$crop = New-Object System.Windows.Rect 127, 105, 770, 770
[void](SavePng (Render $image 512 $crop $false) $logoPng)
"Logo gespeichert: $logoPng"

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = SavePng (Render $image $s $crop $false) $null }

# ICO-Datei mit PNG-Einträgen (seit Windows Vista unterstützt)
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$s].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$s].Length
}
foreach ($s in $sizes) { $w.Write($pngs[$s]) }
$w.Flush()
[IO.File]::WriteAllBytes($icoPath, $out.ToArray())
"Icon gespeichert: $icoPath"

if ($PreviewDir) {
    foreach ($s in 256, 32, 16) { [IO.File]::WriteAllBytes((Join-Path $PreviewDir "icon-$s.png"), $pngs[$s]) }
    "Vorschau gespeichert: $PreviewDir"
}
