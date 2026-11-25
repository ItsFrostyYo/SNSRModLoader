# SNSRModLoader

A minimal Windows x64 mod loader for Subnautica **September 2018**, **December 2021**, **March 2023**, and **October 2025**.

## Installation

1. Close Subnautica.
2. Download the latest `SNSRModLoader-VERSION-win-x64.zip` from [Releases](https://github.com/ItsFrostyYo/SNSRModLoader/releases).
3. Extract it into your game folder, next to `Subnautica.exe`.
4. Put compatible SNSRModLoader mod DLLs directly inside `Mods`, then launch the game normally.

Do not install this loader alongside BepInEx or QModManager. Their plugins need a version made specifically for SNSRModLoader.

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

After the online check succeeds, loaded mods appear in this order: verified, known, then unknown. With multiple mods, commas separate the names; the last name has no comma. All warnings and update prompts are external Windows dialogs.

| Status | Name color | Leaderboards |
| --- | --- | --- |
| Verified | White | Eligible for the main leaderboard, subject to submission rules |
| Known | Yellow | Not allowed, unless its entry explicitly permits UCE |
| Known with `UCE=True` | Yellow | Unofficial Category Extensions Leaderboard only |
| Unknown | Red | Not allowed on any leaderboard |
| Conflicted | Orange | Blocked from loading; all conflict pairs are reported |

Conflicted plugins appear separately under “Conflicts (not loaded)”. Pending, unavailable or disabled checks do not grant approval; unchecked names remain red.

## Verified mods

| Verified mod | Version | Supported Versions |
| --- | --- | --- |
| Optimizations Mod | 1.0.2 | September 2018 |

The authoritative [verified-mod list](verified-mods.xml) and [known-mod list](known-mods.xml) check the plugin's exact ID, version, DLL SHA-256 and supported game version. Renaming a DLL does not change its classification, but modifying or rebuilding it does. Verified status takes precedence over known status.

The lists are fetched into memory at launch and never saved or cached in the game folder. If all loaded mods are verified, no classification warning appears and the known list is not requested. A failed connection leaves approval unknown and the game still runs. Verification is informational; leaderboard moderators decide whether a submission is accepted.

Set `checkVerifiedMods=false` in `Mods/Loader/Config/loader.ini` to disable both online classification checks and their popups. Conflict checks still run, names remain unapproved/red, and leaderboard eligibility is not granted while checks are disabled.

## Updates

The loader checks for an update in the background after the game launches. If one is available, an external Windows prompt offers to prepare it or wait until a future launch.

After accepting, let the download finish, close the game normally, and wait for the successful-update message before launching again. The updater replaces the loader and its dependencies together, while preserving your mods, settings and existing configuration. It does not close or restart the game for you.

Mods whose authors enable automatic updates are checked together with the loader. One prompt lists all available compatible updates; accepted updates install together after you close the game once. Mod settings are preserved. Updated mod DLLs are checked against the verified list again on the next launch; an automatic update does not automatically grant verification.

If one mod's update source is unavailable, other sources can still be checked. No installed files change until the complete selected batch has downloaded and validated. A write failure attempts to restore the previous files for the whole batch.

If the connection fails, the game continues and the check is retried next launch. In `Mods/Loader/Config/loader.ini`, set `checkForUpdates=false` to disable loader checks and `checkForModUpdates=false` to disable mod checks. Mods without the author's opt-in are never fetched or updated.

## Troubleshooting and removal

For a failed plugin or startup, check `Mods/Loader/loader.log` and `bootstrap.log`. Update details are in `Mods/Loader/updater.log`. A failed verification check does not prevent play, but verification remains unknown until a successful online check.

If an update reports a rollback failure, keep the game closed and reinstall the complete loader release manually, preserving `Mods/Settings`, your mod DLLs and your existing `loader.ini`.

To uninstall, close the game and remove this loader's `winhttp.dll` and `Mods/Loader`. Keep your mod DLLs and `Mods/Settings` if you want to preserve them.
