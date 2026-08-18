param(
    [string]$RecordsPath = (Join-Path $PSScriptRoot '..\DockPetWin\bin\Debug\net8.0-windows\UserData\Agents\memory\permanent\records.json')
)

$resolved = [System.IO.Path]::GetFullPath($RecordsPath)
if (-not (Test-Path -LiteralPath $resolved)) {
    throw "缺少原子记忆记录：$resolved"
}

try {
    $records = @(Get-Content -LiteralPath $resolved -Raw -Encoding UTF8 | ConvertFrom-Json)
}
catch {
    throw "原子记忆记录不是有效 JSON：$resolved`n$($_.Exception.Message)"
}

foreach ($record in $records) {
    if ([string]::IsNullOrWhiteSpace($record.id) -or [string]::IsNullOrWhiteSpace($record.content)) {
        throw "记忆记录缺少 id 或 content：$($record | ConvertTo-Json -Compress)"
    }

    if ($record.importance -lt 1 -or $record.importance -gt 5 -or $record.confidence -lt 1 -or $record.confidence -gt 5) {
        throw "记忆记录权重超出范围：$($record | ConvertTo-Json -Compress)"
    }

    if ([string]::IsNullOrWhiteSpace($record.status) -or [string]::IsNullOrWhiteSpace($record.source_path)) {
        throw "记忆记录缺少状态或来源：$($record | ConvertTo-Json -Compress)"
    }
}

"PASS: 原子记忆记录有效，共 $($records.Count) 条；原始聊天仍独立保存在 conversation.jsonl / conversations 中。"
