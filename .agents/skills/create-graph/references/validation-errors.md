# グラフのバリデーションエラー対応表

`POST /graphs/<name>/validate` と `PUT /graphs/<name>` が返す `errors[].code` の一覧です。
判定の実体は `src/WorkAgents.Orchestration/Graph/GraphValidator.cs`、
利用者向けの文言の正本は `src/WorkAgents.Core/Authoring/ValidationMessageCatalog.cs` です。
挙動が食い違ったらこの表ではなくそちらを信じてください。

`errors[]` の各件には `nodeIds` と `edgeIds` が付きます。どのノード・エッジが原因かはそこを見ます。

| code | 何が起きているか | 直し方 |
|---|---|---|
| `validation_failed` | YAML として読めない、またはスキーマ違反。`message` に原文が入る | 未知のキー、インデントのずれ、型の誤りを疑う。VS Code の赤線を確認する |
| `unsupported_version` | `version` が 1 以外 | `version: 1` にする |
| `name_mismatch` | `name` とフォルダー名が不一致 | `graphs/<フォルダー名>/graph.yaml` の `name` をフォルダー名と完全一致させる (大文字小文字も区別) |
| `duplicate_id` (nodeIds あり) | ノード ID の重複 | `nodes[].id` をグラフ内で一意にする |
| `duplicate_id` (edgeIds あり) | エッジ ID の重複 | `edges[].id` を消して自動採番 (`<from>-to-<to>`) に任せる。同じ from/to の組が 2 本あるなら片方に別の id を付ける |
| `unknown_node_ref` | エッジの `from` / `to` が存在しないノードを指している | ノード ID の改名に追随させる。`nodes` に定義済みの id だけを書く |
| `unknown_node_kind` | `kind` が既定の 9 種類以外 | `agent` / `team` / `code` / `approval` / `branch` / `parallel` / `join` / `loop` / `subgraph` のいずれかにする |
| `unknown_definition_ref` | 存在しないエージェント / チーム / グラフを参照している | つづりを確認するか、先にその定義を作る。Host の validate エンドポイントはこれを検出しないので、GUI 保存時か起動時に出る |
| `invalid_condition` | `condition` が式として読めない | 使えるのは `${...}` 参照、`== != < <= > >=`、`&& \|\| !`、括弧、数値、`true` / `false`、引用符つき文字列だけ。関数呼び出しと算術演算は不可 |
| `unresolved_reference` | `${...}` の中身を解決できない | 使えるのは `${mission.goal}`、`${mission.id}`、`${loop.iteration}`、`${loop.previous.output}`、`${loop.previous.score}`、`${nodes.<存在するノード id>....}` のみ。ノード ID のつづりを確認する |
| `missing_default_branch` | `branch` から出るエッジすべてに `condition` がある | `condition` を書かないエッジを 1 本足す。どの条件にも当てはまらないと実行がそこで止まる |
| `missing_join_policy` | `join` に `joinPolicy` が無い | `all` (全入力を待つ) か `any` (最初の 1 件で進む) を指定する |
| `missing_code_file` | `kind: code` に `codeFile` が無い | グラフフォルダーからの相対パスを書く。拡張子は `.csx` (`.cs` はビルド出力へコピーされない) |
| `missing_alternate_target` | `onPartialFailure: alternate` なのに `alternate` が無い | 迂回先のノード ID を `alternate` に書く |
| `missing_stop_condition` | `loop` に `stop` が無い、または中身が空 | `maxIterations` / `costLimitUsd` / `timeLimitSeconds` / `scoreThreshold` のいずれか 1 つ以上を指定する |
| `max_iterations_out_of_range` | `stop.maxIterations` が範囲外 | 1 以上 100 以下にする |
| `score_threshold_out_of_range` | `stop.scoreThreshold` が範囲外 | 0.0 以上 1.0 以下にする (例: 0.8) |
| `undeclared_cycle` | `loopBack: true` が付いていない循環がある | 意図したループなら後戻りするエッジに `loopBack: true` を付ける。意図していないならエッジの向きを直す |
| `unreachable_node` | どこからも到達できないノードがある | 起点から辿れるようエッジを足すか、使わないノードを消す |
| `subgraph_recursion` | `subgraph` の呼び出しが直接・間接に再帰している | 呼び出しの連鎖を断つ |

## 到達判定と循環判定の細かい挙動

どちらも `loopBack: true` のエッジを完全に無視して計算します。ここを取り違えると直し方を間違えます。

- 起点は「`loopBack: false` のエッジで入ってくる本数が 0 のノード」全部です。起点が複数あっても構いません
- 起点が 1 つも無い場合 (全ノードが循環している場合)、`nodes` の先頭 1 件だけを起点に扱います
- したがって、後戻りでないエッジに誤って `loopBack: true` を付けると、その先が到達不能になり `unreachable_node` が出ます。`undeclared_cycle` を消そうとして手当たり次第に `loopBack` を付けると、この形で別のエラーに化けます

## よくある詰まり方

`undeclared_cycle` と `unreachable_node` が同時に出る
: ループの後戻りエッジを取り違えています。ループの出口から入口へ戻る 1 本だけに `loopBack: true` を付け、
  それ以外からは外します。

`unresolved_reference` が消えない
: ノード ID にハイフンが入っていても参照は書けます (`${nodes.track-a.output}`)。
  消えないならノード ID のつづりか、`nodes.` の綴り (`node.` になっていないか) を確認します。

`validation_failed` だけ返って原因がわからない
: YAML のパース段階で落ちています。`errors[].message` に原文が入るのでそこを読みます。
  ノード単位で切り分けるなら、`nodes` を数件に減らした最小の YAML を作って validate に投げると早いです。
