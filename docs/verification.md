# 検証記録

更新日: 2026-11-04

2026-11-04にプロジェクトをLogiBatteryDockへ改名した。以下の既存の記録にあるGHubTray、パッケージ名、コマンドは検証当時の表記を保持している。現在のソースコードと実行コマンドはREADMEを参照する。

## 自動・再現可能な確認

- `dotnet test tests/GHubTray.Core.Tests/GHubTray.Core.Tests.csproj -c Release --no-restore`
  - 46件合格、失敗0件。CoreのG HUB応答パース、G HUB優先／HID++フォールバック、汎用電圧カーブ、推定フラグのIPC伝播、範囲外電圧の拒否、HID++、表示ポリシー、IPC契約、プロセス内状態ストアを確認。
- `dotnet test tests/GHubTray.App.Tests/GHubTray.App.Tests.csproj -c Release --no-restore`
  - 5件合格、失敗0件。トレイの状態カード、推定％とツールチップ、古い値、読み取り失敗、複数機器を全件列挙するホバー用テキストを注入リーダーで確認。WinUIの実画面は対象外。
- `dotnet test tests/GHubTray.CmdPal.Tests/GHubTray.CmdPal.Tests.csproj -c Release --no-restore`
  - 4件合格、失敗0件。トレイプロセスを使わず、注入リーダーから一覧とDockを作れること、推定％の行表示、空の受信機結果が読み込み中で固まらないこと、デバイス行が`NoOpCommand`でコピー操作を持たず、更新を`MoreCommands`へ持つことを確認。
- `dotnet build src/GHubTray.App/GHubTray.App.csproj -c Release --no-restore`
  - 成功、警告0件。トレイフライアウトを1台1行のコンパクトレイアウトへ変更し、カスタムホバーツールチップ、マウス型生成アイコン、固定サイズの設定Windowをコンパイル確認した。
- `dotnet build src/GHubTray.CmdPal/GHubTray.CmdPal.csproj -c Release -p:Platform=x64 --no-restore`
  - 成功、警告0件。
- `dotnet run --project src/GHubTray.Probe/GHubTray.Probe.csproj -c Release --no-restore`
  - トレイプロセスなしでG HUBエージェントへ接続し、G703の`percentage=70`、`Source=GHubAgent`、`Discharging`、失敗0件を取得。G HUBの値をそのまま表示候補にしている（過去の2026-09-02確認は`71%`）。
  - G HUBが利用できない場合のHID++フォールバックは、既存の実機確認で`0x1001`、`3927 mV`、`Percent=null`、`Discharging`を取得済み。表示層では互換実装と同じ汎用カーブだけで推定`%`を付与し、G HUB観測値はNative HID++の補正へ保存・適用しない。
- `GHubTray.CmdPal.exe -RegisterProcessAsComServer`
  - COMサーバーとして1秒生存し、手動終了後にプロセスが消えることを確認。
- `dotnet publish src/GHubTray.CmdPal/GHubTray.CmdPal.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true --no-restore`
  - x64 MSIXを生成。`MakeAppx unpack`で展開とmanifest検証に成功。
  - `AppListEntry="none"`を含む`0.1.0.9`を生成した。通常アプリ入口をAll Appsへ出さず、拡張入口だけを残す。
  - `mspdbcmf.exe`が無いためシンボルパッケージを作らない警告だけが残る。実行パッケージ生成は成功している。
- `pwsh -File tools/smoke_ipc.ps1`
  - トレイ側の任意IPC `get_status`と`refresh`の応答、要求ID照合、終了後のプロセス後始末を確認。CmdPalはこのIPCを使わない。
  - G703実測: `BatteryText=70%`, `percent=70`, `Source=GHubAgent`, `Discharging`。Probeと同じG HUB応答値になった。スクリプトは`NativeVoltageEstimate`でも`%`が出ることを検査する。
- `pwsh -File tools/smoke_fallback.ps1`
  - G HUB接続先を未使用ポート`65534`へ切り替え、トレイプロセスなしのネイティブフォールバックを確認する。G703の`0x1001`は`3921 mV`から汎用カーブで`Percent=70`、`Source=NativeVoltageEstimate`になる。同時点のG HUB Probe値`70%`との一致は一地点の結果であり、校正による一致とは扱わない。
- `python tools/smoke_hidden.py`
  - 呼び出し元desktopを`Default`のまま維持し、hidden desktop上で3秒生存、終了後にプロセスとdesktopを閉じた。画面の外観・クリック操作は判定していない。

