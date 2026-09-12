using PhotoPrivacy.Core.Pipeline;

namespace PhotoPrivacy.Core.Tests.Pipeline;

public sealed class LocalFileOperationsTests
{
    [Fact]
    public void Move_Should_Fallback_To_Copy_And_Delete_When_Primary_Move_Throws_IOException()
    {
        var copied = false;
        var deleted = false;

        var ops = new LocalFileOperations(
            move: (_, _) => throw new IOException("cross-volume"),
            copy: (_, _, overwrite) =>
            {
                copied = overwrite;
            },
            delete: _ => deleted = true,
            ensureDirectory: _ => { });

        ops.Move(@"D:\hot\a.jpg", @"D:\clean\a.jpg");

        Assert.True(copied);
        Assert.True(deleted);
    }
}
