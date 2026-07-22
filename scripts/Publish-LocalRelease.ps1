[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [switch]$NoPublish
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $projectRoot 'artifacts'
$innoUrl = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$innoSha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'

# 1. Check gh CLI
if (-not $NoPublish -and -not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required but not installed."
}

# 2. Setup Inno Compiler (if not skipping installer)
$innoCompiler = $null
if (-not $SkipInstaller) {
    $innoDir = Join-Path $projectRoot '.build\InnoSetup'
    $innoCompiler = Join-Path $innoDir 'ISCC.exe'
    
    if (-not (Test-Path $innoCompiler)) {
        Write-Host "Downloading Inno Setup..."
        $installer = Join-Path $projectRoot '.build\innosetup-installer.exe'
        New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot '.build') | Out-Null
        Invoke-WebRequest $innoUrl -OutFile $installer
        $actualHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
        if (!$actualHash.Equals($innoSha256, [StringComparison]::OrdinalIgnoreCase)) { throw 'Inno Setup hash verification failed.' }
        if ((Get-AuthenticodeSignature -LiteralPath $installer).Status -ne 'Valid') { throw 'Inno Setup signature verification failed.' }
        Write-Host "Installing Inno Setup locally to $innoDir ..."
        $process = Start-Process $installer -Wait -PassThru -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',("/DIR=`"$innoDir`"")
        if ($process.ExitCode -ne 0) { throw "Inno Setup install failed: $($process.ExitCode)" }
    }
    if ((Get-AuthenticodeSignature -LiteralPath $innoCompiler).Status -ne 'Valid') {
        throw 'Inno Setup compiler signature verification failed.'
    }
}

# 3. 릴리스 한 번에만 사용하는 자체서명 인증서를 생성합니다.
$selfSignedPfx = Join-Path $projectRoot ('.build\mineharbor-self-signed-release-' + [Guid]::NewGuid().ToString('N') + '.pfx')
$selfSignedState = & (Join-Path $projectRoot 'scripts\New-SelfSignedReleaseCertificate.ps1') -CertificatePath $selfSignedPfx
try {
    Write-Host "Building project..."
    $buildArgs = @{
        OutputDirectory = $artifacts
        SigningCertificatePath = $selfSignedState.CertificatePath
        SigningCertificatePassword = $selfSignedState.Password
        AllowUntrustedSelfSigned = $true
    }
    if (-not $SkipInstaller) {
        $buildArgs.BuildInstaller = $true
        $buildArgs.InnoCompiler = $innoCompiler
    }
    $buildResult = & (Join-Path $projectRoot 'build.ps1') @buildArgs
    if (-not $SkipInstaller) {
        & (Join-Path $projectRoot 'scripts\sign-build.ps1') -ExePath $buildResult.SetupExe -CertificatePath $selfSignedState.CertificatePath -CertificatePassword $selfSignedState.Password -AllowUntrustedSelfSigned
    }

# 4. Generate Release Artifacts & Metadata
Write-Host "Generating release artifacts metadata..."
$versionJson = Get-Content -LiteralPath (Join-Path $projectRoot 'version.json') -Raw | ConvertFrom-Json
$tag = "v$($versionJson.productVersion)"
& (Join-Path $projectRoot 'scripts\New-ReleaseArtifacts.ps1') -ArtifactsDirectory $artifacts -ReleaseTag $tag

# 5. Verify Release Artifacts
Write-Host "Verifying artifacts..."
& (Join-Path $projectRoot 'scripts\Test-ReleaseArtifacts.ps1') -ArtifactsDirectory $artifacts -RequireSelfSignedSignature

# 6. Upload to GitHub Releases
$title = "MineHarbor - Minecraft Server Launcher v$($versionJson.productVersion) (build $($versionJson.buildNumber))"
$notes = Join-Path $artifacts 'release-notes.md'

$assetList = @(
    (Join-Path $artifacts 'MineHarbor.exe'),
    (Join-Path $artifacts 'Minecraft-Server-Launcher.exe'),
    (Join-Path $artifacts "MineHarbor-Portable-v$($versionJson.productVersion).zip"),
    (Join-Path $artifacts "MineHarbor-Command-Bridge-Paper-v$($versionJson.productVersion).jar"),
    (Join-Path $artifacts 'SHA256SUMS.txt'),
    (Join-Path $artifacts 'update.json')
)
if (-not $SkipInstaller) {
    $assetList += (Join-Path $artifacts "MineHarbor-Setup-v$($versionJson.productVersion).exe")
}

if ($NoPublish) {
    Write-Host 'Self-signed release artifacts were built and verified without publishing.' -ForegroundColor Green
    return
}

Write-Host "Publishing to GitHub Releases ($tag)..."
# Check if release exists
$existingTags = gh release list --limit 100 --json tagName | ConvertFrom-Json
$releaseExists = @($existingTags | Where-Object { $_.tagName -eq $tag }).Count -gt 0

if ($releaseExists) {
    Write-Host "Updating existing release $tag..."
    gh release edit $tag --title $title --notes-file $notes --latest
    gh release upload $tag $assetList --clobber
} else {
    Write-Host "Creating new release $tag..."
    gh release create $tag $assetList --title $title --notes-file $notes --latest
}

Write-Host "Release published successfully!" -ForegroundColor Green
}
finally {
    & (Join-Path $projectRoot 'scripts\Remove-SelfSignedReleaseCertificate.ps1') -CertificatePath $selfSignedState.CertificatePath -Thumbprint $selfSignedState.Thumbprint
}
