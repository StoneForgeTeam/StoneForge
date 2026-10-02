# Rebuilds the third-party native binaries StoneForge ships, from their upstream source, into lib\:
#   lib\Aurie\AurieCore.dll, lib\Aurie\AuriePatcher.exe   Aurie at $AurieCommit + lib\Aurie\stoneforge.patch
#   lib\YYToolkit\YYToolkit.dll                           YYToolkit at $YYToolkitCommit, unmodified
# Both are AGPL-3.0 (see their LICENSE files in lib\). The clones go in build\.thirdparty (not part of the repo).
# Needs git and Visual Studio's C++ tools. Usage: powershell -ExecutionPolicy Bypass -File build\BuildThirdParty.ps1
param(
    [string]$PlatformToolset = "v145"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path $PSScriptRoot ".thirdparty"
$lib = Join-Path $root "lib"

$AurieRepo = "https://github.com/AurieFramework/Aurie.git"
$AurieCommit = "5c4839ea19d47c6afb7d459358f53643b2774e3a"
$YYToolkitRepo = "https://github.com/AurieFramework/YYToolkit.git"
$YYToolkitCommit = "86c133dc7f4e87991dd2cb7f2eac056dc5f13124"

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild (Visual Studio) not found" }

# A clean checkout of $commit in build\.thirdparty\$name.
function Checkout([string]$name, [string]$repo, [string]$commit) {
    $dir = Join-Path $work $name
    if (-not (Test-Path (Join-Path $dir ".git"))) {
        git clone --quiet $repo $dir
        if ($LASTEXITCODE -ne 0) { throw "Couldn't clone $repo" }
    }
    git -C $dir fetch --quiet origin
    git -C $dir checkout --quiet --force $commit
    if ($LASTEXITCODE -ne 0) { throw "$name has no commit $commit" }
    git -C $dir clean --quiet -fdx
    return $dir
}

function Build([string]$solution, [string]$targets) {
    & $msbuild $solution "-t:$targets" "-p:Configuration=Release" "-p:Platform=x64" "-p:PlatformToolset=$PlatformToolset" -v:minimal -nologo
    if ($LASTEXITCODE -ne 0) { throw "Building $solution failed" }
}

function Put([string]$from, [string]$to) {
    Copy-Item $from $to -Force
    $hash = (Get-FileHash $to -Algorithm SHA256).Hash
    Write-Host ("  {0}  {1}" -f $hash, $to.Substring($root.Length + 1))
}

New-Item -ItemType Directory -Force $work | Out-Null

$aurie = Checkout "Aurie" $AurieRepo $AurieCommit
git -C $aurie apply --whitespace=nowarn (Join-Path $lib "Aurie\stoneforge.patch")
if ($LASTEXITCODE -ne 0) { throw "lib\Aurie\stoneforge.patch doesn't apply to Aurie $AurieCommit" }
Build (Join-Path $aurie "Aurie.sln") "AurieCore;AuriePatcher"

$yytk = Checkout "YYToolkit" $YYToolkitRepo $YYToolkitCommit
Build (Join-Path $yytk "YYToolkit.sln") "YYToolkit"

Write-Host "Updated lib\ (SHA-256 - record them in lib\Aurie\README.md and lib\YYToolkit\README.md):"
# (A solution build puts its projects' output in the solution's x64\Release.)
Put (Join-Path $aurie "x64\Release\AurieCore.dll") (Join-Path $lib "Aurie\AurieCore.dll")
Put (Join-Path $aurie "x64\Release\AuriePatcher.exe") (Join-Path $lib "Aurie\AuriePatcher.exe")
Put (Join-Path $yytk "x64\Release\YYToolkit.dll") (Join-Path $lib "YYToolkit\YYToolkit.dll")
