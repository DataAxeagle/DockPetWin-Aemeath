param(
    [string[]]$RuntimeIdentifiers = @("osx-arm64", "osx-x64"),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\DockPet.Aemeath.Avalonia\DockPet.Aemeath.Avalonia.csproj"
$outputRoot = Join-Path $root "output\release"
$appName = "DockPetWin-Aemeath"
$executableName = "DockPet.Aemeath.Avalonia"

foreach ($rid in $RuntimeIdentifiers) {
    $publishDir = Join-Path $outputRoot "$rid\publish"
    $appDir = Join-Path $outputRoot "$rid\$appName.app"
    $macosDir = Join-Path $appDir "Contents\MacOS"
    $resourcesDir = Join-Path $appDir "Contents\Resources"
    $zipPath = Join-Path $outputRoot "$appName-$rid-prototype.zip"

    dotnet publish $project `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:UseAppHost=true `
        -o $publishDir

    New-Item -ItemType Directory -Force -Path $macosDir, $resourcesDir | Out-Null
    Copy-Item -Path (Join-Path $publishDir "*") -Destination $macosDir -Recurse -Force

    $infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>$appName</string>
  <key>CFBundleDisplayName</key>
  <string>$appName</string>
  <key>CFBundleIdentifier</key>
  <string>com.dataaxeagle.dockpetwin.aemeath</string>
  <key>CFBundleVersion</key>
  <string>0.1.0</string>
  <key>CFBundleShortVersionString</key>
  <string>0.1.0</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleExecutable</key>
  <string>$executableName</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>LSUIElement</key>
  <true/>
</dict>
</plist>
"@

    Set-Content -LiteralPath (Join-Path $appDir "Contents\Info.plist") -Value $infoPlist -Encoding UTF8

    $readme = @(
        "# $appName macOS $rid prototype",
        "",
        "This is a macOS prototype build for Aemeath. It is not signed or notarized.",
        "",
        "## How to open",
        "",
        "After unzipping, if double-clicking does not open the app, run:",
        "",
        "    chmod +x `"$appName.app/Contents/MacOS/$executableName`"",
        "    open `"$appName.app`"",
        "",
        "If macOS says the developer cannot be verified, open System Settings > Privacy & Security and choose Open Anyway, or right-click the app and choose Open.",
        "",
        "## Included in this prototype",
        "",
        "- Transparent borderless topmost pet window",
        "- Aemeath preview image",
        "- Default salutation: Piao Bo Zhe",
        "- First-run API setup bubble",
        "- Left-button window dragging",
        "- Context menu entries",
        "",
        "## Not included yet",
        "",
        "- Full AI chat",
        "- API settings persistence UI",
        "- Full home-room interaction",
        "- macOS menu bar icon",
        "- Apple code signing and notarization"
    ) -join [Environment]::NewLine

    Set-Content -LiteralPath (Join-Path $outputRoot "$rid\README-MAC-TEST.md") -Value $readme -Encoding UTF8

    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -LiteralPath $appDir, (Join-Path $outputRoot "$rid\README-MAC-TEST.md") -DestinationPath $zipPath -Force
    Write-Host "Created $zipPath"
}
