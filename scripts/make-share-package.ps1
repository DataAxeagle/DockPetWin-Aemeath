param(
    [string]$Configuration = "Release",
    [string]$Output = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")
$desktopPetRoot = Resolve-Path -LiteralPath (Join-Path $repoRoot "..\..")
$project = Join-Path $repoRoot "DockPetWin\DockPetWin.csproj"

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $desktopPetRoot ("output\release\{0}\DockPetWin-share" -f (Get-Date -Format "yyyy-MM-dd"))
}

$publishDir = [System.IO.Path]::GetFullPath($Output)
$desktopRootFull = [System.IO.Path]::GetFullPath($desktopPetRoot.Path)

if (-not $publishDir.StartsWith($desktopRootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Share output must stay under ${desktopRootFull}: $publishDir"
}

if ((Test-Path -LiteralPath $publishDir) -and -not $Force) {
    throw "Share output already exists: $publishDir. Re-run with -Force if you want to replace it."
}

if ((Test-Path -LiteralPath $publishDir) -and $Force) {
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
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $publishDir $doc) -Force
    }
}

function Copy-PublicDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (!(Test-Path -LiteralPath $Source)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $sourceRoot = [System.IO.Path]::GetFullPath($Source).TrimEnd('\') + '\'
    Get-ChildItem -LiteralPath $Source -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($sourceRoot.Length)
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
}

function Sanitize-PublicText {
    param([Parameter(Mandatory = $true)][string]$Root)

    $xun = [string]([char]0x5de1) + [string]([char]0x5de1)
    $user = [string]([char]0x6f02) + [string]([char]0x6cca) + [string]([char]0x8005)

    Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.Extension -in ".md", ".txt", ".json", ".yaml", ".yml" } |
        ForEach-Object {
            $text = [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
            $text = $text.Replace($xun, $user)
            $text = $text.Replace("$user = $user", $user)
            $text = $text.Replace("$user=$user", $user)
            [System.IO.File]::WriteAllText($_.FullName, $text, [System.Text.UTF8Encoding]::new($false))
        }
}
function Join-Chars {
    param([int[]]$Codes)
    return -join ($Codes | ForEach-Object { [char]$_ })
}

function Copy-FirstExistingDirectory {
    param(
        [Parameter(Mandatory = $true)][string[]]$Candidates,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    foreach ($candidate in $Candidates) {
        if (Test-Path -LiteralPath $candidate) {
            Copy-PublicDirectory -Source $candidate -Destination $Destination
            return $true
        }
    }

    return $false
}

function New-CleanUserData {
    param([Parameter(Mandatory = $true)][string]$DestinationRoot)

    $sourceAgents = Join-Path $repoRoot "DockPetWin\bin\Debug\net8.0-windows\UserData\Agents"
    $sourceUserData = Join-Path $repoRoot "DockPetWin\bin\Debug\net8.0-windows\UserData"
    $templateUserData = Join-Path $repoRoot "templates\AemeathUserData"
    $userDataRoot = Join-Path $DestinationRoot "UserData"
    $agentsRoot = Join-Path $userDataRoot "Agents"
    $workspaceRoot = Join-Path $agentsRoot "workspace"

    if (!(Test-Path -LiteralPath $sourceAgents) -and (Test-Path -LiteralPath $templateUserData)) {
        Copy-PublicDirectory -Source $templateUserData -Destination $userDataRoot
        return
    }

    New-Item -ItemType Directory -Force -Path $agentsRoot | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $agentsRoot "conversations") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $agentsRoot "home-life") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $agentsRoot "memory") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $agentsRoot "tasks") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $agentsRoot "tool_outputs") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $userDataRoot "AssetPacks") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $userDataRoot "Home") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $workspaceRoot "output") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $workspaceRoot "changes") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $workspaceRoot "rules") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $workspaceRoot "skills") | Out-Null

    $workspaceProject = @(
        "# Aemeath Agent Workspace",
        "",
        "This is the clean local workspace bundled with DockPetWin.",
        "",
        "- `skills/` stores the bundled desktop skills.",
        "- `rules/` stores local workspace rules.",
        "- `changes/` and `output/` are intentionally empty in the share package.",
        "- Private chats, personal memories, API keys, generated task logs, and local user names are not included.",
        "",
        "New users can fill API keys and personal preferences in DockPetWin settings after first launch."
    )
    [System.IO.File]::WriteAllLines((Join-Path $workspaceRoot "PROJECT.md"), $workspaceProject, [System.Text.UTF8Encoding]::new($false))

    $workspaceRulesReadme = @(
        "# Workspace Rules",
        "",
        "This folder is reserved for local rules created by the user.",
        "The share package starts clean so each user can build their own local context."
    )
    [System.IO.File]::WriteAllLines((Join-Path $workspaceRoot "rules\README.md"), $workspaceRulesReadme, [System.Text.UTF8Encoding]::new($false))

    foreach ($skillName in @("desktop-memory", "skill-creator")) {
        $copied = Copy-FirstExistingDirectory -Candidates @(
            (Join-Path $sourceAgents "workspace\skills\$skillName"),
            (Join-Path $repoRoot "DockPetWin\bin\Release\net8.0-windows\UserData\Agents\workspace\skills\$skillName"),
            (Join-Path $env:USERPROFILE ".codex\skills\$skillName")
        ) -Destination (Join-Path $workspaceRoot "skills\$skillName")

        if (-not $copied) {
            throw "Required bundled workspace skill was not found: $skillName"
        }
    }

    $aemeathPackSource = Join-Path $sourceUserData "AssetPacks\my-pink-character"
    if (!(Test-Path -LiteralPath $aemeathPackSource)) {
        throw "Required Aemeath asset pack was not found: $aemeathPackSource"
    }
    Copy-PublicDirectory -Source $aemeathPackSource -Destination (Join-Path $userDataRoot "AssetPacks\my-pink-character")

    foreach ($homeConfigName in @("placements.local.json", "furniture.local.json")) {
        $homeConfigSource = Join-Path $sourceUserData "Home\$homeConfigName"
        if (Test-Path -LiteralPath $homeConfigSource) {
            Copy-Item -LiteralPath $homeConfigSource -Destination (Join-Path $userDataRoot "Home\$homeConfigName") -Force
        }
    }

    $appSettings = [ordered]@{
        CatName = Join-Chars @(0x7231,0x5f25,0x65af)
        CatIdentifier = "Aemeath"
        UserSalutation = Join-Chars @(0x6f02,0x6cca,0x8005)
        SelectedAssetPackID = "my-pink-character"
        RemindersEnabled = $true
        WaterReminderIntervalSeconds = 1800
        MovementReminderIntervalSeconds = 3600
        ReminderSchemaInitialized = $false
        Reminders = @()
        RestDurationMinimumSeconds = 120
        RestDurationMaximumSeconds = 180
        WalkDurationMinimumSeconds = 120
        WalkDurationMaximumSeconds = 300
        WalkBaseSpeed = 36
        CatScalePercent = 20
        StartPositionPercent = 75
        ActivityDisplayID = $null
    }
    ($appSettings | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath (Join-Path $userDataRoot "settings.json") -Encoding UTF8

    foreach ($fileName in @("default-agent.md", "README.md")) {
        $source = Join-Path $sourceAgents $fileName
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $agentsRoot $fileName) -Force
        }
    }

    Copy-PublicDirectory -Source (Join-Path $sourceAgents "character") -Destination (Join-Path $agentsRoot "character")
    $characterEval = Join-Path $agentsRoot "character\eval"
    if (Test-Path -LiteralPath $characterEval) {
        Remove-Item -LiteralPath $characterEval -Recurse -Force
    }

    $knowledgeRoot = Join-Path $agentsRoot "knowledge"
    New-Item -ItemType Directory -Force -Path $knowledgeRoot | Out-Null
    foreach ($fileName in @("index.md", "world.md")) {
        $source = Join-Path $sourceAgents "knowledge\$fileName"
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination (Join-Path $knowledgeRoot $fileName) -Force
        }
    }
    foreach ($dirName in @("characters", "quotes", "story")) {
        Copy-PublicDirectory -Source (Join-Path $sourceAgents "knowledge\$dirName") -Destination (Join-Path $knowledgeRoot $dirName)
    }

    Sanitize-PublicText -Root $agentsRoot

    $identityPath = Join-Path $agentsRoot "character\00_identity.md"
    if (Test-Path -LiteralPath $identityPath) {
        $identity = [System.IO.File]::ReadAllText($identityPath, [System.Text.Encoding]::UTF8)
        $salutationLabel = Join-Chars @(0x5bf9,0x7528,0x6237,0x7684,0x79f0,0x547c)
        $colon = [string]([char]0xff1a)
        $pattern = '(?m)^- ' + [System.Text.RegularExpressions.Regex]::Escape($salutationLabel) + '.+$'
        $replacement = '- ' + $salutationLabel + $colon + (Join-Chars @(0x6f02,0x6cca,0x8005))
        $identity = [System.Text.RegularExpressions.Regex]::Replace($identity, $pattern, $replacement)
        [System.IO.File]::WriteAllText($identityPath, $identity, [System.Text.UTF8Encoding]::new($false))
    }

    $settings = [ordered]@{
        provider = "deepseek"
        pet_name = Join-Chars @(0x7231,0x5f25,0x65af)
        pet_identifier = "Aemeath"
        user_salutation = Join-Chars @(0x6f02,0x6cca,0x8005)
        base_url = "https://api.deepseek.com"
        model = "deepseek-v4-flash"
        api_key = ""
        api_key_env = "DEEPSEEK_API_KEY"
        search_provider = "tavily"
        tavily_api_key = ""
        tavily_api_key_env = "TAVILY_API_KEY"
        temperature = 0.7
        max_history_messages = 30
        enable_tools = $true
        max_tool_rounds = 8
    }
    ($settings | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath (Join-Path $agentsRoot "settings.local.json") -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $agentsRoot "conversation.jsonl") -Value "" -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $agentsRoot "memory\MEMORY.md") -Value "# Memory Index`r`n" -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $agentsRoot "tasks\tasks.jsonl") -Value "" -Encoding UTF8
}
New-CleanUserData -DestinationRoot $publishDir

