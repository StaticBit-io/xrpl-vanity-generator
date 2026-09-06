using System.Text.Json;
using System.Text.Json.Serialization;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Core.Storage;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public JsonSettingsStore(string folder)
    {
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public string? LastError { get; private set; }

    /// <summary>
    /// Settings are written from Blazor event handlers and from the session-completion
    /// continuation; a read-only data folder there would fault the continuation and strand the
    /// view model in <c>Searching</c> with a dead session. A failed write is recorded, not thrown.
    /// </summary>
    public void Save(AppSettings settings)
    {
        try
        {
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, _path, overwrite: true);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LastError = $"settings.json could not be written: {ex.Message}";
        }
    }
}
