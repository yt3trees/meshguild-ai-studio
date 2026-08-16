# Copilot Instructions — WorkAgents ワークフロー執筆規約

このリポジトリで GitHub Copilot を使うエンジニア・レビュア・コミッタ向けの追加指示です。
GitHub Copilot Chat 用の `#instructions` 相当の内容を、Copilot が自然に拾って提案してくるようにワークフロー執筆規約だけまとめています。
リポジト全体のコーディング規約は別途 `AGENTS.md`(あるいは `CLAUDE.md`)を守ってください。

## どこに何が入るか

```
src/WorkAgents.Agents/workflows/<name>/
  workflow.yaml              # 1ワークフロー=1フォルダ。host 起動時に自動走査(FileBasedWorkflowLoader)。
  scripts/<任意の名前>.cs     # kind: code ステップから `codeFile:` で呼び出す。
  scripts/<任意の名前>.csx    # 上と同じ。.cs / .csx 両方 OK(実行時に Roslyn scripting で評価・拡張子非依存)。
```

- `workflows/` 直下の各フォルダが1つのワークフローになる。
- `agents/<name>/` と並列規約なので、同じ「フォルダ置くだけで追加」の原則で運用する。
- `workflows/<name>.yaml` のようにフォルダでなく単独ファイルを置いても拾われないことに注意(必ずサブフォルダ)。
- フォルダ名と `name:` が揃うようにする。`name:` 省略時はフォルダ名が fallback するが、明示を優先。

## workflow.yaml のスキーマ

```yaml
kind: Workflow                    # 必須。固定値 "Workflow"。
name: <ID>                        # 必須。スネークケースまたはケバブケース。他の agent/workflow と衝突禁止。
displayName: <概観名>
description: |
  1〜2行で用途を書く。WebUI /workflows に表示される。
schedule:
  cron: "<5 or 6 段階 Cron 書式>"  # 省略可。Local 時刻で解釈する(Cronos)。
                                  # 例: "0 9 * * 1"     = 毎週月曜 09:00
                                  #     "*/30 * * * *"  = 30 分ごと
                                  #     "0 0 1 * *"     = 毎月 1 日の 00:00
steps:                            # 必須。順に逐次実行。
  - name: <ステップID>           # 必須。後続ステップから ${steps.<ID>.*} で参照。
    kind: <agent | code | approve># 省略時は agent。
    ...
```

### kind: agent

LLM エージェントを呼び出す。`agents/` 配下のエージェントを指す。

```yaml
- name: minutes
  kind: agent
  agent: meeting-agent           # 必須。agents/<name>/agent.yaml の name と一致。
  input: |                       # テンプレート文字列。後述の変数を置換して渡す。
    以下を議事録として整形してください。
    ${workflow.input}
```

- 結果(応答テキスト)は `${steps.minutes.result}` で後続参照。
- 同期経路(`/chat` 同期実行・`/schedules` の Run now)でも動く(承認を含まない限り)。

### kind: code

C# スクリプトを実行する。`Inputs`(`IDictionary<string, object?>`)を読み、`return` で値を返す。

- **コードは `scripts/<name>.cs`(または `.csx`)に分離し、`codeFile:` で参照する**ことを推奨。
  長い・複数人で編集する・テスト容易性を考える場合。
- 1〜3 行の極短ワンライナーのみインライン `code:` を許容する。それ以上は .cs ファイルへ。
- 書式は **Roslyn C# Script 構文**(トップレベル文 + `return`)。.cs / .csx 両方とも同じ構文・拡張子非依存。
- 標準 using は事前バインド済み: `System`, `System.IO`, `System.Linq`, `System.Text`, `System.Collections.Generic`, `System.Globalization`, `System.Threading`, `System.Threading.Tasks`。`System.*` を明示 `using` し直す必要はない。
- **Host プロセス権限で実行**される。ファイル書き込み・`HttpClient` 等も動く。危険操作は必ず直前の `approve` ステップで人間に確認させる運用。
- 戻り値のオブジェクト(匿名型・辞書等)はフラット化され、後続から `${steps.<name>.output.<key>}` で参照できる。`Inputs` にもそのまま渡る。

