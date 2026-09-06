using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Core.Storage;

/// <summary>
/// The only file that holds seeds. A UTF-8 JSON array protected with DPAPI for the current
/// Windows user; rewritten as a whole on every change (histories are small). A file that cannot
/// be unprotected or parsed makes the store read-only for the process lifetime rather than
/// letting a later append overwrite what might still be recoverable.
/// </summary>
/// <remarks>
/// Not annotated with <c>[SupportedOSPlatform("windows")]</c>: that would push CA1416 onto every
/// caller, including the callers of <see cref="IResultStore"/> that are meant to stay
/// platform-agnostic. The DPAPI calls below are the only Windows-only surface in this type; the
/// pragma suppressions are scoped to exactly those call sites.
/// </remarks>
public sealed class DpapiResultStore : IResultStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("io.staticbit.vanity");
    private readonly string _path;
    private readonly object _gate = new();
    private List<StoredResult>? _cache;

    public DpapiResultStore(string folder)
    {
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "results.dat");
    }

    public bool IsReadOnly { get; private set; }
    public string? ReadOnlyReason { get; private set; }

    public IReadOnlyList<StoredResult> LoadAll()
    {
        lock (_gate)
        {
            return EnsureLoaded().ToArray();
        }
    }

    public void Append(StoredResult result)
    {
        lock (_gate)
        {
            List<StoredResult> current = EnsureLoaded();
            ThrowIfReadOnly();
            List<StoredResult> updated = new(current) { result };
            Write(updated);
            _cache = updated;
        }
    }

    /// <summary>One encrypt-and-rewrite for the whole batch, not one per result.</summary>
    public void AppendRange(IEnumerable<StoredResult> results)
    {
        List<StoredResult> batch = results.ToList();
        if (batch.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            List<StoredResult> current = EnsureLoaded();
            ThrowIfReadOnly();
            List<StoredResult> updated = new(current.Count + batch.Count);
            updated.AddRange(current);
            updated.AddRange(batch);
            Write(updated);
            _cache = updated;
        }
    }

    public void Delete(string address)
    {
        lock (_gate)
        {
            List<StoredResult> current = EnsureLoaded();
            ThrowIfReadOnly();
            List<StoredResult> updated = current.Where(r => !string.Equals(r.Address, address, StringComparison.Ordinal)).ToList();
            Write(updated);
            _cache = updated;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            EnsureLoaded();
            ThrowIfReadOnly();
            List<StoredResult> updated = new List<StoredResult>();
            Write(updated);
            _cache = updated;
        }
    }

    private List<StoredResult> EnsureLoaded()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(_path))
        {
            _cache = new List<StoredResult>();
            return _cache;
        }

        try
        {
#pragma warning disable CA1416 // Windows-only by design; RequireWindows() skips the covering tests elsewhere.
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            _cache = JsonSerializer.Deserialize<List<StoredResult>>(plain) ?? new List<StoredResult>();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            IsReadOnly = true;
            ReadOnlyReason = $"results.dat could not be read: {ex.Message}";
            _cache = new List<StoredResult>();
        }

        return _cache;
    }

    private void ThrowIfReadOnly()
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException(ReadOnlyReason);
        }
    }

    private void Write(List<StoredResult> all)
    {
        try
        {
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(all);
#pragma warning disable CA1416 // Windows-only by design; RequireWindows() skips the covering tests elsewhere.
            byte[] protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
            string temp = _path + ".tmp";
            File.WriteAllBytes(temp, protectedBytes);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            IsReadOnly = true;
            ReadOnlyReason = $"results.dat could not be written: {ex.Message}";
            throw new InvalidOperationException(ReadOnlyReason, ex);
        }
    }
}
