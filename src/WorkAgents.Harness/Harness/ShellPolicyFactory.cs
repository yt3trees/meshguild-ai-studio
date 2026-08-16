using System.Text.RegularExpressions;
using Microsoft.Agents.AI.Tools.Shell;

namespace WorkAgents.Harness.Harness;

/// <summary>
/// <see cref="ShellPolicy"/> の既定 denylist/allowlist 構築(5.4, 5.11)。
/// denylist は UX 前処理であり境界ではない(境界は egress 制御と FS 制限)。
/// Local プロファイルを Windows ネイティブで動かす場合も PowerShell/cmd 系を網羅する(第3章 2)。
/// </summary>
public static class ShellPolicyFactory
{
    /// <summary>
    /// 既定の拒否パターン(POSIX + Windows PowerShell/cmd 系)。
    /// <list type="bullet">
    /// <item>破壊的削除: rm -rf / sudo / Remove-Item -Recurse / rd /s / del /s</item>
    /// <item>egress 持ち出し: curl / wget / Invoke-WebRequest / iwr / scp / rsync / nc</item>
    /// <item>git push(承認 + denylist の二重封鎖・5.5)</item>
    /// <item>権限昇格: sudo / runas</item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultDenyList =
    [
        // 破壊的削除(POSIX)
        @"\brm\s+(-[a-zA-Z]*r[a-zA-Z]*f?|--recursive)\b",
        @"\brmdir\s+/s\b",
        @"\bsudo\b",
        // 破壊的削除(Windows)
        @"\bRemove-Item\b.*-Recurse",
        @"\brd\s+/s\b",
        @"\bdel\s+/[a-zA-Z]*s\b",
        // egress 持ち出し
        @"\bcurl\b",
        @"\bwget\b",
        @"\bInvoke-WebRequest\b",
        @"\biwr\b",
        @"\bscp\b",
        @"\brsync\b",
        @"\bnc\b",
        // git push(5.5 二重封鎖)
        @"\bgit\s+push\b",
        // 権限昇格(Windows)
        @"\brunas\b",
    ];

    /// <summary>
    /// workspace.yaml の <c>shell.denyList</c>/<c>shell.allowList</c> と既定 denylist を合成する。
    /// 重複は <see cref="HashSet{T}"/> で排除。allowList は現状アドホック許可用(既定は空)。
    /// </summary>
    public static ShellPolicy Build(WorkspaceShellYaml? shell)
    {
        var deny = new HashSet<string>(DefaultDenyList, StringComparer.Ordinal);
        if (shell?.DenyList is { } extra)
        {
            foreach (var d in extra)
            {
                if (!string.IsNullOrWhiteSpace(d))
                {
                    deny.Add(d.Trim());
                }
            }
        }

        var allow = new List<string>();
        if (shell?.AllowList is { } a)
        {
            foreach (var x in a)
            {
                if (!string.IsNullOrWhiteSpace(x))
                {
                    allow.Add(x.Trim());
                }
            }
        }

        // 入力の正規表現正当性を軽く検証(壊れたパターンは除外・ログ目視用に例外は投げない)。
        var validDeny = deny.Where(p => IsValidRegex(p)).ToList();
        return new ShellPolicy(validDeny, allow);
    }

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = Regex.IsMatch(string.Empty, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}