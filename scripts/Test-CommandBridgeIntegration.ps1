[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$LauncherPath,
    [Parameter(Mandatory = $true)][string]$ServerJar,
    [Parameter(Mandatory = $true)][string]$BridgeJar,
    [Parameter(Mandatory = $true)][string]$JavaPath,
	[string]$RuntimeCacheDirectory,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
foreach ($required in @($LauncherPath, $ServerJar, $BridgeJar, $JavaPath)) {
    if (!(Test-Path -LiteralPath $required)) { throw "Integration test input not found: $required" }
}

function Send-BridgeMessage([IO.StreamWriter]$Writer, [hashtable]$Message) {
    $Writer.WriteLine(($Message | ConvertTo-Json -Compress -Depth 8))
    $Writer.Flush()
}

function Wait-BridgeMessage([IO.StreamReader]$Reader, [string]$Type, [DateTime]$Deadline) {
    if ($Type -eq 'players-update' -and $script:lastPlayersUpdate) { return $script:lastPlayersUpdate }
    while ([DateTime]::UtcNow -lt $Deadline) {
        $task = $Reader.ReadLineAsync()
        while (!$task.Wait(1000)) {
            if ([DateTime]::UtcNow -ge $Deadline) { throw "Timed out waiting for bridge message: $Type" }
        }
        $line = $task.Result
        if ($null -eq $line) { throw 'Bridge connection closed unexpectedly.' }
        $message = $line | ConvertFrom-Json
        if ($message.type -eq 'players-update') { $script:lastPlayersUpdate = $message }
        if ($message.type -eq 'ping') {
            Send-BridgeMessage $script:bridgeWriter @{ type = 'pong'; id = [string]$message.id }
            continue
        }
        if ($message.type -eq $Type) { return $message }
    }
    throw "Timed out waiting for bridge message: $Type"
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ('mcsl-paper-bridge-' + [Guid]::NewGuid().ToString('N'))
$listener = $null
$process = $null
try {
    New-Item -ItemType Directory -Force -Path (Join-Path $temporary 'plugins') | Out-Null
    Copy-Item -LiteralPath $ServerJar -Destination (Join-Path $temporary 'server.jar')
	if (![string]::IsNullOrWhiteSpace($RuntimeCacheDirectory)) {
		$cacheRoot = [IO.Path]::GetFullPath($RuntimeCacheDirectory)
		foreach ($cacheName in @('cache', 'libraries', 'versions')) {
			$cacheSource = Join-Path $cacheRoot $cacheName
			if (Test-Path -LiteralPath $cacheSource) { Copy-Item -LiteralPath $cacheSource -Destination (Join-Path $temporary $cacheName) -Recurse }
		}
	}
    Copy-Item -LiteralPath $BridgeJar -Destination (Join-Path $temporary 'plugins\MineHarbor-Command-Bridge-Paper.jar')
    [IO.File]::WriteAllText((Join-Path $temporary 'eula.txt'), "eula=true`r`n", [Text.UTF8Encoding]::new($false))
    $properties = "server-port=0`r`nonline-mode=false`r`nmax-players=1`r`nview-distance=2`r`nsimulation-distance=2`r`nlevel-name=bridge-test-world`r`nmotd=Bridge integration test`r`n"
    [IO.File]::WriteAllText((Join-Path $temporary 'server.properties'), $properties, [Text.UTF8Encoding]::new($false))

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start(2)
    $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    $tokenBytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($tokenBytes)
    $token = -join ($tokenBytes | ForEach-Object { $_.ToString('x2') })
    $session = [ordered]@{ port = $port; token = $token; protocol = 1; profile = 'integration-test'; expiresUtc = [DateTime]::UtcNow.AddMinutes(10).ToString('o') }
    [IO.File]::WriteAllText((Join-Path $temporary '.mcsl-command-bridge-session.json'), ($session | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = [IO.Path]::GetFullPath($JavaPath)
    $start.Arguments = '-Xms512M -Xmx2G -jar server.jar --nogui'
    $start.WorkingDirectory = $temporary
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $script:lastPlayersUpdate = $null

    $accept = $listener.AcceptTcpClientAsync()
    while (!$accept.Wait(1000)) {
        if ($process.HasExited) { throw "Paper exited before bridge connection: $($process.ExitCode)" }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Timed out waiting for the first bridge connection.' }
    }
    $client = $accept.Result
    try {
        $reader = [IO.StreamReader]::new($client.GetStream(), [Text.UTF8Encoding]::new($false), $false, 4096, $true)
        $script:bridgeWriter = [IO.StreamWriter]::new($client.GetStream(), [Text.UTF8Encoding]::new($false), 4096, $true)
        $script:bridgeWriter.AutoFlush = $true
        $hello = Wait-BridgeMessage $reader 'hello' $deadline
        if ($hello.token -ne $token -or $hello.profile -ne 'integration-test' -or [int]$hello.protocol -ne 1) { throw 'Bridge handshake values do not match the temporary session.' }
        Send-BridgeMessage $script:bridgeWriter @{ type = 'capabilities'; id = [string]$hello.id; protocol = 1; commandList = $true; suggest = $true; players = $true }
        Send-BridgeMessage $script:bridgeWriter @{ type = 'command-list-request'; id = 'commands-1' }
        $commands = Wait-BridgeMessage $reader 'command-list-response' $deadline
        if (@($commands.commands).Count -lt 20) { throw 'The live Paper command list is unexpectedly small.' }
        $versionCommand = @($commands.commands | Where-Object { $_.name -eq 'version' } | Select-Object -First 1)
        if ($versionCommand.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$versionCommand[0].description)) { throw 'Command name and description serialization failed.' }
		foreach ($property in @('aliases', 'usage', 'plugin')) { if ($versionCommand[0].PSObject.Properties.Name -notcontains $property) { throw "Command metadata is missing: $property" } }
        Send-BridgeMessage $script:bridgeWriter @{ type = 'suggest-request'; id = 'suggest-1'; input = 'ver' }
        $suggestions = Wait-BridgeMessage $reader 'suggest-response' $deadline
        if (@($suggestions.suggestions | Where-Object { $_ -match 'version' }).Count -eq 0) { throw 'Paper partial command suggestions did not include version.' }
        $players = Wait-BridgeMessage $reader 'players-update' $deadline
        if ($null -eq $players.players) { throw 'Player update was not serialized.' }
    }
    finally {
        $script:bridgeWriter.Dispose()
        $reader.Dispose()
        $client.Dispose()
    }

    $reconnect = $listener.AcceptTcpClientAsync()
    while (!$reconnect.Wait(1000)) {
        if ($process.HasExited) { throw "Paper exited before bridge reconnection: $($process.ExitCode)" }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'Timed out waiting for bridge reconnection.' }
    }
    $secondClient = $reconnect.Result
    try {
        $secondReader = [IO.StreamReader]::new($secondClient.GetStream(), [Text.UTF8Encoding]::new($false), $false, 4096, $true)
        $script:bridgeWriter = [IO.StreamWriter]::new($secondClient.GetStream(), [Text.UTF8Encoding]::new($false), 4096, $true)
        $script:bridgeWriter.AutoFlush = $true
        $secondHello = Wait-BridgeMessage $secondReader 'hello' $deadline
        if ($secondHello.token -ne $token) { throw 'Bridge reconnection used a different token.' }
    }
    finally {
        $script:bridgeWriter.Dispose()
        $secondReader.Dispose()
        $secondClient.Dispose()
    }

    $process.StandardInput.WriteLine('stop')
    $process.StandardInput.Flush()
    if (!$process.WaitForExit(60000)) { throw 'Temporary Paper server did not stop normally.' }
    if ($process.ExitCode -ne 0) { throw "Temporary Paper server exited with code $($process.ExitCode)." }
    Write-Host 'BRIDGE_INTEGRATION_PASSED=1'
}
catch {
    if ($process -and !$process.HasExited) {
        try { $process.StandardInput.WriteLine('stop'); $process.StandardInput.Flush(); $process.WaitForExit(30000) | Out-Null } catch { }
    }
    $log = ''
    if ($stdoutTask -and $stdoutTask.IsCompleted) { $log += $stdoutTask.Result }
    if ($stderrTask -and $stderrTask.IsCompleted) { $log += "`r`n" + $stderrTask.Result }
    if ($log.Length -gt 8000) { $log = $log.Substring($log.Length - 8000) }
    throw ($_.Exception.Message + "`r`nTemporary Paper log:`r`n" + $log)
}
finally {
    if ($listener) { $listener.Stop() }
    if ($process) { $process.Dispose() }
    Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
}
