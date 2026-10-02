# Sends one bounded request to an explicitly enabled running development host.
param(
    [Parameter(Mandatory=$true)][string]$Request,
    [string]$GameFolder = 'C:\Program Files (x86)\Steam\steamapps\common\Stoneshard'
)
$ErrorActionPreference = 'Stop'
$pipeName = (Get-Content -LiteralPath (Join-Path $GameFolder 'dotnet\testhost.pipe') -Raw).Trim()
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
try {
    $pipe.Connect(3000)
    $reader = [IO.StreamReader]::new($pipe)
    $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false))
    $writer.AutoFlush = $true
    $writer.WriteLine($Request)
    $reply = $reader.ReadLineAsync()
    if (-not $reply.Wait(14000)) { throw 'Response timed out; a mutation may already have started. Check game state before retrying.' }
    $reply.Result
} finally { $pipe.Dispose() }
