# Aemeath Agent Workspace

This is the clean local workspace bundled with DockPetWin.

## Directories

- `skills/` stores the bundled desktop skills.
- `rules/` stores local workspace rules.
- `changes/` records important workflow, skill, rule, or tool changes.
- `scripts/` stores helper scripts that can be used from the restricted Python tool.
- `output/notes/YYYY-MM-DD/` stores notes and lightweight text outputs.
- `output/reports/YYYY-MM-DD/` stores reports.
- `output/sql-output/YYYY-MM-DD/` stores SQL files.
- `output/spreadsheets/YYYY-MM-DD/` stores CSV/XLSX-style spreadsheet outputs.
- `output/documents/YYYY-MM-DD/` stores DOCX-style document outputs.

## Rules

- Every workspace task must follow this `PROJECT.md` and `rules/README.md` first.
- If the user asks to organize messy files, create indexes, manifests, or categorized copies instead of deleting originals.
- Important changes must be recorded under `changes/YYYY-MM-DD/task-name/summary.md`.
- Ordinary outputs must be categorized under `output/<category>/YYYY-MM-DD/`.
- Python is restricted to this workspace and should use `scripts/aemeath_tools.py` for basic CSV, JSON, DOCX, and XLSX creation.
- Writable targets are only `workspace/**` and `../default-agent.md`.
- Deleting files, folders, reminders, or task records is disabled.

Private chats, personal memories, API keys, generated task logs, and local user names are not included.

New users can fill API keys and personal preferences in DockPetWin settings after first launch.
