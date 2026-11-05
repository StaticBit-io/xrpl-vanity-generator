using System.Reflection;
using System.Runtime.InteropServices;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace XrplVanity.MobileBench;

/// <summary>
/// Starts a run as soon as it opens and keeps the screen on, because that is how a search in the
/// wallet would have to run. Results go to the screen, to logcat under <see cref="LogTag"/> and to
/// results.txt in the app's external files directory. Intent extras <c>stage</c>, <c>pipeline</c>,
/// <c>sustain</c> and <c>window</c> override the durations, in seconds.
/// </summary>
[Activity(Label = "XRPL Vanity Bench", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public sealed class MainActivity : Activity
{
    private const string LogTag = "XVBench";

    /// <summary>
    /// Static, because an activity recreated by a configuration change must not start a second run
    /// that wipes the file and halves the rates of the first.
    /// </summary>
    private static int _running;

    private TextView _output = null!;
    private string? _resultsPath;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

        _output = new TextView(this) { Typeface = Android.Graphics.Typeface.Monospace, TextSize = 11 };
        _output.SetTextIsSelectable(true);
        _output.SetPadding(24, 96, 24, 24);
        ScrollView scroll = new(this);
        scroll.AddView(_output);
        SetContentView(scroll);

        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            _output.Text = "A run is already in progress. Its results go to results.txt.";
            return;
        }

        _resultsPath = Path.Combine(GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir!.AbsolutePath, "results.txt");
        File.WriteAllText(_resultsPath, string.Empty);

        BenchOptions options = new(
            Intent?.GetIntExtra("stage", 3) ?? 3,
            Intent?.GetIntExtra("pipeline", 10) ?? 10,
            Intent?.GetIntExtra("sustain", 300) ?? 300,
            Intent?.GetIntExtra("window", 30) ?? 30);

        new Thread(() => RunBench(options)) { IsBackground = true, Name = "bench" }.Start();
    }

    private void RunBench(BenchOptions options)
    {
        try
        {
            Write(DescribeDevice());
            Bench.Run(options, Write, ReadBattery);
            Write("DONE");
        }
        catch (Exception ex)
        {
            Write($"FAILED: {ex}");
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private string DescribeDevice()
    {
        string? variant = typeof(MainActivity).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BenchVariant")?.Value;
        string soc = OperatingSystem.IsAndroidVersionAtLeast(31) ? $"{Build.SocManufacturer} {Build.SocModel}" : Build.Hardware ?? "unknown";
        return $"device: {Build.Manufacturer} {Build.Model}, soc {soc}, Android {Build.VERSION.Release}\n"
            + $"cores: {System.Environment.ProcessorCount}, arch:{RuntimeInformation.ProcessArchitecture}\n"
            + $"runtime: {RuntimeInformation.FrameworkDescription}, variant: {variant}, "
            + $"dynamic code: {System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeCompiled}";
    }

    /// <summary>Sticky broadcast, so reading it needs no receiver and no permission.</summary>
    private string ReadBattery()
    {
        Intent? battery = RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));
        int level = battery?.GetIntExtra(BatteryManager.ExtraLevel, -1) ?? -1;
        double celsius = (battery?.GetIntExtra(BatteryManager.ExtraTemperature, 0) ?? 0) / 10.0;
        return $"battery {level}% {celsius:N1}C";
    }

    private void Write(string line)
    {
        Log.Info(LogTag, line);
        if (_resultsPath is not null)
        {
            File.AppendAllText(_resultsPath, line + "\n");
        }

        RunOnUiThread(() => _output.Append(line + "\n"));
    }
}
