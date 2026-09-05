using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 29（架构恢复第七轮）组合根 + DI 容器骨架 source-lint（票 31 追加轮询宿主服务断言）：
/// - AppComposition 为唯一装配点（ServiceCollection 单例注册 + BuildServiceProvider）
/// - Program.Start 在应用启动处调用组合根装配，不再手写服务依赖链
/// - App 解析 MainWindow 自容器（不再手写 new MainWindowViewModel）
/// - MainWindow/MainWindowViewModel 构造注入（可选参数默认值保留无参兼容）
/// - 检查点 B：LocalizationService.Instance 注册进容器，ViewModel 静态直引收敛为构造注入 _localization
/// </summary>
public sealed class CompositionRootSourceTests
{
    [Fact]
    public void CompositionRoot_Should_Register_Core_Services_As_Singletons()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Composition", "AppComposition.cs");
        Assert.Contains("new ServiceCollection()", source, StringComparison.Ordinal);
        Assert.Contains("BuildServiceProvider()", source, StringComparison.Ordinal);
        foreach (var svc in new[]
                 {
                     "AddSingleton<WorkerIpcClient>",
                     "AddSingleton<WorkerProcessManager>",
                     "AddSingleton<ServiceManager>",
                     "AddSingleton<IServiceManagerOps>",
                     "AddSingleton<FormatRulesStore>",
                     "AddSingleton<LocalizationService>",
                     "AddSingleton<MainWindowViewModel>",
                     "AddSingleton<MainWindow>",
                     "WindowPollingHostedService("
                 })
        {
            Assert.Contains(svc, source, StringComparison.Ordinal);
        }

        // 检查点 B：容器内注册的就是既有单例实例（构造私有），容器外静态直引按 spec Out of Scope 逐票迁移
        Assert.Contains("LocalizationService.Instance", source, StringComparison.Ordinal);

        // 票 31：轮询宿主服务经工厂注册（只依赖 VM 单例破 DI 环，版本源惰性读 App.RuntimeOptions）
        Assert.Contains("new WindowPollingHostedService(", source, StringComparison.Ordinal);
        Assert.Contains("() => App.RuntimeOptions.GetExifToolVersionAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_Start_Should_Assemble_Container_At_Composition_Root()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Program.cs");
        Assert.Contains("AppComposition.Build(", source, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<BackgroundUiOptions>", source, StringComparison.Ordinal);
        // ADR 0053 M1 两阶段启动语义不被本票改变（AppStartupPolicyTests 延续）
        Assert.Contains("StartWithClassicDesktopLifetime", source, StringComparison.Ordinal);
        // 运行时选项构建收口组合根（Program 不再持有装配逻辑）
        Assert.DoesNotContain("BuildRuntimeOptions(", source, StringComparison.Ordinal);
        // 组合根外禁止手写核心服务 new（防回潮）
        Assert.DoesNotContain("new WorkerIpcClient(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new WorkerProcessManager(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ServiceManager()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_Should_Resolve_MainWindow_From_Container()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "App.axaml.cs");
        Assert.Contains("GetRequiredService<MainWindow>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext = new MainWindowViewModel()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Should_Take_Dependencies_By_Constructor()
    {
        var source = SourceLint.Read("src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        Assert.Contains("public MainWindow(", source, StringComparison.Ordinal);
        Assert.Contains("MainWindowViewModel viewModel", source, StringComparison.Ordinal);
        Assert.Contains("IServiceManagerOps serviceManagerOps", source, StringComparison.Ordinal);
        // 手写 new 依赖链不再回潮（组合根持有装配；窗口只落位自身视图缝）
        Assert.DoesNotContain("new ServiceManagerOps(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new WorkerProcessManager(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new WorkerIpcClient(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateServiceModeController", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowViewModel_Should_Receive_Localization_By_Constructor()
    {
        // 检查点 B：VM 静态单例直引收敛为构造注入 _localization；
        // 允许且仅允许构造默认回落一处 Instance（容器与 Instance 同一实例，行为不变）。
        var stripped = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "ViewModels", "MainWindowViewModel.cs");
        var instanceRefs = Regex.Matches(stripped, "LocalizationService.Instance").Count;
        Assert.True(instanceRefs == 1,
            $"expected exactly 1 LocalizationService.Instance fallback in MainWindowViewModel, got {instanceRefs}");
        Assert.Contains("private readonly LocalizationService _localization;", stripped, StringComparison.Ordinal);
        Assert.Contains("public MainWindowViewModel(LocalizationService? localization = null", stripped, StringComparison.Ordinal);
        Assert.Contains("_localization.Get(", stripped, StringComparison.Ordinal);
    }
}
