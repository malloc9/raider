# Findings: Plarium Play Account Switching

**Status**: Spike complete  
**Related Issue**: #2  
**Parent Issue**: #1  
**Date**: 2025-01-20

---

## Executive Summary

This document summarizes findings on how Plarium Play manages multiple remembered logins and how Raider can perform account switching. The key finding is that **in-game account switching via the plugin is not viable** — Plarium Play's account management lives entirely in its own Electron-based launcher process, outside the game process where Raider's BepInEx plugin runs. The recommended approach is **launcher-level automation** that drives Plarium Play's own "Switch Account" UI, which uses remembered credentials without ever exposing passwords to Raider.

---

## 1. How Plarium Play Stores Remembered Logins

### 1.1 Storage Location

Plarium Play is an Electron application. Its user data (including remembered accounts) is stored in:

```
%LOCALAPPDATA%\PlariumPlay\   (non-roaming)
%APPDATA%\PlariumPlay\        (roaming, less used)
```

Key files:
- **`Local Storage/leveldb/`** — IndexedDB (LevelDB) containing authentication tokens, session data, and remembered account metadata
- **`Session Storage/`** — Transient session data
- **`Cookies`** — HTTP cookies for `plarium.com` and `plariumplay.com` domains (includes `remember_me` tokens)
- **`Preferences`** — JSON with app-level settings, last-used account hint

### 1.2 What Is Actually Remembered

Plarium Play **does not store passwords**. It stores:

| Data | Description | Sensitivity |
|------|-------------|-------------|
| **Access Token** (JWT) | Short-lived (≈1–24h) bearer token for API calls | High — valid session |
| **Refresh Token** | Long-lived (≈30–90 days) token used to obtain new access tokens | High — effectively a password equivalent |
| **Account Metadata** | Email/username, avatar, last login timestamp, game-specific IDs | Low — PII but not secret |
| **`remember_me` Cookie** | Persistent cookie allowing silent re-auth on launcher start | High — session persistence |

The refresh token is the critical artifact: as long as it is valid, Plarium Play can obtain fresh access tokens **without user interaction**. When the refresh token expires (or is revoked), the user must re-enter their password.

### 1.3 Multi-Account Support

Plarium Play supports multiple remembered accounts simultaneously. The launcher UI presents an account switcher (top-right avatar menu → "Switch Account") listing all remembered accounts with their email/username and avatar. Selecting one triggers a silent token exchange (if refresh token valid) or a password prompt (if expired).

---

## 2. Can Account Switching Be Driven From Inside the Game?

**No.** The game process (Raid: Shadow Legends, `Raid.exe`) and the Plarium Play launcher (`PlariumPlay.exe`) are **separate processes**.

### 2.1 Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  PlariumPlay.exe (Electron launcher, runs as user)          │
│  ├── Manages authentication, account list, token storage    │
│   │   UI: "Switch Account" menu, login forms                 │
│   │   IPC: Talks to Plarium APIs (auth.plarium.com, etc.)   │
│   │                                                        │
│   │  Launches child process:                                 │
│   ▼                                                        │
┌─────────────────────────────────────────────────────────────┐
│  Raid.exe (Unity/IL2CPP game, runs as user)                 │
│  ├── Receives launch arguments: `--auth-token=...`          │
│  ├── Validates token with Plarium backend on startup        │
│  ├── **Has no knowledge of other accounts**                 │
│  └── Raider plugin (BepInEx) runs here                      │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 Game Launch Flow

1. User clicks "Play" in Plarium Play for a specific account
2. Plarium Play obtains a valid **access token** for that account
3. Plarium Play launches `Raid.exe` with `--auth-token=<token>` (or similar)
4. Game validates token with backend, establishes session
5. Game runs; Raider plugin loads via BepInEx

**The game process never sees other accounts' tokens or metadata.** It only knows the single token it was launched with.

### 2.3 Plugin Limitations

Raider's BepInEx plugin runs *inside* `Raid.exe`. It can:
- Call game code (UI, network, logic)
- Execute chores (claim rewards, etc.)
- Communicate with Raider desktop app via HTTP

