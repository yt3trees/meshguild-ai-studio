# MeshGuild AI Studio ドキュメント

`docs/` には、MeshGuild AI Studio の構成、設定、定義、検証、設計判断に関するリポジトリ内文書を置いています。
初めて使う人向けの操作手順は [利用者向けマニュアル](../manual/) を参照してください。

## 利用者向け

- [利用者向けマニュアル](../manual/)：インストール、起動、ミッション、定義編集、設定、FAQ
- [インストールと起動](../manual/_pages/getting-started.md)：ソースからの起動と初回モデル登録
- [はじめてのミッション](../manual/_pages/first-mission.md)：`demo-team` の実行と Team Room の確認
- [概念を絵で理解する](../manual/_pages/concepts.md)：ミッション、Team、Graph、承認、成果物の関係
- [API から実行する](../manual/_pages/api.md)：Mission API、単独 Run、承認、成果物
- [トラブルシューティング](../manual/_pages/troubleshooting.md)：起動、設定、定義、承認、成果物の問題

## 定義作成者と運用者向け

- [エージェントと定義ファイルの追加](adding-agents.md)：Agent、Team、Graph、Skill、外部定義ソース、ツールプラグイン
- [設定リファレンス](configuration.md)：LLM プロバイダー、保存先、キュー、実行上限、MCP
- [セキュリティと秘密情報](security-and-secrets.md)：Local 実行、秘密情報、承認、HTTP API の境界

## 開発者向け

- [テストガイド](testing.md)：単体テスト、Playwright E2E、MCP、ストリーミング、トレイの手動確認
- [マニュアルサイトの開発](manual-site-development.md)：`manual/` の Jekyll 開発、ビルド、公開
- [機能仕様](../specs/)：仕様、データモデル、契約、検証手順
- [設計判断](decisions/)：LLM プロバイダーとオーケストレーションの ADR

## 現在の実行経路

ミッションの実行エンジンは `WorkAgents.Host` に一本化しています。

| 経路 | プロジェクト | 入口 | 実行方式 | 状態保存 |
|---|---|---|---|---|
| ミッション | `WorkAgents.Host` | `POST /missions` | `WorkAgents.Orchestration` と BackgroundService で非同期実行 | SQLite(Mission、Message、Graph、Loop) |
| Web UI | `WorkAgents.Web` | `/missions`、`/approvals` | Host の HTTP API と SignalR を使う観測・操作クライアント | Host と同じ SQLite を参照 |
| 旧 Run | `WorkAgents.Host` | `POST /runs` | 既存利用者向けの互換経路 | SQLite(Run、Session) |

Web は実行エンジンを持ちません。
Host と Web は別プロセスとして起動し、同じ `Runs:DatabasePath` を参照します。
この構成と、定義が読み込まれる順序を変更しないでください。

## 文書の使い分け

- ルートの `README.md`：プロジェクトの概要、概念、最短の起動手順、入口となるリンク (英語版が正本)
- ルートの `README-ja.md`：`README.md` の日本語版。内容を変更したら両方を更新する
- `manual/`：利用者が画面を操作するための手順。GitHub Pages のサイトとして公開する文書
- `docs/*.md`：リポジトリの開発者、定義作成者、運用者が参照する現行の技術文書
- `specs/`：機能単位の仕様、契約、データモデル、計画、検証手順
- `docs/decisions/`：設計上の判断と、その理由を記録する ADR
- `docs/_old/`：過去の文書。現行実装と一致しないため、一次情報として扱わない

設定や実行経路を変更した場合は、関連する `docs/`、`manual/`、`specs/` の記述も確認してください。