```yaml
# yaml 側
- name: package
  kind: code
  codeFile: scripts/package.cs
```

```csharp
// scripts/package.cs(同一フォルダ内)
var minutes = Inputs["minutes"] as string ?? "";
var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
return new
{
    title = $"weekly-minutes-{stamp}.md",
    body = minutes,
};
```

- `codeFile:` は **workflow フォルダからの相対パス**で書く。
- loader が絶対パスへ解決し、存在をチェックする。Host 起動時に .cs を編集していたら再ビルドで bin コピーが更新される(Content include 規約)。
- インライン `code:` も使えるが、.cs への分離を優先する(参照の `codeFile:` が `code:` より優先される)。
- .cs ファイルはプロジェクトの Compile 対象から外される(`workflows/**` の .cs/.csx は Compile Remove)。Rider・VS で Definition 除去が必要なら都度。

`Inputs` に入るもの:

| キー                                     | 値                                                   |
| -------------------------------------- | --------------------------------------------------- |
| `workflow.input`                       | 呼出元メッセージ(ユーザ入力・schedule.Input)。                      |
| `<前段 step name>`(そのままの名前)              | 前段の結果。`code` ステップなら `IDictionary<string,object?>`、`agent` / `approve` なら文字列。 |

実行順の保証のため、ステップ名をキャメルケース・ケバブケース等で混ぜないこと。スネークケースまたはケバブケースを用途に合わせて統一。

### kind: approve

実行を一時停止し、Web `/approvals` に承認要求を表示する。却下・タイムアウトでワークフロー全体を Abort。

```yaml
- name: confirm
  kind: approve
  title: 議事録の保存確認                # 必須。WebUI とデスクトップ通知に表示。
  summary: |                        # 承認一覧の本文に表示するテンプレート。
    以下の議事録を保存します。対象: ${steps.package.output.title}
    内容:
    ${steps.package.output.body}
  timeoutMinutes: 15                # 省略時 15 分。
```

- **非同期 run 経路(`POST /runs` 経由)でしか動かない**。`/chat` 同期経路では runId が無く例外を投げる。承認を含む workflow は `/schedules` の Run now・`/chat` で動かさないこと。
- `summary:` はテンプレート置換後の本文。物象を全部出して良し。シークレットは出さない(運用)。
- タイムアウトは `Aborted`。WebUI「Approve」で再開、「Reject」で Abort。決定は監査に残る。
- 承認要求が作られると WebUI の `ApprovalWatcher`(Blazor 内で5 秒周期ポーリング)→ ブラウザ Notification API → デスクトップ通知を出す。ユーザ初期設定で許可が必要(`requestPermission` は自動で飛ぶ)。

## テンプレート変数

テンプレート文字列(`input` / `summary` / `code:` / `codeFile:` 読込後本文には無し)に以下を埋め込める:

| 書式                                   | 意味                                         |
| ------------------------------------ | ------------------------------------------ |
| `${workflow.input}`                   | 呼出元の入力文字列。schedule 実行なら `schedule.Input`。  |
| `${steps.<stepname>.result}`         | 文字列結果(`agent` 応答本文、`approve` なら `"approved"`)。 |
| `${steps.<stepname>.output.<key>}`   | `code` ステップの戻り値 dict の `<key>`。ネスティングはドットでたどれる。 |
| `${steps.<stepname>.<key>}`          | `output.<key>` の省略形(慣習)                     |

## スケジュール(`schedule.cron`)

