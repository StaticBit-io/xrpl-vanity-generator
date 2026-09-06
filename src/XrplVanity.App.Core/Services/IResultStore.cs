using XrplVanity.App.Core.Storage;

namespace XrplVanity.App.Core.Services;

public interface IResultStore
{
    bool IsReadOnly { get; }
    string? ReadOnlyReason { get; }
    IReadOnlyList<StoredResult> LoadAll();
    void Append(StoredResult result);
    void Delete(string address);
    void Clear();
}
