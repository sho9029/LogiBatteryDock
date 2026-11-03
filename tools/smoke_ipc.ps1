param(
    [int]$StartupWaitMilliseconds = 1000
)

$exe = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\src\GHubTray.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\GHubTray.App.exe'))
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Release app was not found: $exe"
}

$process = Start-Process -FilePath $exe -PassThru

function Invoke-Ipc([string]$command) {
    $pipe = $null
    $writer = $null
    $reader = $null
    try {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
            '.',
            'GHubTray.Status.v1',
            [System.IO.Pipes.PipeDirection]::InOut,
            [System.IO.Pipes.PipeOptions]::Asynchronous)
        $pipe.Connect(3000)

        $requestId = [guid]::NewGuid().ToString('N')
        $writer = [System.IO.StreamWriter]::new(
            $pipe,
            [System.Text.UTF8Encoding]::new($false),
            1024,
            $true)
        $writer.AutoFlush = $true
        $writer.WriteLine(
            '{"version":1,"command":"' + $command + '","requestId":"' + $requestId + '"}')

        $reader = [System.IO.StreamReader]::new(
            $pipe,
            [System.Text.UTF8Encoding]::new($false),
            $false,
            1024,
            $true)
        $readTask = $reader.ReadLineAsync()
        if (-not $readTask.Wait(5000)) {
            throw "IPC response timed out: $command"
        }
        $line = $readTask.Result
        if ([string]::IsNullOrEmpty($line)) {
            throw "IPC response was empty: $command"
        }

        $response = $line | ConvertFrom-Json
        if ($response.requestId -ne $requestId) {
            throw "IPC request ID mismatch: $command"
        }
        return $response
    }
    finally {
        if ($reader) { $reader.Dispose() }
        if ($writer) { $writer.Dispose() }
        if ($pipe) { $pipe.Dispose() }
    }
}

try {
    Start-Sleep -Milliseconds $StartupWaitMilliseconds
    $initial = Invoke-Ipc 'get_status'
    if ($initial.ok -ne $true) {
        throw "get_status failed: $($initial.error)"
    }

    $refreshed = Invoke-Ipc 'refresh'
    if ($refreshed.ok -ne $true -or $null -eq $refreshed.status) {
        throw "refresh failed: $($refreshed.error)"
    }

    $devices = @($refreshed.status.devices)
    foreach ($device in $devices) {
        if ($device.batteryText -eq '残量は未提供' -and $null -ne $device.percent) {
            throw "voltage-only telemetry unexpectedly contains a percentage: $($device.name)"
        }
    }

    [pscustomobject]@{
        InitialStatus = $initial.status.error
        ReceiverCount = $refreshed.status.receiverCount
        DeviceCount = $devices.Count
        Devices = $devices | ForEach-Object {
            [pscustomobject]@{
                Name = $_.name
                BatteryText = $_.batteryText
                Percent = $_.percent
                Millivolts = $_.millivolts
                PowerState = $_.powerState
            }
        }
    } | ConvertTo-Json -Depth 5
}
finally {
    if (-not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 500
        if (-not $process.HasExited) {
            $process.Kill()
        }
    }
    $process.WaitForExit(5000) | Out-Null
}
