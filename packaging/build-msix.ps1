# build-msix.ps1 - Builds a signed MSIX package from the compiled exe.
# Run from the repo root:  powershell -ExecutionPolicy Bypass -File packaging\build-msix.ps1
#
# Prerequisites:
#   - bin\DeskWidgets.exe must already be built (run build.bat first)
#   - Windows 10 SDK (MakeAppx.exe, MakeCert.exe, SignTool.exe)
#
# Output:  bin\DeskWidgets.msix  (signed with a dev certificate)

param(
    [string]$SdkVersion = "10.0.26100.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$bin  = Join-Path $root "bin"
$exe  = Join-Path $bin "DeskWidgets.exe"

if (-not (Test-Path $exe)) {
    Write-Host "bin\DeskWidgets.exe not found - run build.bat first." -ForegroundColor Red
    exit 1
}

# --- Locate SDK tools ---
$sdkBin = "C:\Program Files (x86)\Windows Kits\10\bin\$SdkVersion\x64"
$makeappx = Join-Path $sdkBin "makeappx.exe"
$signtool = Join-Path $sdkBin "signtool.exe"
$makecert = Join-Path $sdkBin "makecert.exe"
$pvk2pfx  = Join-Path $sdkBin "pvk2pfx.exe"

if (-not (Test-Path $makeappx)) {
    Write-Host "MakeAppx.exe not found at $sdkBin - check SdkVersion parameter." -ForegroundColor Red
    exit 1
}

# --- Generate logo PNGs from the app icon ---
Write-Host "Generating logo assets from app.ico..."
$icoPath = Join-Path $root "assets\app.ico"
$assetDir = Join-Path $root "packaging\assets"
if (-not (Test-Path $assetDir)) { New-Item -ItemType Directory -Path $assetDir -Force | Out-Null }

Add-Type -AssemblyName System.Drawing

# Extract the largest image from the ICO (256x256) as the master source.
$icon256 = New-Object System.Drawing.Icon($icoPath, 256, 256)
$masterSrc = $icon256.ToBitmap()
$icon256.Dispose()

function Export-IconPng([string]$outPath, [int]$width, [int]$height) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($masterSrc, 0, 0, $width, $height)
    $g.Dispose()
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Export-IconPng (Join-Path $assetDir "Square44x44Logo.png")   44  44
Export-IconPng (Join-Path $assetDir "Square150x150Logo.png") 150 150
Export-IconPng (Join-Path $assetDir "StoreLogo.png")         50  50

# Target-size variants for the taskbar and Start menu icon (unplated).
foreach ($sz in @(16, 24, 32, 48, 256)) {
    Export-IconPng (Join-Path $assetDir "Square44x44Logo.targetsize-$sz.png") $sz $sz
}

# Wide logo: 310x150 with centered icon
$wide = New-Object System.Drawing.Bitmap(310, 150)
$g = [System.Drawing.Graphics]::FromImage($wide)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::Transparent)
$g.DrawImage($masterSrc, 91, 11, 128, 128)
$g.Dispose()
$wide.Save((Join-Path $assetDir "Wide310x150Logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$wide.Dispose()

$masterSrc.Dispose()

Write-Host "  Tile logos, target-size variants, Wide310x150Logo.png, StoreLogo.png"

# --- Create package layout ---
Write-Host "Creating package layout..."
$layout = Join-Path $bin "msix-layout"
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Path $layout | Out-Null
New-Item -ItemType Directory -Path (Join-Path $layout "assets") | Out-Null

Copy-Item $exe (Join-Path $layout "DeskWidgets.exe")
Copy-Item (Join-Path $root "packaging\AppxManifest.xml") (Join-Path $layout "AppxManifest.xml")
Copy-Item (Join-Path $assetDir "*.png") (Join-Path $layout "assets\")

# --- Build .msix ---
$msixPath = Join-Path $bin "DeskWidgets.msix"
if (Test-Path $msixPath) { Remove-Item $msixPath -Force }

Write-Host "Packing MSIX..."
& $makeappx pack /d $layout /p $msixPath /o
if ($LASTEXITCODE -ne 0) {
    Write-Host "MakeAppx failed." -ForegroundColor Red
    exit 1
}

# --- Self-signed certificate for dev/sideloading ---
$certDir  = Join-Path $root "packaging\cert"
$pfxPath  = Join-Path $certDir "DeskWidgets-dev.pfx"
$password = "DeskWidgets"
$subject  = "CN=EC8DAD99-48BE-4472-BCFD-69FB32F35F9C"   # must match Identity/Publisher in AppxManifest.xml

if (-not (Test-Path $pfxPath)) {
    Write-Host "Creating self-signed dev certificate..."
    if (-not (Test-Path $certDir)) { New-Item -ItemType Directory -Path $certDir -Force | Out-Null }

    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $subject `
        -KeyUsage DigitalSignature `
        -FriendlyName "DeskWidgets Dev" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    $securePw = ConvertTo-SecureString -String $password -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePw | Out-Null
    Write-Host "  Certificate saved to packaging\cert\DeskWidgets-dev.pfx (password: $password)"
    Write-Host "  To sideload, install the certificate into Trusted People on the target machine."
}

Write-Host "Signing MSIX..."
& $signtool sign /fd SHA256 /a /f $pfxPath /p $password $msixPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "SignTool failed - the .msix was created but is unsigned." -ForegroundColor Yellow
} else {
    Write-Host "Signed successfully."
}

# --- Done ---
$size = [math]::Round((Get-Item $msixPath).Length / 1MB, 2)
$sizeText = "$size MB"
Write-Host ""
Write-Host "Done: bin\DeskWidgets.msix ($sizeText)" -ForegroundColor Green
Write-Host ""
Write-Host "To install (sideload):"
Write-Host "  1. Install packaging\cert\DeskWidgets-dev.pfx into 'Trusted People' on the target PC"
Write-Host "  2. Double-click bin\DeskWidgets.msix"
Write-Host ""
Write-Host "For Store distribution, replace the self-signed cert with one from Partner Center."
