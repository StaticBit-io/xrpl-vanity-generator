using XrplVanity.App.Core.Storage;

namespace XrplVanity.App.Core.Services;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
