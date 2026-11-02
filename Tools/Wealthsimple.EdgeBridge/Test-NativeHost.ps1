param([Parameter(Mandatory=$true)][string]$HostExecutable)
$ErrorActionPreference = 'Stop'
$resolvedHost = (Resolve-Path -LiteralPath $HostExecutable).Path
# Offline fixture: only synthetic bytes, an isolated user-only pipe and this helper process.
$pairing = [Convert]::ToHexStringLower([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$pipe = [IO.Pipes.NamedPipeServerStream]::new('TraderVI.EdgeLogin.' + $pairing,
    [IO.Pipes.PipeDirection]::InOut, 1, [IO.Pipes.PipeTransmissionMode]::Byte,
    [IO.Pipes.PipeOptions]::Asynchronous -bor [IO.Pipes.PipeOptions]::CurrentUserOnly)
$fixtureTimeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(15))
$process = $null
try {
    $waiting = $pipe.WaitForConnectionAsync($fixtureTimeout.Token)
    $start = [Diagnostics.ProcessStartInfo]::new($resolvedHost)
    $start.ArgumentList.Add('chrome-extension://ikocfanmafmhmmidhampgihpjnjpiffd/')
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $message = [Text.Encoding]::UTF8.GetBytes((@{pairing=$pairing; fixture='synthetic-only'} | ConvertTo-Json -Compress))
    $process.StandardInput.BaseStream.Write([BitConverter]::GetBytes([int]$message.Length))
    $process.StandardInput.BaseStream.Write($message)
    $process.StandardInput.BaseStream.Flush()
    [void]$waiting.GetAwaiter().GetResult()
    $prefix = [byte[]]::new(4)
    [void]$pipe.ReadExactlyAsync($prefix, $fixtureTimeout.Token).AsTask().GetAwaiter().GetResult()
    $length = [BitConverter]::ToInt32($prefix)
    if ($length -ne $message.Length) { throw 'Native host changed fixture framing.' }
    $received = [byte[]]::new($length)
    [void]$pipe.ReadExactlyAsync($received, $fixtureTimeout.Token).AsTask().GetAwaiter().GetResult()
    if (-not [Linq.Enumerable]::SequenceEqual[byte]($message, $received)) { throw 'Native host changed fixture payload.' }
    $receipt = [Text.Encoding]::UTF8.GetBytes('{"connected":true}')
    $pipe.Write([BitConverter]::GetBytes([int]$receipt.Length))
    $pipe.Write($receipt)
    $pipe.Flush()
    $stdout = [IO.MemoryStream]::new()
    [void]$process.StandardOutput.BaseStream.CopyToAsync($stdout, $fixtureTimeout.Token).GetAwaiter().GetResult()
    $expected = [byte[]]([BitConverter]::GetBytes([int]$receipt.Length) + $receipt)
    if (-not [Linq.Enumerable]::SequenceEqual[byte]($expected, $stdout.ToArray())) { throw 'Unexpected native stdout.' }
    if (-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0 -or $process.StandardError.ReadToEnd().Length -ne 0) {
        throw 'Native host fixture failed.'
    }
    Write-Output 'Native host offline framing, user-only pipe, receipt and clean stdout checks passed.'
} finally {
    $pipe.Dispose()
    $fixtureTimeout.Dispose()
    if ($null -ne $process) {
        if (-not $process.HasExited) { $process.Kill() }
        $process.Dispose()
    }
}
