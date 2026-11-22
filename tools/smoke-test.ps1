param([string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common', [string[]]$Versions = @('Subnautica2018','Subnautica2021','Subnautica2023','SubnauticaCurrent'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$evidence = Join-Path $repo 'artifacts/smoke'
New-Item -ItemType Directory -Force $evidence | Out-Null
foreach ($name in $Versions) {
    $game = Join-Path $GameRoot $name
    & "$PSScriptRoot/install.ps1" -GamePath $game
    $example = Join-Path $game 'Mods/HelloMultiVersion.dll'
    $previous = $null
    if (Test-Path -LiteralPath $example) {
        $previous = [IO.File]::ReadAllBytes($example)
    }
    Copy-Item -LiteralPath "$repo/artifacts/examples/HelloMultiVersion.dll" -Destination $example -Force
    $testPlugin = Join-Path $game 'Mods/HarmonyIntegrationTests.dll'
    $previousTestPlugin = $null
    if (Test-Path -LiteralPath $testPlugin) { $previousTestPlugin = [IO.File]::ReadAllBytes($testPlugin) }
    Copy-Item -LiteralPath "$repo/artifacts/testing/HarmonyIntegrationTests.dll" -Destination $testPlugin -Force
    $watermarkTestPlugin = Join-Path $game 'Mods/WatermarkLifecycleTests.dll'
    $previousWatermarkLifecycleTests = $null
    if (Test-Path -LiteralPath $watermarkTestPlugin) { $previousWatermarkLifecycleTests = [IO.File]::ReadAllBytes($watermarkTestPlugin) }
    Copy-Item -LiteralPath "$repo/artifacts/testing/WatermarkLifecycleTests.dll" -Destination $watermarkTestPlugin -Force
    $exe = Join-Path $game 'Subnautica.exe'
    $started = Get-Date
    $process = $null
    $env:SNSR_SMOKE_TEST = '1'
    try {
        $process = Start-Process -FilePath $exe -ArgumentList '-screen-fullscreen 0 -screen-width 1280 -screen-height 720' -WorkingDirectory $game -WindowStyle Hidden -PassThru
        $screenshot = Join-Path $game 'Mods/Loader/menu-smoke.png'
        $timer = [Diagnostics.Stopwatch]::StartNew()
        do {
            Start-Sleep -Milliseconds 500
            if ($process.HasExited) { throw "$name exited before creating its menu watermark." }
            $capture = Get-Item -LiteralPath $screenshot -ErrorAction SilentlyContinue
        } until (($capture -and $capture.LastWriteTime -gt $started -and $capture.Length -gt 0) -or $timer.Elapsed.TotalSeconds -gt 50)
        if (!$capture -or $capture.LastWriteTime -lt $started) { throw "$name did not produce a fresh menu screenshot within 50 seconds." }
        $log = Join-Path $game 'Mods/Loader/loader.log'
        $stream = [IO.File]::Open($log, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = New-Object IO.StreamReader($stream)
        try { $content = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if (!$content.Contains('Loaded Hello Multi Version') -or !$content.Contains('Harmony prefix/postfix/transpiler/finalizer PASS') -or !$content.Contains('Harmony audit test plugin PASS') -or !$content.Contains('INSTALLED ReversePatch') -or !$content.Contains('FAILED patch class') -or !$content.Contains('Native-lifecycle watermark created') -or !$content.Contains('Watermark lifecycle PASS') -or $content.Contains('failed to load') -or $content.Contains('Watermark lifecycle FAIL') -or $content.Contains('Loader startup failed')) { throw "$name smoke test failed. See $log" }
        Copy-Item -LiteralPath $log -Destination "$evidence/$name.log" -Force
        Copy-Item -LiteralPath $screenshot -Destination "$evidence/$name.png" -Force
        Write-Host "PASS $name bootstrap, shared runtime, plugins, Harmony, and native watermark lifecycle"
    } finally {
        Remove-Item Env:SNSR_SMOKE_TEST -ErrorAction SilentlyContinue
        if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit() }
        if ($previous -ne $null) { [IO.File]::WriteAllBytes($example, $previous) }
        elseif (Test-Path -LiteralPath $example) { Remove-Item -LiteralPath $example }
        if ($previousTestPlugin -ne $null) { [IO.File]::WriteAllBytes($testPlugin, $previousTestPlugin) }
        elseif (Test-Path -LiteralPath $testPlugin) { Remove-Item -LiteralPath $testPlugin }
        if ($previousWatermarkLifecycleTests -ne $null) { [IO.File]::WriteAllBytes($watermarkTestPlugin, $previousWatermarkLifecycleTests) }
        elseif (Test-Path -LiteralPath $watermarkTestPlugin) { Remove-Item -LiteralPath $watermarkTestPlugin }
    }
}
