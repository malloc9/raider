# Routine Persistence as JSON in Roaming AppData

## Context

Accounts need their Routines (enabled/disabled chores and order) to persist between app restarts. The player configures the Routine in the UI, and Runs must use the saved Routine.

## Decision

Store settings as JSON in the user's roaming app-data folder under `Raider`, using `electron-store`.

- Path: `%APPDATA%\Raider\raider-settings.json` (Windows)
- Schema validates Accounts and their Routines
- `electron-store` handles atomic writes, encryption (optional), and schema migration

## Consequences

- **Pro**: Simple, no database required; roaming profile follows user across machines
- **Pro**: Human-readable/editable for debugging
- **Con**: Not suitable for high-frequency writes (but Routine changes are infrequent)
- **Con**: Single-file; corruption loses all settings (mitigated by electron-store's atomic writes)

## Alternatives Considered

- **SQLite**: Overkill for this data volume; adds native dependency
- **LocalAppData (non-roaming)**: Doesn't follow user across machines
- **Custom JSON handling**: Reinvents atomic writes, schema validation, migration