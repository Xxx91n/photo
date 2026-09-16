using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 29（架构恢复第七轮）组合根 + DI 容器骨架 source-lint（票 31 追加轮询宿主服务断言）：
/// - AppComposition 为唯一装配点（ServiceCollection 单例注册 + BuildServiceProvider）
/// - Program.Start 在应用启动处调用组合根装配，不再手写服务依赖链
/// - App 解析 MainWindow 自容器（不再手写 new MainWindowViewModel）
/// - MainWindow/MainWindowViewModel 构造注入（可选参数默认值保留无参兼容）
/// - 检查点 B：LocalizationService.Instance 注册进容器，ViewModel 静态直引收敛为构造注入 _localization
/// - 票 06（correctness-round A-008 / D-003.4）：点状升级为结构性断言——组合根外的服务层
///   手写 new 改用封闭视图缝白名单核验（观测集合 == 登记集合），替代枚举黑名单的开放式漏报
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

    // ---------- 票 06（A-008 / D-003.4）：结构性断言 —— 封闭视图缝白名单 ----------

    /// <summary>
    /// 组合根外允许手写 new 的服务层类型登记（视图缝 / 启动缝）：
    /// Program.cs —— UiSingleInstance：单实例检查必须先于容器装配（AppComposition.Build 之前）。
    /// App.axaml.cs —— 空集：容器内类型一律经解析。
    /// MainWindow.axaml.cs —— 六处视图缝：ServiceModeController（需 MainWindowUiHost /
    /// MainWindowViewModelView 两个绑 this 的适配器，直注会成 MS DI 死环——见
    /// WindowPollingHostedServiceSourceTests 装配破环锁）、AuditTailService 与
    /// ConfigFileWatcher（InitializeRuntime 内依运行时选项与 UI 回调构造）、TrayHost（窗口实例）。
    /// 断言 = 观测集合 == 登记集合（双向钉死：新增未登记者红，缝消失未同步清单亦红）。
    /// </summary>
    private static readonly (string[] Segments, string[] DeclaredSeams)[] SeamContracts =
    [
        (["src", "PhotoPrivacy.Ui", "Program.cs"], ["UiSingleInstance"]),
        (["src", "PhotoPrivacy.Ui", "App.axaml.cs"], []),
        (["src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs"],
         ["ServiceModeController", "MainWindowUiHost", "MainWindowViewModelView",
          "AuditTailService", "TrayHost", "ConfigFileWatcher"]),
    ];

    /// <summary>手写构造识别：new Type( 与 new Qualified.Type( 均命中（捕获末段类型名）。</summary>
    private static readonly Regex NewConstructionPattern =
        new(@"\bnew\s+(?:[A-Z_]\w*\.)*([A-Z]\w*)\s*\(", RegexOptions.Compiled);

    /// <summary>服务层类型宇宙：Services/ 目录声明的全部 class/record/struct 名（手工清单的结构性替代）。</summary>
    private static HashSet<string> ServiceLayerTypeUniverse()
    {
        var dir = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Services");
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(dir, "*.cs"))
        {
            foreach (Match m in Regex.Matches(
                         File.ReadAllText(file), @"(?:class|record|struct)\s+([A-Z]\w*)"))
            {
                types.Add(m.Groups[1].Value);
            }
        }

        return types;
    }

    internal static List<string> ClassifyOutOfCompositionServiceNews(
        string strippedSource,
        IReadOnlySet<string> serviceTypeUniverse,
        IReadOnlySet<string> declaredSeams,
        string fileLabel)
    {
        var observed = NewConstructionPattern.Matches(strippedSource)
            .Select(m => m.Groups[1].Value)
            .Where(serviceTypeUniverse.Contains)
            .ToHashSet(StringComparer.Ordinal);

        var violations = new List<string>();
        foreach (var undeclared in observed.Except(declaredSeams).OrderBy(x => x, StringComparer.Ordinal))
        {
            violations.Add(fileLabel + ": 未登记的服务层手写构造 new " + undeclared +
                           "( —— 迁入 AppComposition 装配，或登记为视图缝（须过评审）");
        }

        foreach (var stale in declaredSeams.Except(observed).OrderBy(x => x, StringComparer.Ordinal))
        {
            violations.Add(fileLabel + ": 已登记视图缝 " + stale +
                           " 在手写构造中不再出现——请同步缝清单（宣称-实现一致）");
        }

        return violations;
    }

    [Fact]
    public void Out_Of_Composition_Service_News_Should_Match_Declared_View_Seams()
    {
        var universe = ServiceLayerTypeUniverse();
        var violations = new List<string>();
        foreach (var (segments, declaredSeams) in SeamContracts)
        {
            var stripped = SourceLint.ReadStripped(segments);
            violations.AddRange(ClassifyOutOfCompositionServiceNews(
                stripped,
                universe,
                declaredSeams.ToHashSet(StringComparer.Ordinal),
                string.Join("/", segments)));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void View_Seam_Guard_Negative_Self_Proof()
    {
        // 失效即红自证：对绿态样本做定向突变，分类器必须报红；边界用例不得误伤。
        var universe = ServiceLayerTypeUniverse();
        var mainWindow = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var program = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Program.cs");
        var mainWindowSeams = SeamContracts[2].DeclaredSeams.ToHashSet(StringComparer.Ordinal);
        var programSeams = SeamContracts[0].DeclaredSeams.ToHashSet(StringComparer.Ordinal);

        var failures = new List<string>();

        var injected = mainWindow + Environment.NewLine + "var leaked = new WorkerProcessManager(null!);";
        if (ClassifyOutOfCompositionServiceNews(injected, universe, mainWindowSeams, "MW").Count == 0)
        {
            failures.Add("MISSED: 注入未登记 new WorkerProcessManager( 未红");
        }

        var shrunk = mainWindowSeams.Where(s => s != "TrayHost").ToHashSet(StringComparer.Ordinal);
        if (ClassifyOutOfCompositionServiceNews(mainWindow, universe, shrunk, "MW").Count == 0)
        {
            failures.Add("MISSED: 缝清单漏登 TrayHost（源仍在手写）未红");
        }

        var removed = mainWindow.Replace("new ConfigFileWatcher(", "ConfigFileWatcher(", StringComparison.Ordinal);
        if (removed == mainWindow)
        {
            failures.Add("MUTATION DID NOT APPLY: new ConfigFileWatcher( 未找到");
        }
        else if (ClassifyOutOfCompositionServiceNews(removed, universe, mainWindowSeams, "MW").Count == 0)
        {
            failures.Add("MISSED: 缝消失（ConfigFileWatcher）未同步清单未红");
        }

        var programRemoved = program.Replace("new UiSingleInstance(", "UiSingleInstance(", StringComparison.Ordinal);
        if (programRemoved == program)
        {
            failures.Add("MUTATION DID NOT APPLY: new UiSingleInstance( 未找到");
        }
        else if (ClassifyOutOfCompositionServiceNews(programRemoved, universe, programSeams, "Program").Count == 0)
        {
            failures.Add("MISSED: Program 启动缝消失未红");
        }

        var benign = mainWindow + Environment.NewLine + "var ok = new DirectoryInfo(\".\");";
        if (ClassifyOutOfCompositionServiceNews(benign, universe, mainWindowSeams, "MW").Count != 0)
        {
            failures.Add("FALSE-POSITIVE: 非服务层 new DirectoryInfo 被误伤");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
