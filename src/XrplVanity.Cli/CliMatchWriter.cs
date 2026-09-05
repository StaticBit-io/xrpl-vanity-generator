using System;
using System.IO;
using XrplVanity.Core;

namespace XrplVanity.Cli;

/// <summary>Writes an accepted match to stdout and, when given, appends it to the --out file.</summary>
public sealed class CliMatchWriter : IDisposable
{
    private readonly TextWriter _stdout;
    private readonly TextWriter? _outFile;

    public CliMatchWriter(string? outFile, TextWriter stdout)
    {
        _stdout = stdout ?? throw new ArgumentNullException(nameof(stdout));
        _outFile = outFile is null ? null : new StreamWriter(outFile, append: true);
    }

    public void Write(Match match)
    {
        _stdout.WriteLine($"Address: {match.Address}\tSeed: {match.Seed}");
        _outFile?.WriteLine($"{match.Address}\t{match.Seed}");
        _outFile?.Flush();
    }

    public void Dispose()
    {
        _outFile?.Dispose();
    }
}
