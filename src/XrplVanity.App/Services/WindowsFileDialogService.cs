using Windows.Storage;
using Windows.Storage.Pickers;
using XrplVanity.App.Core.Services;

namespace XrplVanity.App.Services;

public sealed class WindowsFileDialogService : IFileDialogService
{
    private readonly WindowsWindowService _windowService;

    public WindowsFileDialogService(WindowsWindowService windowService)
    {
        _windowService = windowService;
    }

    public async Task<string?> PickSaveAsync(string suggestedName, string label, string extension)
    {
        if (_windowService.Handle == IntPtr.Zero)
        {
            // No window to own the picker yet (e.g. called before Attach()); fail soft instead of
            // letting WinRT.Interop.InitializeWithWindow throw on a zero handle.
            return null;
        }

        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
            DefaultFileExtension = extension,
        };
        picker.FileTypeChoices.Add(label, new List<string> { extension });

        // A WinUI picker has no window of its own; it has to be told which one owns it.
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowService.Handle);

        StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
