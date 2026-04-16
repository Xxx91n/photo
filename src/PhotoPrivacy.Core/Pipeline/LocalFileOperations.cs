namespace PhotoPrivacy.Core.Pipeline;

public sealed class LocalFileOperations : IFileOperations
{
    public void EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public void Move(string source, string destination)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        File.Move(source, destination);
    }

    public void Copy(string source, string destination, bool overwrite)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
        File.Copy(source, destination, overwrite);
    }
}
