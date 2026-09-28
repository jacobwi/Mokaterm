<#
.SYNOPSIS
	Draws the image Setup.exe shows while it installs.

.DESCRIPTION
	Velopack's Setup puts the splash image in a borderless window the size of the image, scaled by the display's DPI,
	and paints its progress bar over the bottom 12 px of it, full width and square. So the image is drawn at 1x, its
	bottom corners are square and its last 12 px are a dark rail the bar fills. Without an image, Setup shows a generic
	progress box instead.

	package-windows.ps1 runs this for every package, so the version on the splash is always the one being installed.
	Fonts come from Windows: Cascadia Mono (Consolas where it is missing) and Segoe UI, the fallbacks of the Moka type
	stack.

.PARAMETER Version
	The version to print, as the package carries it (0.2.0 or 0.2.0-beta.1).

.PARAMETER OutFile
	Where to write the PNG.

.EXAMPLE
	powershell -NoProfile -STA -ExecutionPolicy Bypass -File build/make-installer-splash.ps1 -Version 0.1.1 -OutFile splash.png
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)] [string] $Version,
	[Parameter(Mandatory)] [string] $OutFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
	throw 'WPF draws the splash and needs a single-threaded apartment: run this with Windows PowerShell or pwsh -STA.'
}

Add-Type -AssemblyName PresentationCore, WindowsBase

# Logical size. Velopack scales the whole image by the display's DPI, so 1 px here is 1.5 px at 150%.
$width = 520
$height = 236
# Velopack's progress bar: 12 px high along the bottom edge.
$railHeight = 12
$radius = 10
$padding = 36

function New-Color([string] $Hex, [double] $Alpha = 1.0) {
	$color = [System.Windows.Media.ColorConverter]::ConvertFromString($Hex)
	[System.Windows.Media.Color]::FromArgb([byte][Math]::Round(255 * $Alpha), $color.R, $color.G, $color.B)
}

function New-Brush([string] $Hex, [double] $Alpha = 1.0) {
	$brush = New-Object System.Windows.Media.SolidColorBrush (New-Color $Hex $Alpha)
	$brush.Freeze()
	$brush
}

# Moka tokens: near-black surfaces, red accent, red-tinted borders.
$background = New-Brush '#060608'
$surface = New-Brush '#101015'
$rail = New-Brush '#14141a'
$text = New-Brush '#e8e8ec'
$textMuted = New-Brush '#a0a0aa'
$textFaint = New-Brush '#6a6a74'
$red = New-Brush '#ef5350'
$borderPen = New-Object System.Windows.Media.Pen (New-Brush '#ef5350' 0.22), 1
$borderPen.Freeze()
$hairline = New-Brush '#ef5350' 0.14

$monoFamily = New-Object System.Windows.Media.FontFamily 'Cascadia Mono, Consolas'
$sansFamily = New-Object System.Windows.Media.FontFamily 'Segoe UI'
$culture = [System.Globalization.CultureInfo]::InvariantCulture

function New-Text([string] $Value, $Family, $Weight, [double] $Size, $Brush) {
	$typeface = New-Object System.Windows.Media.Typeface $Family, ([System.Windows.FontStyles]::Normal), $Weight, ([System.Windows.FontStretches]::Normal)
	New-Object System.Windows.Media.FormattedText $Value, $culture, ([System.Windows.FlowDirection]::LeftToRight), $typeface, $Size, $Brush, 1.0
}

# WPF has no letter spacing, so a micro label is laid out a character at a time. Returns the width it took.
function Measure-Spaced([string] $Value, [double] $Size, [double] $Tracking) {
	$total = 0.0
	foreach ($character in $Value.ToCharArray()) {
		$glyph = New-Text ([string] $character) $sansFamily ([System.Windows.FontWeights]::SemiBold) $Size $textFaint
		$total += $glyph.WidthIncludingTrailingWhitespace + $Size * $Tracking
	}

	$total - $Size * $Tracking
}