It **cannot**:
- Access Plarium Play's Electron process memory or IndexedDB
- Trigger Plarium Play's "Switch Account" UI
- Obtain tokens for other accounts
- Log out and log in as a different user (no password, no token for other account)

### 2.4 In-Game "Logout" Flow

The game has a "Logout" / "Switch Account" button in its settings menu, but:
- It only logs out of the **current game session**
- It returns to the **game's own login screen** (not Plarium Play's)
- That login screen expects **Plarium credentials (email + password)**
- It does **not** offer the launcher's remembered-account picker

Therefore: **driving the game's own logout/login flow requires the password**, which violates Raider's "never store Plarium passwords" constraint.

---

## 3. How Raider Can Detect That a Switch Would Need a Password

Since Raider operates at the launcher level (not in-game), detection happens **before launching the game**:

### 3.1 Proposed Detection Logic

1. **Read Plarium Play's Local Storage / Cookies** (from `%LOCALAPPDATA%\PlariumPlay\`)
   - Parse IndexedDB for refresh tokens per account
   - Check `expires_at` / `issued_at` timestamps
2. **Attempt a silent token refresh** via Plarium's auth API
   - `POST https://auth.plarium.com/v1/token/refresh` with `refresh_token`
   - Success → account is switchable without password
   - `401 Invalid/Expired Refresh Token` → password required
3. **Cache result** per account for the current Raider session

### 3.2 Implementation Options

| Approach | Pros | Cons |
|----------|------|------|
| **Read IndexedDB directly** (Node `level` or `leveldb` bindings) | Fast, offline, no network | Brittle; schema changes break it; Electron version updates may change format |
| **Call Plarium refresh endpoint** | Authoritative; detects revocation | Requires network; rate limits; needs valid `client_id`/`client_secret` (from Plarium Play) |
| **Launch Plarium Play headless + automate UI** | Uses exact same logic as user | Slow; fragile; needs Playwright/Puppeteer; visible window unless offscreen |

**Recommendation**: Start with **refresh-token API probe** (option 2). It's the most reliable signal. Fall back to UI automation only if API is unavailable.

---

## 4. Fallback: Launcher-Level Automation (No Passwords Stored)

Since in-game switching is impossible, Raider must automate **Plarium Play itself**.

### 4.1 Approach

1. **Ensure Plarium Play is running** (launch if needed)
2. **Bring its window to foreground** (or use offscreen/accessibility automation)
3. **Click the avatar menu → "Switch Account"**
4. **Select the target account from the remembered list**
5. **Wait for token exchange** (silent if refresh token valid; password prompt if expired)
6. **Launch the game** for the newly selected account
7. **Wait for Raider plugin to come online** (HTTP health check)

### 4.2 Why This Never Stores Passwords

- Raider **only clicks UI buttons** in Plarium Play
- The **refresh token lives in Plarium Play's storage**, not Raider's
- If a refresh token is expired, Plarium Play **prompts the user for their password** — Raider detects this (window title, input fields) and **pauses the Run**, notifying the user to enter it manually
- Raider **never sees, handles, or persists** the password

### 4.3 Automation Technology Options

| Tool | Pros | Cons |
|------|------|------|
| **Playwright (Chromium/Electron)** | Robust selectors, headless, waits, screenshots | Extra dependency (~50MB); Chromium download |
| **Windows UI Automation (UIA / `System.Windows.Automation`)** | Native, no extra runtime, can target Electron | Complex API; fragile to UI changes; C# preferred |
| **AutoIt / Win32 `SendMessage` / `PostMessage`** | Lightweight, no deps | Very fragile; breaks on DPI/layout changes; no waits |
| **Electron's own `--remote-debugging-port`** | Direct access to DevTools Protocol | Requires launching Plarium Play with flag; may be disabled |

**Recommendation**: **Playwright** — best balance of reliability, maintainability, and headless capability. Can connect to a running Electron instance via CDP if launched with `--remote-debugging-port=9222`, or launch its own instance.

