# After BuildThirdParty.ps1: exercise Aurie's actual control handler with concurrent logging.
# Uses its own hidden console and automatically selects Close log window; never loads the game.
param([string]$PlatformToolset = 'v145')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio MSBuild not found.' }
if (-not (Test-Path -LiteralPath "$PSScriptRoot\.thirdparty\Aurie\Aurie\x64\Release\allocator.obj")) {
    throw 'Run build\BuildThirdParty.ps1 first to prepare the patched Aurie source and native dependencies.'
}
& $msbuild "$PSScriptRoot\tests\ConsoleClose.vcxproj" -p:Configuration=Release -p:Platform=x64 "-p:PlatformToolset=$PlatformToolset" -v:minimal -nologo
if ($LASTEXITCODE -ne 0) { throw 'Console regression test build failed.' }
$work = Join-Path $root 'artifacts\console-close-test'
for ($attempt = 1; $attempt -le 5; $attempt++) {
    $test = Start-Process -FilePath "$work\bin\ConsoleClose.exe" -WorkingDirectory $work -WindowStyle Hidden -PassThru
    if (-not $test.WaitForExit(15000)) {
        $test.Kill()
        throw 'Console test timed out; stopped the isolated test process.'
    }
    if ($test.ExitCode -ne 0) { throw "Console close test failed (exit $($test.ExitCode))." }
    if (-not (Select-String -LiteralPath "$work\aurie.log" -Pattern 'LOG_AFTER_CLOSE' -Quiet)) {
        throw 'Console detached, but file logging stopped.'
    }
}
Write-Host 'Passed: Ctrl+C handler returns, log window is hidden, and file logging continues (5 runs).'
