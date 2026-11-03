$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'CmdPalFlowSmoke\CmdPalFlowSmoke.csproj'
& dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Flow smoke build failed' }
$executable = Join-Path $PSScriptRoot 'CmdPalFlowSmoke\bin\Release\net10.0-windows10.0.26100.0\win-x64\GHubTray.CmdPal.FlowSmoke.exe'
$outputRoot = Join-Path $PSScriptRoot ('obj\flow-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputRoot | Out-Null

function Wait-Signal([string]$path, [string]$pattern, [System.Diagnostics.Process]$process) {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ((Test-Path -LiteralPath $path) -and (Select-String -LiteralPath $path -Pattern $pattern -Quiet)) { return }
        if ($process.HasExited) { throw "Smoke process exited early: $path" }
        Start-Sleep -Milliseconds 100
    }
    throw "Timed out waiting for $pattern in $path"
}

foreach ($fault in @('disconnected-host', 'blocked-host')) {
    $run = [guid]::NewGuid().ToString('N')
    $serverLog = Join-Path $outputRoot "$fault-server.log"
    $oldLog = Join-Path $outputRoot "$fault-old-host.log"
    $server = Start-Process -FilePath $executable -ArgumentList @('server', $run) -WindowStyle Hidden -PassThru -RedirectStandardOutput $serverLog -RedirectStandardError "$serverLog.err"
    $old = $null
    try {
        Wait-Signal $serverLog 'server.ready' $server
        $old = Start-Process -FilePath $executable -ArgumentList @($fault, $run) -WindowStyle Hidden -PassThru -RedirectStandardOutput $oldLog -RedirectStandardError "$oldLog.err"
        Wait-Signal $oldLog 'old-host.call=1' $old
        & $executable client $run | Tee-Object -FilePath (Join-Path $outputRoot "$fault-client.log")
        if ($LASTEXITCODE -ne 0) { throw "Flow smoke client failed: $fault" }
    }
    finally {
        # Only this smoke's named events/processes are touched. No installed
        # extension, device, CmdPal process, package, or registry entry is changed.
        foreach ($suffix in @('release', 'done')) {
            $signal = $null
            if ([System.Threading.EventWaitHandle]::TryOpenExisting("Local\GHubTrayFlowSmoke-$run-$suffix", [ref]$signal)) {
                $signal.Set() | Out-Null
                $signal.Dispose()
            }
        }
        if (-not $server.WaitForExit(15000)) { throw 'Smoke server did not exit' }
        if ($old -and -not $old.WaitForExit(15000)) { throw 'Smoke old host did not exit' }
    }
    if ((Select-String -LiteralPath $oldLog -Pattern 'old-host.call=').Count -ne 1) {
        throw 'A failed/blocked subscriber was called more than once'
    }
    Write-Output "$fault : PASS"
}
Write-Output "Trace directory: $outputRoot"
