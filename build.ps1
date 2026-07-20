[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [switch]$BuildInstaller,
    [switch]$SkipDependencyDownload,
    [switch]$SkipCompile,
    [string]$InnoCompiler,
    [string]$SigningCertificatePath,
    [string]$SigningCertificatePassword
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$version = Get-Content -LiteralPath (Join-Path $projectRoot 'version.json') -Raw | ConvertFrom-Json
$generated = & (Join-Path $projectRoot 'scripts\Generate-VersionInfo.ps1')
$dependencyDirectory = Join-Path $projectRoot '.build\dependencies'
if (!$SkipCompile -and !$SkipDependencyDownload) {
    & (Join-Path $projectRoot 'scripts\Prepare-BuildResources.ps1') -DestinationDirectory $dependencyDirectory | Out-Null
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
$portableExe = Join-Path $output 'MineHarbor.exe'
$legacyPortableExe = Join-Path $output 'Minecraft-Server-Launcher.exe'
$sources = @(
    'decompiled\Launcher.decompiled.cs',
    'ChildProcessTracker.cs',
    'AssemblyInfo.cs',
    'StorageConfiguration.cs',
    'ThemePalette.cs',
    'Localization.cs',
    'CustomControls.cs',
	'ModernUiControls.cs',
    'RoundedProgressBar.cs',
    'ModernLauncherGui.cs',
	'ModernDialogs.cs',
    'RuntimeCompatibility.cs',
    'UpnpExternalAccess.cs',
    'BackupAndProfileTools.cs',
    'ContentAndDiagnostics.cs',
    'ContentManagementServices.cs',
	'ContentManagementUi.cs',
	'ServerAutomation.cs',
	'ServerManagementFeatures.cs',
    'ManagedServerDashboard.cs',
	'ServerTrash.cs',
    'NetworkAndPlayerTools.cs',
	'QuickCommandsAndBridge.cs',
	'QuickCommandUi.cs',
	'QuickCommandPickerUi.cs',
	'UpnpCore.cs',
    'obj\GeneratedVersionInfo.cs'
) | ForEach-Object { Join-Path $projectRoot $_ }

$arguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/debug-',
    "/win32icon:$projectRoot\launcher-icon.ico",
    "/win32manifest:$projectRoot\app.manifest",
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll',
    '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll',
    '/reference:System.Net.Http.dll',
    "/out:$portableExe"
) + $sources
if (!$SkipCompile) {
	foreach ($dependency in @((Join-Path $dependencyDirectory 'paper-api-26.2.build.56-alpha.jar'), (Join-Path $dependencyDirectory 'adventure-api-5.2.0.jar'), (Join-Path $dependencyDirectory 'adventure-key-5.2.0.jar'))) {
        if (!(Test-Path -LiteralPath $dependency)) { throw "Missing build dependency: $dependency" }
    }
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (!(Test-Path -LiteralPath $csc)) { throw 'The .NET Framework 4.x C# compiler was not found.' }
    & $csc @arguments
	if ($LASTEXITCODE -ne 0) { throw "C# build failed with exit code $LASTEXITCODE." }
	# v1.3.2는 최소 크기 제한이 있는 구버전에서 넘어오는 마지막 호환 다리입니다.
	# v1.3.3 이상은 이 조건을 자동으로 벗어나 컴파일된 실제 크기 그대로 배포됩니다.
	$minimumLegacyUpdateSize = if ([Version]$version.productVersion -le [Version]'1.3.2') { 1MB } else { 0 }
	$portableInfo = Get-Item -LiteralPath $portableExe
	if ($minimumLegacyUpdateSize -gt 0 -and $portableInfo.Length -lt $minimumLegacyUpdateSize) {
		$stream = [IO.File]::Open($portableExe, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::None)
		try { $stream.SetLength($minimumLegacyUpdateSize) }
		finally { $stream.Dispose() }
	}
	$bridgeBuild = & (Join-Path $projectRoot 'scripts\Build-CommandBridge.ps1') -OutputDirectory $output -DependencyDirectory $dependencyDirectory
}
elseif (!(Test-Path -LiteralPath $portableExe)) {
    throw "Portable EXE does not exist for -SkipCompile: $portableExe"
}

if (![string]::IsNullOrWhiteSpace($SigningCertificatePath)) {
    & (Join-Path $projectRoot 'scripts\sign-build.ps1') -ExePath $portableExe -CertificatePath $SigningCertificatePath -CertificatePassword $SigningCertificatePassword
}
# 기존 런처가 새 브랜드 버전을 자동 업데이트할 수 있도록 같은 바이너리의 예전 자산 이름을 함께 제공합니다.
Copy-Item -LiteralPath $portableExe -Destination $legacyPortableExe -Force

$packageRoot = Join-Path $projectRoot 'obj\portable'
Remove-Item -LiteralPath $packageRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
Copy-Item -LiteralPath $portableExe -Destination $packageRoot
foreach ($document in @('README.md', 'LICENSE', 'PRIVACY.md')) {
    $source = Join-Path $projectRoot $document
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $packageRoot }
}
$portableZip = Join-Path $output ("MineHarbor-Portable-v{0}.zip" -f $version.productVersion)
Remove-Item -LiteralPath $portableZip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $portableZip -CompressionLevel Optimal

$setupPath = $null
if ($BuildInstaller) {
    if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
        $candidate = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($candidate) { $InnoCompiler = $candidate.Source }
    }
    if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or !(Test-Path -LiteralPath $InnoCompiler)) {
        throw 'Inno Setup compiler was not found. Pass -InnoCompiler or install JRSoftware.InnoSetup.'
    }
    $marker = Join-Path $projectRoot 'obj\installed.mode'
    [IO.File]::WriteAllText($marker, "installed`r`n", [Text.UTF8Encoding]::new($false))
    & $InnoCompiler "/DMyAppVersion=$($version.productVersion)" "/DMyBuildNumber=$($version.buildNumber)" "/DSourceExe=$portableExe" "/DProjectRoot=$projectRoot" "/DOutputDir=$output" (Join-Path $projectRoot 'installer\MineHarbor.iss')
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed with exit code $LASTEXITCODE." }
    $setupPath = Join-Path $output ("MineHarbor-Setup-v{0}.exe" -f $version.productVersion)
}

[pscustomobject]@{
    ProductVersion = $version.productVersion
    BuildNumber = $version.buildNumber
    PortableExe = $portableExe
	LegacyPortableExe = $legacyPortableExe
    PortableZip = $portableZip
    SetupExe = $setupPath
	CommandBridgeJar = if ($bridgeBuild) { $bridgeBuild.Jar } else { $null }
}
