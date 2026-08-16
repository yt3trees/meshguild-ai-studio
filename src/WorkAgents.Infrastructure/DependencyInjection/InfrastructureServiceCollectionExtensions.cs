using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkAgents.Core;
using WorkAgents.Core.Abstractions;
using WorkAgents.Infrastructure.Approvals;
using WorkAgents.Infrastructure.Execution;
using WorkAgents.Infrastructure.Queue;
using WorkAgents.Infrastructure.Secrets;
using WorkAgents.Infrastructure.Stores;
using WorkAgents.Infrastructure.Workflows;
using WorkAgents.Orchestration;
using WorkAgents.Orchestration.Admission;
using WorkAgents.Orchestration.Budgets;
using WorkAgents.Orchestration.Context;
using WorkAgents.Orchestration.Checkpoints;
using WorkAgents.Orchestration.Graph;
using WorkAgents.Orchestration.Teams;

namespace WorkAgents.Infrastructure.DependencyInjection;

/// <summary>
/// Infrastructure の DI 登録口。設定 <c>Profile:Local|Azure</c> で実装を差し替える(第3章、第5章冒頭)。
/// M0 では配線のみ。各M で IRunQueue / IRunStore / ISessionStore /
/// IArtifactStore / ISecretStore の実装を順次登録する。
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddWorkAgentsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var profileValue = configuration["Profile"];
        var profile = Enum.TryParse<Profile>(profileValue, ignoreCase: true, out var p)
            ? p
            : Profile.Local;

        var profileOptions = new ProfileOptions
        {
            Profile = profile,
            WorkspaceRoot = configuration["Workspace:Root"] ?? @"C:\work-agents\runs",
            ArtifactsRoot = configuration["Artifacts:Root"] ?? @"C:\work-agents\artifacts",
        };
        services.AddSingleton(profileOptions);

        // M2: ISecretStore(Local: DPAPI 保護ファイル / Azure: Key Vault+マネージドID は M7)。
        services.AddSingleton<ISecretStore>(sp =>
        {
            var root = configuration["SecretStore:Root"];
            if (string.IsNullOrWhiteSpace(root))
            {
                root = LocalFileSecretStore.DefaultRoot;
            }
            return new LocalFileSecretStore(root, sp.GetService<ILogger<LocalFileSecretStore>>());
        });

        var queueCapacity = configuration.GetValue<int?>("Runs:QueueCapacity") ?? 100;
        var databasePath = configuration["Runs:DatabasePath"]
            ?? Path.Combine(profileOptions.WorkspaceRoot, "..", "state", "work-agents.db");
        services.AddSingleton<IRunQueue>(_ => new ChannelRunQueue(queueCapacity));
        services.AddSingleton<IRunStore>(_ => new SqliteRunStore(databasePath));
        services.AddSingleton<IRunCancellationRegistry, InMemoryRunCancellationRegistry>();
        services.AddSingleton<IMissionCancellationRegistry, InMemoryMissionCancellationRegistry>();
        services.AddSingleton<ISessionStore>(_ => new SqliteSessionStore(databasePath));
        services.AddSingleton<IApprovalStore>(sp => new SqliteApprovalStore(databasePath, sp.GetRequiredService<ISecretRedactor>()));
        services.AddSingleton<IChatTranscriptStore>(_ => new SqliteChatTranscriptStore(databasePath));
        services.AddSingleton<IChatTraceStore>(_ => new SqliteChatTraceStore(databasePath));
        services.AddSingleton<ILlmModelStore>(sp =>
            new SqliteLlmModelStore(databasePath, sp.GetRequiredService<ISecretStore>()));
        services.AddSingleton<ApprovalService>();
        services.AddSingleton<IApprovalService>(sp => sp.GetRequiredService<ApprovalService>());
        services.AddSingleton<IScheduleStore>(_ => new SqliteScheduleStore(databasePath));
        services.AddSingleton<IWorkflowScriptRunner, RoslynWorkflowScriptRunner>();
        services.AddSingleton<ICostStore>(_ => new SqliteCostStore(databasePath));

        // M6: 予算判定・上限到達時のAbort・モデルfallback・コストダッシュボードは今後の課題。

        // 自律型マルチエージェント統括基盤 (001-multi-agent-orchestration): Foundational 層の配線。
        services.AddSingleton<ISecretRedactor>(sp =>
            new StoreBackedSecretRedactor(sp.GetRequiredService<ISecretStore>()));
        services.AddSingleton<IMissionStore>(sp => new SqliteMissionStore(databasePath, sp.GetRequiredService<ISecretRedactor>()));
        services.AddSingleton<IMcpSubmissionStore>(_ => new SqliteMcpSubmissionStore(databasePath));
        services.AddSingleton<IMessageStore>(sp => new SqliteMessageStore(databasePath, sp.GetRequiredService<ISecretRedactor>()));
        services.AddSingleton<IInterventionStore>(_ => new SqliteInterventionStore(databasePath));
        services.AddSingleton<IBudgetStore>(_ => new SqliteBudgetStore(databasePath));
        services.AddSingleton<ILoopStore>(sp => new SqliteLoopStore(databasePath, sp.GetRequiredService<ISecretRedactor>()));
        services.AddSingleton<IGraphVersionStore>(_ => new SqliteGraphVersionStore(databasePath));
        services.AddSingleton<ITriggerStore>(_ => new SqliteTriggerStore(databasePath));
        services.AddSingleton<ICheckpointStore>(_ => new SqliteCheckpointStore(databasePath));
        services.AddSingleton<IMissionWorkspaceStore>(_ => new SqliteMissionWorkspaceStore(databasePath));
        services.AddSingleton(sp => new MissionWorkspacePathResolver(profileOptions.WorkspaceRoot));
        services.AddSingleton<IMissionWorkspaceProvider, MissionWorkspaceProvider>();
        services.AddSingleton<IMissionWorkspaceReader, MissionWorkspaceReader>();
        services.AddSingleton<SqliteArtifactStore>(sp => new SqliteArtifactStore(databasePath, profileOptions.ArtifactsRoot, sp.GetRequiredService<ISecretRedactor>()));
        services.AddSingleton<IMissionArtifactStore>(sp => sp.GetRequiredService<SqliteArtifactStore>());
        services.AddSingleton<IAgentInstanceStore>(_ => new SqliteAgentInstanceStore(databasePath));
        services.AddSingleton<IMissionQueueStore>(_ => new SqliteMissionQueueStore(databasePath));
        services.AddSingleton<MessageBus>();
        services.AddSingleton<ContextAssembler>();
        services.AddSingleton<TeamExecutor>();
        services.AddSingleton<RosterManager>();
        services.AddSingleton<CostAttribution>();
        services.AddSingleton<BudgetLedger>();
        services.AddSingleton<WorkAgents.Orchestration.Loops.Evaluator>();
        services.AddSingleton<WorkAgents.Orchestration.Loops.LoopExecutor>();
        services.AddSingleton<ExpressionEvaluator>();
        services.AddSingleton<GraphValidator>();
        services.AddSingleton<GraphExecutor>();
        services.AddSingleton<WorkAgents.Orchestration.Replay.ReplayService>();
        services.AddSingleton<WorkAgents.Orchestration.Replay.MissionReportBuilder>();
        services.AddSingleton(sp => new CheckpointManager(
            sp.GetRequiredService<ICheckpointStore>(),
            sp.GetRequiredService<IMessageStore>(),
            sp.GetRequiredService<ISecretRedactor>(),
            new CheckpointOptions
            {
                WorkspaceRoot = profileOptions.WorkspaceRoot,
                MaxWorkspaceBytes = configuration.GetValue<long?>("Orchestration:Checkpoint:MaxWorkspaceBytes") ?? 512L * 1024 * 1024,
            }));

        var maxConcurrentMissions = configuration.GetValue<int?>("Orchestration:Limits:MaxConcurrentMissions") ?? 5;
        var maxConcurrentAgents = configuration.GetValue<int?>("Orchestration:Limits:MaxConcurrentAgents") ?? 12;
        services.AddSingleton(sp => new AdmissionController(
            sp.GetRequiredService<IMissionQueueStore>(),
            maxConcurrentMissions,
            maxConcurrentAgents));
        services.AddSingleton<MissionEngine>();

        var engineEnabled = configuration.GetValue<bool?>("Orchestration:Engine:Enabled") ?? false;
        if (engineEnabled)
        {
            services.AddHostedService<MissionBackgroundService>();
            services.AddHostedService<TriggerBackgroundService>();
            services.AddHostedService<MissionRecoveryHostedService>();
        }

        // ワークスペース保持期限スイープ(004-workspace-artifact-lifecycle FR-001〜FR-004)。
        var retentionOptions = new WorkspaceRetentionOptions
        {
            Enabled = configuration.GetValue<bool?>("Workspace:Retention:Enabled") ?? true,
            RetentionPeriod = configuration.GetValue<TimeSpan?>("Workspace:Retention:RetentionPeriod") ?? TimeSpan.FromDays(7),
            SweepInterval = configuration.GetValue<TimeSpan?>("Workspace:Retention:SweepInterval") ?? TimeSpan.FromHours(1),
        };
        services.AddSingleton(retentionOptions);
        services.AddSingleton<IWorkspaceUsageSnapshot, WorkspaceUsageSnapshot>();
        services.AddSingleton<WorkspaceUsageReportBuilder>();
        services.AddSingleton<Stores.ArtifactDownloadResolver>();
        if (retentionOptions.Enabled && engineEnabled)
        {
            // engineEnabled と同じフラグでゲートし、Web/Host双方でAddWorkAgentsInfrastructureが
            // 呼ばれても二重にスイープが走らないようにする(既存のMissionBackgroundService等と同じ運用前提)。
            services.AddHostedService(sp => new WorkspaceRetentionBackgroundService(
                sp.GetRequiredService<IRunStore>(),
                profileOptions.WorkspaceRoot,
                sp.GetRequiredService<WorkspaceRetentionOptions>(),
                sp.GetRequiredService<IWorkspaceUsageSnapshot>(),
                sp.GetRequiredService<ILogger<WorkspaceRetentionBackgroundService>>(),
                sp.GetRequiredService<IMissionStore>(),
                sp.GetRequiredService<IMissionWorkspaceStore>()));
        }

        // Git認証(004-workspace-artifact-lifecycle FR-005〜FR-007)。秘密鍵はISecretStore経由でのみ取得する。
        services.AddSingleton(sp => new WorkAgents.Harness.GitAuth.GitAuthOptions
        {
            AppId = configuration.GetValue<int?>("GitAuth:AppId") ?? 0,
            InstallationId = configuration.GetValue<long?>("GitAuth:InstallationId") ?? 0,
            PrivateKeySecretName = configuration["GitAuth:PrivateKeySecretName"] ?? "github-app-private-key",
        });
        services.AddSingleton<WorkAgents.Harness.GitAuth.GitCredentialStoreInitializer>();
        services.AddSingleton<WorkAgents.Harness.GitAuth.IInstallationTokenSource, WorkAgents.Harness.GitAuth.GitHubAppTokenMinter>();
        services.AddSingleton<WorkAgents.Harness.GitAuth.IGitAuth, WorkAgents.Harness.GitAuth.GitAuthenticator>();

        return services;
    }
}
