param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$game = (Resolve-Path -LiteralPath $GamePath).Path
$exe = Join-Path $game 'Subnautica.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Choose a Subnautica install containing Subnautica.exe.' }
$assembly = Join-Path $game 'Subnautica_Data/Managed/Assembly-CSharp.dll'
[xml]$builds = Get-Content -LiteralPath (Join-Path $repo 'registry/game-builds.xml')
$hash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
if (!($builds.gameBuilds.build | Where-Object { $_.sha256 -eq $hash })) { throw 'Unknown Assembly-CSharp hash. Install refused.' }
if (Get-Process Subnautica -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { throw 'Close this game before installing.' }
$package = Join-Path $repo 'artifacts/package'
if (!(Test-Path -LiteralPath "$package/winhttp.dll")) { throw 'Run tools/build.ps1 first.' }
$proxy = Join-Path $game 'winhttp.dll'
$marker = Join-Path $game 'Mods/Loader/installed-proxy.sha256'
$oldMarker = Join-Path $game 'Mods/Loader/Config/installed-proxy.sha256'
if (!(Test-Path -LiteralPath $marker) -and (Test-Path -LiteralPath $oldMarker)) { Move-Item -LiteralPath $oldMarker -Destination $marker }
if (Test-Path -LiteralPath $proxy) {
    if (!(Test-Path -LiteralPath $marker)) { throw 'Existing winhttp.dll belongs to another loader. Back it up and remove it before installing SNSRModLoader.' }
    if ((Get-FileHash -LiteralPath $proxy).Hash -ne (Get-Content -LiteralPath $marker).Trim()) { throw 'Existing winhttp.dll was changed by another tool. Install refused.' }
}
$destination = Join-Path $game 'Mods/Loader'
if ((Test-Path -LiteralPath $destination) -and !(Test-Path -LiteralPath "$destination/SNSRModLoader.Bootstrap.dll")) { throw 'An unrelated Mods/Loader directory already exists.' }
New-Item -ItemType Directory -Force "$game/Mods/Settings", "$destination/Config" | Out-Null
# Clean only the named files/directories shipped by the early development layout.
foreach ($obsolete in @('Config/game-builds.xml','Config/verified-mods.xml','Runtime/Legacy/SNSRModLoader.Runtime.dll','Runtime/Modern/SNSRModLoader.Runtime.dll')) {
    $obsoletePath = Join-Path $destination $obsolete
    if (Test-Path -LiteralPath $obsoletePath) { Remove-Item -LiteralPath $obsoletePath -Force }
}
foreach ($obsolete in @('Runtime/Legacy','Runtime/Modern','Runtime')) {
    $obsoletePath = Join-Path $destination $obsolete
    if ((Test-Path -LiteralPath $obsoletePath) -and !(Get-ChildItem -LiteralPath $obsoletePath -Force)) { Remove-Item -LiteralPath $obsoletePath }
}
foreach ($file in Get-ChildItem -LiteralPath "$package/Mods/Loader" -Recurse -File) {
    $relative = $file.FullName.Substring(("$package/Mods/Loader").Replace('/','\').Length).TrimStart('\')
    $target = Join-Path $destination $relative
    if ($relative -eq 'Config\loader.ini' -and (Test-Path -LiteralPath $target)) { continue }
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
Copy-Item -LiteralPath "$package/winhttp.dll" -Destination $proxy -Force
(Get-FileHash -LiteralPath $proxy).Hash | Set-Content -LiteralPath $marker
Write-Host "Installed SNSRModLoader in $game"
