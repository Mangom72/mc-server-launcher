[CmdletBinding()]
param(
    [string]$ExePath = "artifacts\MineHarbor.exe",
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [switch]$AllowUntrustedSelfSigned
)

$ErrorActionPreference = 'Stop'

if (!(Test-Path -LiteralPath $ExePath)) {
    throw "Executable not found at $ExePath"
}
if (!(Test-Path -LiteralPath $CertificatePath)) {
    throw "Code-signing certificate not found: $CertificatePath"
}

$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertificatePath, $CertificatePassword)
if (!$certificate.HasPrivateKey) {
    throw 'The code-signing certificate does not contain a private key.'
}
if ($AllowUntrustedSelfSigned -and ![string]::Equals($certificate.Subject, $certificate.Issuer, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The release signing certificate must be self-signed.'
}

Write-Host "Signing $ExePath ..."
$signResult = Set-AuthenticodeSignature -FilePath $ExePath -Certificate $certificate -TimestampServer "http://timestamp.digicert.com" -HashAlgorithm SHA256
$verified = Get-AuthenticodeSignature -FilePath $ExePath
if (!$verified.SignerCertificate -or ![string]::Equals($verified.SignerCertificate.Thumbprint, $certificate.Thumbprint, [StringComparison]::OrdinalIgnoreCase)) {
	throw 'Signing failed because the embedded signer does not match the requested certificate.'
}
$acceptedSelfSignedStatuses = @('Valid', 'NotTrusted', 'UnknownError')
if ((!$AllowUntrustedSelfSigned -and $verified.Status -ne 'Valid') -or ($AllowUntrustedSelfSigned -and $acceptedSelfSignedStatuses -notcontains [string]$verified.Status)) {
    throw "Signing failed. Status: $($signResult.Status) - $($signResult.StatusMessage)"
}
Write-Host "Successfully signed $ExePath. Trust status: $($verified.Status)" -ForegroundColor Green
