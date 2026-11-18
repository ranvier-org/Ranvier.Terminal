param(
    [Parameter(Mandatory)][string]$ReferenceDirectory,
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [switch]$VisibleOnly
)

$ErrorActionPreference = 'Stop'

function Read-Frame($file) {
    $frame = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8 | ConvertFrom-Json
    if ($file.Name -ne "monitor-$($frame.Width)x$($frame.Height).json" -or
        $frame.Width -lt 1 -or $frame.Height -lt 1 -or $frame.Cells.Count -ne $frame.Width * $frame.Height) {
        throw "Invalid dimensions in $($file.FullName)"
    }
    return $frame
}

function Find-Clock($frame) {
    if ($frame.Height -lt 2) { return -1 }
    # Scan cells so wide glyphs and multi-code-point strings cannot shift coordinates.
    for ($x = 1; $x + 8 -lt $frame.Width; $x++) {
        $i = $frame.Width + $x
        if ($frame.Cells[$i - 1].Glyph -cne '┤' -or $frame.Cells[$i + 8].Glyph -cne '├') { continue }
        $time = $frame.Cells[$i..($i + 7)].Glyph -join ''
        if ($time -cmatch '^(?:[01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9]$') { return $i }
    }
    return -1
}

$reference = @(Get-ChildItem -LiteralPath $ReferenceDirectory -Filter 'monitor-*.json' -File | Sort-Object Name)
$candidate = @(Get-ChildItem -LiteralPath $CandidateDirectory -Filter 'monitor-*.json' -File | Sort-Object Name)
if (!$reference.Count -or ($reference.Name -join ',') -cne ($candidate.Name -join ',')) {
    throw "Capture sets must be nonempty and have matching names. Reference [$($reference.Name -join ', ')] Candidate [$($candidate.Name -join ', ')]"
}

$failed = $false
for ($n = 0; $n -lt $reference.Count; $n++) {
    $before = Read-Frame $reference[$n]
    $after = Read-Frame $candidate[$n]
    $clock = Find-Clock $before
    if ($clock -ne (Find-Clock $after)) { $clock = -1 }
    $counts = [ordered]@{ Glyph = 0; Foreground = 0; Background = 0; Width = 0 }
    $changed = 0
    $clockChanges = 0
    $hiddenColors = 0
    for ($i = 0; $i -lt $before.Cells.Count; $i++) {
        $a = $before.Cells[$i]
        $b = $after.Cells[$i]
        $details = foreach ($field in 'Glyph', 'Foreground', 'Background', 'Width') {
            if ([string]::Equals($a.$field, $b.$field, [StringComparison]::Ordinal)) { continue }
            if ($field -eq 'Glyph' -and $clock -ge 0 -and $i -ge $clock -and $i -lt $clock + 8) {
                $clockChanges++
                continue
            }
            if ($VisibleOnly -and $a.Glyph -ceq $b.Glyph -and
                (($field -eq 'Foreground' -and $a.Glyph -ceq ' ') -or
                 ($field -eq 'Background' -and $a.Glyph -ceq '█'))) {
                $hiddenColors++
                continue
            }
            $counts[$field]++
            "$field '$($a.$field)' -> '$($b.$field)'"
        }
        if (!$details) { continue }
        $changed++
        if ($changed -le 8) {
            $row = 1 + [math]::Floor($i / $before.Width)
            $column = 1 + $i % $before.Width
            Write-Output "$($reference[$n].Name) row $row column $column  $($details -join ', ')"
        }
    }
    $summary = foreach ($field in $counts.Keys) { "$field=$($counts[$field])" }
    Write-Output "measured here: $($reference[$n].Name) $changed/$($before.Cells.Count) cells differ ($($summary -join ', ')); $clockChanges clock digit changes and $hiddenColors invisible color changes ignored."
    if ($changed) { $failed = $true }
}
if ($failed) { exit 1 }