電圧推定のカーブは、[OpenMouseの公開実装](https://github.com/OpenMouse-Project/openmouse/blob/main/src/logitech-hidpp.ts)を参照した汎用値であり、G HUBの機種別テーブルを再現したものではない。公開互換実装もNative HID++とG HUBの変換を別系統として扱っているため、G HUB観測点との一致をフォールバックの保証にはしない。[LGSNotifyBattery](https://github.com/nixxquality/LGSNotifyBattery)

## UI検索結果の切り分け

- 添付のCmdPal画面にあった`GHubTray.Probe`、`GHubTray.App`、複数の`GHubTray.CmdPal`は、`src/**/bin`、`obj`、`AppPackages`に残った開発成果物と一致する。PowerToysの拡張ログ上はGHubTrayが1拡張・1 command・1 bandで、トレイの多重起動を示す証拠ではない。
- 実画面を確認するときは、生成物を整理してからCmdPalを再起動する。生成物を残した状態の検索件数を拡張の登録数として扱わない。
- 今回は対象ワークスペース内の生成物ディレクトリ14件を削除してCmdPalを再起動した。再確認時点で`src`・`tests`配下の`bin`、`obj`、`AppPackages`は0件で、`Get-StartApps`にもGHubTrayの通常アプリ入口はない。

## 署名・PowerToys登録確認

- `pwsh -File tools/install_cmdpal_dev.ps1 -RestartCommandPalette`
  - 既存の`0.1.0.8`は同一バージョンで中身が異なるため再インストールを拒否した。manifestのバージョンを`0.1.0.9`へ上げて再発行し、開発用コード署名証明書で署名したMSIXを生成済みインストーラー経由で現在ユーザーへ登録した。初回の証明書信頼には管理者UACを使用した。
  - `Get-AppxPackage -Name GHubTrayCmdPal` は `Status=Ok`、`Publisher=CN=GHubTray`、`PackageFullName=GHubTrayCmdPal_0.1.0.9_x64__6cgfa9zbmbqp2` を返した。`Get-StartApps`にはGHubTrayの通常アプリ入口が出ず、Logicool G HUBだけが残った。
  - Command Palette `0.11.11762.0`を背景再起動した後、`commandProviderCache.json`に`GHubTrayCmdPal_6cgfa9zbmbqp2!App!GHubTray` / `DisplayName=GHubTray`、`settings.json`に`IsEnabled=true`が確認できた。ログはGHubTrayを1拡張、1 command、1 bandとして読み込んだ。
  - `GHubTray.CmdPal.exe -RegisterProcessAsComServer -Embedding` が起動し、トレイプロセスなしでもホストから拡張プロセスが生存した。これは拡張の登録・COM起動・プロバイダー再発見の確認であり、実画面の一覧やDock描画の確認ではない。
- `tools/install_cmdpal_dev.ps1` は、`dotnet publish`、x64 MSIX署名、開発用登録、必要ならCommand Palette再起動を再現する。秘密鍵はユーザー証明書ストアに置き、証明書ファイルとMSIXは生成物としてGit対象外にしている。

## 2026-09-04 CmdPal表示変更の反映

- `pwsh -NoProfile -File tools/install_cmdpal_dev.ps1 -RestartCommandPalette`
  - `Package.appxmanifest`の版数を自動で繰り上げ、`0.1.0.10`のx64 MSIXを発行・署名・現在ユーザーへ登録した。`Get-AppxPackage -Name GHubTrayCmdPal` は `Version=0.1.0.10`、`Status=Ok`、`PackageFullName=GHubTrayCmdPal_0.1.0.10_x64__6cgfa9zbmbqp2` を返し、Command Paletteを再起動した。
  - 今回の表示変更を登録済み開発版へ反映する作業ではこのスクリプトを使い、MSIX生成後の登録まで確認した。通常のビルドにインストール処理を必須とはしない。

## 2026-10-21 Dockの更新通知停止

- 修正前の実機では、ProbeがG703を`51%`、`Source=GHubAgent`、`Discharging`、`Failures=[]`として取得。登録済み`0.1.0.27`のCOMプロバイダーもページ・Dockとも`51%`を返したが、ユーザーのDock表示は「デバイス応答なし」のままだった。自動更新を別のCOMクライアントで観測すると、ページの`ItemsChanged`は2回届き、Dockの通知は0回だった。
- SDK `0.11.260520004`と`0.12.260812002`で、最初の通知先が`RPC_E_DISCONNECTED`を返すと次の通知先へ届かない条件を再現。実行中の旧版は通知例外を記録していないため、今回の実機で発生していた例外の種類までは確定していない。Dock専用の`IListPage.ItemsChanged`を実装し、通知先ごとに例外を分離して既知の切断先だけを解除するよう変更した。プロバイダー全体の再読み込み通知は使用しない。
- CmdPalテスト12件、Coreテスト46件が合格。CmdPalのx64 Releaseビルドは警告・エラー0件。トレイプロセスなしで、別々のCOMサーバー・失敗する旧ホスト役・新ホスト役を使い、「デバイス応答なし」から`51%`、`50%`への更新通知と旧ホストへの呼び出しが1回で止まることも確認した。一時診断用プロジェクトは`tools/obj/CmdPalRemoteSmoke`に配置しており、Git対象外。
- ユーザー承認後に`0.1.0.28`を発行・署名・登録し、CmdPalを再起動。パッケージは`Status=Ok`、署名は`Valid`、ホストログは1 command・1 bandの読み込みを記録。登録済み拡張はG703の`51%`を返し、17:58:18 JSTの自動更新でページ・Dock両方の通知を受信した。MSIX発行時の警告は既存のシンボル生成ツール不足のみ。実画面とスリープ復帰後の継続確認は未完了。

## 2026-10-21 取得と画面通知の構造分離

- [データの流れ](telemetry-data-flow.md)を実装と実行トレースで確認。Coreの同期`StatusChanged`を廃止し、容量1の購読Channelへ保存済み状態を発行する形に変更した。取得の完了は画面通知を待たない。ページ・Dock・詳細画面は保存値を読み、通知先ごとに実行中1件と最新の待機1件だけを扱う。手動更新も同じ保存・通知経路を使う。ホスト再初期化時には古い購読を解除し、新しい購読へ最新状態を再提示する。
- Coreテスト48件、トレイ状態テスト5件、CmdPalテスト18件が合格。両アプリのReleaseビルドは警告・エラー0件。疑似トレースでは、ページまたはDockの通知先を停止した状態で100回保存し、別の通知先が最新値を読めること、手動更新が42%の保存後に戻ること、停止解除後に42%へ追いつくことを確認。未読のCore購読も容量1に収まり、停止した通知先への呼び出しが更新ごとに増えない。再接続・終了・詳細フォーム不変性・機器消失時の未確認表示・停止中の定期取得も検査した。JSON行トレースを含むTRXは`tools/obj/flow-traces/`に出力する。
- `pwsh -NoProfile -File tools/smoke_cmdpal_flow.ps1`が成功。別プロセスの古いホストが切断例外を返す場合と通知中に停止する場合の両方で、新しいホストが`51% → 50%`を取得した。最新のsmokeログは`tools/obj/flow-smoke-54d26baecd9a4e8fa7f8004ac7bf642b/`。停止条件では、旧通知先の`recipient=1, revision=1`が実行中のまま、`recipient=2`がrevision 2・3の通知を完了している。旧通知先への呼び出しは1回だけで、テスト終了時に解放した。
- 最終版`0.1.0.30`を署名・現在ユーザーへ登録し、CmdPalを再起動した。登録は`Status=Ok`、ホストログは1 command・1 band。トレイプロセスなしで、G703の`50% / GHubAgent / Discharging / Failures=[]`をProbeから取得し、登録済み拡張のページ・Dockも50%を返した。ホスト初期化完了後、18:41:51と18:42:21 JSTにページ・Dock両方の自動更新通知を受信した。実画面の描画と実際のスリープ復帰は未確認。

## 2026-11-04 スリープ復帰時の取得

- トレイとCmdPalは各プロセスで`PowerRegisterSuspendResumeNotification`へ登録し、`PBT_APMRESUMEAUTOMATIC`で最大1回取得する。重複通知と後続の対話的復帰通知では取得を増やさず、次のサスペンドで再び受付可能になる。コールバックは処理を予約するだけでHIDやG HUBへ同期アクセスしない。終了時には登録を解除し、保留中の取得をキャンセルする。登録に失敗しても通常の定期・手動更新を利用できる。
- スリープをまたいだ取得が進行中なら、その終了後に新しい取得を1回行う。別の復帰が先の復帰取得と重なった場合も、新しい復帰を古い取得で済ませない。通常の更新間隔は変更せず、復帰取得が失敗しても追加の再試行は行わない。既存の状態マージ・表示・通知経路を利用する。
- Core 54件、トレイ状態7件、CmdPal 19件の計80件が合格。Windowsの実APIへの登録・解除、模擬通知の重複、旧取得との競合、別のスリープ周期、終了時のキャンセル、失敗時に再試行しないことを検査した。トレイとCmdPalはそれぞれ注入リーダーによる単独テストで表示の復旧を確認した。両アプリのReleaseビルドは警告・エラー0件。
- `tools/install_cmdpal_dev.ps1 -RestartCommandPalette`で`0.1.0.31`を署名・登録し、Command Paletteを再起動した。新しい登録先の拡張プロセスの起動を確認した。MSIX発行時の警告は既存のシンボル生成ツール不足のみ。G HUB稼働中、トレイプロセスなしのProbeはG703を`46% / GHubAgent / Discharging / Failures=[]`として取得した。
- 実際のOSスリープは実行していない。復帰直後の1回でG HUB・受信機から取得できるか、Dockの実画面がいつ戻るかは未確認。復帰時に取得できなかった場合は次の定期・手動更新を待つ。

## 2026-11-04 復帰処理の整理

- 復帰専用のタスク列を廃止し、進行中の取得を待ってから既存の`RefreshAsync`を呼ぶ形へ簡略化した。通知の重複は引き続き通知側でまとめる。Windows登録エラーは戻り値で処理し、例外へ変換して直後に捕捉する経路と、テスト用の登録有無フラグを削除した。
- 別のスリープ周期との重なりだけを検査するテスト、既存の取得失敗テストと重なるテスト、取得中の復帰テストが包含するトレイの単純な復帰テストの3件を削除した。Core 52件、トレイ状態6件、CmdPal 19件の計77件が合格。実API登録、重複通知、取得中の復帰、終了キャンセル、独立した各画面への反映は検証を維持した。
- トレイのReleaseビルドは警告・エラー0件。G HUB稼働中のProbeはG703を`46% / GHubAgent / Discharging / Failures=[]`として取得した。実スリープ復帰後の表示確認は引き続き未実施。
- CmdPalは`0.1.0.32`を署名・登録して再起動した。登録は`Status=Ok`。MSIX発行は成功し、警告は既存のシンボル生成ツール不足のみ。

## 2026-11-04 LogiBatteryDockへの改名

- ソース・テストのディレクトリとプロジェクト、名前空間、型名、表示名、IPC・単一起動・コマンド識別子、設定先、自動起動名、MSIXの識別情報、署名用の名前をLogiBatteryDockへ統一した。G HUBを取得元として表す名前は変更していない。既存の検証履歴と保存済みのGemini提案は当時の表記を維持した。
- Core 52件、トレイ状態6件、CmdPal 19件の計77件が新しいパスで合格。両アプリのReleaseビルドは警告・エラー0件。別プロセスCOMの通知停止・切断smokeも成功した。診断専用の一時コードは削除済み。
- トレイの実行確認で`TrayIcon`リソースの初期化が失敗した。生成アイコンの`BorderThickness="5"`だけを除く比較で起動・IPC応答が復旧したため、このSingle型の値だけをC#で同じ5に設定した。旧実行ファイルでのIPC確認もタイムアウトしたが、旧版の失敗原因が同じとは確定していない。smokeが失敗時に成功終了しないよう停止エラーを有効にした。最終トレイIPCはG703を`45% / GHubAgent / Discharging`として取得した。枠線を含む外観の実画面確認は未実施。
- 新名の`LogiBatteryDockCmdPal 0.1.0.34`を新しいCLSIDと開発用署名で登録し、`Status=Ok`を確認した。新しい証明書の信頼にはユーザー承認済みの管理者ランナーを使用した。旧パッケージは置き換え、Command Paletteの設定をバックアップしたうえで既存Dockの固定先を新provider・command IDへ移行した。実際の登録済みCOM拡張は`provider=LogiBatteryDock`、一覧`G703 45% / 接続中`、Dock`45% G703`を返した。トレイプロセスは停止した状態で確認した。
- G HUBを使わない実機フォールバックは、G703の`0x1001 / 3791 mV / 44% / NativeVoltageEstimate`を取得した。既存のアプリ設定ファイルと自動起動登録はこのPCに存在せず、その移行は不要だった。READMEの変更は名称・パスの置換に限定し、Geminiの全文提案は未適用としている。

## 未確認の境界

- 開発用自己署名での登録まで確認したが、製品証明書による配布、更新、アンインストール手順は未確定。
- CmdPalの独立読み取りは模擬テストとProbeで確認したが、実機上でPowerToysのページを開いて値を表示する操作は未確認。
- PowerToysでの拡張登録・COM起動は確認したが、一覧ページの実画面、Dockへの固定、Dock上の動的な値更新は未確認。
- `open_tray`はトレイ側の任意IPCに残るが、独立CmdPalの機能からは外した。この非対話的な実行環境では実デスクトップ上のトレイポップアップ操作確認が必要。
- CmdPalの文字列短縮、コピー操作削除、選択行だけに更新を出す`MoreCommands`、トレイのカスタムホバーツールチップと生成アイコン、設定Windowのサイズ指定は、コード・manifest・自動テスト／ビルドまで確認したが、ユーザーの実画面での最終スクリーンショット確認は未完了。
- HIDの対応範囲、スリープ復帰、受信機抜き差し、G HUB同時稼働の継続測定は未完了。
