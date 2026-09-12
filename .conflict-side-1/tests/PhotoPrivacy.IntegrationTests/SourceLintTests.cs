namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// 票 07 — 共享 SourceLint helper 自证：仓库根定位、源码读取、剥注释与枚举过滤语义锁定。
/// </summary>
public sealed class SourceLintTests
{
    [Fact]
    public void RepoRoot_Should_Contain_Solution_File()
        => Assert.True(File.Exists(Path.Combine(SourceLint.RepoRoot, "PhotoPrivacy.sln")), SourceLint.RepoRoot);

    [Fact]
    public void UiSourceRoot_Should_Point_At_Ui_Project()
        => Assert.True(File.Exists(Path.Combine(SourceLint.UiSourceRoot, "PhotoPrivacy.Ui.csproj")), SourceLint.UiSourceRoot);

    [Fact]
    public void Read_Should_Return_Ui_Program_Source()
        => Assert.Contains("UiProgram", SourceLint.Read("src", "PhotoPrivacy.Ui", "Program.cs"), StringComparison.Ordinal);

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
        var raw = SourceLint.Read("src", "PhotoPrivacy.Ui", "Program.cs");
        var stripped = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Program.cs");
        Assert.True(stripped.Length <= raw.Length, "stripped must not be longer than raw");
    }

    [Fact]
    public void UiCsFiles_Should_Exclude_Generated_Code()
    {
        var files = SourceLint.UiCsFiles();
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.False(f.EndsWith(".g.cs"), f));
    }
}
