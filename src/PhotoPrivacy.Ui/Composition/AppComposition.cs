using Microsoft.Extensions.DependencyInjection;
using PhotoPrivacy.Core.Configuration;
using System.IO;
using PhotoPrivacy.Ipc;
using PhotoPrivacy.Ui.Localization;
using PhotoPrivacy.Ui.Services;
using PhotoPrivacy.Ui.ViewModels;
using PhotoPrivacy.Ui.Views;

namespace PhotoPrivacy.Ui.Composition;

/// <summary>
/// 票 29（架构恢复第七轮）：组合根 —— 全局服务装配唯一出处（spec 研究输入 Q1/Q2：
/// 工业范式 = 组合根持有单例服务 + VM 构造注入，StabilityMatrix / Files.App / MarkSmith 模板）。
/// Program.Start（应用启动处）是 Build 的唯一调用点；MainWindow 停止手写 new 依赖。
/// 静态单例逐票收敛（spec Out of Scope）：本票先收敛 LocalizationService 入口（检查点 B）。
/// </summary>
public static class AppComposition
{
    public static IServiceProvider Build(AppConfig config, string configPath, string? workerPath)
    {
        // WorkerIpcClient / WorkerProcessManager / ServiceManager 均无实例状态，
        // 进程级单例与原先散落的多个手写 new 实例行为等价（每次调用各自建 transport）。
        var workerIpc = new WorkerIpcClient();
        var workerManager = new WorkerProcessManager(workerIpc);
        var serviceManager = new ServiceManager();

        var services = new ServiceCollection();

        // 票20 不可变快照 + 委托闭包语义整体迁移自 Program.BuildRuntimeOptions（读取 App.RuntimeOptions 当前值）。
        services.AddSingleton(BuildRuntimeOptions(config, configPath, workerPath, workerIpc, workerManager, serviceManager));

        // ADR 0053 M1：ConnectionState 由 Program.Start 置为 Connecting 后经 SetConnectionState 挂载。
        services.AddSingleton(sp => new ConnectionStateService(
            sp.GetRequiredService<WorkerIpcClient>(), WorkerIpcEndpointNames.BackgroundPipe));

        services.AddSingleton<WorkerIpcClient>(workerIpc);
        services.AddSingleton<WorkerProcessManager>(workerManager);
        services.AddSingleton<ServiceManager>(serviceManager);
        services.AddSingleton<IServiceManagerOps>(sp => new ServiceManagerOps(sp.GetRequiredService<ServiceManager>()));

        // 检查点 B：静态单例收敛第一例 —— Instance（构造私有，天然单例）注册进容器，
        // 容器与静态引用为同一实例；容器外静态直引按 spec Out of Scope 逐票迁移。
        services.AddSingleton<LocalizationService>(_ => LocalizationService.Instance);

        // FormatRulesStore 注册使容器可完整解析 MainWindowViewModel 构造（与 VM 可选参数默认值同一 config 目录）。
        services.AddSingleton<FormatRulesStore>(_ => new FormatRulesStore(Path.Combine(AppContext.BaseDirectory, "config")));
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private static BackgroundUiOptions BuildRuntimeOptions(
        AppConfig config,
        string configPath,
        string? workerPath,
        WorkerIpcClient workerIpc,
        WorkerProcessManager workerManager,
        ServiceManager serviceManager)
    {
        // 票20 检查点 C：一次性构建不可变快照；委托在调用时读取 App.RuntimeOptions 的当前值
        //（与原实现闭包读取同一共享实例语义一致）。
        var options = new BackgroundUiOptions
        {
            WorkerExecutablePath = workerPath ?? string.Empty,
            ConfigPath = configPath,
            AuditDirectory = config.Audit.LogDirectory,
            GetServiceRuntimeState = serviceManager.GetRuntimeState,
            IsWorkerAliveAsync = token => workerIpc.IsAliveAsync(App.RuntimeOptions.WorkerEndpointName, token),
            IsPausedAsync = async token =>
            {
                var res = await workerIpc.GetStatusAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
                return res?.Data?.IsPaused ?? false;
            },
            PauseAsync = async token =>
            {
                await workerIpc.PauseAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
            },
            ResumeAsync = async token =>
            {
                await workerIpc.ResumeAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
            },
            ShutdownWorkerAsync = async token =>
            {
                if (string.Equals(App.RuntimeOptions.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase))
                {
                    await workerIpc.ShutdownAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
                }
            },
            ExitApplicationAsync = async token =>
            {
                if (string.Equals(App.RuntimeOptions.RuntimeKind, "tray", StringComparison.OrdinalIgnoreCase))
                {
                    await workerIpc.ShutdownAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
                }
            },
            GetExifToolVersionAsync = async token =>
            {
                var res = await workerIpc.GetStatusAsync(App.RuntimeOptions.WorkerEndpointName, token).ConfigureAwait(false);
                return res?.Data?.ExifToolVersion ?? "unknown";
            },
            ConnectOrLaunchWorkerAsync = token => workerManager.ConnectOrLaunchAsync(
                workerPath,
                token,
                getServiceRuntimeState: serviceManager.GetRuntimeState,
                configPath: configPath)
        };
        options.UpdateRuntimeState("tray", WorkerIpcEndpointNames.BackgroundPipe, !config.Ui.HideTrayIcon);
        options.UpdateHideFlags(config.Ui.HideMainWindowOnStartup, config.Ui.HideTrayIcon);
        return options;
    }
}
