param(
    [int]$UnavailableGHubPort = 65534
)

$probe = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\src\GHubTray.Probe\bin\Release\net10.0-windows10.0.19041.0\GHubTray.Probe.exe'))
if (-not (Test-Path -LiteralPath $probe)) {
    throw "Release probe was not found: $probe"
}

$json = & $probe '--ghub-port' $UnavailableGHubPort | ConvertFrom-Json
$devices = @($json.Devices)
if ($devices.Count -eq 0) {
    throw 'No device telemetry was returned by the native fallback.'
}

$voltageDevices = @($devices | Where-Object { $_.BatteryFeature -eq 4097 })
foreach ($device in $voltageDevices) {
    if ($device.Source -ne 'NativeVoltageEstimate') {
        throw "Voltage-only device did not use the fallback estimator: $($device.Name)"
    }
    if ($null -eq $device.Percent -or $device.Percent -lt 0 -or $device.Percent -gt 100) {
        throw "Voltage-only device did not expose a valid percentage: $($device.Name)"
    }
}

[pscustomobject]@{
    GHubPort = $UnavailableGHubPort
    DeviceCount = $devices.Count
    VoltageDeviceCount = $voltageDevices.Count
    Devices = $devices | ForEach-Object {
        [pscustomobject]@{
            Name = $_.Name
            BatteryFeature = $_.BatteryFeature
            Percent = $_.Percent
            Millivolts = $_.Millivolts
            Source = $_.Source
        }
    }
} | ConvertTo-Json -Depth 5