function Draw-Spaced($Context, [string] $Value, [double] $Size, [double] $Tracking, $Brush, [double] $X, [double] $Baseline) {
	$cursor = $X
	foreach ($character in $Value.ToCharArray()) {
		$glyph = New-Text ([string] $character) $sansFamily ([System.Windows.FontWeights]::SemiBold) $Size $Brush
		$Context.DrawText($glyph, (New-Object System.Windows.Point $cursor, ($Baseline - $glyph.Baseline)))
		$cursor += $glyph.WidthIncludingTrailingWhitespace + $Size * $Tracking
	}
}

# The card: rounded at the top, square at the bottom where the progress bar runs edge to edge.
$card = New-Object System.Windows.Media.StreamGeometry
$figure = $card.Open()
$figure.BeginFigure((New-Object System.Windows.Point 0, $height), $true, $true)
$figure.LineTo((New-Object System.Windows.Point 0, $radius), $true, $false)
$figure.ArcTo((New-Object System.Windows.Point $radius, 0), (New-Object System.Windows.Size $radius, $radius), 0, $false, ([System.Windows.Media.SweepDirection]::Clockwise), $true, $false)
$figure.LineTo((New-Object System.Windows.Point ($width - $radius), 0), $true, $false)
$figure.ArcTo((New-Object System.Windows.Point $width, $radius), (New-Object System.Windows.Size $radius, $radius), 0, $false, ([System.Windows.Media.SweepDirection]::Clockwise), $true, $false)
$figure.LineTo((New-Object System.Windows.Point $width, $height), $true, $false)
$figure.Close()
$card.Freeze()

$visual = New-Object System.Windows.Media.DrawingVisual
$dc = $visual.RenderOpen()
$dc.PushClip($card)
$dc.DrawGeometry($background, $null, $card)

# A faint red glow behind the mark, the inset glow Moka uses for active surfaces.
$glow = New-Object System.Windows.Media.RadialGradientBrush
$glow.Center = New-Object System.Windows.Point 0.12, 0.18
$glow.GradientOrigin = $glow.Center
$glow.RadiusX = 0.62
$glow.RadiusY = 1.1
$glow.GradientStops.Add((New-Object System.Windows.Media.GradientStop (New-Color '#ef5350' 0.12), 0))
$glow.GradientStops.Add((New-Object System.Windows.Media.GradientStop (New-Color '#ef5350' 0.04), 0.45))
$glow.GradientStops.Add((New-Object System.Windows.Media.GradientStop (New-Color '#ef5350' 0), 1))
$glow.Freeze()
$dc.DrawRectangle($glow, $null, (New-Object System.Windows.Rect 0, 0, $width, $height))

# The app's mark: the icon's rounded tile with the red >_, on a raised surface so it reads against the card.
$tile = 48
$tileTop = 42
$tileRect = New-Object System.Windows.Rect $padding, $tileTop, $tile, $tile
$tilePen = New-Object System.Windows.Media.Pen (New-Brush '#ef5350' 0.28), 1
$dc.DrawRoundedRectangle($surface, $tilePen, $tileRect, 10, 10)
$unit = $tile / 64.0
$glyphPen = New-Object System.Windows.Media.Pen $red, (5 * $unit)
$glyphPen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
$glyphPen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
$glyphPen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
function P([double] $X, [double] $Y) { New-Object System.Windows.Point ($padding + $X * $unit), ($tileTop + $Y * $unit) }
$chevron = New-Object System.Windows.Media.StreamGeometry
$path = $chevron.Open()
$path.BeginFigure((P 16 20), $false, $false)
$path.LineTo((P 30 32), $true, $false)
$path.LineTo((P 16 44), $true, $false)
$path.Close()
$dc.DrawGeometry($null, $glyphPen, $chevron)
$dc.DrawLine($glyphPen, (P 34 44), (P 48 44))

