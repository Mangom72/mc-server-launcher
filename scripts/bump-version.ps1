[CmdletBinding()]
param(
    [switch]$Major,
    [switch]$Minor,
    [switch]$Patch,
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$versionFile = Join-Path $projectRoot 'version.json'
$selectedModes = @($Major, $Minor, $Patch, $BuildOnly | Where-Object { $_ }).Count
if ($selectedModes -ne 1) { throw 'Major, Minor, Patch, BuildOnly 중 하나만 지정해야 합니다.' }
if (!(Test-Path -LiteralPath $versionFile)) { throw 'version.json을 찾지 못했습니다.' }

$json = Get-Content -LiteralPath $versionFile -Raw | ConvertFrom-Json
if ($json.productVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'productVersion은 MAJOR.MINOR.PATCH 형식이어야 합니다.' }
if ($json.buildNumber -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'buildNumber는 네 개의 숫자 구성 요소를 사용해야 합니다.' }

# 제품 버전은 선택한 의미 체계 수준만 올립니다.
$productParts = @($json.productVersion.Split('.') | ForEach-Object { [int]$_ })
if ($Major) {
    $productParts[0] = [int]$productParts[0] + 1
    $productParts[1] = 0
    $productParts[2] = 0
}
elseif ($Minor) {
    $productParts[1] = [int]$productParts[1] + 1
    $productParts[2] = 0
}
elseif ($Patch) {
    $productParts[2] = [int]$productParts[2] + 1
}
$json.productVersion = $productParts -join '.'

# 내부 빌드 번호는 모든 릴리스 변경에서 한 번만 증가합니다.
$buildParts = @($json.buildNumber.Split('.') | ForEach-Object { [int]$_ })
$lastIndex = $buildParts.Length - 1
$buildParts[$lastIndex] = [int]$buildParts[$lastIndex] + 1
$json.buildNumber = $buildParts -join '.'

$serialized = ($json | ConvertTo-Json -Depth 5) + "`r`n"
[IO.File]::WriteAllText($versionFile, $serialized, [Text.UTF8Encoding]::new($false))

Write-Host 'Updated version.json:'
Write-Host "Product Version : $($json.productVersion)"
Write-Host "Build Number    : $($json.buildNumber)"
