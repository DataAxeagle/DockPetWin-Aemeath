# Workspace Rules

This folder is reserved for local rules created by the user.
The share package starts clean so each user can build their own local context.

## Output Categories

- Notes: `output/notes/YYYY-MM-DD/`
- Reports: `output/reports/YYYY-MM-DD/`
- SQL: `output/sql-output/YYYY-MM-DD/`
- Spreadsheets: `output/spreadsheets/YYYY-MM-DD/`
- Documents: `output/documents/YYYY-MM-DD/`

## Permissions

- Write only inside `workspace/**` or `../default-agent.md`.
- Do not delete files or directories.
- Do not read or write API keys, tokens, passwords, `.env`, `.key`, or `.pem` files.
- When cleaning or organizing, create a new index, manifest, or categorized copy.
