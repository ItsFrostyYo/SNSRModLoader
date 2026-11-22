# SNSRModLoader

A Windows x64 mod loader specifically for four Subnautica releases: **September 2018**, **December 2021**, **March 2023**, and **October 2025**. September 2018 is the primary compatibility baseline.

The game-root footprint is exactly `winhttp.dll` and `Mods`. One shared managed runtime supports all four releases; nothing needs a separate runtime folder. The original game assemblies stay untouched on disk.

```text
Subnautica.exe
winhttp.dll
Mods/
  YourMod.dll
  Loader/
    SNSRModLoader.Bootstrap.dll
    SNSRModLoader.dll
    SNSRModLoader.Runtime.dll
    SNSRModLoader.Updater.exe     (external .NET Framework 4.7.2 helper)
    0Harmony.dll
    Mono.Cecil.dll                 (+ symbol/IL support DLLs)
    MonoMod.RuntimeDetour.dll
    MonoMod.Utils.dll
    Config/
      loader.ini
    Licenses/
    bootstrap.log                 (created at launch)
    loader.log                    (created at launch)
    installed-proxy.sha256        (installer ownership record)
    installed-files.txt           (loader package ownership inventory)
    updater.log                   (created when checking updates)
  Settings/
    your.unique.plugin.id/
      settings.ini
      additional-mod-files...
```

`Assembly-CSharp.dll` SHA-256 fingerprints are compiled into the loader. No build registry is installed in `Config`. Verified-mod information is fetched into memory from the loader's GitHub repository, with **no local list, cache, or offline fallback**. The repository endpoint is deliberately unset until this project is published.

## Build and install

Build requirements: .NET 9 SDK, Visual Studio 2022 C++ x64 tools and Windows SDK, and the locally installed games. NuGet restores pinned open-source dependencies. Proprietary game assemblies are only build references and are never packaged. The external updater requires Windows .NET Framework 4.7.2 or newer; it runs outside the game's Mono runtime.

```powershell
powershell -ExecutionPolicy Bypass -File tools/build.ps1
powershell -ExecutionPolicy Bypass -File tools/install.ps1 `
  -GamePath 'C:\Program Files (x86)\Steam\steamapps\common\Subnautica2018'
```

`tools/build.ps1` builds the loader and stages an installable folder in `artifacts/package`. Run `tools/package-release.ps1` to rebuild production binaries and create `dist/SNSRModLoader-1.0.0-win-x64.zip`. Extract its contents next to `Subnautica.exe` to install manually. Put plugin DLLs directly in `Mods`. Mods are separate projects and are never included in the loader release or repository.

The installer refuses to replace another loader's `winhttp.dll` or an unknown game build. Close the target game before installing. It preserves plugin settings and an existing `loader.ini`. Do not run BepInEx and SNSRModLoader together in one install.

For other install locations, pass `-GameRoot` to the build script or provide `local.paths.props` overriding `LegacyManaged`. Only the September 2018 managed assemblies are needed to compile the universal runtime; all four installs are used for compatibility checks.

## Write a plugin

Reference `Mods/Loader/SNSRModLoader.dll` and `0Harmony.dll` with Copy Local disabled. Target **.NET Framework 3.5** for a plugin intended to run in September 2018 or across all four builds. Reference the matching game's Unity/game assemblies only if needed, also with Copy Local disabled. A mod using game APIs that changed between releases needs version-specific handling or separate builds; declaring compatibility does not adapt those APIs automatically.

```csharp
using SNSRModLoader;

[SNSRModLoader("mxz.morequickslots", "More Quickslots", "1.0.0")]
[TargetVersions(GameVersion.September2018, GameVersion.December2021)]
[PluginDependency("author.requiredmod", "1.0.0")] // Optional; repeat for more dependencies.
[Conflicts("author.otherquickslots", "Other Quickslots")] // Optional; repeat for more conflicts.
public sealed class MoreQuickslots : Plugin
{
    public override void Load(PluginContext context)
    {
        int slots = context.Settings.Get("slots", 8);
        context.Settings.Set("slots", slots);
        context.Settings.Save();
        context.Logger.Info("Loading with " + slots + " slots");
        context.Harmony.PatchAll(typeof(MoreQuickslots).Assembly);
    }

