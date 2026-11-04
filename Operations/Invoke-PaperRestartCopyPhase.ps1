[CmdletBinding()] param(
    [Parameter(Mandatory)][guid]$Token,
    [Parameter(Mandatory)][ValidateSet('adopt','restart','append','audit','resume','negative')][string]$Phase
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$folder = Join-Path $root "artifacts/paper-rehearsal/$($Token.ToString('N'))"
$copy = Get-Content -LiteralPath (Join-Path $folder 'copy.json') -Raw | ConvertFrom-Json
if ($copy.Token -ne $Token.ToString('N') -or $copy.Stage -ne 'restored') { throw 'Require the restored marked private copy.' }
$priorToken=$env:TRADERVI_PAPER_REHEARSAL
$priorPhase=$env:TRADERVI_PAPER_PHASE
$log = Join-Path $folder "$Phase-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))-console.log"
try {
    $env:TRADERVI_PAPER_REHEARSAL=$Token.ToString('N')
    $env:TRADERVI_PAPER_PHASE=$Phase
    Push-Location $root
    try {
        & dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --artifacts-path artifacts/build --no-build --no-restore --filter FullyQualifiedName~PaperRestartCopyTests --logger 'console;verbosity=detailed' *> $log
        $result=$LASTEXITCODE
    } finally { Pop-Location }
} finally {
    $env:TRADERVI_PAPER_REHEARSAL=$priorToken
    $env:TRADERVI_PAPER_PHASE=$priorPhase
}
Write-Output "Isolated phase $Phase exit code: $result"
exit $result
