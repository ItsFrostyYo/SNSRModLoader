param([string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common', [switch]$Diagnostics,
    [string]$GitHubRepository = '', [string]$GitHubBranch = 'main')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    if ($GitHubRepository -and $GitHubRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Use owner/repository for GitHubRepository.' }
    if ($GitHubBranch -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Use a simple branch name.' }
    $raw = if ($GitHubRepository) { "https://raw.githubusercontent.com/$GitHubRepository/$GitHubBranch/" } else { '' }
    $verified = if ($raw) { $raw + 'verified-mods.xml' } else { '' }
    $updates = if ($raw) { $raw + 'loader-update.xml' } else { '' }
    New-Item -ItemType Directory -Force artifacts/generated | Out-Null
    "<online verifiedModsUrl='$verified' updateManifestUrl='$updates'/>" | Set-Content artifacts/generated/online.xml -Encoding UTF8
    $projects = @('src/SNSRModLoader.Bootstrap','src/SNSRModLoader.Runtime','src/SNSRModLoader.Updater')
    if ($Diagnostics) { $projects += @('examples/HelloMultiVersion','tests/HarmonyIntegrationTests','tests/WatermarkLifecycleTests') }
    foreach ($project in $projects) {
        & dotnet build $project -c Release "-p:GameRoot=$GameRoot" "-p:Diagnostics=$($Diagnostics.IsPresent.ToString().ToLowerInvariant())" --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (!$msbuild) { throw 'Visual Studio C++ x64 build tools are required.' }
    & $msbuild native/Proxy/Proxy.vcxproj /p:Configuration=Release /p:Platform=x64 /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'Native bootstrap build failed.' }
    $package = Join-Path $repo 'artifacts/package'
    # Remove only the disposable build output under this repository.
    if (Test-Path -LiteralPath $package) {
        $resolved = (Resolve-Path -LiteralPath $package).Path
        if ($resolved -ne (Join-Path $repo 'artifacts\package')) { throw 'Unexpected package output path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    $loader = Join-Path $package 'Mods/Loader'
    New-Item -ItemType Directory -Force "$loader/Config", "$loader/Licenses", "$package/Mods/Settings" | Out-Null
    Copy-Item native/Proxy/bin/winhttp.dll $package
    Copy-Item src/SNSRModLoader.Bootstrap/bin/Release/net35/*.dll $loader
    Copy-Item src/SNSRModLoader.Runtime/bin/Release/net35/SNSRModLoader.Runtime.dll $loader
    Copy-Item src/SNSRModLoader.Updater/bin/Release/net472/SNSRModLoader.Updater.exe $loader
    @('# SNSRModLoader configuration', 'enabled=true', "verifiedModsUrl=$verified", 'checkForUpdates=true', "updateManifestUrl=$updates") | Set-Content "$loader/Config/loader.ini"
    Copy-Item native/LICENSE.UnityDoorstop "$loader/Licenses/UnityDoorstop.txt"
    Copy-Item THIRD-PARTY-NOTICES.txt "$loader/Licenses"
    Copy-Item native/Licenses/*.txt "$loader/Licenses"
    foreach ($spec in @(@('harmonyx','2.9.0'),@('monomod.runtimedetour','22.1.29.1'),@('monomod.utils','22.1.29.1'),@('mono.cecil','0.10.4'))) {
        $dir = Join-Path $env:USERPROFILE ('.nuget/packages/' + $spec[0] + '/' + $spec[1])
        Get-ChildItem -LiteralPath $dir -Filter '*LICENSE*' -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination "$loader/Licenses/$($spec[0]).txt" }
    }
    if ($Diagnostics) {
        New-Item -ItemType Directory -Force artifacts/examples | Out-Null
        Copy-Item examples/HelloMultiVersion/bin/Release/net35/HelloMultiVersion.dll artifacts/examples
        New-Item -ItemType Directory -Force artifacts/testing | Out-Null
        Copy-Item tests/HarmonyIntegrationTests/bin/Release/net35/HarmonyIntegrationTests.dll artifacts/testing
        Copy-Item tests/WatermarkLifecycleTests/bin/Release/net35/WatermarkLifecycleTests.dll artifacts/testing
    }
    (Get-FileHash -LiteralPath "$package/winhttp.dll" -Algorithm SHA256).Hash | Set-Content "$loader/installed-proxy.sha256"
    $owned = @(Get-ChildItem -LiteralPath $package -Recurse -File | ForEach-Object { $_.FullName.Substring($package.Length + 1).Replace('\','/') }) + 'Mods/Loader/installed-files.txt'
    $owned | Sort-Object | Set-Content "$loader/installed-files.txt" -Encoding UTF8
    Write-Host "Package: $package"
} finally { Pop-Location }
