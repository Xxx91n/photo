using System.Reflection;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class UiAvailabilityTests
{
    [Fact]
    public void UiProgram_Type_Should_Be_Resolvable()
    {
        var type = Type.GetType("PhotoPrivacy.Ui.UiProgram, PhotoPrivacy.Ui", throwOnError: false);

        Assert.NotNull(type);
    }

    [Fact]
    public void LegacyCliAssembly_Should_Not_Be_Resolvable_After_Split()
    {
        var type = Type.GetType("PhotoPrivacy.Cli.RuntimeMode, PhotoPrivacy.Cli", throwOnError: false);

        Assert.Null(type);
    }

    [Fact]
    public void UiProgram_Source_Should_Not_Enable_Trace_In_Release_By_Default()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Program.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("#if DEBUG", source, StringComparison.Ordinal);
        Assert.Contains(".LogToTrace();", source, StringComparison.Ordinal);
    }
}
