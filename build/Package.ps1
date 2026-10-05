# Builds StoneForge and puts a release together: artifacts\StoneForge-<version>\ and artifacts\StoneForge-<version>.zip.
#
#   StoneForge-<version>\
#     Install StoneForge.cmd, Uninstall StoneForge.cmd, README.txt, LICENSES\   (from build\release\)
#     files\        what goes into the game folder, as it goes there:
#       AurieCore.dll                 Aurie (third-party, lib\Aurie - see its README)
#       aurie\YYToolkit.dll           YYToolkit (third-party, lib\YYToolkit)
#       aurie\StoneForge.Bridge.dll   the native bridge
#       dotnet\                       StoneForge.Loader, StoneForge.API (+ docs), Roslyn
#       dotnet\patcher\               StoneForge.Patcher (+ GML, UndertaleModLib), AuriePatcher.exe
#
# usage: powershell -ExecutionPolicy Bypass -File build\Package.ps1 [-Configuration Release] [-NoBuild]
param(
    [string]$Configuration = "Release",
    [switch]$NoBuild
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$lib = Join-Path $root "lib"

if (-not $NoBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if (-not $msbuild) { throw "MSBuild (Visual Studio) not found" }
    & $msbuild (Join-Path $root "StoneForge.slnx") -restore "-p:Configuration=$Configuration" "-p:PlatformToolset=v145" -v:minimal -nologo
    if ($LASTEXITCODE -ne 0) { throw "The build failed" }
}

$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version
$out = Join-Path $root "artifacts\StoneForge-$version"
$files = Join-Path $out "files"
if (Test-Path $out) {
    $resolved = (Resolve-Path -LiteralPath $out).Path
    $artifactRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "Invalid release output path: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
New-Item -ItemType Directory -Force $files | Out-Null

function Put([string]$from, [string]$to) {
    if (-not (Test-Path $from)) { throw "Missing: $from" }
    New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
    Copy-Item $from $to
}

# Aurie and YYToolkit: third-party, prebuilt in lib\ (build\BuildThirdParty.ps1 rebuilds them from source).
Put "$lib\Aurie\AurieCore.dll" "$files\AurieCore.dll"
Put "$lib\YYToolkit\YYToolkit.dll" "$files\aurie\YYToolkit.dll"
Put "$lib\Aurie\AuriePatcher.exe" "$files\dotnet\patcher\AuriePatcher.exe"
# StoneForge.
Put "$root\StoneForge.Bridge\bin\x64\$Configuration\StoneForge.Bridge.dll" "$files\aurie\StoneForge.Bridge.dll"
$loader = "$root\StoneForge.Loader\bin\$Configuration\net10.0"
foreach ($f in "StoneForge.Loader.dll", "StoneForge.Loader.deps.json", "StoneForge.Loader.runtimeconfig.json",
               "StoneForge.API.dll", "StoneForge.API.xml", "StoneForge.GmlGenerator.dll", "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll") {
    Put "$loader\$f" "$files\dotnet\$f"
}
$patcher = "$root\StoneForge.Patcher\bin\$Configuration\net10.0-windows"
# A stale build directory must not silently reintroduce dependencies the patcher no longer has.
foreach ($obsolete in 'UndertaleModTool.dll', 'Serilog.dll') {
    if (Test-Path (Join-Path $patcher $obsolete)) { throw "Stale patcher dependency $obsolete. Clean and rebuild the patcher before packaging." }
}
Get-ChildItem $patcher -Recurse -File | Where-Object Extension -ne ".pdb" | ForEach-Object {
    Put $_.FullName (Join-Path "$files\dotnet\patcher" $_.FullName.Substring($patcher.Length + 1))
}

# The release's own files (Windows line endings for the .cmd files), the version in the README, the licences.
Get-ChildItem "$PSScriptRoot\release" -Recurse -File | ForEach-Object {
    $target = Join-Path $out $_.FullName.Substring("$PSScriptRoot\release".Length + 1)
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    $text = (Get-Content $_.FullName -Raw).Replace("{VERSION}", $version) -replace "`r?`n", "`r`n"
    [IO.File]::WriteAllText($target, $text)
}
Put "$root\LICENSE" "$out\LICENSES\StoneForge-MIT.txt"
Put "$lib\Aurie\LICENSE" "$out\LICENSES\Aurie-AGPL-3.0.txt"
Put "$lib\YYToolkit\LICENSE" "$out\LICENSES\YYToolkit-AGPL-3.0.txt"
# (Where the AGPL binaries come from: upstream commits, and the patch for the modified Aurie.)
Put "$lib\Aurie\README.md" "$out\LICENSES\Aurie-SOURCE.md"
Put "$lib\Aurie\stoneforge.patch" "$out\LICENSES\Aurie-stoneforge.patch"
Put "$lib\YYToolkit\stoneforge.patch" "$out\LICENSES\YYToolkit-stoneforge.patch"
Put "$lib\YYToolkit\README.md" "$out\LICENSES\YYToolkit-SOURCE.md"
Put "$lib\UndertaleModLib\LICENSE.txt" "$out\LICENSES\UndertaleModLib-GPL-3.0.txt"
Put "$lib\UndertaleModLib\Underanalyzer-LICENSE.txt" "$out\LICENSES\Underanalyzer-MPL-2.0.txt"
Put "$lib\UndertaleModLib\README.md" "$out\LICENSES\UndertaleModLib-SOURCE.md"

$zip = Join-Path $root "artifacts\StoneForge-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# (Each entry named with '/' - the zip standard. Windows PowerShell's Compress-Archive and .NET Framework's
# ZipFile write '\', which other tools trip on.)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $out -Recurse -File | ForEach-Object {
        $name = $_.FullName.Substring($out.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
    }
}
finally { $archive.Dispose() }
$count = (Get-ChildItem $out -Recurse -File).Count
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "StoneForge $version : $count files in $out"
Write-Host "  $zip ($size MB)"
