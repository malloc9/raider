# Raider

A Windows desktop tool that drives Raid: Shadow Legends accounts on the Plarium Play client, performing routine in-game work without the player.

## Architecture

This is a .NET 8 solution with four projects:

| Project | Description |
|---------|-------------|
| **Raider.Core** | Shared contracts, models, and service interfaces |
| **Raider.Desktop** | WPF desktop app (self-contained single exe) |
| **Raider.Plugin** | BepInEx 6 IL2CPP plugin for in-game execution |
| **Raider.Tests** | Unit and integration tests |

## Walking Skeleton Features

- ✅ Solution builds; app publishes as self-contained single exe
- ✅ App finds Raid via Plarium Play's game storage record (registry), with fallback to default StandAloneApps location
- ✅ On clean game folder, app installs bundled BepInEx 6 and plugin without admin rights
- ✅ Per-install secret token generated and stored in game folder
- ✅ Plugin serves `http://localhost:<port>/raider/health` on a free port, requiring the `X-Raider-Token` header
- ✅ Health endpoint reports: in-game status, game build, plugin version
- ✅ App UI shows plugin status: Not Installed / Installed / Connected, and game build
- ✅ Interop assemblies are git-ignored and documented as generated from local install

## Building

```bash
# Build all projects
dotnet build

# Publish self-contained single exe
dotnet publish src/Raider.Desktop/Raider.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Running

1. Build and publish the desktop app
2. Run `Raider.Desktop.exe` 
3. Click "Detect Game" to find your Raid installation
4. Click "Install Plugin" to install BepInEx 6 and the plugin
5. Launch Raid via Plarium Play
6. Click "Check Health" to verify connection

## Plugin Health Endpoint

Each running Instance's plugin listens on a free localhost port and registers it in `%LOCALAPPDATA%\Raider\instances\<pid>.json` (`{pid, port, processStart}`). Every request, including `/raider/health`, requires the `X-Raider-Token` header.

Response:
```json
{
  "isInGame": true,
  "gameBuild": "7.20.0",
  "pluginVersion": "1.0.0"
}
```

## Interop Assemblies

The plugin requires game assemblies (`Assembly-CSharp.dll`, `UnityEngine.CoreModule.dll`, etc.) from a local Raid installation. These are **never committed** - see `src/Raider.Plugin/Interop/README.md`.

## Testing

```bash
dotnet test
```

## License

Private use only.