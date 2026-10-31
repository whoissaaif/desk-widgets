# Fails when a tracked text file contains a character that can make code read differently from how
# it runs ("Trojan Source"): a lone CR, other control characters except tab and LF, U+0085, U+2028,
# U+2029, zero-width and bidi characters, or a byte order mark after the start. Same rule as
# source_is_plain (server/schema.sql), Plugins.IsHidden (src) and hiddenCharacters (docs).
# Runs from build.bat and from the pre-commit hook in .githooks. Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-hidden.ps1 [-Staged]
param([switch]$Staged)

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# The pattern is assembled from pieces so this file never contains the characters (or an escape a
# tool could turn into them) itself: $u + '2028' becomes the regex escape for U+2028.
$u = '\' + 'u'
$class = '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F-\x9F' + $u + '200B-' + $u + '200F' + $u + '2028' + $u + '2029' +
         $u + '202A-' + $u + '202E' + $u + '2060-' + $u + '2069' + $u + 'FEFF]'
$hidden = New-Object System.Text.RegularExpressions.Regex ('\r(?!\n)|' + $class)

$binary = '.ico', '.png', '.jpg', '.jpeg', '.gif', '.bmp', '.dll', '.exe', '.zip', '.pdf', '.woff', '.woff2'
if ($Staged) { $files = git diff --cached --name-only --diff-filter=ACMR }
else { $files = git ls-files }

$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
$found = 0
foreach ($f in $files) {
    if (-not $f -or $binary -contains [IO.Path]::GetExtension($f).ToLowerInvariant()) { continue }
    $path = Join-Path $root $f
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    try { $text = $utf8.GetString([IO.File]::ReadAllBytes($path)) } catch { continue } # not UTF-8 text
    foreach ($m in $hidden.Matches($text)) {
        if ($m.Index -eq 0 -and [int]$m.Value[0] -eq 0xFEFF) { continue } # a BOM at the very start is fine
        $line = ([regex]::Matches($text.Substring(0, $m.Index), "`n")).Count + 1
        Write-Host ("{0}:{1}: hidden character U+{2:X4}" -f $f, $line, [int]$m.Value[0])
        $found++
    }
}
if ($found -gt 0) {
    Write-Host "$found hidden character(s) found. They can make code look different from what runs; write each one as an escape (backslash, u, then its four hex digits) in C# or JS, or remove them."
    exit 1
}
exit 0
