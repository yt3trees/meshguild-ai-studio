// kind: code ステップ用 C# スクリプト(5.13.1)。
// 書き方: トップレベル文 + return(Roslyn C# Script 構文)。.cs / .csx 両方 OK。
// Inputs に workflow.input と前ステップ結果が入る。return した値は ${steps.<name>.output.<key>} で参照可能。
//
// Inputs には前のステップ(ここでは 'minutes' agent ステップ)の戻り値がその名前で入る。
// agent ステップは文字列(応答本文)を結果として持つ。

var minutes = Inputs["minutes"] as string ?? "";
var stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
var title = $"weekly-minutes-{stamp}.md";

return new
{
    title,
    body = minutes,
};