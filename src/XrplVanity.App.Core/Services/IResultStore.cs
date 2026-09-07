using XrplVanity.App.Core.Storage;

namespace XrplVanity.App.Core.Services;

public interface IResultStore
{
    bool IsReadOnly { get; }
    string? ReadOnlyReason { get; }
    IReadOnlyList<StoredResult> LoadAll();
    void Append(StoredResult result);

    /// <summary>
    /// Appends a whole batch as a single write. The match callback runs inside the search's
    /// global lock, so rewriting the store once per hit throttles every worker to disk speed;
    /// the view model collects hits and flushes them through this method on the progress tick.
    /// </summary>
    void AppendRange(IEnumerable<StoredResult> results);
    void Delete(string address);

    /// <summary>
    /// Removes every named address in one rewrite. The store re-encrypts and rewrites the whole
    /// file on each change, so deleting a selection through <see cref="Delete"/> would leave one
    /// crash window per row; this leaves one for the batch.
    /// </summary>
    void DeleteMany(IEnumerable<string> addresses);
    void Clear();
}