- Cronos 書式。`分 時 日 月 曜日`(`*` はワイルドカード)。秒フィールドは Cronos の場合省略可(6 段階なら先頭が秒)。
- **Local プロファイルではローカル時刻で解釈**。Azure 移行後は `Schedules:TimeZone` 設定(`Asia/Tokyo` 等)で UTC から切替予定。
- `schedules` SQLite テーブルに Host 起動時に cron 付き workflow が自動 bootstrap される(`INSERT OR IGNORE` で上書きしない)。ユーザ編集値は保持。
- 非同期 run を Host の `IRunQueue` に投入する → `RunBackgroundService` が掴んで workflow を1つの run として完走。
- Cron 不正 → 当該 workflow の schedule 行化をスキップして log warning。
- approve ステップ含む workflow を cron で走らせる場合、承認タイムアウトで全体 Abort になるため、`timeoutMinutes` を業務実態に合わせること(就業時間内に届く、など)。

## 安全上のルール(Copilot は必ず守る)

- **危険操作は直前の `approve` ステップで確認させる**。`File.WriteAllText` / `Process.Start` / `HttpClient` 呼び出し・外部 API POST・`Directory.Delete` 等。
- **シークレット・PII を `Inputs` または戻り値に直接書かない**。token・API key・個人情報は `ISecretStore` 経由で取得し、`Outputs` 結果には入れない。`summary:` に貼らない。
- **ネットワーク egress は allowlist の意図がある**。`HttpClient` で外部を叩く場合、許可されるホストに限定する(運用上)。Local プロファイルは強制不能だが自律を守る。
- **テンポラリ/出力ファイルは `ProfileOptions.ArtifactsRoot`(`C:\work-agents\artifacts`) 配下に**。システム領域やユーザ Home 直下に書かない。`workspaceRoot`(`C:\work-agents\runs\<runId>`) は run ごとの作業FS。`code` ステップから `WorkspaceRoot` を参照したい phase は将来拡張で `Inputs["__workspacePath"]` の注入を足す可能性あり(現状は .yaml に固定値運用)。
- **path traversal**: `codeFile:` は loader が `Path.GetFullPath` して絶対解決済みなので組み立て不要。スクリプト内で user 由来 path を使う時は `Path.Combine` 後に `StartsWith(profileRoot)` を確認(mvp は任意だが推奨)。

## Copilot はワークフロー定義を編集するときに

- yaml 構文 lint を入れない。ワークフロー固有の規約だけを守る。
- 新しいステップ `kind`(`condition` / `http` / `parallel`)を**提案しない**。まだ実装されていないため、提案する場合はまずローダと `AgentRegistry` の対応実装を提示する。インライン `|` で「将来実装予定」と書くなら OK。
- `code:` インラインを使う提案は、1〜3 行の簡易ワンライナーのみ。それ以上は .cs 分離を提示する。
- ステップ名は他ステップの参照キーになるので、名前変更時は `${steps.<old>.*}` 全てを同時に更新する。
- `${steps.<name>.output.<key>}` の `<key>` は .cs の `return new { ... }` プロパティ名と一致させる。匿名型のプロパティ名にスペース・ピリオドは使えないので、出力キーは識別子安全。

## レビュー観点(ワークフロー PR のとき)

- [ ] 全ステップに `name` が一意かつ識別子安全。
- [ ] `kind: code` で外部副作用(File/Network)があるとき、直前の `approve` が置かれているか。
- [ ] `schedule.cron` を置くとき、`approve` を含むワークフローは承認者が常駐する時間帯か確認。
- [ ] `${steps.<n>.output.<k>}` の `<k>` が .cs の戻り値の実プロパティと合致するか。
- [ ] .cs ファイルは `scripts/` 配下に置かれ、`codeFile:` が workflow フォルダからの相対で書かれている。
- [ ] `code:` と `codeFile:` 両方を同じステップに置かない(`codeFile:` が優先され `code:` は無視されるが混乱)。
- [ ] シークレット・個人情報が yaml / .cs に直書きされていない。