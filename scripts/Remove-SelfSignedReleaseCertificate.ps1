[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CertificatePath,
    [Parameter(Mandatory = $true)][string]$Thumbprint
)

$ErrorActionPreference = 'Stop'
$normalizedThumbprint = ($Thumbprint -replace '\s', '').ToUpperInvariant()
if ($normalizedThumbprint -notmatch '^[0-9A-F]{40,128}$') {
    throw '자체서명 인증서 지문 형식이 올바르지 않습니다.'
}

Remove-Item -LiteralPath ([IO.Path]::GetFullPath($CertificatePath)) -Force -ErrorAction SilentlyContinue
$storePath = 'Cert:\CurrentUser\My\' + $normalizedThumbprint
if (Test-Path -LiteralPath $storePath) {
    Remove-Item -LiteralPath $storePath -Force
}

Write-Host '임시 자체서명 인증서와 PFX를 정리했습니다.'
