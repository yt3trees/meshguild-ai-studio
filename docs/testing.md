# テストガイド

通常の検証は次の二つです。

```powershell
dotnet build WorkAgents.sln
dotnet test tests/WorkAgents.UnitTests/WorkAgents.UnitTests.csproj
```

オーケストレーションのテストは `IAgentInvoker` の `ScriptedAgentInvoker` を使います。
台本をエージェント名へ登録すると、LLM や API キーへ接続せずに発言とツール呼び出しを再現できます。

状態遷移、承認、待機列、秘密情報、チェックポイント、循環検出はテスト先行の対象です。
ストアを使うテストは `Path.GetTempPath()` 配下へ一意なデータベースを作り、終了時に削除します。

Host の手動確認は次の順で行います。

```powershell
dotnet run --project src/WorkAgents.Host
dotnet run --project src/WorkAgents.Web
```

ミッションの発言、承認、上限到達、再開、Webhook の認証失敗を確認します。
30 秒観察による画面の理解度と Graph Studio の操作性は自動テストでは代替しません。

## MCPサーバの手動確認

MCPはHostが提供するLocal-onlyのStreamable HTTP endpointです。`Mcp:Enabled=true` で起動し、MCP Inspectorまたは対応クライアントを `http://127.0.0.1:5160/mcp` に接続して、次を確認します。

- [ ] `server/discover` が対応バージョンと実装済みcapabilityだけを返す
- [ ] `tools/list` に定義一覧、Mission、観測、承認参照のToolだけが出て、Shell・承認決定・定義書き込みが出ない
- [ ] 無効なOrigin、protocol version、対象、Resource URI、サイズ上限が安全に拒否される
- [ ] MCPからMissionを投入し、Mission IDで状態とGraph観測を取得できる
- [ ] 承認待ちのMissionは `/approvals` で決定するまで進まず、MCPにapprove/reject操作がない
- [ ] MCP接続切断だけではMissionが暗黙に中断されず、明示的cancelだけが許可状態を変更する
- [ ] MCP応答・Resource・ログに秘密値、絶対パス、raw YAML、例外全文が含まれない

## 応答のストリーミング表示 (Team Room) の手動確認

Team Room の暫定発言は Host の SignalR (`/hubs/missions`) が配信するため、Web だけを起動する
Playwright E2E では再現できません。Host と Web を両方起動して手動で確認します。

- [ ] ミッション実行中、エージェントの発言が確定を待たずに少しずつ表示される (`生成中` タグと点滅キャレット付き)
- [ ] 発言が確定すると暫定表示が消え、通常の発言バブル 1 件に置き換わる (二重表示にならない)
- [ ] Host を落として再接続すると暫定表示が消え、確定済みの発言だけが残る
- [ ] `Streaming:Enabled=false` (設定画面またはトレイ設定) で起動すると、発言が確定してからまとめて表示される
- [ ] シェル承認を伴うエージェントでは、承認待ちに入った時点で暫定表示が閉じ、承認後に確定発言が出る

## WorkAgents.Tray (常駐トレイランチャー) の手動スモークテスト

自動テストの対象外(UI依存)のため、`specs/007-tray-icon-app/quickstart.md` の各シナリオを
手動で確認します。実行前に `dotnet build WorkAgents.sln` でTrayを含めてビルドしておきます。

- [ ] シナリオ1: 起動してWeb UIを開く(`WorkAgents.Tray.exe` 起動 → トレイの「開く」→ ブラウザでトップページ表示)
- [ ] シナリオ2: エージェント定義追加後の「更新」(定義追加 → 「更新」→ 一覧に反映)
- [ ] シナリオ3: 進行中Runがある状態での更新/終了確認(確認ダイアログのキャンセル/続行)
- [ ] シナリオ4: 二重起動(2つ目のプロセスが即終了し、既存トレイへ通知)
- [ ] シナリオ5: 予期せぬクラッシュ時の子プロセス後始末(ランチャーを強制終了→Host/Webも終了)
- [ ] シナリオ6: 稼働中のクラッシュ検知とエラー表示(Hostのみ強制終了→トレイがエラー表示→更新で復帰)
- [ ] シナリオ7: ポート設定の変更(範囲外/同一値の拒否、有効値保存後の再起動反映)
- [ ] シナリオ8: appsettings関連の詳細設定(Workspace保存先フォルダ、`キー=値`形式の詳細設定、不正行の拒否、追加エージェント定義フォルダ)。配布物では`dist\definitions`へ追加した定義/SkillをHost/Webへ個別コピーせず、トレイの「更新」で両方へ反映できること

