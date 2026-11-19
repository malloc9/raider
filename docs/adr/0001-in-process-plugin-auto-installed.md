# Control the game through an auto-installed in-process plugin

Raider controls Raid: Shadow Legends through a BepInEx IL2CPP plugin that runs inside the game process and calls the game's own code, rather than reading memory from outside and clicking like RSL Helper and Gestal do. The desktop app installs and repairs BepInEx and the plugin in the game folder itself, so the player still installs nothing by hand. We chose this because Chores across several Accounts on a schedule are mostly *acting*, and clicking needs the game window focused and visible.

## Considered Options

- **External memory reading + simulated clicks** (RSL Helper, Gestal): leaves the game folder untouched, but every action is a click at a computed position, so Runs take over the mouse and need the window visible.
- **Vision (screenshots + template matching)**: least detectable, but the least reliable and slowest.

## Consequences

- The game folder is modified; a Plarium Play update can remove BepInEx or the plugin, and the app must detect and reinstall.
- Game patches can rename or change the classes the plugin calls, breaking Chores until the plugin is updated.
- An in-process mod is easier for Plarium to detect than an external tool; ban risk is accepted for private, own-account use.
