namespace PhotoPrivacy.Core.Pipeline;

public interface IProcessedRecordStore
{
    bool IsProcessed(string key);
    void MarkProcessed(string key);
}