    public override void Unload()
    {
        // Destroy your own Unity objects, stop workers, and unsubscribe events.
        // The loader removes patches owned by context.Harmony automatically.
    }
}
```

The three registration values mean:

| Value | Purpose |
| --- | --- |
| `mxz.morequickslots` | Stable unique plugin ID; used for settings, dependencies, conflicts and verification. |
| `More Quickslots` | Human-readable plugin name, shown verbatim in the watermark. |
| `1.0.0` | Numeric plugin version in `System.Version` format. |

The DLL filename is independent of the ID and display name. Each mod DLL declares exactly one public, concrete `Plugin` class and must specify `[TargetVersion(...)]` or `[TargetVersions(...)]`. Supported enum names are `September2018`, `December2021`, `March2023`, and `October2025`.

Conflicts match the stable plugin ID, regardless of the installed DLL filename or renamed display name. The optional second argument documents its friendly name. A declaration on either side blocks **both** plugins and reports their actual names, IDs, and DLL filenames. Dependencies of blocked or failed plugins are also skipped. Dependencies load before dependents; missing dependencies, incompatible minimum versions, cycles, and duplicate IDs are reported rather than silently selected.

`Load` runs on Unity's main thread near startup. Mods can create their own `MonoBehaviour` components for `Awake`, `Update`, coroutines, UI, and other Unity callbacks. Use `DontDestroyOnLoad` and the game's `SceneCleanerPreserve` where appropriate. `PluginContext` supplies logging, a dedicated Harmony owner, game version, settings, and `GetSettingsPath(...)` for assets or extra files. Auxiliary managed DLLs belong in that plugin's settings folder, not at `Mods` root. Plugins with additional DLLs are treated as unverified until the future verification protocol can cover those files too.

`examples/HelloMultiVersion` demonstrates registration across all four targets. It is compiled only for diagnostics and never included in a loader release.

The loader logs Harmony patch-class attempts and each hook's kind, method, target signature, owner, and installation result, plus a per-plugin summary. Failed target resolution and installation exceptions remain visible even when plugin code catches them. Direct patches and reverse patches are also audited. This observes Harmony installation, not every invocation or arbitrary non-Harmony/native hooks created by a plugin.

## Startup and verification

Every root `Mods/*.dll` is inspected as metadata before execution. Foreign, corrupt, malformed, wrong-version, duplicate, conflicting, and dependency-blocked plugins are identified by filename in the log and external Windows warning dialogs. BepInEx plugins must be ported; they are not loaded as SNSR plugins.

The loader shows only this noninteractive watermark, using plugin display names:

```text
SNSRModLoader
Using SavePreloadBase,
Another Mod
```

The first mod follows `Using `; subsequent lines contain only the mod name. Commas separate mods; the last or only mod has no trailing comma. With no loaded mods, only the header is shown. It uses the game's own version-watermark font, white for verified plugins and light yellow for unverified or unknown plugins. Its visibility follows the actual `uGUI_BuildWatermark` instances, including startup and loading, and ends when the game's watermark does. There is no main-menu dependency or one-second delay. Scene replacement, reactivation, canvas visibility, and font updates are handled by native lifecycle hooks and frame updates. All warnings and errors use external native Windows dialogs and loader logs; there are no loader panels, buttons, or error popups inside the game. The dialogs run on a worker thread so the game can continue. Automated smoke-test mode suppresses native dialogs and records this in the log.

Published builds embed their GitHub endpoints; `verifiedModsUrl` in `Mods/Loader/Config/loader.ini` can override the verification endpoint. A background request uses Windows WinHTTP so TLS works even in the old 2018 Mono runtime. A successful list matches plugin ID, numeric version, SHA-256 of the exact DLL, and game target. An unlisted loaded mod produces:

> The Plugin [name] ([filename]) is not a Verified Mod/Plugin, and is not Eligible for Leaderboard Submissions

No connection, malformed responses, missing endpoints, and HTTP failures leave verification **unknown** and display a warning. They never stop the game or silently trust an offline list. While a request is pending, mods remain yellow and the session is ineligible. The list is not downloaded to disk. See [VERIFIED_MODS.md](VERIFIED_MODS.md) for the publication format.

The status is informational, not an anti-cheat or a substitute for leaderboard moderation. Plugins execute arbitrary trusted code; this loader cannot prove a game process or a locally modified loader is untampered.

Set `enabled=false` in `loader.ini` to disable managed startup, or set the `SNSR_DISABLE` environment variable before launching to bypass the native startup hook. To uninstall, remove this loader's `winhttp.dll` and `Mods/Loader`; keep `Mods/Settings` if you want to preserve mod data.

## Validate

```powershell
dotnet build tests -c Release -p:Diagnostics=true
& tests/bin/Release/net472/SNSRModLoader.Tests.exe .
powershell -ExecutionPolicy Bypass -File tools/smoke-test.ps1
```

Run `tools/build.ps1 -Diagnostics` first for integration tests; the normal build excludes all test plugins, test access attributes, and screenshot code. The assertion suite checks real game fingerprints, metadata discovery, dependency/conflict rules, verification parsing and offline handling, settings paths, all four native watermark hook signatures, and the absence of a main-menu dependency. The smoke script installs the package in all four named game folders, temporarily adds Hello, Harmony, and watermark lifecycle tests, launches each game in turn, copies logs/images to `artifacts/smoke`, and closes only the processes it started. The tests check Harmony patch types and exercise immediate watermark creation, deactivation/reactivation, loading-canvas visibility, and destruction using an isolated native-watermark fixture with the real game's font. On 2018/2021 the image is a game screenshot; on 2023/2025 it renders a copy of the actual overlay with the native version-watermark TMP font offscreen because those players do not present their full scene in a hidden window. No save is loaded. Automated startup/lifecycle checks do not replace visible-window or gameplay compatibility testing.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the bootstrap design and [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for bundled dependency provenance.

Run `dotnet run --project tests/Updater -c Release` for update manifest/archive validation, ownership, preservation, dependency removal and injected-failure rollback tests. Test fixtures and build output stay under ignored `artifacts`, `bin` and `obj` directories.

## Loader updates

Three seconds after managed startup, the loader starts one hidden external update check. There is no periodic polling or Unity frame work. An unavailable network or endpoint is logged and does not interrupt the game. When a newer numeric version exists, a native Windows dialog offers to prepare the update or defer until the next launch. Downloads begin only after acceptance.

The helper validates HTTPS GitHub repository identity, the archive SHA-256, every file SHA-256, the package inventory and all destination paths. It stages the full release outside the game, then waits for the exact launched game process to exit. Another running copy of that game cancels installation. A temporary copy of the updater can replace the installed updater itself, the native proxy, managed loader and dependencies. Obsolete files from the previous loader inventory are removed. Existing config, root mod DLLs, settings and unrelated files are preserved. The native proxy ownership record is checked before replacement. File-write failures trigger rollback; failures and recovery details go to `Mods/Loader/updater.log`. An interrupted updater or machine shutdown during installation may still require a manual reinstall; do not relaunch while it reports installation in progress.

Set `checkForUpdates=false` to disable checks. `updateManifestUrl` optionally overrides the embedded endpoint. Both online endpoints remain unset in unpublished builds.

See [UPDATES.md](UPDATES.md) for creating and publishing a complete release. This repository contains the loader, its SDK example and its tests; game DLLs, actual mod projects, local prompts and generated outputs are excluded.
