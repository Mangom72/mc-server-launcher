[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts'),
    [string]$DependencyDirectory = (Join-Path $PSScriptRoot '..\.build\dependencies')
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$dependencies = [IO.Path]::GetFullPath($DependencyDirectory)
$version = Get-Content -LiteralPath (Join-Path $projectRoot 'version.json') -Raw | ConvertFrom-Json
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'build-resources.json') -Raw | ConvertFrom-Json
$paperApi = Join-Path $dependencies $manifest.paperApi.fileName
$javaZip = Join-Path $dependencies $manifest.java.fileName
$adventureApi = Join-Path $dependencies $manifest.adventureApi.fileName
$adventureKey = Join-Path $dependencies $manifest.adventureKey.fileName
foreach ($required in @($paperApi, $javaZip, $adventureApi, $adventureKey)) {
    if (!(Test-Path -LiteralPath $required)) { throw "Missing bridge build dependency: $required" }
}

# 시스템 Java 버전에 따라 결과가 달라지지 않도록 해시로 고정한 JDK만 사용합니다.
$compilerRoot = Join-Path $projectRoot '.build\bridge-jdk'
$cachedCompiler = Get-ChildItem -LiteralPath $compilerRoot -Recurse -Filter javac.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if (!$cachedCompiler) {
    Remove-Item -LiteralPath $compilerRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $compilerRoot | Out-Null
    Expand-Archive -LiteralPath $javaZip -DestinationPath $compilerRoot -Force
    $cachedCompiler = Get-ChildItem -LiteralPath $compilerRoot -Recurse -Filter javac.exe | Select-Object -First 1
}
$javac = if ($cachedCompiler) { $cachedCompiler.FullName } else { $null }
if (!$javac) { throw 'Java compiler was not found.' }

$work = Join-Path $projectRoot 'obj\command-bridge'
$classes = Join-Path $work 'classes'
Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $classes | Out-Null
$sourceRoot = Join-Path $projectRoot 'bridge\paper\src\main\java'
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter *.java | ForEach-Object FullName)
$stubRoot = Join-Path $projectRoot 'bridge\paper\compile-stubs'
$sources += @(Get-ChildItem -LiteralPath $stubRoot -Recurse -Filter *.java | ForEach-Object FullName)
if ($sources.Count -eq 0) { throw 'Command bridge Java sources were not found.' }
$compileClasspath = @($paperApi, $adventureApi, $adventureKey) -join [IO.Path]::PathSeparator
& $javac '--release' '8' '-Xlint:-options' '-encoding' 'UTF-8' '-classpath' $compileClasspath '-d' $classes @sources
if ($LASTEXITCODE -ne 0) { throw "Command bridge compilation failed with exit code $LASTEXITCODE." }
# Paper가 런타임에 제공하는 자리표시자 클래스는 플러그인 JAR에 넣지 않습니다.
Remove-Item -LiteralPath (Join-Path $classes 'net') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $classes 'org\jetbrains') -Recurse -Force -ErrorAction SilentlyContinue

$pluginTemplate = Join-Path $projectRoot 'bridge\paper\src\main\resources\plugin.yml'
$pluginOutput = Join-Path $classes 'plugin.yml'
$pluginText = (Get-Content -LiteralPath $pluginTemplate -Raw).Replace('@VERSION@', [string]$version.productVersion)
[IO.File]::WriteAllText($pluginOutput, $pluginText, [Text.UTF8Encoding]::new($false))

New-Item -ItemType Directory -Force -Path $output | Out-Null
$jar = Join-Path $output ("MineHarbor-Command-Bridge-Paper-v{0}.jar" -f $version.productVersion)
Remove-Item -LiteralPath $jar -Force -ErrorAction SilentlyContinue
$jarTool = Join-Path (Split-Path -Parent $javac) 'jar.exe'
if (!(Test-Path -LiteralPath $jarTool)) { throw 'Java JAR tool was not found.' }
& $jarTool '--create' '--file' $jar '-C' $classes '.'
if ($LASTEXITCODE -ne 0) { throw "Command bridge packaging failed with exit code $LASTEXITCODE." }
if (!(Test-Path -LiteralPath $jar) -or (Get-Item -LiteralPath $jar).Length -lt 1024) { throw 'Command bridge JAR was not created.' }

[pscustomobject]@{
    ProductVersion = [string]$version.productVersion
    ProtocolVersion = 1
    Jar = $jar
}
