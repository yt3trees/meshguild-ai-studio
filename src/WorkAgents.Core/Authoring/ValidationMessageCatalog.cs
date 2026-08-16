using System.Text.RegularExpressions;

namespace WorkAgents.Core.Authoring;

/// <summary>
/// 検証エラーを書き手向けの日本語へ翻訳するカタログ (案D)。
/// GraphValidator のエラーコードと、team.yaml ローダーが投げる英文メッセージの両方を受け付ける。
/// GUI と CLI の双方から同じ文言を出せるよう、依存を Core 内に閉じている。
/// </summary>
public static partial class ValidationMessageCatalog
{
    /// <summary>
    /// GraphValidator の 1 件を翻訳する。未知のコードは原文をそのまま見せる
    /// (黙って握り潰すと直しようがなくなるため)。
    /// </summary>
    public static AuthoringDiagnostic ForGraph(
        string code,
        string rawMessage,
        IReadOnlyList<string>? nodeIds = null,
        IReadOnlyList<string>? edgeIds = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        var nodes = nodeIds ?? Array.Empty<string>();
        var edges = edgeIds ?? Array.Empty<string>();
        var node = Names(nodes);
        var edge = Names(edges);

        var (message, fix, severity) = code switch
        {
            "unsupported_version" =>
                ("version が 1 ではありません。",
                 "version: 1 に直してください。現在サポートしているのは 1 だけです。",
                 DiagnosticSeverity.Error),

            "name_mismatch" =>
                ("name とフォルダー名が一致していません。",
                 "graphs/<フォルダー名>/graph.yaml の name は、そのフォルダー名と同じにしてください。",
                 DiagnosticSeverity.Error),

            "duplicate_id" when edges.Count > 0 =>
                ($"エッジ ID が重複しています ({edge})。",
                 "エッジの id はグラフ内で一意にしてください。id を省略すると <from>-to-<to> で自動採番されます。",
                 DiagnosticSeverity.Error),

            "duplicate_id" =>
                ($"ノード ID が重複しています ({node})。",
                 "ノードの id はグラフ内で一意にしてください。",
                 DiagnosticSeverity.Error),

            "unknown_node_ref" =>
                ($"エッジ {edge} が、存在しないノードを指しています ({node})。",
                 "from と to には、nodes に定義済みの id を指定してください。ノード名を変えたときは、そのノードを指すエッジも直す必要があります。",
                 DiagnosticSeverity.Error),

            "invalid_condition" =>
                ($"エッジ {edge} の condition が式として読めません。",
                 "condition で使えるのは ${...} 参照、比較演算 (== != < <= > >=)、論理演算 (&& || !)、括弧、数値、真偽値、引用符つき文字列だけです。関数呼び出しや算術演算は使えません。",
                 DiagnosticSeverity.Error),

            "unknown_node_kind" =>
                ($"ノード {node} の kind が不明です。",
                 "kind は agent / team / code / approval / branch / parallel / join / loop / subgraph のいずれかです。",
                 DiagnosticSeverity.Error),

            "unknown_definition_ref" =>
                ($"ノード {node} が、存在しない{DefinitionKindOf(rawMessage)}を参照しています。",
                 $"{DefinitionKindOf(rawMessage)}名のつづりを確認するか、先にその定義を作成してください。",
                 DiagnosticSeverity.Error),

            "missing_default_branch" =>
                ($"分岐ノード {node} から出るエッジすべてに condition が付いています。",
                 "どの条件にも当てはまらなかったときの行き先が無く、実行がそこで止まります。condition を書かないエッジを 1 本足して、既定の経路にしてください。",
                 DiagnosticSeverity.Error),

            "missing_join_policy" =>
                ($"合流ノード {node} に joinPolicy がありません。",
                 "全部の入力を待つなら all、最初の 1 件で先へ進むなら any を指定してください。",
                 DiagnosticSeverity.Error),

            "missing_code_file" =>
                ($"ノード {node} は kind: code なので、codeFile が必要です。",
                 "グラフフォルダーからの相対パスでスクリプトを指定してください (例: scripts/summarize.csx)。拡張子は .csx にしてください。graphs 配下の .cs はビルド出力へコピーされません。",
                 DiagnosticSeverity.Error),

            "missing_alternate_target" =>
                ($"ノード {node} は onPartialFailure: alternate なので、迂回先の alternate が必要です。",
                 "一部が失敗したときに進むノード ID を alternate に指定してください。",
                 DiagnosticSeverity.Error),

            "missing_stop_condition" =>
                ($"ループノード {node} に停止条件 (stop) がありません。",
                 "maxIterations (回数)、costLimitUsd (コスト)、timeLimitSeconds (時間)、scoreThreshold (スコア) のうち 1 つ以上を指定してください。どれも無いと止まらなくなるため必須です。",
                 DiagnosticSeverity.Error),

            "max_iterations_out_of_range" =>
                ($"ループノード {node} の stop.maxIterations が範囲外です。",
                 "1 以上 100 以下で指定してください。",
                 DiagnosticSeverity.Error),

            "score_threshold_out_of_range" =>
                ($"ループノード {node} の stop.scoreThreshold が範囲外です。",
                 "0.0 以上 1.0 以下で指定してください (例: 0.8)。",
                 DiagnosticSeverity.Error),

            "unresolved_reference" =>
                ($"{ReferenceOf(rawMessage)} という参照を解決できません{Located(node, edge)}。",
                 "使えるのは ${mission.goal}、${mission.id}、${loop.iteration}、${loop.previous.output}、${loop.previous.score}、および ${nodes.<ノード ID>.output} です。ノード ID のつづりを確認してください。",
                 DiagnosticSeverity.Error),

            "undeclared_cycle" =>
                ($"ノードが循環しています ({node})。",
                 "意図したループなら、後戻りするエッジに loopBack: true を付けて明示してください。意図していないならエッジの向きを見直してください。",
                 DiagnosticSeverity.Error),

            "unreachable_node" =>
                ($"どこからも到達できないノードがあります ({node})。",
                 "開始ノードから辿り着けるようにエッジを足すか、使わないノードなら削除してください。",
                 DiagnosticSeverity.Error),

            "subgraph_recursion" =>
                ("subgraph の呼び出しが再帰しています。",
                 "グラフが自分自身を直接または間接的に呼んでいます。呼び出しの連鎖を断ち切ってください。",
                 DiagnosticSeverity.Error),

            _ => (rawMessage, (string?)null, DiagnosticSeverity.Error),
        };

        return new AuthoringDiagnostic
        {
            Code = code,
            Message = message,
            Fix = fix,
            Severity = severity,
            NodeIds = nodes,
            EdgeIds = edges,
            RawMessage = rawMessage,
        };
    }

