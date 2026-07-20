[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceLauncherPath,
    [Parameter(Mandatory = $true)][string]$UpdateMetadataPath,
    [Parameter(Mandatory = $true)][string]$DestinationPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sourceLauncher = [IO.Path]::GetFullPath($SourceLauncherPath)
$metadata = [IO.Path]::GetFullPath($UpdateMetadataPath)
$destination = [IO.Path]::GetFullPath($DestinationPath)
if (!(Test-Path -LiteralPath $sourceLauncher)) { throw "이전 공개 런처를 찾을 수 없습니다: $sourceLauncher" }
if (!(Test-Path -LiteralPath $metadata)) { throw "업데이트 메타데이터를 찾을 수 없습니다: $metadata" }
if (Test-Path -LiteralPath $destination) { throw "검증 다운로드 대상이 이미 존재합니다: $destination" }

$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# 컴파일러를 찾을 수 없습니다.' }

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('MineHarborPublicUpdateTest\' + [Guid]::NewGuid().ToString('N'))
$testExecutable = Join-Path $temporaryRoot 'PublicAutoUpdate.Tests.exe'
New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
try {
    & $compiler /nologo /target:exe "/out:$testExecutable" (Join-Path $projectRoot 'tests\PublicAutoUpdate.Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw '공개 자동 업데이트 검증 도구를 컴파일하지 못했습니다.' }
    & $testExecutable $sourceLauncher $metadata $destination
    if ($LASTEXITCODE -ne 0) { throw '이전 공개 런처의 실제 자동 업데이트 경로 검증에 실패했습니다.' }
}
finally {
    $safeTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $expectedRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'MineHarborPublicUpdateTest'))
    if ($safeTemporaryRoot.StartsWith($expectedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $safeTemporaryRoot)) {
        Remove-Item -LiteralPath $safeTemporaryRoot -Recurse -Force
    }
}
