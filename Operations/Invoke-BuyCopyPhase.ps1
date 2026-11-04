[CmdletBinding()] param(
    [Parameter(Mandatory)][guid]$Token,
    [Parameter(Mandatory)][ValidateSet('handoff','maintenance','review','reload')][string]$Phase,
    [ValidateSet('Debug','Release')][string]$Configuration='Debug'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$folder = Join-Path $root "artifacts/paper-rehearsal/$($Token.ToString('N'))"
$copy = Get-Content -LiteralPath (Join-Path $folder 'copy.json') -Raw | ConvertFrom-Json
if ($copy.Token -ne $Token.ToString('N') -or $copy.Stage -ne 'restored') { throw 'Require a restored marked private copy.' }
$priorToken=$env:TRADERVI_BUY_COPY
$priorPhase=$env:TRADERVI_BUY_COPY_PHASE
$log = Join-Path $folder "buy-$Phase-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')).log"
try {
    $env:TRADERVI_BUY_COPY=$Token.ToString('N')
    $env:TRADERVI_BUY_COPY_PHASE=$Phase
    Push-Location $root
    try {
        & dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj -c $Configuration --artifacts-path artifacts/build --no-build --no-restore --filter FullyQualifiedName~FilledBuyCopyTransition --logger 'console;verbosity=detailed' *> $log
        $result=$LASTEXITCODE
    } finally { Pop-Location }
} finally {
    $env:TRADERVI_BUY_COPY=$priorToken
    $env:TRADERVI_BUY_COPY_PHASE=$priorPhase
}
Write-Output "Isolated buy $Phase exit code: $result"
exit $result
