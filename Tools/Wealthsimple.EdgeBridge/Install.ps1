param([Parameter(Mandatory=$true)][string]$HostExecutable)
$ErrorActionPreference = 'Stop'
$resolvedHost = (Resolve-Path -LiteralPath $HostExecutable).Path
if ([IO.Path]::GetFileName($resolvedHost) -ne 'Wealthsimple.EdgeBridge.exe') { throw 'Select the built EdgeBridge executable.' }
$bridgeDirectory = Join-Path $env:LOCALAPPDATA 'TraderVI\Wealthsimple\EdgeBridge'
New-Item -ItemType Directory -Path $bridgeDirectory -Force | Out-Null
$manifestPath = Join-Path $bridgeDirectory 'host.json'
@{
    name = 'com.tradervi.wealthsimple_login'
    description = 'TraderVI Wealthsimple login handoff'
    path = $resolvedHost
    type = 'stdio'
    allowed_origins = @('chrome-extension://ikocfanmafmhmmidhampgihpjnjpiffd/')
} | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8
$registration = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.tradervi.wealthsimple_login'
New-Item -Path $registration -Force | Out-Null
Set-Item -LiteralPath $registration -Value $manifestPath
Write-Output 'Native host registered for this Windows user. Load the Extension directory from edge://extensions (Developer mode → Load unpacked).'
