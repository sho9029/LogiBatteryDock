# Repository Guidelines

## Project Structure & Module Organization

- `src/LogiBatteryDock.Core/`: G HUB local-agent WebSocket client, HID++ framing, device discovery, battery decoding, generic voltage fallback selection, and telemetry models; depends on HidSharp.
- `src/LogiBatteryDock.App/`: unpackaged WinUI 3 tray application using H.NotifyIcon. `TrayResources.xaml` defines the flyout; `TrayViewModel.cs` handles display state; `App.xaml.cs` manages refresh and lifecycle.
- `src/LogiBatteryDock.CmdPal/`: packaged PowerToys Command Palette extension. It owns its own Core reader (G HUB agent preferred, HID++ fallback) and exposes status, refresh, and Dock items without requiring the tray app; it must not open the tray app's IPC as a prerequisite.
- `src/LogiBatteryDock.Probe/`: console diagnostic that prints G HUB-preferred device telemetry and failure reasons as JSON.
- `tests/LogiBatteryDock.Core.Tests/`: xUnit tests for frame normalization, telemetry decoding, HID++ sessions, stale-state merging, and the per-process status store.
- `tests/LogiBatteryDock.App.Tests/`: tray-state tests that compile the UI-independent `TrayViewModel` against Core; visual WinUI behavior remains a manual check.
- `tests/LogiBatteryDock.CmdPal.Tests/`: Command Palette page/Dock adapter tests with an injected reader; they prove the adapter does not need a tray process.
- `tools/smoke_ipc.ps1`, `tools/smoke_fallback.ps1`, `tools/smoke_hidden.py`, and `tools/install_cmdpal_dev.ps1`: repeatable optional tray-IPC checks, native voltage-fallback check, local Windows startup/liveness check, and development MSIX signing/registration. The tray icon is generated in XAML.
- Keep generated `bin/`, `obj/`, and `AppPackages/` files out of contributions.

## Build, Test, and Development Commands

Use Windows and the .NET 10 SDK. The app targets Windows 10 build 19041 or later and `win-x64`.

```powershell
dotnet build src/LogiBatteryDock.App/LogiBatteryDock.App.csproj -c Release
dotnet build src/LogiBatteryDock.CmdPal/LogiBatteryDock.CmdPal.csproj -c Release -p:Platform=x64
dotnet publish src/LogiBatteryDock.CmdPal/LogiBatteryDock.CmdPal.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
dotnet run --project src/LogiBatteryDock.App/LogiBatteryDock.App.csproj -c Release
dotnet run --project src/LogiBatteryDock.Probe/LogiBatteryDock.Probe.csproj -c Release
pwsh -File tools/smoke_ipc.ps1
pwsh -File tools/smoke_fallback.ps1
dotnet test tests/LogiBatteryDock.Core.Tests/LogiBatteryDock.Core.Tests.csproj -c Release
dotnet test tests/LogiBatteryDock.App.Tests/LogiBatteryDock.App.Tests.csproj -c Release
dotnet test tests/LogiBatteryDock.CmdPal.Tests/LogiBatteryDock.CmdPal.Tests.csproj -c Release
python tools/smoke_hidden.py
```

The first three commands build the tray app, compile the extension, and create a test MSIX under `src/LogiBatteryDock.CmdPal/AppPackages/`. The remaining commands launch the tray UI, inspect hardware directly, exercise the optional tray `get_status`/`refresh` IPC, run the Core, tray-state, and CmdPal adapter tests, and check hidden-desktop liveness. The probe exits with code `2` when no device telemetry is returned. The IPC check requires a Release app build and leaves no process running. The Python check requires a Release build and the external helper under `~/.codex/skills/windows-hidden-desktop-gui/scripts`; it is not a portable CI test.

When a registered development CmdPal package needs to be updated for a manual UI or registration check, use `pwsh -File tools/install_cmdpal_dev.ps1 -RestartCommandPalette`. It increments `Package.appxmanifest`'s package version, publishes the x64 MSIX, signs and registers it for the current user, verifies `Status=Ok`, and restarts Command Palette. A normal build does not require this installation step.

