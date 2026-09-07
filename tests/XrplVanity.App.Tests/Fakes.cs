using XrplVanity.App.Core.Services;
using XrplVanity.App.Core.Storage;

namespace XrplVanity.App.Tests;

public sealed class FakeNotifications : INotificationService
{
    public List<(string Title, string Body)> Sent { get; } = new();
    public bool IsAvailable => true;
    public void Notify(string title, string body) { lock (Sent) Sent.Add((title, body)); }
}

public sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; private set; }
    public TimeSpan? ClearAfter { get; private set; }
    public Task CopyAsync(string text) { Text = text; ClearAfter = null; return Task.CompletedTask; }
    public Task CopySecretAsync(string text, TimeSpan clearAfter) { Text = text; ClearAfter = clearAfter; return Task.CompletedTask; }

    public Task ClearPendingSecretAsync()
    {
        ClearPendingSecretCalls++;
        Text = null;
        ClearAfter = null;
        return Task.CompletedTask;
    }

    public int ClearPendingSecretCalls { get; private set; }
}

public sealed class MemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; private set; } = new();
    public int Saves { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>When set, every <see cref="Save"/> fails the way a read-only data folder would.</summary>
    public bool FailSaves { get; init; }

    /// <summary>When set, <see cref="Save"/> throws — a store that breaks the non-throwing contract.</summary>
    public bool ThrowOnSave { get; init; }

    public AppSettings Load() => Current;

    public void Save(AppSettings settings)
    {
        if (ThrowOnSave)
        {
            throw new IOException("settings.json is read-only");
        }

        if (FailSaves)
        {
            LastError = "settings.json could not be written";
            return;
        }

        Current = settings;
        Saves++;
        LastError = null;
    }
}

public sealed class MemoryResultStore : IResultStore
{
    private readonly List<StoredResult> _items = new();
    private int _appends;
    private int _appendRanges;
    private int _deleteManyCalls;

    public bool IsReadOnly { get; init; }
    public string? ReadOnlyReason { get; init; }

    /// <summary>When set, every write fails the way a read-only <c>results.dat</c> would. Settable so a test can seed rows first.</summary>
    public bool ThrowOnWrite { get; set; }

    /// <summary>Single-result appends: a batched search must not perform any.</summary>
    public int Appends => Volatile.Read(ref _appends);

    /// <summary>Batch appends.</summary>
    public int AppendRanges => Volatile.Read(ref _appendRanges);

    /// <summary>Bulk deletes: the History page must take this path, not a loop over Delete.</summary>
    public int DeleteManyCalls => Volatile.Read(ref _deleteManyCalls);

    public IReadOnlyList<StoredResult> LoadAll() { lock (_items) return _items.ToArray(); }

    public void Append(StoredResult result)
    {
        Interlocked.Increment(ref _appends);
        ThrowIfBroken();
        lock (_items) _items.Add(result);
    }

    public void AppendRange(IEnumerable<StoredResult> results)
    {
        Interlocked.Increment(ref _appendRanges);
        ThrowIfBroken();
        lock (_items) _items.AddRange(results);
    }

    public void Delete(string address)
    {
        ThrowIfBroken();
        lock (_items) _items.RemoveAll(r => r.Address == address);
    }

    public void DeleteMany(IEnumerable<string> addresses)
    {
        Interlocked.Increment(ref _deleteManyCalls);
        HashSet<string> doomed = new(addresses, StringComparer.Ordinal);
        ThrowIfBroken();
        lock (_items) _items.RemoveAll(r => doomed.Contains(r.Address));
    }

    public void Clear()
    {
        ThrowIfBroken();
        lock (_items) _items.Clear();
    }

    private void ThrowIfBroken()
    {
        if (ThrowOnWrite)
        {
            throw new InvalidOperationException(ReadOnlyReason ?? "results.dat could not be written");
        }
    }
}

public sealed class FakeFileDialog : IFileDialogService
{
    public string? PathToReturn { get; set; }
    public Task<string?> PickSaveTsvAsync(string suggestedName) => Task.FromResult(PathToReturn);
}

public sealed class FakeWindowService : IWindowService
{
    public bool HideToTrayCalled { get; private set; }
    public bool ExitCalled { get; private set; }
    public string DataFolder => "C:\\data";

    public event Action? CloseRequested;

    /// <summary>Number of live subscribers on <see cref="CloseRequested"/> — used to confirm a disposed subscriber unhooked.</summary>
    public int SubscriberCount => CloseRequested?.GetInvocationList().Length ?? 0;

    public void ShowWindow() { }
    public void HideToTray() => HideToTrayCalled = true;
    public void Exit() => ExitCalled = true;
    public void OpenDataFolder() { }
    public void RaiseCloseRequested() => CloseRequested?.Invoke();
}
