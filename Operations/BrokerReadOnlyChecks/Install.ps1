# One-time setup under the operator's broker-read helper authorization. Never allow-list this installer.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$repo = 'C:\src\TraderVI'
$install = Join-Path $env:USERPROFILE '.codex\tools\tradervi-read-only-broker\v1'
$rulesPath = Join-Path $env:USERPROFILE '.codex\rules\tradervi-read-only-broker.rules'
$evidence = Join-Path $repo 'artifacts\read-only-broker'
$pwshPath = (Get-Process -Id $PID).Path
if ((Resolve-Path -LiteralPath $PSScriptRoot).Path -ne (Join-Path $repo 'Operations\BrokerReadOnlyChecks')) { throw 'Unexpected repository location.' }
if ((Test-Path -LiteralPath $install) -or (Test-Path -LiteralPath $rulesPath)) { throw 'Installation already exists; review an upgrade instead.' }
$sourceExecutable = Join-Path $PublishDirectory 'TraderVI.BrokerReadOnlyChecks.exe'
$offline = & $sourceExecutable --self-test | ConvertFrom-Json
if ($offline.HelperVersion -ge 2) { throw 'The history-enabled build requires reviewed Install-HistoryV2.ps1; never install expanded reads under the v1 rule.' }
if ($LASTEXITCODE -ne 0 -or $offline.OfflineChecksPassed -lt 54 -or $offline.NetworkRequests -ne 0) { throw 'Offline verification failed.' }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
New-Item -ItemType Directory -Path $install | Out-Null
Get-ChildItem -LiteralPath $PublishDirectory -Force | Copy-Item -Destination $install -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Invoke.ps1') -Destination (Join-Path $install 'Invoke.ps1')
$manifest = @(Get-ChildItem -LiteralPath $install -File -Recurse | ForEach-Object {
    [pscustomobject]@{Path=[IO.Path]::GetRelativePath($install,$_.FullName);Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $install 'files.json')
$nativeText = & $pwshPath -NoProfile -File (Join-Path $install 'Invoke.ps1') 2>&1
$nativeExit = $LASTEXITCODE
$native = (($nativeText | ForEach-Object { $_.ToString() }) -join "`n") | ConvertFrom-Json
if ($native.OrdersSent -ne 0 -or $native.TokenRefreshes -ne 0 -or $native.SavedCredentialsChanged) { throw 'Native boundary verification failed.' }
$native | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'installation-native.json')
$offline | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'installation-offline.json')
# A missing/expired login still leaves a safe installed command. Do not refresh/retry it.
$pattern = @($pwshPath, '-NoProfile', '-File', "$install\Invoke.ps1")
$rules = "# Fixed Wealthsimple read-only broker checks; no caller arguments or general shell/dotnet access.`n"
$rules += 'prefix_rule(pattern=' + (ConvertTo-Json -InputObject $pattern -Compress) + ', decision="allow")' + "`n"
$candidate = Join-Path $evidence 'approval-candidate.rules'
[IO.File]::WriteAllText($candidate,$rules,[Text.UTF8Encoding]::new($false))
$checks = [Collections.Generic.List[object]]::new()
foreach ($case in @(
    @{Name='Installed launcher';Args=$pattern;Allow=$true},
    @{Name='Editable repository launcher';Args=@($pwshPath,'-NoProfile','-File',"$PSScriptRoot\Invoke.ps1");Allow=$false},
    @{Name='General PowerShell';Args=@($pwshPath,'-Command','Write-Output unexpected');Allow=$false},
    @{Name='Arbitrary dotnet';Args=@('dotnet','unreviewed.dll');Allow=$false}
)) {
    $policy = & codex execpolicy check --rules $candidate @($case.Args) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or (($policy.decision -eq 'allow') -ne $case.Allow)) { throw 'Approval-scope test failed.' }
    $checks.Add([pscustomobject]@{Check=$case.Name;Passed=$true})
}
$checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'installation-policy.json')
[IO.File]::WriteAllText($rulesPath,$rules,[Text.UTF8Encoding]::new($false))
[pscustomobject]@{InstalledUtc=[DateTimeOffset]::UtcNow;InstalledDirectory=$install;RulesFile=$rulesPath;
    InvokeCommand="& '$pwshPath' -NoProfile -File '$install\Invoke.ps1'";
    OfflineChecksPassed=$offline.OfflineChecksPassed;NativeSucceeded=$native.Success;NativeExitCode=$nativeExit;
    Files=$manifest.Count;PolicyChecksPassed=$checks.Count;OrdersSent=0;TokenRefreshes=0;ExistingChatsNeedRulesReload=$true} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'installation.json')
[pscustomobject]@{Installed=$true;OfflineChecksPassed=$offline.OfflineChecksPassed;NativeSucceeded=$native.Success;
    PolicyChecksPassed=$checks.Count;OrdersSent=0;TokenRefreshes=0;ExistingChatsNeedRulesReload=$true} | ConvertTo-Json -Compress
