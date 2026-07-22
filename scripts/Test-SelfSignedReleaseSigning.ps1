[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$LauncherPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = [IO.Path]::GetFullPath($LauncherPath)
if (!(Test-Path -LiteralPath $source)) { throw "서명 테스트 대상이 없습니다: $source" }

$testDirectory = Join-Path $projectRoot 'obj\self-signed-release-test'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$signedCopy = Join-Path $testDirectory 'MineHarbor.exe'
$tamperedCopy = Join-Path $testDirectory 'MineHarbor-tampered.exe'
$pfx = Join-Path $testDirectory 'release-test.pfx'
Copy-Item -LiteralPath $source -Destination $signedCopy -Force
$state = $null
try {
    $state = & (Join-Path $PSScriptRoot 'New-SelfSignedReleaseCertificate.ps1') -CertificatePath $pfx -ValidDays 1
    & (Join-Path $PSScriptRoot 'sign-build.ps1') -ExePath $signedCopy -CertificatePath $state.CertificatePath -CertificatePassword $state.Password -AllowUntrustedSelfSigned
    $signature = Get-AuthenticodeSignature -LiteralPath $signedCopy
    if (!$signature.SignerCertificate -or $signature.SignerCertificate.Subject -ne 'CN=MineHarbor Self-Signed Release') { throw '자체서명 서명자 검증에 실패했습니다.' }
    if (![string]::Equals($signature.SignerCertificate.Subject, $signature.SignerCertificate.Issuer, [StringComparison]::OrdinalIgnoreCase)) { throw '서명 인증서가 자체서명이 아닙니다.' }
    if (@('Valid', 'NotTrusted', 'UnknownError') -notcontains [string]$signature.Status) { throw "자체서명 무결성 검증에 실패했습니다: $($signature.Status)" }
    Copy-Item -LiteralPath $signedCopy -Destination $tamperedCopy -Force
    $stream = [IO.File]::Open($tamperedCopy, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.WriteByte(0) }
    finally { $stream.Dispose() }
    $tamperedSignature = Get-AuthenticodeSignature -LiteralPath $tamperedCopy
    if (@('HashMismatch', 'NotSigned') -notcontains [string]$tamperedSignature.Status) { throw "변조된 자체서명 파일이 거부되지 않았습니다: $($tamperedSignature.Status)" }
    Write-Host "SELF_SIGNED_RELEASE_SIGNING_OK=$($signature.Status)"
    Write-Host "SELF_SIGNED_TAMPER_REJECTED=$($tamperedSignature.Status)"
}
finally {
    if ($state) {
        & (Join-Path $PSScriptRoot 'Remove-SelfSignedReleaseCertificate.ps1') -CertificatePath $state.CertificatePath -Thumbprint $state.Thumbprint
    }
    Remove-Item -LiteralPath $signedCopy,$tamperedCopy,$pfx -Force -ErrorAction SilentlyContinue
}
if ($state -and ((Test-Path -LiteralPath $state.CertificatePath) -or (Test-Path -LiteralPath ('Cert:\CurrentUser\My\' + $state.Thumbprint)))) {
    throw '자체서명 테스트 후 인증서 자료가 남았습니다.'
}
Write-Host 'SELF_SIGNED_CLEANUP_OK'
