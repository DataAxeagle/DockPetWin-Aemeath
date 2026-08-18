param(
    [string[]]$Prompts = @(
        "你最近喜欢吃什么？",
        "我今天中午吃了面。",
        "拉海洛方块配辣汤底会好吃吗？"
    )
)

$projectRoot = Split-Path -Parent $PSScriptRoot
$probeProject = Join-Path $PSScriptRoot 'AemeathLiveResponseProbe\AemeathLiveResponseProbe.csproj'
if (-not (Test-Path -LiteralPath $probeProject)) {
    throw "找不到真实回答测试宿主：$probeProject"
}

Push-Location $projectRoot
try {
    dotnet run --project $probeProject -c Debug --no-restore -- @Prompts
}
finally {
    Pop-Location
}
