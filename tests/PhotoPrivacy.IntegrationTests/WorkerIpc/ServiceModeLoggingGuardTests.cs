namespace PhotoPrivacy.IntegrationTests.WorkerIpc;

/// <summary>
/// 回潮守卫（#85）：服务进程不得保留 <c>AddWindowsService</c> 隐式登记的 EventLog provider。
/// 服务会话下该 provider 写失败（<c>Cannot open log for source</c>，Win32 1722）会让 MEL
/// <c>Logger.Log</c> 直接抛 <c>AggregateException</c>；ReloadConfig 的 catch 链每层都在打日志，
/// 于是异常炸穿 IPC 响应路径 → 服务管道 ReloadConfig 挂起。
///
/// 摘除手段是「<c>AddWindowsService</c> 之后紧跟 <c>builder.Logging.ClearProviders()</c>」，
/// 因此断言必须校验**顺序**：MEL 的 <c>AddFilter(string, …)</c> 首参是类别前缀而非 provider 名
/// （net10 参考程序集无 provider-name 重载），靠类别过滤摘不掉这个 provider。
/// </summary>
public sealed class ServiceModeLoggingGuardTests
{
    private const string ClearProvidersToken = "builder.Logging.ClearProviders()";
    private const string AddWindowsServiceToken = "AddWindowsService";

    internal static List<string> ClassifyServiceLogging(string strippedProgramWithLineBreaks)
    {
        var violations = new List<string>();
        var windowsServiceIndex = strippedProgramWithLineBreaks.IndexOf(AddWindowsServiceToken, StringComparison.Ordinal);
        var clearProvidersIndex = strippedProgramWithLineBreaks.IndexOf(ClearProvidersToken, StringComparison.Ordinal);

        if (windowsServiceIndex < 0)
        {
            violations.Add("Program.cs 不再注册 AddWindowsService：守卫前提失效，需复审本守卫");
        }

        if (clearProvidersIndex < 0)
        {
            violations.Add(
                "Program.cs 缺少 builder.Logging.ClearProviders()：AddWindowsService 隐式挂载的 EventLog provider 会留在服务进程里（#85 挂起形态可回潮）");
        }
        else if (windowsServiceIndex >= 0 && clearProvidersIndex < windowsServiceIndex)
        {
            violations.Add(
                "ClearProviders 排在 AddWindowsService 之前：EventLog provider 会在其后重新登记，等于未摘除（#85）");
        }

        if (strippedProgramWithLineBreaks.Contains("AddEventLog", StringComparison.Ordinal))
        {
            violations.Add("Program.cs 直接登记 EventLog provider：ClearProviders 之后重新挂载同样会让服务会话抛异常（#85）");
        }

        return violations;
    }

    [Fact]
    public void Service_Mode_Should_Drop_EventLog_Provider_After_Registering_WindowsService()
    {
        // 防的具体回潮形态：① 删掉 ClearProviders（EventLog provider 随 AddWindowsService 重新挂上）；
        // ② 把 ClearProviders 挪到 AddWindowsService 之前（同样留活口）；③ 之后又显式
        // AddEventLog；④ 换成 AddFilter("EventLog", …) —— 该重载首参是类别前缀，摘不掉 provider。
        // ①–③ 由本断言直接判红；④ 由上面的类型/重载说明与自证样本兜住。关联 issue #85。
        var violations = ClassifyServiceLogging(ReadStrippedProgramWithLineBreaks());
        Assert.True(violations.Count == 0, string.Join("；", violations));
    }

    [Fact]
    public void Service_Logging_Guard_Negative_Self_Proof()
    {
        // 失效即红自证：绿态必须零违规；删掉 ClearProviders、把它挪到 AddWindowsService 之前、
        // 或在其后重新 AddEventLog，分类器都必须报红——防「摘除被删而守卫仍绿」的恒绿事故。
        // 关联 issue #85。
        var green = ReadStrippedProgramWithLineBreaks();
        Assert.Empty(ClassifyServiceLogging(green));

        var lines = green.Split('\n');
        var withoutClear = string.Join(
            "\n",
            lines.Where(line => !line.Contains(ClearProvidersToken, StringComparison.Ordinal)));
        Assert.NotEmpty(ClassifyServiceLogging(withoutClear));

        var clearLine = lines.First(line => line.Contains(ClearProvidersToken, StringComparison.Ordinal));
        Assert.NotEmpty(ClassifyServiceLogging(clearLine + "\n" + withoutClear));

        Assert.NotEmpty(ClassifyServiceLogging(green + "\nbuilder.Logging.AddEventLog();"));
    }

    /// <summary>
    /// <c>SourceLint.ReadStripped</c> 逐行剥注释后 <c>string.Concat</c>，产物不含换行——用于
    /// 「存在性」断言足够，但本守卫要判**顺序**并对样本做增删，必须保留行结构，故在此按行拼接。
    /// </summary>
    private static string ReadStrippedProgramWithLineBreaks()
        => string.Join(
            "\n",
            SourceLint.Read("src", "PhotoPrivacy.Worker", "Program.cs")
                .Split('\n')
                .Select(line => SourceLint.StripLineComment(line.TrimEnd('\r'))));
}