    /// <summary>
    /// team.yaml ローダー (FileBasedTeamLoader) の英文メッセージを翻訳する。
    /// ローダー側は例外 1 個しか投げないため、文面で突き合わせる。
    /// </summary>
    public static AuthoringDiagnostic ForTeam(string rawMessage)
    {
        ArgumentNullException.ThrowIfNull(rawMessage);

        if (rawMessage.StartsWith("team.yaml not found", StringComparison.Ordinal))
        {
            return Team("team_yaml_missing",
                "team.yaml が見つかりません。",
                "teams/<チーム名>/team.yaml という配置になっているか確認してください。", rawMessage);
        }

        if (rawMessage.StartsWith("unknown agent: ", StringComparison.Ordinal))
        {
            var agent = rawMessage["unknown agent: ".Length..].Trim();
            return Team("unknown_agent",
                $"エージェント {agent} が見つかりません。",
                "agents/<name>/agent.yaml に同名の定義が必要です。つづりを確認するか、先にエージェントを作成してください。", rawMessage);
        }

        if (rawMessage.StartsWith("duplicate member: ", StringComparison.Ordinal))
        {
            var agent = rawMessage["duplicate member: ".Length..].Trim();
            return Team("duplicate_member",
                $"エージェント {agent} が members に 2 回以上出てきます。",
                "同じエージェントを複数体動かしたい場合は、行を増やすのではなく maxInstances を上げてください。", rawMessage);
        }

        if (rawMessage.StartsWith("unknown channels.default: ", StringComparison.Ordinal))
        {
            return Team("unknown_channels_default",
                "channels.default の値が不正です。",
                "via-orchestrator (すべて統括経由) か direct (直接会話可) のどちらかを指定してください。", rawMessage);
        }

        if (rawMessage.StartsWith("unknown message kind: ", StringComparison.Ordinal))
        {
            var kind = rawMessage["unknown message kind: ".Length..].Trim();
            return Team("unknown_message_kind",
                $"メッセージ種別 {kind} は使えません。",
                "kinds に指定できるのは question (質問)、answer (回答)、share (共有) だけです。", rawMessage);
        }

        if (rawMessage.StartsWith("unknown key: ", StringComparison.Ordinal))
        {
            return Team("unknown_key",
                $"team.yaml に知らないキーがあります{UnknownKeyHint(rawMessage)}。",
                "キー名のつづり間違いか、インデントがずれて別の階層に入っている可能性があります。", rawMessage);
        }

        return rawMessage switch
        {
            "unsupported team.yaml version" => Team("unsupported_version",
                "version が 1 ではありません。",
                "version: 1 に直してください。現在サポートしているのは 1 だけです。", rawMessage),

            "team name must match folder name" => Team("name_mismatch",
                "name とフォルダー名が一致していません。",
                "teams/<フォルダー名>/team.yaml の name は、そのフォルダー名と同じにしてください。", rawMessage),

            "team must have an orchestrator" => Team("missing_orchestrator",
                "統括エージェント (orchestrator) が指定されていません。",
                "分解と進行管理を担うエージェントを 1 体、orchestrator.agent に指定してください。", rawMessage),

            "team must have at least one member" => Team("no_members",
                "members が 1 件もありません。",
                "統括以外のサブエージェントを最低 1 体、members に追加してください。", rawMessage),

            "member requires an agent name" => Team("member_without_agent",
                "members の中に agent 名が空の行があります。",
                "各メンバーに agent を指定してください。", rawMessage),

            "channel refers to an agent outside the team" => Team("channel_outside_team",
                "channels.allow が、このチームにいないエージェントを指しています。",
                "from と to には、orchestrator か members に含まれるエージェントだけを指定してください。", rawMessage),

            "maxDelegationDepth out of range" => Team("delegation_depth_out_of_range",
                "limits.maxDelegationDepth が範囲外です。",
                "1 以上 10 以下で指定してください (省略時は 3)。", rawMessage),

            "member instances exceed team parallel limit" => Team("parallel_limit_exceeded",
                "統括とメンバーの maxInstances の合計が、limits.maxParallelInstances を超えています。",
                "各 maxInstances を減らすか、limits.maxParallelInstances を合計以上に上げてください。", rawMessage),

            "scoreThreshold out of range" => Team("score_threshold_out_of_range",
                "evaluation.scoreThreshold が範囲外です。",
                "0.0 以上 1.0 以下で指定してください (例: 0.8)。", rawMessage),

            _ => Team("unknown", rawMessage, null, rawMessage),
        };
    }

