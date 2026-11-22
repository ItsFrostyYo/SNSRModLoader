# Loader architecture

## Startup path

1. Windows loads the small `winhttp.dll` proxy beside `Subnautica.exe`. It forwards WinHTTP exports to the real system DLL. It hooks Unity's lookup of `mono_jit_init_version` in either the old executable-based player or `UnityPlayer.dll`.
2. The proxy lets Unity initialize its own Mono domain, then invokes `SNSRModLoader.Bootstrap.Entrypoint.Main` from `Mods/Loader`. It only targets the 64-bit `Subnautica.exe`. It does not start another runtime, support IL2CPP, redirect game logs, or require a root INI.
3. The managed bootstrap installs dependency resolution, reads `enabled` from `loader.ini`, hashes `Subnautica_Data/Managed/Assembly-CSharp.dll`, and looks up the result in the compiled build registry. Unknown binaries are rejected with their fingerprint in `bootstrap.log`; folder names and the game's uninformative `0.0.0.0` assembly version are not used as compatibility evidence.
4. Mono.Cecil injects a call to the shared runtime into `UnityEngine.GameObject`'s static constructor **in memory**. The bootstrap uses `UnityEngine.dll` for 2018 and `UnityEngine.CoreModule.dll` for later releases. It loads the modified bytes without rewriting the installed game DLL.
5. The first Unity `GameObject` use starts the loader on the main thread. A persistent host with `SceneCleanerPreserve` survives game scene cleanup. Metadata discovery and dependency/conflict checks run before accepted plugin classes are instantiated.
6. Plugins receive independent settings directories, log sources, and Harmony owners. Load errors are isolated; dependent plugins are skipped and patches belonging to failed loads are removed. Successful instances unload in reverse order on normal application quit. A force kill cannot call plugin cleanup.
7. Harmony hooks `uGUI_BuildWatermark.Start` and `UpdateText`, shared by all four builds. An observer on each actual native watermark handles activation, deactivation, parent changes, and destruction immediately; `LateUpdate` follows graphic/canvas visibility without a menu lookup or timed polling. The loader uses the font from that specific native Text/TMP component and mirrors its canvas scaling. The game retains control of its own loading coroutine and hide conditions: 2018/2021 wait for lightmapped prefabs, scene loading and terrain; 2023/2025 wait for the main level and `WaitScreen`. The loader never copies those conditions or changes the native coroutine. A background verification fetch updates metadata and overlay colors on the main thread. Pending or unavailable verification never authorizes a session.

## One runtime across four versions

The loader API and runtime target .NET 3.5 and compile against the oldest supported Unity/game API. Later Unity managed facades forward the shared Unity types into their module assemblies, and later Mono can run these assemblies. The runtime uses only common APIs observed in all four installs. Bootstrap assembly selection still differs because Unity reorganized its engine assemblies.

This does not make every game-specific mod portable. Mods must declare targets and adapt game API or semantic differences themselves. A newer-framework DLL is rejected for September 2018 before execution. A single universal mod that avoids changed Unity/game types can use the same DLL in all four versions.

HarmonyX **2.9.0**, MonoMod **22.1.29.1**, and Cecil **0.10.4** are deliberately pinned to the combination from the working 2018 BepInEx installation. A newer tested candidate hit an old-Mono reflection initialization failure; upgrading patching dependencies requires running the 2018 live smoke test, not just a modern .NET unit test.

## Boundaries

The scope is Windows x64 Mono Subnautica for the four registered binaries. BepInEx, QModManager, other-game compatibility layers, a second CLR, IL2CPP interop generators, and a prelaunch assembly-patcher ecosystem are not bundled. Harmony supports prefixes, postfixes, transpilers, finalizers, reverse patches, and runtime patch inspection through its normal API. Mods can use Unity and managed/native APIs available in the selected game runtime; no claim is made that every possible BepInEx facility is implemented.

Exact hashes intentionally reject unknown Steam/Epic variants, game updates, and modified game assemblies. Add a fingerprint to `registry/game-builds.xml` only after identifying and testing that binary, then rebuild the loader. The registry source belongs in the development repository and is embedded as a resource; it is not shipped as an editable game config.

Remote verification identifies approved plugin binaries and targets, not approved behavior of an entire process. The authoritative endpoint will belong to this loader's own GitHub repository when published. No local registry, cached fallback, or user-editable approval file is consulted. A local URL setting, executable mod code, and loader binary can all be changed by the computer owner; leaderboard enforcement must independently verify submissions.

The native entry code and forwarding generator derive from UnityDoorstop 3.4.0.0 under CC0. This project keeps that tested Mono entry mechanism and removes the generic runtime/configuration paths. It does not depend on or distribute BepInEx itself.

## External updater

`SNSRModLoader.Updater.exe` targets the Windows .NET Framework 4.7.2 runtime and is isolated from Unity/Mono and its networking limitations. The managed runtime schedules one delayed process launch after startup. The helper uses a repository-bound HTTPS manifest and hashes for the complete package, stages outside the game, and offers an external Windows update prompt. An accepted update uses a temporary helper copy to wait for the original process ID/start time, then replaces all owned loader files, including the updater and native proxy. The installed inventory removes obsolete dependencies; configuration, plugins and settings are preserved. A per-game mutex prevents concurrent updater operations. Preflight checks reject altered native proxy ownership and paths through filesystem links. Write failures restore the original bytes; a rollback error keeps staging and requests manual recovery. Process/machine crashes during writes still require manual recovery.

Repository endpoints are embedded by `tools/build.ps1 -GitHubRepository owner/name`. The default remains unset until publication. Online verification lists are never cached, while the installed file inventory records local package ownership only. Actual mod projects live outside this repository.
