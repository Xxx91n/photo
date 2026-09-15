using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 08（ui-craft2 / D-007 / A-003 / 规范 §6 升现役）source-lint：
/// - toast 宿主必须挂在 MainWindow 壳层根 Grid 末位（应用级右下锚定，跨页面恒定）
/// - ToastService 必须维持 sonner 堆叠契约（上限 4 / 默认 4s / 14px+0.05 每层 / 同 key 去重）
/// - toast 卡片必须走 AppTheme Border.toast-card 四型语义浅底 + TransformOperationsTransition 可中断过渡
/// - toast 容器不得抢焦点且必须 hover 驱动展开（规范 §6 T5/T6）
/// - toast 文案必须十语言本地化（toast.* 键存在于全部 locale JSON）
/// </summary>
public sealed class ToastSourceTests
{
    private static string UiPath(params string[] segs) => Path.Combine(new[] { SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui" }.Concat(segs).ToArray());

    private static string ReadAll(string path) => File.ReadAllText(path, Encoding.UTF8);

    [Fact]
    public void ToastHost_Must_Be_Mounted_In_MainWindow_Shell_Root()
    {
        // R1（票 08）：防 toast 宿主被移出壳层根（挪进页面/弹层会导致跨页面不恒定、
        // 被标题栏/分割栏遮挡或随页面切换失锚）——规范 §6 T2 应用级右下锚定的结构证据。
        var shell = ReadAll(UiPath("Views", "MainWindow.axaml"));
        Assert.Contains("<controls:ToastHost", shell, StringComparison.Ordinal);
        Assert.Contains("Grid.RowSpan=\"2\"", shell, StringComparison.Ordinal);
        Assert.Contains("HorizontalAlignment=\"Right\"", shell, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Bottom\"", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void ToastService_Must_Enforce_Sonner_Stack_Contract()
    {
        // R1（票 08）：防堆叠契约退化 —— 上限被改、时长被改、层深步进被改、
        // 同 key 去重链被删（规范 §6 T2/T6/T7 定值的服务侧证据）。
        var svc = ReadAll(UiPath("Services", "ToastService.cs"));
        Assert.Contains("MaxVisible = 4", svc, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(4)", svc, StringComparison.Ordinal);
        Assert.Contains("StackOffsetPerLayer = 14", svc, StringComparison.Ordinal);
        Assert.Contains("StackWidthStep = 18", svc, StringComparison.Ordinal);
        Assert.Contains("_byKey", svc, StringComparison.Ordinal);
        Assert.Contains("SetExpanded", svc, StringComparison.Ordinal);
        Assert.Contains("PauseAutoDismiss", svc, StringComparison.Ordinal);
    }

    [Fact]
    public void ToastCards_Must_Use_Semantic_Light_Palette_And_Interruptible_Transition()
    {
        // R1（票 08）：防 rich-colors 语义浅底被改回硬编码/单底色、防可中断过渡被换成
        // keyframe Animation（规范 §6 T4/T7 的样式侧证据；Transition 从当前值续动才可中断，
        // Avalonia 12.1.1 无 TransformOperationsTransition 故用 ThicknessTransition/DoubleTransition）。
        var theme = ReadAll(UiPath("Styling", "AppTheme.axaml"));
        foreach (var kind in new[] { "success", "info", "warning", "error" })
        {
            Assert.Contains($"Border.toast-card.{kind}", theme, StringComparison.Ordinal);
        }
        foreach (var res in new[] { "SemiColorSuccessLight", "SemiColorInformationLight", "SemiColorWarningLight", "SemiColorDangerLight" })
        {
            Assert.Contains(res, theme, StringComparison.Ordinal);
        }
        Assert.Contains("ThicknessTransition", theme, StringComparison.Ordinal);
        Assert.Contains("Elevation4", theme, StringComparison.Ordinal);
        Assert.Contains("RadiusLg", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void ToastHost_Must_Not_Steal_Focus_And_Must_Be_Hover_Driven()
    {
        // R1（票 08）：防容器/卡片抢焦点或改模态、防 hover 展开链被删（规范 §6 T5/T6）；
        // Popup 承载被禁（焦点/命中测试坑，atomcode 调研落地要点）。
        var host = ReadAll(UiPath("Views", "Controls", "ToastHost.axaml"));
        Assert.Contains("Focusable=\"False\"", host, StringComparison.Ordinal);
        Assert.Contains("PointerEntered", host, StringComparison.Ordinal);
        Assert.Contains("PointerExited", host, StringComparison.Ordinal);
        Assert.DoesNotContain("<Popup", host, StringComparison.Ordinal);
        var code = ReadAll(UiPath("Views", "Controls", "ToastHost.axaml.cs"));
        Assert.Contains("SetExpanded", code, StringComparison.Ordinal);
        Assert.Contains("Dismiss", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Toast_Feedback_Must_Be_Localized_In_All_Locales()
    {
        // R1（票 08）：防 toast 文案硬编码/漏译 —— toast 节点与其 10 个子键必须存在于
        // 10 个 locale JSON（规范 §6 T4「各 10 语言文案」；flat-key 对等由既有
        // All_Locales_Have_Identical_Flat_Key_Sets 守卫覆盖全集，本断言钉 toast 组存在性）。
        var localeDir = UiPath("Localization", "Locales");
        var required = new[]
        {
            "config_saved", "config_saved_worker_down", "config_save_failed",
            "rules_saved", "rules_reset",
            "service_success", "service_failed", "service_cancelled", "service_skipped",
            "dismiss",
        };
        foreach (var file in Directory.GetFiles(localeDir, "*.json"))
        {
            var json = ReadAll(file);
            Assert.Contains("\"toast\"", json, StringComparison.Ordinal);
            foreach (var key in required)
            {
                Assert.Contains($"\"{key}\"", json, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Toast_Wiring_Must_Cover_Config_Rules_And_Service_Feedback_Points()
    {
        // R1（票 08）：防反馈链接线被拆 —— 三处既有反馈点必须仍调用 toast（规范 §6 T1）：
        // 配置防抖保存（MainWindow）、规则保存/重置（RulesPanelViewModel）、服务操作（ServiceModeController）。
        var window = ReadAll(UiPath("Views", "MainWindow.axaml.cs"));
        Assert.Contains("_toastService.Show", window, StringComparison.Ordinal);
        Assert.Contains("toast.config_saved", window, StringComparison.Ordinal);
        Assert.Contains("toast.config_save_failed", window, StringComparison.Ordinal);
        var rules = ReadAll(UiPath("ViewModels", "RulesPanelViewModel.cs"));
        Assert.Contains("toast.rules_saved", rules, StringComparison.Ordinal);
        Assert.Contains("toast.rules_reset", rules, StringComparison.Ordinal);
        var controller = ReadAll(UiPath("Services", "ServiceModeController.cs"));
        Assert.Contains("toast.service_success", controller, StringComparison.Ordinal);
        Assert.Contains("toast.service_failed", controller, StringComparison.Ordinal);
        var composition = ReadAll(UiPath("Composition", "AppComposition.cs"));
        Assert.Contains("AddSingleton<ToastService>", composition, StringComparison.Ordinal);
    }
}
