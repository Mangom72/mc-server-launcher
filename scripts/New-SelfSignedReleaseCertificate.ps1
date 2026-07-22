[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CertificatePath,
    [string]$Subject = 'CN=MineHarbor Self-Signed Release',
    [ValidateRange(1, 90)][int]$ValidDays = 30
)

$ErrorActionPreference = 'Stop'
$resolvedPath = [IO.Path]::GetFullPath($CertificatePath)
$parent = Split-Path -Parent $resolvedPath
if (![string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

$passwordBytes = New-Object byte[] 48
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $random.GetBytes($passwordBytes)
}
finally {
    $random.Dispose()
}
$password = [Convert]::ToBase64String($passwordBytes)
$securePassword = ConvertTo-SecureString -String $password -AsPlainText -Force
$certificate = $null

try {
    # 자체서명 인증서는 공개 신뢰를 제공하지 않으므로 릴리스 한 번에만 쓰고 즉시 폐기합니다.
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy Exportable `
        -NotBefore (Get-Date).AddMinutes(-5) `
        -NotAfter (Get-Date).AddDays($ValidDays)
    Export-PfxCertificate -Cert $certificate -FilePath $resolvedPath -Password $securePassword -ChainOption EndEntityCertOnly -NoProperties | Out-Null
    if (!(Test-Path -LiteralPath $resolvedPath) -or (Get-Item -LiteralPath $resolvedPath).Length -eq 0) {
        throw '임시 자체서명 PFX를 만들지 못했습니다.'
    }

    [pscustomobject]@{
        CertificatePath = $resolvedPath
        Password = $password
        Thumbprint = $certificate.Thumbprint
        StorePath = 'Cert:\CurrentUser\My\' + $certificate.Thumbprint
        Subject = $certificate.Subject
        NotAfter = $certificate.NotAfter
    }
}
catch {
    Remove-Item -LiteralPath $resolvedPath -Force -ErrorAction SilentlyContinue
    if ($certificate) {
        Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) -Force -ErrorAction SilentlyContinue
    }
    throw
}
