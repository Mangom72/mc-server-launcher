[CmdletBinding()]
param(
    [string]$ExePath = "artifacts\MineHarbor.exe",
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,
    [string]$CertificatePassword
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

Write-Host "Signing $ExePath ..."
$signResult = Set-AuthenticodeSignature -FilePath $ExePath -Certificate $certificate -TimestampServer "http://timestamp.digicert.com"
if ($signResult.Status -ne "Valid") {
    throw "Signing failed. Status: $($signResult.Status) - $($signResult.StatusMessage)"
}
Write-Host "Successfully signed $ExePath." -ForegroundColor Green