# Wordmark, centred on the tile.
$wordmark = New-Text 'mokaterm' $monoFamily ([System.Windows.FontWeights]::SemiBold) 28 $text
$wordX = $padding + $tile + 16
$wordY = $tileTop + ($tile - $wordmark.Height) / 2
$dc.DrawText($wordmark, (New-Object System.Windows.Point $wordX, $wordY))

# What it speaks, as a micro label under the mark.
$dot = [string] [char] 0x00B7
$protocols = ('SSH', 'SFTP', 'FTP', 'VNC', 'RDP', 'TELNET') -join "  $dot  "
Draw-Spaced $dc $protocols 10 0.08 $textFaint $padding ($tileTop + $tile + 34)

# Status row above the rail: a glowing dot and the step, the version on the right.
$statusBaseline = $height - $railHeight - 28
$dotCenter = New-Object System.Windows.Point ($padding + 3), ($statusBaseline - 3.5)
$halo = New-Object System.Windows.Media.RadialGradientBrush
$halo.GradientStops.Add((New-Object System.Windows.Media.GradientStop (New-Color '#ef5350' 0.45), 0))
$halo.GradientStops.Add((New-Object System.Windows.Media.GradientStop (New-Color '#ef5350' 0), 1))
$halo.Freeze()
$dc.DrawEllipse($halo, $null, $dotCenter, 9, 9)
$dc.DrawEllipse($red, $null, $dotCenter, 3, 3)
Draw-Spaced $dc 'INSTALLING' 10 0.08 $textMuted ($padding + 16) $statusBaseline

$versionText = New-Text ('v' + $Version) $monoFamily ([System.Windows.FontWeights]::Normal) 11 $textFaint
$dc.DrawText($versionText, (New-Object System.Windows.Point ($width - $padding - $versionText.Width), ($statusBaseline - $versionText.Baseline)))

# The rail the progress bar fills, with a hairline above it.
$dc.DrawRectangle($rail, $null, (New-Object System.Windows.Rect 0, ($height - $railHeight), $width, $railHeight))
$dc.DrawRectangle($hairline, $null, (New-Object System.Windows.Rect 0, ($height - $railHeight - 1), $width, 1))
$dc.Pop()

# Border half a pixel inside the edge, down both sides and over the top; the bottom edge is the rail.
$edge = New-Object System.Windows.Media.StreamGeometry
$outline = $edge.Open()
$outline.BeginFigure((New-Object System.Windows.Point 0.5, $height), $false, $false)
$outline.LineTo((New-Object System.Windows.Point 0.5, $radius), $true, $false)
$outline.ArcTo((New-Object System.Windows.Point $radius, 0.5), (New-Object System.Windows.Size ($radius - 0.5), ($radius - 0.5)), 0, $false, ([System.Windows.Media.SweepDirection]::Clockwise), $true, $false)
$outline.LineTo((New-Object System.Windows.Point ($width - $radius), 0.5), $true, $false)
$outline.ArcTo((New-Object System.Windows.Point ($width - 0.5), $radius), (New-Object System.Windows.Size ($radius - 0.5), ($radius - 0.5)), 0, $false, ([System.Windows.Media.SweepDirection]::Clockwise), $true, $false)
$outline.LineTo((New-Object System.Windows.Point ($width - 0.5), $height), $true, $false)
$outline.Close()
$dc.PushClip($card)
$dc.DrawGeometry($null, $borderPen, $edge)
$dc.Pop()
$dc.Close()

$bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $width, $height, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($visual)
$straight = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $bitmap, ([System.Windows.Media.PixelFormats]::Bgra32), $null, 0
$encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($straight))
$directory = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutFile))
if (-not (Test-Path $directory)) {
	New-Item -ItemType Directory -Path $directory | Out-Null
}

$stream = [System.IO.File]::Create($OutFile)
try {
	$encoder.Save($stream)
}
finally {
	$stream.Dispose()
}

Write-Host ("Wrote $OutFile ({0}x{1})" -f $width, $height)
