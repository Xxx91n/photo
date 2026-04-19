using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ExifToolVersionSnapshotTests
{
    [Fact]
    public void ReadInitial_Should_Default_To_Unknown_When_Source_Is_Blank()
    {
        var snapshot = new ExifToolVersionSnapshot(() => "  ");

        var version = snapshot.ReadInitial();

        Assert.Equal("unknown", version);
    }

    [Fact]
    public void TryReadChanged_Should_Return_New_Value_When_Source_Changes()
    {
        var current = "unknown";
        var snapshot = new ExifToolVersionSnapshot(() => current);
        _ = snapshot.ReadInitial();

        current = "13.40";
        var changed = snapshot.TryReadChanged();

        Assert.Equal("13.40", changed);
    }

    [Fact]
    public void TryReadChanged_Should_Return_Null_When_Value_Unchanged()
    {
        var snapshot = new ExifToolVersionSnapshot(() => "13.40");
        _ = snapshot.ReadInitial();

        var changed = snapshot.TryReadChanged();

        Assert.Null(changed);
    }
}
