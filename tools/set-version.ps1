# set-version.ps1 - the one version the whole project ships with, written to all three files.
#
#   tools\set-version.ps1 1.8.8          set the version
#   tools\set-version.ps1 -Check         report what the three files say, and disagree loudly
#   tools\set-version.ps1 1.8.8 -Check   also require them to say exactly that (CI uses this)
#
# The three have drifted apart before: the manifest sat a release behind AppUpdater and the Store
# build reported an older version than the app it contained.

param(
    [Parameter(Position = 0)][string]$Version,
    [switch]$Check
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# Each file holds the version in its own shape. The capture group is the part that changes.
$targets = @(
    @{ Path = "packaging\AppxManifest.xml"; Pattern = '(?<=<Identity Name="[^"]*"\s+Publisher="[^"]*"\s+Version=")(\d+\.\d+\.\d+\.\d+)'; Four = $true },
    @{ Path = "packaging\installer.iss";    Pattern = '(?<=#define MyAppVersion ")(\d+\.\d+\.\d+)';                                     Four = $false },
    @{ Path = "src\AppUpdater.cs";          Pattern = '(?<=internal const string AppVersion = ")(\d+\.\d+\.\d+)';                       Four = $false }
)

function Read-Version($t) {
    $full = Join-Path $root $t.Path
    $text = [IO.File]::ReadAllText($full)
    $m = [regex]::Match($text, $t.Pattern)
    if (-not $m.Success) { throw "No version found in $($t.Path) - has its format changed?" }
    return $m.Value
}

# The MSIX wants four parts with the last one 0; everything else uses three.
function Shape($three, $four) {
    if ($four) { return "$three.0" }
    return $three
}

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        Write-Host "Version must be three parts, e.g. 1.8.8 (the manifest gets the trailing .0)." -ForegroundColor Red
        exit 1
    }
}

$problems = @()
foreach ($t in $targets) {
    $current = Read-Version $t
    if ($Version) {
        $want = Shape $Version $t.Four
        if ($Check) {
            if ($current -ne $want) { $problems += "$($t.Path): says $current, expected $want" }
        }
        elseif ($current -ne $want) {
            $full = Join-Path $root $t.Path
            $text = [IO.File]::ReadAllText($full)
            [IO.File]::WriteAllText($full, [regex]::Replace($text, $t.Pattern, $want))
            Write-Host "$($t.Path): $current -> $want"
        }
        else { Write-Host "$($t.Path): already $current" }
    }
    else { Write-Host "$($t.Path): $current" }
}

# With no version given, they still have to agree with each other.
if (-not $Version) {
    $three = $targets | ForEach-Object { (Read-Version $_) -replace '\.0$', '' } | Sort-Object -Unique
    if ($three.Count -ne 1) { $problems += "the three files disagree: $($three -join ', ')" }
}

if ($problems.Count -gt 0) {
    Write-Host ""
    foreach ($p in $problems) { Write-Host $p -ForegroundColor Red }
    exit 1
}

if ($Check) { Write-Host "Versions agree." -ForegroundColor Green }