    private static AuthoringDiagnostic Team(string code, string message, string? fix, string raw)
        => new()
        {
            Code = code,
            Message = message,
            Fix = fix,
            RawMessage = raw,
        };

    /// <summary>
    /// agent.yaml の検証メッセージを翻訳する。
    /// エージェントのローダーは不正な値を握りつぶしてログに落とすだけなので、
    /// team のように読み直しでは検証できない。原文は GUI 側の検証が組み立てる。
    /// </summary>
    public static AuthoringDiagnostic ForAgent(string rawMessage)
    {
        ArgumentNullException.ThrowIfNull(rawMessage);

        if (rawMessage.StartsWith("unknown skill: ", StringComparison.Ordinal))
        {
            var skill = rawMessage["unknown skill: ".Length..].Trim();
            return Agent("unknown_skill",
                $"共有スキル {skill} が見つかりません。",
                "skills/<name>/SKILL.md が必要です。つづりを確認するか、先に SKILL.md を置いてください。", rawMessage);
        }

        if (rawMessage.StartsWith("duplicate skill: ", StringComparison.Ordinal))
        {
            var skill = rawMessage["duplicate skill: ".Length..].Trim();
            return Agent("duplicate_skill",
                $"共有スキル {skill} が 2 回以上指定されています。",
                "同じスキルは 1 回だけ指定してください。", rawMessage);
        }

        return rawMessage switch
        {
            "agent name is required" => Agent("agent_name_required",
                "name が空です。",
                "エージェント名を入力してください。フォルダー名にもなります。", rawMessage),

            "invalid agent name" => Agent("invalid_agent_name",
                "name に使えない文字が含まれています。",
                "英小文字、数字、ハイフン、アンダースコアだけが使えます。先頭は英小文字か数字にしてください。", rawMessage),

            "unknown fileStore" => Agent("unknown_file_store",
                "harness.fileStore の値が不正です。",
                "workspace (作業用 FS) か artifacts (成果物の置き場) のどちらかを指定してください。", rawMessage),

            _ => Agent("unknown", rawMessage, null, rawMessage),
        };
    }

    private static AuthoringDiagnostic Agent(string code, string message, string? fix, string raw)
        => new()
        {
            Code = code,
            Message = message,
            Fix = fix,
            RawMessage = raw,
        };

    /// <summary>unknown_definition_ref の原文から、参照先の種類を日本語で取り出す。</summary>
    private static string DefinitionKindOf(string rawMessage)
    {
        if (rawMessage.Contains("agent", StringComparison.OrdinalIgnoreCase)) return "エージェント";
        if (rawMessage.Contains("team", StringComparison.OrdinalIgnoreCase)) return "チーム";
        if (rawMessage.Contains("graph", StringComparison.OrdinalIgnoreCase)) return "グラフ";
        return "定義";
    }

    /// <summary>unresolved_reference の原文から ${...} の中身を取り出す。</summary>
    private static string ReferenceOf(string rawMessage)
    {
        var match = ReferenceRegex().Match(rawMessage);
        return match.Success ? "${" + match.Groups["reference"].Value + "}" : "参照";
    }

    private static string Located(string node, string edge)
    {
        if (node != Unknown) return $" (ノード {node})";
        if (edge != Unknown) return $" (エッジ {edge})";
        return string.Empty;
    }

    private static string UnknownKeyHint(string rawMessage)
    {
        var match = PropertyNameRegex().Match(rawMessage);
        return match.Success ? $" ({match.Groups["name"].Value})" : string.Empty;
    }

    private const string Unknown = "不明";

    private static string Names(IReadOnlyList<string> ids)
    {
        var named = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        return named.Length == 0 ? Unknown : string.Join(", ", named);
    }

    [GeneratedRegex(@"'(?<reference>[^']+)'")]
    private static partial Regex ReferenceRegex();

    [GeneratedRegex(@"Property '(?<name>[^']+)' not found", RegexOptions.IgnoreCase)]
    private static partial Regex PropertyNameRegex();
}
