using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XrplVanity.Core;

namespace XrplVanity.Cli;

public sealed class CliOptions
{
    public required IReadOnlyList<(MatchPosition Position, string Pattern)> Patterns { get; init; }
    public bool CaseSensitive { get; init; }
    public int Threads { get; init; } = Environment.ProcessorCount;
    public string? OutFile { get; init; }
    public int? Count { get; init; }
    public string? ListFile { get; init; }
    public int ListMinLength { get; init; } = 4;

    public static CliOptions Parse(string[] args)
    {
        List<(MatchPosition, string)> patterns = new();
        bool caseSensitive = false;
        int threads = Environment.ProcessorCount;
        string? outFile = null;
        int? count = null;
        string? listFile = null;
        int listMinLength = 4;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--starts":
                    patterns.Add((MatchPosition.Starts, Next(args, ref i, arg)));
                    break;
                case "--ends":
                    patterns.Add((MatchPosition.Ends, Next(args, ref i, arg)));
                    break;
                case "--contains":
                    patterns.Add((MatchPosition.Contains, Next(args, ref i, arg)));
                    break;
                case "--case-sensitive":
                    caseSensitive = true;
                    break;
                case "--threads":
                    threads = int.Parse(Next(args, ref i, arg));
                    break;
                case "--out":
                    outFile = Next(args, ref i, arg);
                    break;
                case "--count":
                    count = int.Parse(Next(args, ref i, arg));
                    break;
                case "--list":
                    listFile = Next(args, ref i, arg);
                    // optional numeric min length may follow
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int min))
                    {
                        listMinLength = min;
                        i++;
                    }
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"Unknown option '{arg}'.");
                    }
                    patterns.Add((MatchPosition.Starts, arg));
                    break;
            }
        }

        return new CliOptions
        {
            Patterns = patterns,
            CaseSensitive = caseSensitive,
            Threads = threads,
            OutFile = outFile,
            Count = count,
            ListFile = listFile,
            ListMinLength = listMinLength,
        };
    }

    private static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"Option '{option}' requires a value.");
        }

        return args[++i];
    }
}
