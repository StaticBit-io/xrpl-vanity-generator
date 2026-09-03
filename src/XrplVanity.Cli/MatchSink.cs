using System;
using System.IO;
using XrplVanity.Core;

namespace XrplVanity.Cli;

/// <summary>
/// The single place matches are written: stdout, the optional --out file, and the --count
/// limit. Thread-safe; shared by the CPU workers and the GPU loop.
/// </summary>
public sealed class MatchSink : IDisposable
{
    private readonly object _gate = new();
    private readonly int? _limit;
    private readonly TextWriter _stdout;
    private readonly TextWriter? _outFile;

    public MatchSink(int? limit, string? outFile, TextWriter stdout)
    {
        _limit = limit;
        _stdout = stdout;
        _outFile = outFile is null ? null : new StreamWriter(outFile, append: true);
    }

    public bool CountSatisfied { get; private set; }
    public long Reported { get; private set; }

    /// <summary>
    /// Writes the match unless the --count limit is already met. Returns false once the limit
    /// is reached (including on the call that reaches it) so the caller stops searching.
    /// </summary>
    public bool TryReport(Match match)
    {
        lock (_gate)
        {
            if (_limit is int already && Reported >= already)
            {
                return false;
            }

            _stdout.WriteLine($"Address: {match.Address}\tSeed: {match.Seed}");
            _outFile?.WriteLine($"{match.Address}\t{match.Seed}");
            _outFile?.Flush();
            Reported++;

            if (_limit is int limit && Reported >= limit)
            {
                CountSatisfied = true;
                return false;
            }

            return true;
        }
    }

    public void Dispose()
    {
        _outFile?.Dispose();
    }
}
