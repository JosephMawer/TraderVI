param(
    [Parameter(Mandatory)][string]$InputPath,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
# Rendering only: no process, database, network, broker or execution operations.
$report = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
function Encode($value) { [System.Net.WebUtility]::HtmlEncode([string]$value) }
if (-not $report.Title -or -not $report.GeneratedAt -or -not $report.Verdict -or -not $report.Checks) {
    throw 'Report requires title, generation time, verdict and explicit checks.'
}
$rows = foreach ($check in $report.Checks) {
    if ($check.State -notin @('Verified','Waiting','Attention','Unavailable','Not applicable')) { throw 'Unknown check state.' }
    '<tr><th scope="row">' + (Encode $check.Name) + '</th><td><span class="state">' + (Encode $check.State) + '</span></td><td>' + (Encode $check.Detail) + '</td><td>' + (Encode $check.ObservedAt) + '</td></tr>'
}
$actions = @($report.Actions | ForEach-Object { '<li>' + (Encode $_) + '</li>' }) -join "`n"
if (-not $actions) { $actions = '<li>No operator action identified by the available checks.</li>' }
$pending = @($report.PendingAtOpen | ForEach-Object { '<li>' + (Encode $_) + '</li>' }) -join "`n"
$html = @"
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
<title>$(Encode $report.Title)</title><style>
:root{color-scheme:light dark}body{margin:0;background:#101820;color:#edf2f7;font:16px/1.6 system-ui,sans-serif}main{max-width:1120px;margin:0 auto;padding:48px 28px}header{border-top:5px solid #69d3bd;padding-top:22px}.eyebrow{letter-spacing:.14em;text-transform:uppercase;color:#8edcca;font-size:12px;font-weight:700}h1{font-size:36px;line-height:1.2;margin:12px 0}h2{font-size:21px;margin-top:0}.meta,footer{color:#b6c6d5;font-size:14px}.verdict{padding:24px;background:#1a2c38;border:1px solid #456071;border-radius:12px;margin:28px 0}.verdict strong{display:block;font-size:24px;margin-bottom:8px}.columns{display:grid;grid-template-columns:1fr 1fr;gap:20px}section{background:#19232e;border:1px solid #364858;border-radius:12px;padding:24px;margin:0 0 24px}ul{padding-left:22px;margin-bottom:0}li+li{margin-top:10px}.scroll{overflow:auto}table{border-collapse:collapse;width:100%;font-size:14px}th,td{text-align:left;vertical-align:top;padding:14px 12px;border-bottom:1px solid #364858}thead th{color:#9fb5c9;font-size:12px;text-transform:uppercase}tbody th{min-width:120px}td:last-child{min-width:140px;color:#b6c6d5}.state{font-weight:650;white-space:nowrap}footer{padding-top:12px}@media(max-width:700px){main{padding:24px 16px}.columns{grid-template-columns:1fr}h1{font-size:29px}}@media print{body{background:white;color:#152534}section,.verdict{background:white;border-color:#aaa}.meta,footer,thead th,td:last-child{color:#444}main{padding:0}.scroll{overflow:visible}}
</style></head><body><main><header><div class="eyebrow">TraderVI · Morning readiness</div><h1>$(Encode $report.Title)</h1><p class="meta">Prepared $(Encode $report.GeneratedAt) · Toronto time</p></header>
<div class="verdict"><strong>$(Encode $report.Verdict)</strong>$(Encode $report.Summary)</div>
<div class="columns"><section><h2>Your next steps</h2><ul>$actions</ul></section><section><h2>Still required at market open</h2><ul>$pending</ul></section></div>
<section><h2>Readiness checks</h2><div class="scroll"><table><thead><tr><th>Component</th><th>Finding</th><th>What this means</th><th>Evidence time</th></tr></thead><tbody>$($rows -join "`n")</tbody></table></div></section>
<section><h2>Coverage and limitations</h2><p>$(Encode $report.Limitations)</p></section>
<footer>This report is a dated observation, not trading approval or a guarantee of a trade. Missing or stale evidence remains unverified. No order, permission or risk setting is changed by this report.</footer>
</main></body></html>
"@
Set-Content -LiteralPath $OutputPath -Value $html -Encoding utf8
