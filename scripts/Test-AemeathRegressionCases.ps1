param(
    [string]$CasesPath = (Join-Path $PSScriptRoot '..\DockPetWin\bin\Debug\net8.0-windows\UserData\Agents\character\aemeath-regression-cases.json')
)

$resolved = [System.IO.Path]::GetFullPath($CasesPath)
if (-not (Test-Path -LiteralPath $resolved)) {
    throw "找不到爱弥斯回归题库：$resolved。请先启动一次新版桌宠以生成默认题库。"
}

$cases = Get-Content -LiteralPath $resolved -Raw -Encoding UTF8 | ConvertFrom-Json
if ($cases.Count -lt 12) {
    throw "回归题库数量不足：$($cases.Count)，至少需要 12 条。"
}

$lanes = @($cases.lane | Sort-Object -Unique)
if (@('immersive', 'tool') | Where-Object { $_ -notin $lanes }) {
    throw "回归题库必须同时覆盖 immersive 和 tool 两条链路。"
}

foreach ($case in $cases) {
    if ([string]::IsNullOrWhiteSpace($case.id) -or [string]::IsNullOrWhiteSpace($case.input) -or $case.expected_signals.Count -eq 0 -or $case.forbidden_signals.Count -eq 0) {
        throw "题库条目不完整：$($case | ConvertTo-Json -Compress)"
    }
}

"PASS: 已验证 $($cases.Count) 条爱弥斯角色回归题，覆盖 $($lanes -join ' / ') 两条链路。"
