param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
# Offline renderer checks. Does not inspect or start TraderVI components.
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$inputFile = Join-Path $OutputDirectory 'renderer-test.json'
$outputFile = Join-Path $OutputDirectory 'renderer-test.html'
$report = @{
    Title = 'Preview <script>alert(1)</script>'; GeneratedAt = '2026-10-30 09:15'
    Verdict = 'Unable to verify'; Summary = 'No live checks were performed.'
    Actions = @('Sign in <only if needed> & retry'); PendingAtOpen = @('Fresh inputs')
    Limitations = 'Offline test'; Checks = @(@{ Name='Engine'; State='Unavailable'; Detail='<img src=x>'; ObservedAt='Not read' })
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $inputFile
& "$PSScriptRoot/Write-Report.ps1" -InputPath $inputFile -OutputPath $outputFile
$html = Get-Content -LiteralPath $outputFile -Raw
if ($html.Contains('<script>') -or $html.Contains('<img src=x>')) { throw 'Unescaped input reached HTML.' }
foreach ($expected in @('&lt;script&gt;', '&lt;img src=x&gt;', '&amp; retry', 'Content-Security-Policy', 'Unavailable', 'Not read')) {
    if (-not $html.Contains($expected)) { throw "Missing renderer evidence: $expected" }
}
$report.Checks[0].State = 'Healthy without evidence'
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $inputFile
$rejected = $false
try { & "$PSScriptRoot/Write-Report.ps1" -InputPath $inputFile -OutputPath $outputFile } catch { $rejected = $true }
if (-not $rejected) { throw 'Unknown state was accepted.' }
$report.Checks = @()
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $inputFile
$rejected = $false
try { & "$PSScriptRoot/Write-Report.ps1" -InputPath $inputFile -OutputPath $outputFile } catch { $rejected = $true }
if (-not $rejected) { throw 'Evidence-free report was accepted.' }
'PASS: HTML escaping, explicit evidence and allowed-state validation.'
