<#
.SYNOPSIS
	Draws the desktop app's icon, src/Hosts/Mokaterm.Maui/Platforms/Windows/mokaterm.ico.

.DESCRIPTION
	MAUI writes a Windows icon with a single 64x64 image. Explorer wants a 256x256 one for desktop and Start menu sizes
	and, without it, draws the small image in the middle of an empty square. This script draws the favicon's artwork (a
	rounded square with a red >_) at every size Windows asks for and writes them all into one .ico, which the exe and
	Setup.exe both carry.

	Below 64 px each stroke is a whole number of pixels wide and its straight edges sit on pixel boundaries, so the
	taskbar and title bar sizes stay sharp instead of being scaled down from a large image. Sizes up to 48 px are stored
	as 32-bit bitmaps and the larger ones as PNG, the layout Windows' own icons use, because some older readers of .ico
	files only understand bitmaps at the classic sizes.

	The result is checked in; run this again after changing the artwork.

.PARAMETER OutFile
	Where to write the icon. Defaults to the desktop host's Platforms/Windows/mokaterm.ico.

.EXAMPLE
	powershell -NoProfile -ExecutionPolicy Bypass -File build/make-app-icon.ps1
#>
[CmdletBinding()]
param(
	[string] $OutFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
	throw 'WPF draws the icon and needs a single-threaded apartment: run this with Windows PowerShell or pwsh -STA.'
}

$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutFile) {
	$OutFile = Join-Path $repo 'src\Hosts\Mokaterm.Maui\Platforms\Windows\mokaterm.ico'
}

Add-Type -AssemblyName PresentationCore, WindowsBase

# Every size the shell, the taskbar and Alt+Tab pick from at 100% to 200% display scaling.
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$largestBitmapSize = 48

function New-Brush([string] $Hex) {
	$brush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($Hex))
	$brush.Freeze()
	$brush
}

$plateBrush = New-Brush '#060608'
$accentBrush = New-Brush '#ef5350'

# The artwork of src/Hosts/Mokaterm.Web/wwwroot/favicon.svg on its 64-unit grid: a square with 12-unit corners, a
# chevron from (16,20) through (30,32) to (16,44) and an underscore from (34,44) to (48,44), stroked 5 units wide with
# round ends.
function Get-IconBitmap([int] $Size) {
	$unit = $Size / 64.0
	$hinted = $Size -lt 64
	$stroke = 5 * $unit
	if ($hinted) {
		$stroke = [Math]::Max(2, [Math]::Round($stroke, [MidpointRounding]::AwayFromZero))
	}

	$half = $stroke / 2
	$snap = {
		param([double] $Edge)
		if ($hinted) { [Math]::Round($Edge, [MidpointRounding]::AwayFromZero) } else { $Edge }
	}

	# The underscore's edges go on pixel rows and the chevron's lower end sits level with it; the upper end mirrors the
	# lower one, so the tip stays on the middle row.
	$lineY = (& $snap (44 * $unit - $half)) + $half
	$left = (& $snap (16 * $unit - $half)) + $half
	$top = New-Object System.Windows.Point $left, ($Size - $lineY)
	$tip = New-Object System.Windows.Point ($left + 14 * $unit), ($Size / 2)
	$bottom = New-Object System.Windows.Point $left, $lineY
	$lineStart = New-Object System.Windows.Point ((& $snap (34 * $unit - $half)) + $half), $lineY
	$lineEnd = New-Object System.Windows.Point ((& $snap (48 * $unit + $half)) - $half), $lineY

	$pen = New-Object System.Windows.Media.Pen $accentBrush, $stroke
	$pen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
	$pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
	$pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
	$pen.Freeze()

	$chevron = New-Object System.Windows.Media.StreamGeometry
	$figure = $chevron.Open()
	$figure.BeginFigure($top, $false, $false)
	$figure.LineTo($tip, $true, $false)
	$figure.LineTo($bottom, $true, $false)
	$figure.Close()

	$visual = New-Object System.Windows.Media.DrawingVisual
	$context = $visual.RenderOpen()
	$radius = 12 * $unit
	$context.DrawRoundedRectangle($plateBrush, $null, (New-Object System.Windows.Rect 0, 0, $Size, $Size), $radius, $radius)
	$context.DrawGeometry($null, $pen, $chevron)
	$context.DrawLine($pen, $lineStart, $lineEnd)
	$context.Close()

	$bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $Size, $Size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
	$bitmap.Render($visual)
	$bitmap.Freeze()
	$bitmap
}

