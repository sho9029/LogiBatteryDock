# LogiBatteryDock

Logicool / Logitech製デバイスの電池残量や充電状態を、タスクトレイやPowerToysのCommand Paletteで確認できます。Command PaletteのDockには、選んだ機器の残量を表示できます。

トレイアプリとCommand Palette拡張があり、使いたい方だけ導入できます。

## 動作環境

- Windows 10 バージョン2004以降のx64環境を対象としています。
- 実機で確認している機器はG703 LIGHTSPEED Wireless Gaming Mouseです。他の機種の動作は未確認です。
- Command Paletteで使う場合は、[PowerToysのCommand Palette](https://learn.microsoft.com/windows/powertoys/command-palette/overview)を有効にしてください。Dock表示にはDockに対応したバージョンが必要です。

G HUBが起動している場合は、G HUBから取得した残量を表示します。G HUBが利用できなくても、対応する機器から直接取得できます。

## 導入

現在はソースコードからビルドして利用します。[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)をインストールし、リポジトリをダウンロードして展開したフォルダーでPowerShellを開いてください。

### トレイアプリ

次のコマンドでビルドして起動します。

```powershell
dotnet run --project src/LogiBatteryDock.App/LogiBatteryDock.App.csproj -c Release
```

通知領域にマウス型のアイコンが表示されます。見当たらない場合は、隠れているアイコンも確認してください。

アイコンにマウスを重ねると各機器の状態を確認でき、クリックすると一覧が開きます。右クリックメニューの「更新」で残量を再取得できます。

同じメニューの「設定」では、更新間隔を10〜3600秒の範囲で変更できます。「Windows起動時に開始」は初期状態では無効です。更新間隔の変更をCommand Paletteにも反映するには、Command Paletteを再起動してください。

### Command Palette拡張

こちらを使う場合は、追加で[PowerShell 7](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows)と、署名ツールを含むWindows SDKが必要です。次のコマンドで拡張をビルドし、現在のユーザーへ登録します。

```powershell
dotnet restore src/LogiBatteryDock.CmdPal/LogiBatteryDock.CmdPal.csproj -p:Platform=x64
pwsh -File tools/install_cmdpal_dev.ps1 -RestartCommandPalette
```

初回は開発用証明書を信頼するための管理者承認が必要です。この手順ではCommand Paletteを再起動します。

登録後、Command Paletteで「LogiBatteryDock」を検索してください。機器を選ぶと詳細が開き、コンテキストメニューの「更新」で残量を再取得できます。トレイアプリを起動しておく必要はありません。

Dockへの配置方法は[Command Palette Dockの案内](https://learn.microsoft.com/windows/powertoys/command-palette/dock)を参照してください。配置した機器表示を右クリックすると、常駐表示する機器を選んだり、残量を手動で更新したりできます。

## 残量の表示と自動更新

通常は60秒間隔で残量を更新します。スリープ復帰時にも1回取得しますが、その時点で取得できなければ、次の定期更新を待つか手動で更新してください。

G HUBや機器から残量を取得できる場合は、その値を表示します。電圧から残量を推定する機器では、G HUBの表示と異なることがあります。推定値であることは詳細に表示しますが、Command Paletteの一覧やDockでは通常の％表示になります。

機器から応答がないときは、現在の残量を確認できない状態として表示します。受信機が残っている場合は最後に取得した値を保持し、受信機も見つからなくなった場合は一覧から外します。

設定は`%LOCALAPPDATA%/LogiBatteryDock/settings.json`に保存されます。

## アップデートとアンインストール

トレイアプリを更新する場合は、右クリックメニューの「終了」で閉じてから、新しいソースコードで起動し直してください。Command Palette拡張は、導入時と同じコマンドで更新できます。

トレイアプリの利用をやめる場合は、設定画面で「Windows起動時に開始」を無効にしてから終了してください。Command Palette拡張は、次のコマンドで削除できます。

```powershell
Get-AppxPackage -Name LogiBatteryDockCmdPal | Remove-AppxPackage
```

## 制限事項

- すべてのLogicool / Logitech製品での動作は保証していません。
- Command Paletteでソースコードのフォルダーを検索すると、ビルドで生成されたファイルも検索結果に出ることがあります。