## Coding Style & Naming Conventions

Follow existing C#: four-space indentation, file-scoped namespaces, PascalCase types/members, camelCase locals, and `_camelCase` private fields. Nullable references and implicit usings are enabled. Match existing XAML indentation and theme resources. Keep HID communication in Core and asynchronous refresh off the UI thread. No repository-specific formatter or linter is configured.

## Testing Guidelines

xUnit is used for Core, tray-state, and Command Palette adapter tests; no coverage threshold is configured. For changes, record build results and relevant probe output, including device model and connection state. Test sleep/disconnect recovery, each frontend with the other process absent, and G HUB coexistence when applicable. Hidden-desktop liveness does not establish visual correctness; UI changes and Command Palette discovery/Dock rendering need separate manual verification. Use descriptive names such as `VoltageFeatureNeverProducesPercentage` for the raw decoder and `VoltageFallbackUsesGenericCurve` for the provider policy.

## Telemetry Invariants

The raw HID++ decoder never converts voltage to percentage: native `0x1001` remains `Percent=null`. The fallback policy may add a percentage only for a valid voltage-only reading, using the generic compatibility curve. Do not persist or apply a name-matched G HUB observation to native telemetry; G HUB's device-specific table and the native curve are separate sources. Mark these values as `NativeVoltageEstimate`; direct `0x1000`/`0x1004` and G HUB `percentage` values remain exact, and no model-specific table or unmarked estimate is allowed. Label retained readings as stale and do not treat missing responses as proof of disconnection. G HUB is an optional preferred source on loopback port 9010.

## Frontend Independence Boundary

"Independent" means the tray executable and the Command Palette package can each start, own their provider chain, and show the latest available state while the other is stopped or absent. Sharing `LogiBatteryDock.Core` and the optional G HUB loopback agent is allowed; a required named pipe, broker, or tray startup is not. Each frontend prefers the G HUB agent for its native percentage and falls back to direct HID++ plus the generic voltage curve when the agent is unavailable. The tray IPC is an optional integration surface for diagnostics or future commands, never the data source for CmdPal. A separate process or package alone is insufficient evidence of independence. This boundary was added after the original audit found that the first CmdPal implementation was process-separated but still unusable without the tray pipe.

Every frontend change needs a standalone test or smoke check for the other process being absent. If measured HID exclusivity or another platform constraint later makes a broker mandatory, record that evidence and revise this section before changing the dependency.

## UI Surface and Host Indexing Boundary

- The tray owns one `TaskbarIcon`, a compact click-open flyout, and a custom hover tooltip. It shows one status row per telemetry device in both surfaces; rows are display-only and never copy to the clipboard. The tooltip must keep a short `ToolTipText` fallback and list every current device.
- Command Palette is a separate page/Dock surface. Its rows use bounded model labels and short status text, with no `CopyTextCommand` action. Estimated percentages use the same plain percentage label as exact values; voltage and HID++ details stay out of the primary row. The device command name shown from the context menu uses the same bounded model label. Refresh is a `MoreCommands` context action on a selected device/status row, not a permanent list item.
- The Command Palette MSIX exposes the `windows.appExtension` provider but sets the host application's `AppListEntry` to `none`; the host must not become a second All Apps entry.
- A large `ghub` result list can be host indexing of repository `bin/`, `obj/`, or `AppPackages/` artifacts. Count provider commands from the Command Palette log separately from file/app search results, and clean generated artifacts before visual verification.

The rule is falsified if the host offers a supported way to exclude development artifacts or if a future UI requires a meaningful device action; update this section and its tests before reintroducing an action or changing packaging metadata.

## Commit & Pull Request Guidelines

Local history uses concise imperative subjects such as `Add live device items to Command Palette Dock`. Keep each completed unit in its own commit. PRs should describe scope, verification, affected hardware, limitations, and screenshots for UI changes; link relevant issues when available.
