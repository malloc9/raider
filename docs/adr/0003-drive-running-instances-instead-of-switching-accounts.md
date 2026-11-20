# Drive Already-Running Instances Instead of Switching Accounts

## Context

Raider first served several Accounts from one game by performing an Account switch before each Run: it drove Plarium Play's own "Switch account" UI (see `docs/account-switching-findings.md`). That needed Raider to run elevated (Plarium Play runs elevated, and Windows only lets an elevated app drive its window), relaunched the game between Accounts, and stopped whenever Plarium Play asked for a password.

The player already runs several Plarium Play / Raid instances side by side with another tool (RSL Helper), each logged into a different Account.

## Decision

Raider no longer switches Accounts. It discovers every running **Instance** (one Raid process, logged into one Account), learns which Account it is logged into from the plugin, and runs that Account's Routine in it. Instances are driven concurrently.

- Account switching is removed: the launcher automation, the `NeedsManualLogin` / `SwitchFailed` Run statuses and the "log in by hand" notification.
- Raider runs without elevation; it only talks to the plugin over localhost.
- Starting the Instances, and logging each into its Account, is the player's job (outside Raider).

Tracked in #28.

## Consequences

- **Pro**: No UI automation of Plarium Play, no elevation, no game relaunches, no password stops.
- **Pro**: Accounts run in parallel instead of one after another.
- **Con**: Raider depends on the player keeping the Instances running; an Account whose Instance is not running gets no Run.
- **Con**: Several plugin copies must coexist (one per Raid process), so discovery can no longer rely on one fixed port.

## Supersedes

The approach in `docs/account-switching-findings.md`.
