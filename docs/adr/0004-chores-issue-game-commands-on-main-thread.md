# Chores Issue the Game's Own Commands on Unity's Main Thread, Reached by Reflection

## Context

Raid is an IL2CPP build: BepInEx generates interop proxy assemblies for it in `build\BepInEx\interop` (`Unity.Model`, `Unity.App`, `Unity.SharedModel`, … and the Unity engine's `UnityEngine.*`). There is no `Assembly-CSharp` and no `Raid_Data\Managed` folder, so the old `Directory.Build.props` that copied interop from there never found anything, and the Chores were written against guessed type names.

The game changes state through command objects (`Client.Model.Gameplay.…Commands.*Cmd`) that its UI creates and starts: `new TakeSessionChestCmd(minutes).Execute()`, with `IfOk` / `IfError` callbacks when the game server answers. Those must be created on Unity's main thread; the plugin's HTTP server runs on thread-pool threads.

## Decision

- **Chores issue the game's own commands**, not UI clicks: each Chore reads the player's state from `AppModel._userWrapper` (`Client.Model.Guard.UserWrapper`), plans the commands, and issues them one at a time, waiting for each server answer.
- **A main-thread pump runs that work.** The plugin adds a MonoBehaviour (`MainThreadPump`, via `BasePlugin.AddComponent`) that drains a work queue every frame; the HTTP thread awaits a `TaskCompletionSource`.
- **Raid's game code is reached by reflection** (by type and member name, `Il2CppReflection`), not compiled against.
- **The Unity engine is compiled against**: the plugin references `UnityEngine.CoreModule` and `Il2Cppmscorlib` from `build\BepInEx\interop` of the local game install, because a MonoBehaviour subclass needs its base type at compile time. `UnityEngine.CoreModule` is referenced under an extern alias, since it ships public copies of compiler attributes.
- **Builds still work without the game.** If the interop folder is missing, the plugin builds without the pump (warning `RAIDER001`) and every Chore fails with "this plugin was built without Raid's interop assemblies".

## Considered Options

- **Typed references to Raid's game assemblies too.** Cleaner code, but Raid's assemblies change with every patch: a renamed member would stop the plugin from loading, or throw at JIT time for a whole handler, instead of failing one Chore with the missing member's name. And every build of the repo (including the desktop app, which builds the plugin) would need the game installed.
- **No compile-time Unity reference** (hook a game method with Harmony, or `Application.onBeforeRender`, to get a per-frame callback). Avoids the game dependency entirely, but depends on game internals or stripped engine code; the MonoBehaviour is BepInEx's supported way.
- **Clicking the UI.** Needs the right screen open and breaks on layout changes; the commands are what the UI calls anyway.

## Consequences

- **Pro**: Each Chore fails on its own, with the name of the missing type or member, when a game update moves something.
- **Pro**: The game's own validation runs before each command (for example "prize already received"), so a wrong plan is refused rather than sent.
- **Con**: A plugin that can act in the game can only be built on a machine where Raid and BepInEx are installed (and BepInEx has generated the interop once).
- **Con**: Reflection hides type errors until runtime; the dry-run mode (`{"dryRun": true}`) exists to check a Chore's plan against a live game without issuing commands.
