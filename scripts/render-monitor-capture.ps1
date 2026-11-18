param([Parameter(Mandatory=$true)][string]$InputPath, [Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frame = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
$cellWidth = 10
$cellHeight = 24
$bitmap = [Drawing.Bitmap]::new([int]($frame.Width * $cellWidth), [int]($frame.Height * $cellHeight))
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$font = [Drawing.Font]::new('Consolas', 16, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$format = [Drawing.StringFormat]::GenericTypographic.Clone()
$format.FormatFlags = $format.FormatFlags -bor [Drawing.StringFormatFlags]::NoClip
$brushes = @{}
function Brush([string]$color) {
    if (-not $brushes.ContainsKey($color)) { $brushes[$color] = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($color)) }
    return $brushes[$color]
}
try {
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    for ($index = 0; $index -lt $frame.Cells.Count; $index++) {
        $cell = $frame.Cells[$index]
        $x = ($index % $frame.Width) * $cellWidth
        $y = [math]::Floor($index / $frame.Width) * $cellHeight
        $graphics.FillRectangle((Brush $cell.Background), [single]$x, [single]$y, [single]$cellWidth, [single]$cellHeight)
    }
    for ($index = 0; $index -lt $frame.Cells.Count; $index++) {
        $cell = $frame.Cells[$index]
        if ($cell.Width -eq 0 -or $cell.Glyph -eq ' ') { continue }
        $x = ($index % $frame.Width) * $cellWidth
        $y = [math]::Floor($index / $frame.Width) * $cellHeight
        if ($cell.Glyph -eq '▄') {
            $graphics.FillRectangle((Brush $cell.Foreground), [single]$x, [single]($y+$cellHeight/2), [single]$cellWidth, [single]($cellHeight/2))
        } elseif ($cell.Glyph -eq '█') {
            $graphics.FillRectangle((Brush $cell.Foreground), [single]$x, [single]$y, [single]$cellWidth, [single]$cellHeight)
        } elseif ($cell.Glyph -in @('─','│','╭','╮','╰','╯','┤','├')) {
            $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($cell.Foreground), [single]1)
            try {
                $mx = $x + $cellWidth / 2
                $my = $y + $cellHeight / 2
                if ($cell.Glyph -in @('─','╮','╯','┤')) { $graphics.DrawLine($pen,[single]$x,[single]$my,[single]$mx,[single]$my) }
                if ($cell.Glyph -in @('─','╭','╰','├')) { $graphics.DrawLine($pen,[single]$mx,[single]$my,[single]($x+$cellWidth),[single]$my) }
                if ($cell.Glyph -in @('│','╰','╯','┤','├')) { $graphics.DrawLine($pen,[single]$mx,[single]$y,[single]$mx,[single]$my) }
                if ($cell.Glyph -in @('│','╭','╮','┤','├')) { $graphics.DrawLine($pen,[single]$mx,[single]$my,[single]$mx,[single]($y+$cellHeight)) }
            } finally { $pen.Dispose() }
        } else {
            $graphics.DrawString($cell.Glyph, $font, (Brush $cell.Foreground), [single]$x, [single]($y+2), $format)
        }
    }
    $bitmap.Save($OutputPath, [Drawing.Imaging.ImageFormat]::Png)
} finally {
    foreach ($brush in $brushes.Values) { $brush.Dispose() }
    $format.Dispose()
    $font.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
