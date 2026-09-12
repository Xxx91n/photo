using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ExifToolVersionSnapshotTests
{
    // ADR 0053 M2: ExifToolVersionSnapshot now uses async delegate (Func<CancellationToken, Task<string>>)
    // ReadInitial removed (never called in production); TryReadChanged replaced by TryReadChangedAsync.

    [Fact]
    public async Task TryReadChangedAsync_Should_Default_To_Unknown_When_Source_Is_Blank()
    {
        var snapshot = new ExifToolVersionSnapshot(_ => Task.FromResult("  "));

        var changed = await snapshot.TryReadChangedAsync(CancellationToken.None);

        Assert.Equal("unknown", changed);
    }

    [Fact]
    public async Task TryReadChangedAsync_Should_Return_New_Value_When_Source_Changes()
    {
        var current = "unknown";
        var snapshot = new ExifToolVersionSnapshot(_ => Task.FromResult(current));
        _ = await snapshot.TryReadChangedAsync(CancellationToken.None);

        current = "13.40";
        var changed = await snapshot.TryReadChangedAsync(CancellationToken.None);

        Assert.Equal("13.40", changed);
    }

    [Fact]
    public async Task TryReadChangedAsync_Should_Return_Null_When_Value_Unchanged()
    {
        var snapshot = new ExifToolVersionSnapshot(_ => Task.FromResult("13.40"));
        _ = await snapshot.TryReadChangedAsync(CancellationToken.None);

        var changed = await snapshot.TryReadChangedAsync(CancellationToken.None);

        Assert.Null(changed);
    }

    [Fact]
    public async Task TryReadChangedAsync_Should_Return_Unknown_When_Source_Throws()
    {
        var snapshot = new ExifToolVersionSnapshot(_ => throw new InvalidOperationException("pipe broken"));

        var changed = await snapshot.TryReadChangedAsync(CancellationToken.None);

        Assert.Equal("unknown", changed);
    }
}
