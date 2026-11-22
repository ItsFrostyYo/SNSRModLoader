param([string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common',
    [string]$GitHubRepository = '', [string]$GitHubBranch = 'main')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
# Always rebuild production binaries, even after a diagnostic build.
& "$PSScriptRoot/build.ps1" -GameRoot $GameRoot -GitHubRepository $GitHubRepository -GitHubBranch $GitHubBranch
Push-Location $repo
try {
    $version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Select-Object -First 1
    $package = Join-Path $repo 'artifacts/package'
    $owned = @(Get-Content -LiteralPath "$package/Mods/Loader/installed-files.txt")
    $zipName = "SNSRModLoader-$version-win-x64.zip"
    New-Item -ItemType Directory -Force dist | Out-Null
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temporaryZip = Join-Path $repo "dist/$zipName.tmp"
    $stream = [IO.File]::Open($temporaryZip, [IO.FileMode]::Create)
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($path in ($owned | Sort-Object)) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $package $path), $path, [IO.Compression.CompressionLevel]::Optimal)
        }
        [void]$zip.CreateEntry('Mods/Settings/')
    } finally { $zip.Dispose(); $stream.Dispose() }
    Move-Item -LiteralPath $temporaryZip -Destination "dist/$zipName" -Force
    if ($GitHubRepository) {
        $xml = New-Object System.Xml.XmlDocument
        $root = $xml.CreateElement('loaderUpdate')
        $root.SetAttribute('schemaVersion','1'); $root.SetAttribute('version', $version)
        $root.SetAttribute('url', "https://github.com/$GitHubRepository/releases/download/v$version/$zipName")
        $root.SetAttribute('sha256', (Get-FileHash -LiteralPath "dist/$zipName" -Algorithm SHA256).Hash.ToLowerInvariant())
        [void]$xml.AppendChild($root)
        foreach ($path in ($owned | Sort-Object)) {
            $file = $xml.CreateElement('file'); $file.SetAttribute('path', $path)
            $file.SetAttribute('sha256', (Get-FileHash -LiteralPath (Join-Path $package $path) -Algorithm SHA256).Hash.ToLowerInvariant())
            [void]$root.AppendChild($file)
        }
        $xml.Save((Join-Path $repo 'dist/loader-update.xml'))
    } else {
        [IO.File]::Delete((Join-Path $repo 'dist/loader-update.xml'))
    }
    Write-Host "Release: $repo/dist/$zipName"
} finally { Pop-Location }
