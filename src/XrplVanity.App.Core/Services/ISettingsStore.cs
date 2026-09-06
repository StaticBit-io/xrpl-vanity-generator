using XrplVanity.App.Core.Storage;

namespace XrplVanity.App.Core.Services;

public interface ISettingsStore
{
    /// <summary>The reason the last <see cref="Save"/> failed, or null when it succeeded.</summary>
    string? LastError { get; }

    AppSettings Load();

    /// <summary>Never throws: a failed settings write must not fault the caller that triggered it.</summary>
    void Save(AppSettings settings);
}