## Playwright E2E テスト

Playwright の E2E suite はリポジトリルートの npm package で管理します。WebServer はテストコマンドから自動起動されるため、別の `dotnet run` を先に起動しないでください。

初回セットアップ:

```powershell
npm ci
npm run test:e2e:install
npm run typecheck
```

全体をローカル headed モードで実行:

```powershell
npm run test:e2e
```

特定のテストファイル、またはタイトル filter を実行:

```powershell
npm run test:e2e -- tests/e2e/navigation.spec.ts
npm run test:e2e -- tests/e2e/models.spec.ts -g "validation"
npm run test:e2e -- tests/e2e/chat.spec.ts
npm run test:e2e -- tests/e2e/approvals.spec.ts
npm run test:e2e -- tests/e2e/user-story-1-team-room.spec.ts
npm run test:e2e -- tests/e2e/user-story-2-human-control.spec.ts
npm run test:e2e -- tests/e2e/user-story-3-loop-console.spec.ts
npm run test:e2e -- tests/e2e/user-story-4-graph-studio.spec.ts
npm run test:e2e -- tests/e2e/user-story-5-triggers.spec.ts
npm run test:e2e -- tests/e2e/user-story-6-replay.spec.ts
```

Playwright の GUI ツール(UI mode)で実行:

```powershell
npx playwright test --ui
```

同じ操作は npm script でも実行できます。

```powershell
npm run test:e2e:ui
```

GUI ではテストの選択・実行、実行ログの確認、失敗した action の inspection、trace の確認を対話的に行えます。特定ファイルだけを対象にする場合は `npx playwright test --ui tests/e2e/navigation.spec.ts` とします。

全テストの結果 screenshot を保存する実行:

```powershell
$env:PW_SCREENSHOT = "on"
npm run test:e2e
Remove-Item Env:PW_SCREENSHOT
```

Git Bash では次のように実行できます。

```bash
PW_SCREENSHOT=on npm run test:e2e
```

通常は容量を抑えるため失敗時だけ screenshot を保存します。`PW_SCREENSHOT=on` を指定した場合は成功テストを含む各テストの screenshot も `test-results/artifacts/` 配下へ保存されます。GUI mode と組み合わせる場合は、PowerShell で `$env:PW_SCREENSHOT = "on"; npx playwright test --ui` とします。

CI と同じ headless/retry 設定で実行:

```powershell
$env:CI = "1"
npm run test:e2e
Remove-Item Env:CI
```

Playwright は `http://127.0.0.1:5049` で `WorkAgents.Web` を `E2E` 環境として起動します。各実行は一時 SQLite、SecretStore、Workspace、Artifacts を使い、開発用の `C:\work-agents` データや API キーへ接続しません。E2E では Chat の provider 呼び出しを決定的レスポンスへ切り替え、Approvals の seed/status endpoint は loopback の E2E 環境だけで有効です。

レポートと失敗診断:

```powershell
npm run test:e2e:report
```

- HTML report: `test-results/html-report/`
- JUnit XML: `test-results/junit/results.xml`
- failure screenshot/trace: `test-results/artifacts/`

テスト終了直後は SQLite process lock のため一時 run root が保持されることがあります。WebServer が終了した後、不要な一時データを削除する場合は次を実行します。

```powershell
npm run test:e2e:clean
```

Playwright の bundled Chromium がない、または `5049` port が使用中の場合は E2E 実行が明確に失敗します。既存の開発サーバーへ接続して実データを検証することはありません。
