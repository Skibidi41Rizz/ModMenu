# ModMenu

An in-game mod manager and config editor for **BRUTALISTICK VR**. It adds a
**MODS [CFG]** panel to the main menu, so you can see every MelonLoader mod you have
installed, switch it on or off, and edit its settings without alt-tabbing to a text file.

## What it does

- Lists every MelonLoader mod the game loaded (name + `[ON]` / `[OFF]`).
- Switches a mod on or off from inside the game and remembers the choice.
- Reads each mod's MelonPreferences config and lets you edit it live:
  - **on/off** buttons for booleans and enums,
  - **- / +** steppers for numbers, tap for one step, hold to accelerate
    (0.1 → 1 → 5 → 10 → 50 → 100 → 500 per tick),
  - text values are shown read-only,
  - **R** resets a single entry, **RESET DEFAULTS** resets every entry of the selected mod.
- Shows config categories that no mod claimed as their own (for example configs written by
  a mod that is no longer installed), so nothing is hidden from you.
- Saves to `MelonPreferences.cfg` immediately, no reload needed.

## How to use

1. Launch the game with MelonLoader installed.
2. On the main menu press **MODS [CFG]** (green button above the menu tabs).
3. Pick a mod on the left. Its settings appear on the right, five per page.
4. `X` closes the panel; switching main-menu tabs closes it too.

## What "disable" means

Disabling is a soft switch: ModMenu stops the mod's per-frame and scene callbacks
(`OnUpdate`, `OnFixedUpdate`, `OnLateUpdate`, `OnGUI`, `OnSceneWasLoaded`,
`OnSceneWasInitialized`, and the matching MonoBehaviour methods) and, if the mod exposes
its own `Enabled` preference, flips that too. It cannot undo Harmony patches a mod has
already applied or restore what it already changed in the current session.

**Restart the game after disabling a mod** if you want a clean state. The choice is
saved and applied again on the next launch.

## Requirements

- BRUTALISTICK VR (Steam)
- MelonLoader 0.7.3 — installed automatically by Thunderstore Mod Manager / r2modman
- No other mods required

## Install

- **Mod manager:** install ModMenu from Thunderstore and launch the game through the manager.
- **Manual:** copy `ModMenu.dll` into `BRUTALISTICK VR/Mods/`.

## Limits

- The list shows the first 10 registered mods; uninstall what you do not use or rely on
  the mod's own config file for the rest.
- Only `bool`, `enum`, `int` and `float` entries are editable; other types are read-only.
- Configs are matched to mods by name, so an unusually named category can appear as its
  own entry (`CFG` in the description) instead of under its mod.

## Troubleshooting

- Log: `BRUTALISTICK VR/MelonLoader/Latest.log`. ModMenu lines are tagged `[ModMenu]`.
- No panel? The UI is attached to the game's `MenuUIManager`; give the main menu a moment
  to load, and check the log for `Failed to build UI`.
- A mod does not react to its toggle: it probably keeps working through Harmony patches
  applied at load time. Turn it off and restart.

## Compatibility

- Client-side UI only. It adds no gameplay content and changes no game values by itself.
- Patches the game's tab switching and pointer handlers (`MenuUIManager.ChangeActiveTab`,
  `Button.OnPointerClick`, `Selectable.OnPointerDown/OnPointerUp/OnPointerExit`); other UI
  mods that rework the main menu may conflict.
- Works with the mods you choose to run. Whatever a mod does in multiplayer is that mod's
  business, not this panel's.

## Credits

- [MelonLoader](https://melonwiki.xyz/) and Harmony — the loader and patching library.
- Built with an AI coding agent (OpenCode, DeepSeek V4.1 Flash); code reviewed and tested
  in game by the author.

## License

MIT — see `LICENSE`.
