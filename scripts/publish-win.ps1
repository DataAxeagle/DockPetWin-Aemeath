param(
    [string]$Configuration = "Release",
    [string]$Output = ".\artifacts\DockPetWin"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $repoRoot "DockPetWin\DockPetWin.csproj"
$publishDir = Join-Path $repoRoot $Output
$artifactsRoot = Join-Path $repoRoot "artifacts"
$resolvedPublishParent = Resolve-Path (Split-Path $publishDir -Parent)
$resolvedArtifactsRoot = if (Test-Path $artifactsRoot) {
    Resolve-Path $artifactsRoot
} else {
    New-Item -ItemType Directory -Force -Path $artifactsRoot
}

if (-not $resolvedPublishParent.Path.StartsWith($resolvedArtifactsRoot.Path, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish output must stay under $($resolvedArtifactsRoot.Path): $publishDir"
}

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

$releaseDocs = @(
    "README.md",
    "LICENSE",
    "ASSET_PACK_GUIDE.md"
)

foreach ($doc in $releaseDocs) {
    $source = Join-Path $repoRoot $doc
    if (Test-Path $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $publishDir $doc) -Force
    }
}

Write-Host "Published DockPetWin to $publishDir"
