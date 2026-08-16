using System.Text;

namespace WorkAgents.Agents.Loading;

/// <summary>
/// 編集された <see cref="AgentDefinition"/> を agent.yaml と instructions.md へ書き戻す。
/// 指示文は agent.yaml に入れず instructions.md に置くという既存の規約 (5.2) に合わせる。
/// なお YAML のコメントは保持されない。
/// </summary>
public sealed class AgentYamlWriter
{
    /// <summary>agent.yaml と instructions.md をフォルダーへ書き出す。</summary>
    public async Task WriteAsync(AgentDefinition agent, string folderPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        Directory.CreateDirectory(folderPath);

        await File.WriteAllTextAsync(Path.Combine(folderPath, "agent.yaml"), ToYaml(agent), Encoding.UTF8, ct);

        // instructions.md は空でも作る。無いとエージェントが何をする役なのか読み手に伝わらないため。
        await File.WriteAllTextAsync(
            Path.Combine(folderPath, "instructions.md"),
            string.IsNullOrWhiteSpace(agent.Instructions) ? string.Empty : agent.Instructions,
            Encoding.UTF8,
            ct);
    }

    public string ToYaml(AgentDefinition agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agent.Kind)) builder.AppendLine($"kind: {Quote(agent.Kind!)}");
        builder.AppendLine($"name: {Quote(agent.Name)}");
        if (!string.IsNullOrWhiteSpace(agent.DisplayName)) builder.AppendLine($"displayName: {Quote(agent.DisplayName)}");
        if (!string.IsNullOrWhiteSpace(agent.Description)) builder.AppendLine($"description: {Quote(agent.Description)}");

        if (agent.SharedSkillNames.Count > 0)
        {
            builder.AppendLine("skills:");
            foreach (var skill in agent.SharedSkillNames)
            {
                builder.AppendLine($"  - {Quote(skill)}");
            }
        }

        if (agent.HarnessShell || !string.IsNullOrWhiteSpace(agent.HarnessFileStore))
        {
            builder.AppendLine("harness:");
            if (agent.HarnessShell) builder.AppendLine("  shell: true");
            if (!string.IsNullOrWhiteSpace(agent.HarnessFileStore)) builder.AppendLine($"  fileStore: {Quote(agent.HarnessFileStore!)}");
        }

        return builder.ToString();
    }

    private static string Quote(string value) => GraphYamlWriter.Quote(value);
}
