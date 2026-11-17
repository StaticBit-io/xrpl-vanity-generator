using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XrplVanity.Launcher;

/// <summary>
/// Starts the desktop application from the app folder next to this executable and exits. The
/// application is a process of its own: it keeps running, sits in the tray and is pinned to the
/// taskbar as itself, so the launcher has nothing to wait for.
/// </summary>
internal static partial class Program
{
    private const string AppFolderName = "app";
    private const string AppExecutableName = "XrplVanity.App.exe";
    private const string Caption = "XRPL Vanity";
    private const uint MessageBoxIconError = 0x10;

    // The launcher owns no window, so without this the box can open behind whatever has focus and
    // the user sees nothing happen.
    private const uint MessageBoxSetForeground = 0x10000;

    private static int Main(string[] args)
    {
        string appFolder = Path.Combine(AppContext.BaseDirectory, AppFolderName);
        string appExecutable = Path.Combine(appFolder, AppExecutableName);
        if (!File.Exists(appExecutable))
        {
            ShowError(
                $"The {AppFolderName} folder next to this file is missing or incomplete.\n\n" +
                "Unpack the whole ZIP archive, then run XRPL Vanity.exe from the unpacked folder.");
            return 1;
        }

        ProcessStartInfo startInfo = new ProcessStartInfo(appExecutable)
        {
            UseShellExecute = false,
            WorkingDirectory = appFolder,
        };
        foreach (string argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process? started = Process.Start(startInfo);
            return 0;
        }
        catch (Win32Exception exception)
        {
            ShowError($"XRPL Vanity could not be started.\n\n{exception.Message}");
            return 1;
        }
    }

    private static void ShowError(string text) => MessageBox(IntPtr.Zero, text, Caption, MessageBoxIconError | MessageBoxSetForeground);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr owner, string text, string caption, uint type);
}
