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
}

public sealed class MemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; private set; } = new();
    public int Saves { get; private set; }
    public AppSettings Load() => Current;
    public void Save(AppSettings settings) { Current = settings; Saves++; }
}

public sealed class MemoryResultStore : IResultStore
{
    private readonly List<StoredResult> _items = new();
    public bool IsReadOnly { get; init; }
    public string? ReadOnlyReason { get; init; }
    public IReadOnlyList<StoredResult> LoadAll() { lock (_items) return _items.ToArray(); }
    public void Append(StoredResult result) { lock (_items) _items.Add(result); }
    public void Delete(string address) { lock (_items) _items.RemoveAll(r => r.Address == address); }
    public void Clear() { lock (_items) _items.Clear(); }
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
