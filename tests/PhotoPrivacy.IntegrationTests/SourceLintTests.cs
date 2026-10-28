namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// 共享 SourceLint helper 自证：仓库根定位、源码读取、剥注释语义锁定。
/// 旧 Ui 工程退役（#61）后，源文件样本统一取 Worker 侧。
/// </summary>
public sealed class SourceLintTests
{
    [Fact]
    public void Read_Should_Return_Worker_Program_Source()
        => Assert.Contains("PhotoPrivacyWorker", SourceLint.Read("src", "PhotoPrivacy.Worker", "Program.cs"), StringComparison.Ordinal);

    [Theory]
    [InlineData("var a = 1; // trailing", "var a = 1; ")]
    [InlineData("no comment", "no comment")]
    [InlineData("/// doc", "")]
    [InlineData("// full line", "")]
    public void StripLineComment_Should_Remove_Only_Comment_Part(string line, string expected)
        => Assert.Equal(expected, SourceLint.StripLineComment(line));

    [Fact]
    public void ReadStripped_Should_Not_Expand_Source()
    {
        // 以真实源文件为样本：剥注释产物必须不长于原文（逐行剥除后拼接语义保持）。
        var raw = SourceLint.Read("src", "PhotoPrivacy.Worker", "Program.cs");
        var stripped = SourceLint.ReadStripped("src", "PhotoPrivacy.Worker", "Program.cs");
        Assert.True(stripped.Length <= raw.Length, "stripped must not be longer than raw");
    }
}
