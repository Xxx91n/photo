namespace PhotoPrivacy.Core.Pipeline;

public interface IFileOperations
{
    void EnsureDirectory(string path);

    void Move(string source, string destination);

    void Copy(string source, string destination, bool overwrite);
}
