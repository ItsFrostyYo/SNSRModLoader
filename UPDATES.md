# Publishing loader updates

The GitHub repository has not been selected yet. Unpublished builds leave update and verified-mod endpoints empty. Once the loader repository exists, build with its actual owner/name:

```powershell
./tools/package-release.ps1 -GitHubRepository YOUR-OWNER/SNSRModLoader -GitHubBranch main
```

This embeds the two raw GitHub endpoints into the API assembly and emits the complete ZIP plus `dist/loader-update.xml`. Existing installations with blank URL settings use those embedded endpoints. No repository checkout, Git client, access token or GitHub API request is needed on the player's computer.

For every release:

1. Increase `Version` in `Directory.Build.props`. Numeric stable versions only; prereleases/downgrades are not offered.
2. Run the loader and updater tests, and smoke-test all four game versions with `-Diagnostics` builds. Rebuild without `-Diagnostics` for publishing.
3. Create the GitHub release tag `vVERSION` and upload the generated `SNSRModLoader-VERSION-win-x64.zip` unchanged.
4. After the asset is available, copy `dist/loader-update.xml` to the repository's root as `loader-update.xml` and commit it to the configured branch. Never publish the manifest before its asset exists. Keep the generated hashes unchanged.
5. Publish `verified-mods.xml` separately using [VERIFIED_MODS.md](VERIFIED_MODS.md). It is fetched only into memory and never shipped or cached locally.

The update manifest lists every archive file and its SHA-256 and binds the package URL to the same GitHub repository. Transport uses HTTPS and permitted GitHub asset redirects only. These hashes detect corruption and package mismatch; they are not an independent signature against compromise of the repository itself. Protect repository and release publishing access.

Only `winhttp.dll`, flat loader DLLs/EXE, loader license text, the default `Config/loader.ini`, the proxy ownership hash and the package inventory may appear in an update. Plugins and their files are outside updater ownership. The inventory is installed under `Mods/Loader`, not `Config`, so future releases can remove obsolete loader dependencies without scanning or deleting user files.

To install manually after a failed/interrupted update, close the game and extract the complete release, preserving an existing `loader.ini` and all mods/settings. A rollback failure is reported explicitly and keeps its staging folder for recovery. Normal automated updates never relaunch or terminate the game.
