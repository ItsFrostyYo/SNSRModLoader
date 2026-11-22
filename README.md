# SNSRModLoader

A minimal Windows x64 mod loader for Subnautica **September 2018**, **December 2021**, **March 2023**, and **October 2025**.

## Installation

1. Close Subnautica.
2. Download `SNSRModLoader-1.0.0-win-x64.zip` from [Releases](https://github.com/ItsFrostyYo/SNSRModLoader/releases).
3. Extract it into your game folder, next to `Subnautica.exe`.
4. Put compatible SNSRModLoader mod DLLs directly inside `Mods`, then launch the game normally.

The release download is available once version 1.0.0 has been published. Do not install this loader alongside BepInEx or QModManager. Their plugins need a version made specifically for SNSRModLoader.

The game folder needs only `winhttp.dll` and `Mods` for the loader:

```text
Subnautica.exe
winhttp.dll
Mods/
  YourMod.dll
  Loader/
  Settings/
```

Loader files and configuration live inside `Mods/Loader`. Each mod keeps its settings and extra files inside `Mods/Settings`. The game's original assemblies are not changed on disk. The external updater requires Windows .NET Framework 4.7.2 or newer.

## Using mods

Each mod declares which game versions it supports. Incompatible, invalid, duplicate, conflicting or missing-dependency plugins are identified by name in an external Windows warning. Other compatible mods can continue loading.

The watermark follows the game's own version watermark during startup and loading. For one mod it shows:

```text
SNSRModLoader
Using Save Preload Base
```

With multiple mods, commas separate the names; the last name has no comma. Verified names are white. Unverified names, or names awaiting verification, are light yellow. All warnings and update prompts are external Windows dialogs.

## Verified mods

| Verified mod | Version | Supported games |
| --- | --- | --- |
| Save Preload Base | 1.1.0 | September 2018, December 2021, March 2023, October 2025 |

The authoritative [verified-mod list](verified-mods.xml) checks the plugin's exact ID, version, DLL SHA-256 and supported game version. Renaming a DLL does not change its verification, but modifying or rebuilding it does.

The list is fetched into memory at launch and is never saved or cached in the game folder. No internet connection, a failed request or an unpublished list produces a verification warning; the game still runs. Unverified plugins are marked as ineligible for leaderboard submissions. Verification is informational, and leaderboard moderators decide whether a submission is accepted.

## Updates

The loader checks for an update in the background after the game launches. If one is available, an external Windows prompt offers to prepare it or wait until a future launch.

After accepting, let the download finish, close the game normally, and wait for the successful-update message before launching again. The updater replaces the loader and its dependencies together, while preserving your mods, settings and existing configuration. It does not close or restart the game for you.

If the connection fails, the game continues and the check is retried next launch. To disable update checks, set `checkForUpdates=false` in `Mods/Loader/Config/loader.ini`.

## Troubleshooting and removal

For a failed plugin or startup, check `Mods/Loader/loader.log` and `bootstrap.log`. Update details are in `Mods/Loader/updater.log`. A failed verification check does not prevent play, but verification remains unknown until a successful online check.

If an update reports a rollback failure, keep the game closed and reinstall the complete loader release manually, preserving `Mods/Settings`, your mod DLLs and your existing `loader.ini`.

To uninstall, close the game and remove this loader's `winhttp.dll` and `Mods/Loader`. Keep your mod DLLs and `Mods/Settings` if you want to preserve them.
