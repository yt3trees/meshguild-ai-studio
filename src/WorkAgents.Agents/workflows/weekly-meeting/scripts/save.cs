// kind: code ステップ用 C# スクリプト(5.13.1)。
// confirm ステップで承認された後、package ステップの戻り値(title/body)を受け取って保存する。
// code ステップの戻り値は次ステップ Inputs に IDictionary<string, object?> で渡る。

var package = Inputs["package"] as System.Collections.Generic.IDictionary<string, object?>;
if (package is null)
{
    return new { saved = false, reason = "package output missing" };
}

var title = package["title"] as string ?? throw new InvalidOperationException("title missing");
var body = package["body"] as string ?? throw new InvalidOperationException("body missing");

var dir = @"C:\work-agents\artifacts";
System.IO.Directory.CreateDirectory(dir);
var path = System.IO.Path.Combine(dir, title);
System.IO.File.WriteAllText(path, body);

return new
{
    saved = true,
    path,
};