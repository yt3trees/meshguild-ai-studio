# セキュリティと秘密情報

秘密情報は `ISecretStore` と DPAPI 保護ファイルにだけ保存します。
`appsettings*.json`、`agent.yaml`、`instructions.md`、会話本文、成果物、ログへ秘密を書きません。

Message、Artifact、Evaluation の所見、ミッションエラー、承認引数は永続化直前に `ISecretRedactor` を通します。
登録値だけでなく URL エンコード形と Base64 形も伏せ字化します。
例外全文は記録へ保存せず、利用者には一般化した失敗理由だけを返します。

Host HTTP API は認証・認可を持ちません。
ループバックまたは認証済みの外部境界で保護された環境以外へ公開しないでください。
Webhook は既定でループバックのみを受け付け、`X-WorkAgents-Trigger-Token` の共有秘密を照合します。

承認は安全性の保証ではありません。
承認後も実行側で対象と引数を再検証し、Shell や外部送信を承認なしに実行する経路を作らないでください。

MCPサーバを有効にする場合も同じ境界を適用します。
`Mcp:Enabled` は既定で `false` とし、Localではloopbackだけにbindします。Origin、入力、Resource URI、成果物サイズを検証し、MCPから承認を決定する操作や任意Shell・ファイル書き込みを公開しません。
MCPの応答、Tool説明、Resource、監査ログへAPIキー、トークン、秘密値、例外全文、絶対パスを出力しないでください。
