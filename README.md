# SNSRModLoader

A minimal mod loader for four versions of **Subnautica**, running on **Windows x64 / Unity Mono**. September 2018 is the primary compatibility baseline.

[Download releases](https://github.com/ItsFrostyYo/SNSRModLoader/releases) · [Verified mods](verified-mods.xml) · [Known mods](known-mods.xml)

- [Install the loader](#install-the-loader)
- [Install a mod](#install-a-mod)
- [How it works](#how-it-works)
- [Compatibility](#compatibility)
- [API documentation](#api-documentation)

## Install the loader

1. Close Subnautica.
2. Download the latest `SNSRModLoader-VERSION-win-x64.zip` from **Releases**.
3. Extract the ZIP into your game folder, next to `Subnautica.exe`.
4. Launch Subnautica normally.

Use a game installation containing one of the supported builds below. Remove any other mod loader before installing SNSRModLoader; an existing `winhttp.dll` may belong to another loader. The external updater requires **Windows .NET Framework 4.7.2 or newer**.

```text
Subnautica/
  Subnautica.exe
  winhttp.dll
  Mods/
    Loader/
    Settings/
```

## Install a mod

1. Download a mod made for **SNSRModLoader** and your specific game version.
2. Close the game and put its main `.dll` directly inside `Mods`.
3. Install any additional files according to the mod author's instructions, then launch the game.

```text
Mods/
  SubnauticaOptimizationMod.dll
  AnotherMod.dll
  Loader/
  Settings/
    ItsFrosti.OptimizationsMod/
      settings.ini
```

Keep main mod DLLs at `Mods` root. Mod settings and extra files belong under `Mods/Settings/<plugin-id>`. When replacing a mod manually, remove its previous DLL if the filename changed: keeping both creates a duplicate plugin ID. BepInEx and QModManager plugins need an SNSRModLoader port.

## How it works

The loader identifies the game using `Assembly-CSharp.dll`, reads each mod's metadata, and loads compatible plugins in dependency order. Invalid plugins, duplicate IDs, incompatible targets, missing dependencies and declared conflicts produce external Windows warnings. Conflicting plugins are blocked, and every conflict pair is reported. The game's original assemblies remain unchanged on disk.

A small watermark follows the game's own version watermark during startup and loading:

```text
SNSRModLoader
Using Optimizations Mod,
Another Mod
```

Loaded mods appear verified first, then known, then unknown. The final name has no trailing comma. Blocked conflicts appear separately under **Conflicts (not loaded)**.

| Classification | Color | Submission status |
| --- | --- | --- |
| Verified | White | Eligible for the main leaderboard, subject to its rules |
| Known | Yellow | Not allowed for leaderboard submission |
| Known with `UCE="True"` | Yellow | Unofficial Category Extensions Leaderboard only |
| Unknown | Red | Not allowed on any leaderboard |
| Conflicted | Orange | Blocked from loading; conflicts must be resolved |

Verified and known entries match the exact plugin ID, registration version, DLL SHA-256 and game target. **UCE permission comes only from `known-mods.xml`, never from the mod.** The lists are fetched into memory and never installed or cached in the game folder. Names remain red until an available list recognizes them; disabling checks leaves loaded names red, while conflicts stay orange. All-verified loads produce no classification warning and skip the known-list request. Classification is informational; leaderboard moderators decide whether a submission is accepted.

After startup, a background update check can offer loader and opted-in mod updates together. Accept the prompt, let downloading finish, close the game normally, and wait for the success message before restarting. Updates preserve settings and existing configuration. Failed writes attempt to roll back the batch. Updating a mod does not automatically verify its new DLL.

Edit `Mods/Loader/Config/loader.ini` to change these options. Missing options use their defaults.

| Option | Default | Effect |
| --- | --- | --- |
| `enabled` | `true` | Enable managed loader startup |
| `checkVerifiedMods` | `true` | Check both verified and known lists; `false` skips their requests and popups without granting approval |
| `checkForUpdates` | `true` | Check for loader updates |
| `checkForModUpdates` | `true` | Check mods whose authors opted in |
| `verifiedModsUrl` | Embedded repository URL | Override the verified-list endpoint |
| `knownModsUrl` | Embedded repository URL | Override the known-list endpoint |
| `updateManifestUrl` | Embedded repository URL | Override the loader-update endpoint |

Startup and plugin details are in `Mods/Loader/loader.log` and `bootstrap.log`; update details are in `updater.log`. To uninstall, close the game and remove this loader's `winhttp.dll` and `Mods/Loader`. Keep your mod DLLs and `Mods/Settings` to preserve them.

## Compatibility

#### Subnautica version compatibility chart

| Capability | September 2018 | December 2021 | March 2023 | October 2025 |
| --- | :---: | :---: | :---: | :---: |
| SNSRModLoader on Windows x64 | ✔️ | ✔️ | ✔️ | ✔️ |
| Shared .NET Framework 3.5 plugin API | ✔️ | ✔️ | ✔️ | ✔️ |
| Harmony patches | ✔️ | ✔️ | ✔️ | ✔️ |
| Native-font watermark and mod classification | ✔️ | ✔️ | ✔️ | ✔️ |
| Loader and opted-in single-DLL mod updates | ✔️ | ✔️ | ✔️ | ✔️ |

✔️ means supported for the registered game builds. A mod must declare each version it supports and use game APIs compatible with those versions. A shared DLL is possible, but declaring multiple targets does not adapt changed game APIs. Unregistered or modified game assemblies are rejected.

#### Platform and mod compatibility chart

| Platform, format or feature | Compatible | Details |
| --- | :---: | --- |
| Windows x64 / Unity Mono | ✔️ | Supported Subnautica builds above |
| SNSRModLoader plugins | ✔️ | One registered plugin class per main DLL |
| Harmony prefixes, postfixes, transpilers, finalizers and reverse patches | ✔️ | Through the plugin's Harmony instance |
| Dependencies and declared conflicts | ✔️ | Matched by plugin ID |
| Per-mod settings and extra files | ✔️ | Stored under `Mods/Settings/<plugin-id>` |
| Auxiliary managed DLLs | ✔️ | Resolved from the plugin's settings directory; currently prevent verified/known approval |
| Automatic single-DLL mod replacement | ✔️ | Author opt-in, GitHub release asset and update XML required |
| Automatic multi-file mod packages | ❌ | Additional binaries/assets require manual installation |
| Unported BepInEx / QModManager plugins | ❌ | A loader-specific port is required |
| Unity IL2CPP | ❌ | Mono only |
| Windows x86, macOS, Linux or ARM | ❌ | No supported loader build |
| Other games or standalone .NET / XNA applications | ❌ | Subnautica only |

## API documentation

The plugin SDK is **`SNSRModLoader.dll`**, in namespace **`SNSRModLoader`**. Reference it and `0Harmony.dll` from `Mods/Loader`, with **Copy Local disabled**. Reference the matching game/Unity assemblies only as needed, also with Copy Local disabled.

Target **.NET Framework 3.5** for September 2018 or a DLL shared across all four versions. Compile against the oldest game APIs you intend to support. A plugin referencing a newer loader API than the installed loader is skipped with an update warning. The Bootstrap and Runtime assemblies implement loader internals; normal plugins use the SDK and `PluginContext`.

### Minimal plugin

```csharp
using SNSRModLoader;

namespace ExampleMod
{
    [SNSRModLoader(PluginId, PluginName, PluginVersion)]
    [TargetVersion(GameVersion.September2018)]
    public sealed class ExamplePlugin : Plugin
    {
        public const string PluginId = "author.example";
        public const string PluginName = "Example Mod";
        public const string PluginVersion = "1.0.0";

        public override void Load(PluginContext context)
        {
            bool enabled = context.Settings.Get("enabled", true);
            context.Settings.Set("enabled", enabled);
            context.Settings.Save();

            if (enabled)
                context.Harmony.PatchAll(typeof(ExamplePlugin).Assembly);

            context.Logger.Info("Example Mod loaded.");
        }

        public override void Unload()
        {
            // Stop your workers, unsubscribe events and destroy your Unity objects.
            // The loader removes patches owned by context.Harmony afterward.
        }
    }
}
```

### Registration and identity

```csharp
[SNSRModLoader(string pluginId, string pluginName, string pluginVersion)]
```

| Value | Meaning |
| --- | --- |
| `pluginId` | Stable unique ID for settings, dependencies, conflicts and classification |
| `pluginName` | Display name used in the watermark, logs and messages |
| `pluginVersion` | Numeric `System.Version` string; use three-part versions such as `1.0.0` |

The ID must start with an ASCII letter or digit and contain only ASCII letters, digits, dots, underscores or hyphens, up to 128 characters. The display name must be nonempty, at most 128 characters, and contain no control characters. Prerelease labels such as `1.0.0-beta` are not valid `System.Version` values.

`SNSRModLoaderAttribute` exposes read-only `PluginId`, `PluginName` and `PluginVersion` properties, plus the writable update properties described below.

Each main mod DLL needs exactly one public, concrete, non-generic, top-level plugin class deriving from `Plugin`, with one registration attribute. The DLL filename, plugin ID, display name and assembly title are independent. The registration version is what classification and mod updating use. You can reuse one version constant for registration and your assembly attributes; a separate `AssemblyInfo.cs` is optional.

### Game targets

Use either a single target or a list:

```csharp
[TargetVersion(GameVersion.September2018)]
```

```csharp
[TargetVersions(
    GameVersion.September2018,
    GameVersion.December2021,
    GameVersion.March2023,
    GameVersion.October2025)]
```

| `GameVersion` value | Game release |
| --- | --- |
| `Unknown = 0` | Detection failure; invalid as a plugin target |
| `September2018 = 1` | September 2018 |
| `December2021 = 2` | December 2021 |
| `March2023 = 3` | March 2023 |
| `October2025 = 4` | October 2025 |

Missing targets, undefined values and unsupported targets are rejected. September 2018 also rejects plugins compiled for the newer CLR.

`TargetVersionAttribute.Version` returns its `GameVersion`; `TargetVersionsAttribute.Versions` returns its `GameVersion[]`.

### Dependencies and conflicts

```csharp
[PluginDependency("author.required", "1.0.0")]
[Conflicts("author.incompatible", "Incompatible Mod")]
```

Both attributes are repeatable. IDs are matched case-insensitively; filenames and display names do not determine the relationship.

| Attribute | Parameters and behavior |
| --- | --- |
| `PluginDependencyAttribute` | `(string pluginId, string minimumVersion = "0.0.0")`; dependency must load successfully first and meet the minimum version |
| `ConflictsAttribute` | `(string pluginId, string pluginName = null)`; the optional name documents the other mod; a declaration on either side blocks both installed plugins |

All copies of a duplicate ID are rejected. Missing/incompatible dependencies, dependency cycles and dependencies of blocked or failed plugins are skipped. Every distinct conflict pair is reported, even when several mods conflict with one another.

`PluginDependencyAttribute` exposes `PluginId` and `MinimumVersion`; `ConflictsAttribute` exposes `PluginId` and `PluginName`. These properties are read-only strings.

### Plugin lifecycle

| Member | Purpose |
| --- | --- |
| `abstract void Load(PluginContext context)` | Called once on Unity's main thread during startup after required dependencies load |
| `virtual void Unload()` | Optional cleanup; called in reverse load order during normal shutdown and attempted after a failed load |

Throwing from `Load` isolates that plugin, removes its loader-owned patches and prevents its dependents from loading. Use `Unload` to clean up your own events, workers and Unity objects. A process crash or forced termination cannot guarantee cleanup.

### PluginContext

The loader passes an initialized context to `Load`.

| Property / method | Type | Purpose |
| --- | --- | --- |
| `PluginId` | `string` | This plugin's registered ID |
| `PluginDirectory` | `string` | The root `Mods` directory |
| `SettingsDirectory` | `string` | `Mods/Settings/<plugin-id>`; created by the context |
| `GameVersion` | `GameVersion` | Detected game release |
| `Logger` | `PluginLogger` | Plugin-specific logging |
| `Settings` | `SettingsFile` | This plugin's `settings.ini` |
| `Harmony` | `HarmonyLib.Harmony` | Patch instance owned by this plugin |
| `GetSettingsPath(string relativePath)` | `string` | Resolve a path within the settings directory; escaping that directory throws |
| `static IsValidId(string id)` | `bool` | Validate a plugin ID |

`GetSettingsPath` resolves the path; it does not create nested directories. For example:

```csharp
string assetPath = context.GetSettingsPath("Assets/example.png");
```

The public `PluginContext(string id, string modsDirectory, GameVersion version)` constructor creates the same directory/settings/Harmony context. Normal plugins use the instance supplied by the loader.

### SettingsFile

```csharp
bool enabled = context.Settings.Get("enabled", true);
context.Settings.Set("enabled", false);
context.Settings.Save();
```

| Member | Behavior |
| --- | --- |
| `SettingsFile(string path)` | Read an existing file; a missing file starts empty |
| `T Get<T>(string key, T fallback)` | Return a converted value or the fallback when missing/invalid; supports common primitives, strings and enums |
| `void Set<T>(string key, T value)` | Change the in-memory value |
| `void Save()` | Create the parent directory if needed and write UTF-8 settings to disk |

Files use `key=value`, invariant-culture conversion and case-sensitive keys. Enum values are read case-insensitively. Blank lines and lines beginning with `#` or `;` are ignored. Keys/values cannot contain line breaks, and keys cannot contain `=`. `Save` rewrites sorted values; it does not preserve comments. Values are not saved automatically.

This is a persistence API. Mods can use the game's native options controls to provide their own settings UI.

### PluginLogger

| Member | Behavior |
| --- | --- |
| `PluginLogger(string source)` | Create a named log source |
| `void Info(object message)` | Write an informational entry |
| `void Warning(object message)` | Write a warning entry |
| `void Error(object message)` | Write an error entry |

Prefer `context.Logger`. Entries go to the loader log and Unity logging. Logger warnings/errors do not themselves open dialogs, disable plugins or change classification. The public static `Action<string> Sink` is the shared logging hook managed by the loader.

### Harmony patches

`context.Harmony` exposes the HarmonyX **2.9.0** `HarmonyLib.Harmony` API. Its owner ID is `SNSRModLoader.plugin.<plugin-id>`.

```csharp
context.Harmony.PatchAll(typeof(ExamplePlugin).Assembly);
```

An example patch class inside that assembly:

```csharp
using HarmonyLib;

[HarmonyPatch(typeof(Base), "Deinitialize")]
internal static class BaseCleanupPatch
{
    private static void Prefix()
    {
        // Run before the game's base cleanup.
    }
}
```

Prefixes, postfixes, transpilers, finalizers, direct patches and reverse patches use Harmony's normal API. The loader audits installation attempts/results and removes patches owned by the context on unload. A plugin remains responsible for hooks it creates under other owners or outside Harmony.

### Automatic mod updates

Opt in with named arguments on the registration attribute:

```csharp
[SNSRModLoader("author.example", "Example Mod", "1.0.0",
    AutomaticUpdate = true,
    UpdateRepository = "YOURNAME/YOURMOD",
    UpdateManifestUrl = "https://raw.githubusercontent.com/YOURNAME/YOURMOD/main/dist/mod-update.xml")]
```

| Registration property | Default | Purpose |
| --- | --- | --- |
| `AutomaticUpdate` | `false` | Author opt-in; false skips mod update requests |
| `UpdateRepository` | Unset | GitHub `owner/repository` hosting the mod |
| `UpdateManifestUrl` | Unset | Raw HTTPS manifest URL in that same repository |

Update metadata requires loader **1.0.2+**. Set the manifest's minimum loader version to at least the SDK version the DLL references. The loader performs all networking, prompts, staging and replacement; the mod does not need updater code or an updater component.

The manifest describes the latest release DLL:

```xml
<modUpdate schemaVersion="1" id="author.example" version="1.0.1"
  minimumLoaderVersion="1.0.4"
  url="https://github.com/YOURNAME/YOURMOD/releases/download/v1.0.1/ExampleMod.dll"
  sha256="0000000000000000000000000000000000000000000000000000000000000000">
  <target version="September2018" />
</modUpdate>
```

Replace the example values, including the hash, with the final DLL's values. Upload that exact DLL as a GitHub release asset first, then publish the XML at the configured URL. Rebuilding the DLL requires regenerating its hash. DLL metadata must agree with the manifest's ID, registration version and game support. Only newer compatible versions are offered.

All opted-in mods are checked before a combined prompt. Accepted loader/mod updates share one game-close/install session. The updater preserves existing installed filenames and settings, checks that installed files have not changed, and attempts rollback on write failure. Mod update assets are limited to one DLL; new auxiliary binaries/assets need manual installation. Network limits are 64 KiB per mod manifest, 32 MiB per mod DLL and 256 MiB for combined downloaded payloads.

### Verified and known registries

These are maintained in the loader repository, separately from a mod's update manifest. A mod cannot declare verified status or UCE permission in its registration.

Verified format:

```xml
<verifiedMods schemaVersion="1">
  <mod id="author.verified" version="1.0.0"
    sha256="0000000000000000000000000000000000000000000000000000000000000000">
    <target version="September2018" />
  </mod>
</verifiedMods>
```

Known format:

```xml
<knownMods schemaVersion="1">
  <mod id="author.extension" version="1.0.0" UCE="True"
    sha256="0000000000000000000000000000000000000000000000000000000000000000">
    <target version="September2018" />
  </mod>
</knownMods>
```

Replace the illustrative identities/hashes before publishing real entries. Each entry needs at least one valid game target. `UCE` is optional, defaults to false, and accepts `True`/`False` case-insensitively. It belongs **only in the known-mod list**. Additional unreviewed DLLs in a plugin's settings directory prevent either approval classification.

Main leaderboard eligibility requires every loaded mod verified, an available verified list and no warnings. UCE eligibility requires every loaded mod verified or known with `UCE=True`, an available classification source and no startup/load/conflict errors. A known non-UCE or unknown mod prevents both. Moderation remains authoritative.

### Advanced SDK reference

These utilities are public for inspection and tooling. Fetch methods are synchronous; call them from a worker rather than Unity's UI thread. Normal plugins can rely on the loader to perform discovery, classification and updates.

| Type | Public data |
| --- | --- |
| `PluginInfo` | `Id`, `Name`, `Version`, `Path`, `TypeName`, `Sha256`, `Targets`; `Verified`, `Status`, `UCEAllowed`, `HasAdditionalAssemblies`; `AutomaticUpdate`, `UpdateRepository`, `UpdateManifestUrl`; lists `Dependencies`, `Conflicts`, `InstalledConflicts` |
| `Dependency` | `Id`, `MinimumVersion` |
| `PluginConflict` | `Id`, optional friendly `Name` |
| `PluginStatus` | `Unknown`, `Verified`, `Known`, `Conflicted` |
| `VerificationResult` | `State`, `Message`, parsed XML `Registry` |
| `VerificationState` | `Pending`, `Available`, `Unavailable`, `Disabled`, `NotRequired` |
| `ModCatalogResult` | `Verified` and `Known` verification results |

`PluginInfo` is loader-generated metadata. `Conflicts` contains declarations; `InstalledConflicts` contains actual installed counterparts. Classification fields are derived results, not plugin-granted permissions. `HasAdditionalAssemblies` tells the classifier that the main DLL hash does not cover the whole plugin.

Identity and update-source fields in `PluginInfo` are strings, including `Version`. `Targets` is `GameVersion[]`, `Status` is `PluginStatus`, and the flags are booleans. Its list types are `List<Dependency>`, `List<PluginConflict>` and `List<PluginInfo>` respectively. `Dependency.MinimumVersion` is a `System.Version`.

| API | Behavior |
| --- | --- |
| `Discovery.Scan(string mods, GameVersion game, Action<string> warning)` | Inspect root DLL metadata and return compatible plugins in dependency order without executing them |
| `Discovery.Scan(string mods, GameVersion game, Action<string> warning, Action<PluginInfo> updateCandidate)` | Also report decoded candidates before game filtering and dependency/conflict ordering |
| `Discovery.Order(List<PluginInfo> candidates, Action<string> warning)` | Resolve dependencies, mark/block conflicts and report skipped candidates |
| `Registry.Hash(string path)` | Return lowercase SHA-256 of the file |
| `Registry.Read(Stream stream)` | Parse XML with DTD/external-entity resolution disabled |
| `Registry.ReadEmbedded(string name)` | Read an embedded registry resource; missing resources throw |
| `Registry.Detect(string assemblyPath)` | Detect the game from its assembly hash; unregistered hashes return `Unknown`, file errors still throw |
| `Registry.DetectHash(XmlDocument registry, string hash)` | Match a hash against a game-build registry |
| `Registry.DisplayName(GameVersion game)` | Return the human-readable game release |
| `Registry.IsVerified(XmlDocument registry, string id, string version, string sha256, GameVersion game)` | Match an exact verified entry |
| `Registry.IsKnown(XmlDocument registry, string id, string version, string sha256, GameVersion game, out bool uce)` | Match a known entry and return its list-controlled UCE permission |
| `Verification.Fetch(string url)` | Fetch and parse a verified list into memory |
| `Verification.FetchKnown(string url)` | Fetch and parse a known list into memory |
| `Verification.ValidateUrl(string url)` | Validate an HTTPS raw GitHub endpoint and return its `Uri` |
| `Verification.Parse(Stream stream)` | Validate the verified-list schema |
| `Verification.ParseKnown(Stream stream)` | Validate the known-list schema and UCE values |
| `ModApproval.Fetch(bool enabled, string verifiedUrl, string knownUrl, PluginInfo[] plugins, GameVersion game)` | Fetch needed catalogs; disabled mode makes no requests, and all-verified sets skip the known fetch |
| `ModApproval.Classify(PluginInfo info, ModCatalogResult catalogs, GameVersion game)` | Update the info's classification fields; conflicts take precedence, then verified, known and unknown |
| `ModApproval.Warning(PluginInfo info)` | Return the known/unknown submission message; verified/conflicted statuses return null, with conflicts reported by discovery |
| `OnlineSources.Get(string name)` | Resolve `verifiedModsUrl`, `knownModsUrl` or `updateManifestUrl` from embedded defaults; unknown names return an empty string |
| `UpdateSource.Validate(string repository, string manifestUrl)` | Validate that a mod's raw HTTPS manifest belongs to its declared GitHub repository |

Registry fetches have timeouts and a 1 MiB response limit. Failed/offline fetches return `Unavailable`; they never authorize an offline fallback. Schema parsing rejects invalid IDs, hashes, versions and targets. `UpdateSource.Validate` rejects query strings, fragments, credentials and nondefault ports. Validation helpers do not execute plugin code.

Source reference: [registration and context](src/SNSRModLoader/Api.cs) · [settings and logging](src/SNSRModLoader/Storage.cs) · [discovery](src/SNSRModLoader/Discovery.cs) · [classification](src/SNSRModLoader/ModApproval.cs)

Bundled dependency notices are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