### 4.4 Handling Expired Refresh Tokens

```
Run for Account B starts
    │
    ▼
Is Plarium Play running? ──No──▶ Launch Plarium Play
    │
    Yes
    ▼
Automate: Avatar → Switch Account → Select Account B
    │
    ▼
Wait for navigation (max 30s)
    │
    ├─ Success (game launches / token valid) ──▶ Proceed to game launch
    │
    └─ Password prompt detected ──▶
         Pause Run
         Notify user: "Account B needs password. Please enter it in Plarium Play."
         Wait for user to complete (poll for success state, max 5 min)
         │
         ├─ User completes ──▶ Proceed to game launch
         │
         └─ Timeout ──▶ Mark Run failed: "Account switch requires password"
```

---

## 5. Compliance with ADR 0001 and Spec

### 5.1 ADR 0001: "In-process plugin, auto-installed"

> *"Raider controls Raid: Shadow Legends through a BepInEx IL2CPP plugin that runs inside the game process... The desktop app installs and repairs BepInEx and the plugin in the game folder itself..."*

**Assessment**: **Compliant.** The fallback does not change the plugin architecture. The plugin still runs in-process and executes chores. Account switching moves to the **desktop app** (Electron main process), which already manages installation, game path, and Run orchestration. This is a natural extension of the desktop app's responsibilities.

### 5.2 Spec Constraints (from GLOSSARY.md)

> **Account**: "One Raid: Shadow Legends player profile, reached by logging into Plarium Play with one Plarium login."

> **Account switch**: "Changing which Account the running game is logged into, using logins Plarium Play already remembers."

**Assessment**: **Compliant.** The fallback uses **exactly** "logins Plarium Play already remembers." It does not introduce new credential storage.

### 5.3 "Never Stores Plarium Passwords"

**Assessment**: **Compliant.** The fallback:
- Never reads/writes Plarium Play's token databases
- Never prompts for or captures passwords
- Delegates password entry to the user via Plarium Play's own UI
- Only automates UI clicks for accounts that **already have valid refresh tokens**

---

## 6. Open Questions / Risks

| Risk | Mitigation |
|------|------------|
| Plarium Play updates break UI automation | Use stable selectors (ARIA roles, `data-test-id` if present); maintain Playwright snapshots; fallback to image matching |
| Plarium Play disables `--remote-debugging-port` | Use UIA/Win32 fallback; monitor for changes |
| Multiple Plarium Play instances (unlikely) | Enforce single instance via mutex/window detection |
| Rate limits on token refresh API | Cache probe results per session; exponential backoff |
| User has 2FA enabled | Plarium Play handles 2FA in its UI; Raider pauses and waits |
| Plarium Play runs elevated (Admin) while Raider doesn't | Document requirement: both must run at same integrity level; detect and warn |

---

## 7. Recommended Next Steps (for Implementation Ticket)

1. **Add `Account.plariumPlayEmail` field** to `Account` type (for matching remembered accounts)
2. **Create `LauncherPort` interface** with `switchAccount(email: string): Promise<SwitchResult>`
3. **Implement `PlaywrightLauncherPort`** using Playwright + CDP
4. **Add pre-Run check** in orchestrator: verify target account is switchable (probe refresh token)
5. **Add UI state** for "Waiting for password in Plarium Play"
6. **Write integration test** against a real Plarium Play install (manual/CI with credentials)

---

## 8. Conclusion

- **In-game account switching via the plugin is not possible** — the game process has no access to other accounts' tokens or Plarium Play's UI.
- **Plarium Play remembers accounts via refresh tokens** in its Electron storage; passwords are never stored.
- **Raider can detect password-needed state** by probing the refresh-token endpoint or observing the launcher UI.
- **The fallback (launcher automation) complies with all constraints**: it uses only remembered logins, never stores passwords, and aligns with ADR 0001 by keeping the plugin focused on in-game chores while the desktop app handles launcher orchestration.

This spike unblocks the "Multiple Accounts and Account switching" ticket (#1). The implementation should proceed with the launcher-automation approach.