# Verified Mods

No mods are approved yet. Every mod requires review; SDK examples are not automatically verified.

When SNSRModLoader is published, this **same GitHub repository** will host the machine-readable verified-mod list and this readable approval list. Endpoint setup is deferred until publication. Installed loaders fetch the list into memory at launch and do not keep a local file or cache.

The planned remote format is XML:

```xml
<verifiedMods schemaVersion="1">
  <mod id="author.plugin" version="1.0.0"
       sha256="EXACT_64_HEX_CHARACTER_SHA256_OF_APPROVED_DLL">
    <target version="September2018" />
    <target version="December2021" />
  </mod>
</verifiedMods>
```

Replace the illustrative SHA-256 before publishing a real entry. Every entry needs an ID, numeric version, exact binary hash, and at least one supported game target. The runtime rejects malformed registry data and external XML entities. It only accepts HTTPS raw.githubusercontent.com URLs, HTTP 200, no redirects, and a response up to 1 MiB. Network operations have timeouts and run away from the Unity main thread. It sends no plugin inventory, settings, or saves; the request only downloads the shared public list.

Future release procedure:

1. Review a mod's source and exact release DLL for each proposed game target.
2. Compute its SHA-256 and publish the exact ID/version/hash/target entry in this repository's list.
3. Add the mod name, version, targets, hash, and review/release links to this page.
4. Set the loader's `verifiedModsUrl` default to that repository's raw list URL when packaging the published release.

Until then, verification is unavailable and sessions are not marked eligible. Once configured, failed or offline requests report that the list could not be checked; they do not use a stale or locally editable approval list.
