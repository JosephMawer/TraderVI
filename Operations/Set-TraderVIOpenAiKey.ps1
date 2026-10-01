#requires -Version 5.1
<#
.SYNOPSIS
Enter an OpenAI key locally without putting it in command history or script output.
.DESCRIPTION
Uses the existing OPENAI_API_KEY configuration. By default saves a Windows user
environment variable and updates this PowerShell process. Environment variables
are not an encrypted credential vault. No API requests or app activation occur.
Use -SessionOnly to avoid persistence, or -CheckOnly to report presence only.
#>
[CmdletBinding(DefaultParameterSetName = 'Set')]
param(
    [Parameter(ParameterSetName = 'Set')] [switch] $SessionOnly,
    [Parameter(Mandatory, ParameterSetName = 'Check')] [switch] $CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($CheckOnly) {
    [pscustomobject]@{
        ProcessKeyPresent = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('OPENAI_API_KEY', 'Process'))
        UserKeyPresent = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('OPENAI_API_KEY', 'User'))
        MachineKeyPresent = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('OPENAI_API_KEY', 'Machine'))
    }
    return
}

$secret = $null
$plain = $null
$buffer = [IntPtr]::Zero
try {
    $secret = Read-Host 'Paste the OpenAI API key here (hidden; not in chat)' -AsSecureString
    $buffer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($buffer)
    if ([string]::IsNullOrWhiteSpace($plain) -or $plain -match '\s') {
        throw 'The key must be nonempty and contain no whitespace. Nothing was saved.'
    }
    if (-not $SessionOnly) {
        [Environment]::SetEnvironmentVariable('OPENAI_API_KEY', $plain, 'User')
    }
    [Environment]::SetEnvironmentVariable('OPENAI_API_KEY', $plain, 'Process')
}
finally {
    if ($buffer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($buffer) }
    if ($null -ne $secret) { $secret.Dispose() }
    $plain = $null
}

if ($SessionOnly) { Write-Output 'Key set for this PowerShell session and programs launched from it.' }
else { Write-Output 'Key saved for your Windows user and this PowerShell session.' }
Write-Output 'Already-running apps keep their old environment. No key validation, API call or AI activation was performed.'