$firstRunGuide = @(
    "DockPetWin first run",
    "",
    "1. Double-click DockPetWin.exe to start.",
    "2. Before chat, smart home planning, or web search, right-click the pet and open Settings.",
    "3. In AI chat settings, fill in a DeepSeek API Key or use the DEEPSEEK_API_KEY environment variable.",
    "4. If you need web search, fill in a Tavily Key or use the TAVILY_API_KEY environment variable.",
    "",
    "This share package does not include the author's local UserData, chats, home memories, task records, usage statistics, or API keys.",
    "The package includes a clean initial UserData with public character and world settings only."
)
[System.IO.File]::WriteAllLines((Join-Path $publishDir "FIRST-RUN.txt"), $firstRunGuide, [System.Text.UTF8Encoding]::new($false))

$privateNames = @(
    "usage-statistics.json",
    "inbox.jsonl",
    "outbox.jsonl"
)

$privateMatches = Get-ChildItem -LiteralPath $publishDir -Recurse -Force |
    Where-Object { $privateNames -contains $_.Name }

if ($privateMatches) {
    $list = ($privateMatches | Select-Object -ExpandProperty FullName) -join [Environment]::NewLine
    throw "Share package contains private runtime data and was not accepted:$([Environment]::NewLine)$list"
}

$shareSettings = Get-Content -LiteralPath (Join-Path $publishDir "UserData\Agents\settings.local.json") -Raw | ConvertFrom-Json
if (-not [string]::IsNullOrWhiteSpace($shareSettings.api_key) -or -not [string]::IsNullOrWhiteSpace($shareSettings.tavily_api_key)) {
    throw "Share package settings.local.json contains an API key."
}

$xunMarker = [string]([char]0x5de1) + [string]([char]0x5de1)
$privatePattern = "$([System.Text.RegularExpressions.Regex]::Escape($xunMarker))|sk-[A-Za-z0-9_-]{20,}|api_key"":\s*""[^""]+|tavily_api_key"":\s*""[^""]+"
$privateTextMatches = Get-ChildItem -LiteralPath (Join-Path $publishDir "UserData") -Recurse -File |
    Where-Object { $_.Extension -in ".md", ".txt", ".json", ".yaml", ".yml" } |
    Select-String -Pattern $privatePattern -ErrorAction SilentlyContinue
if ($privateTextMatches) {
    $list = ($privateTextMatches | Select-Object -First 20 | ForEach-Object { "$($_.Path):$($_.LineNumber)" }) -join [Environment]::NewLine
    throw "Share package UserData contains private text markers:$([Environment]::NewLine)$list"
}

Write-Host "Created sanitized DockPetWin share package:"
Write-Host $publishDir
