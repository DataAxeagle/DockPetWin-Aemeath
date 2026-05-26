param(
    [string]$Title = "Codex",
    [Parameter(Mandatory = $true)]
    [string]$Message,
    [string]$Type = "task_done"
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appRoot = Resolve-Path -LiteralPath (Join-Path $scriptRoot "..\DockPetWin\bin\Debug\net8.0-windows")
$bridgeRoot = Join-Path $appRoot "UserData\CodexBridge"
$inbox = Join-Path $bridgeRoot "inbox.jsonl"

New-Item -ItemType Directory -Force -Path $bridgeRoot | Out-Null
if (!(Test-Path -LiteralPath $inbox)) {
    New-Item -ItemType File -Force -Path $inbox | Out-Null
}

$payload = [ordered]@{
    type = $Type
    title = $Title
    message = $Message
    time = (Get-Date).ToUniversalTime().ToString("o")
    source = "Codex"
}

($payload | ConvertTo-Json -Compress) | Add-Content -LiteralPath $inbox -Encoding UTF8
Write-Output "Wrote notification to $inbox"