# An icon bitmap: a BITMAPINFOHEADER whose height counts both the colour and the mask rows, 32-bit BGRA rows with
# straight alpha from the bottom up, then a 1-bit mask (set where transparent) for readers that ignore alpha.
function ConvertTo-IconDib($Bitmap) {
	$size = $Bitmap.PixelWidth
	$stride = $size * 4
	$pixels = New-Object byte[] ($stride * $size)
	$Bitmap.CopyPixels($pixels, $stride, 0)

	$maskStride = [int][Math]::Ceiling($size / 32.0) * 4
	$mask = New-Object byte[] ($maskStride * $size)
	$stream = New-Object System.IO.MemoryStream
	$writer = New-Object System.IO.BinaryWriter $stream
	$writer.Write([int] 40)
	$writer.Write([int] $size)
	$writer.Write([int] ($size * 2))
	$writer.Write([int16] 1)
	$writer.Write([int16] 32)
	$writer.Write([int] 0)
	$writer.Write([int] ($stride * $size + $maskStride * $size))
	$writer.Write([int] 0)
	$writer.Write([int] 0)
	$writer.Write([int] 0)
	$writer.Write([int] 0)

	for ($y = $size - 1; $y -ge 0; $y--) {
		$maskRow = ($size - 1 - $y) * $maskStride
		for ($x = 0; $x -lt $size; $x++) {
			$i = $y * $stride + $x * 4
			$alpha = $pixels[$i + 3]
			if ($alpha -eq 0) {
				$writer.Write([int] 0)
				$mask[$maskRow + ($x -shr 3)] = $mask[$maskRow + ($x -shr 3)] -bor (0x80 -shr ($x -band 7))
				continue
			}

			# WPF renders premultiplied colour; an icon bitmap holds straight colour.
			for ($channel = 0; $channel -lt 3; $channel++) {
				$writer.Write([byte][Math]::Min(255, [Math]::Round($pixels[$i + $channel] * 255.0 / $alpha)))
			}

			$writer.Write([byte] $alpha)
		}
	}

	$writer.Write($mask)
	$writer.Flush()
	, $stream.ToArray()
}

function ConvertTo-Png($Bitmap) {
	$straight = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $Bitmap, ([System.Windows.Media.PixelFormats]::Bgra32), $null, 0
	$encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
	$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($straight))
	$stream = New-Object System.IO.MemoryStream
	$encoder.Save($stream)
	, $stream.ToArray()
}

$images = foreach ($size in $sizes) {
	$bitmap = Get-IconBitmap $size
	if ($size -le $largestBitmapSize) {
		[pscustomobject]@{ Size = $size; Format = 'bitmap'; Data = (ConvertTo-IconDib $bitmap) }
	}
	else {
		[pscustomobject]@{ Size = $size; Format = 'png'; Data = (ConvertTo-Png $bitmap) }
	}
}

# ICONDIR, one ICONDIRENTRY per image (a width or height of 256 is written as 0), then the images in the same order.
$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([int16] 0)
$writer.Write([int16] 1)
$writer.Write([int16] $images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
	$side = if ($image.Size -ge 256) { 0 } else { $image.Size }
	$writer.Write([byte] $side)
	$writer.Write([byte] $side)
	$writer.Write([byte] 0)
	$writer.Write([byte] 0)
	$writer.Write([int16] 1)
	$writer.Write([int16] 32)
	$writer.Write([int] $image.Data.Length)
	$writer.Write([int] $offset)
	$offset += $image.Data.Length
}

foreach ($image in $images) {
	$writer.Write($image.Data)
}

$writer.Flush()
[System.IO.File]::WriteAllBytes($OutFile, $file.ToArray())

foreach ($image in $images) {
	Write-Host ('{0,5} px  {1,-6}  {2,7:N0} bytes' -f $image.Size, $image.Format, $image.Data.Length)
}

Write-Host ("Wrote $OutFile ({0:N0} bytes)" -f $file.Length)
