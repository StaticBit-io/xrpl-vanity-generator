using System;
using System.Collections.Generic;
using System.IO;
using XrplVanity.Core;

namespace XrplVanity.Cli;

public sealed class CliOptions
{
    public required IReadOnlyList<(MatchPosition Position, string Pattern)> Patterns { get; init; }
    public bool CaseSensitive { get; init; }
    public bool Gpu { get; init; }
    public int Threads { get; init; } = Environment.ProcessorCount;
    public bool ThreadsSpecified { get; init; }
    public string? OutFile { get; init; }
    public int? Count { get; init; }
    public string? ListFile { get; init; }
    public int ListMinLength { get; init; } = 4;
    public string? Backend { get; init; }
    public int? Device { get; init; }
    public bool ListDevices { get; init; }

    public static CliOptions Parse(string[] args)
    {
        List<(MatchPosition, string)> patterns = new();
        bool caseSensitive = false;
        bool gpu = false;
        int threads = Environment.ProcessorCount;
        bool threadsSpecified = false;
        string? outFile = null;
        int? count = null;
        string? listFile = null;
        int listMinLength = 4;
        string? backend = null;
        int? device = null;
        bool listDevices = false;

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
                case "--gpu":
                    gpu = true;
                    break;
                case "--threads":
                    threads = NextInt(args, ref i, arg);
                    threadsSpecified = true;
                    break;
                case "--out":
                    outFile = Next(args, ref i, arg);
                    break;
                case "--count":
                    count = NextInt(args, ref i, arg);
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
                case "--backend":
                    backend = Next(args, ref i, arg);
                    if (!string.Equals(backend, "cuda", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(backend, "opencl", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException("Option '--backend' must be 'cuda' or 'opencl'.");
                    }

                    backend = backend.ToLowerInvariant();
                    break;
                case "--device":
                    device = NextInt(args, ref i, arg);
                    if (device < 0)
                    {
                        throw new ArgumentException("Option '--device' must be a non-negative whole number.");
                    }

                    break;
                case "--list-devices":
                    listDevices = true;
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

        if (backend is not null || device is not null)
        {
            gpu = true;
        }

        return new CliOptions
        {
            Patterns = patterns,
            CaseSensitive = caseSensitive,
            Gpu = gpu,
            Threads = threads,
            ThreadsSpecified = threadsSpecified,
            OutFile = outFile,
            Count = count,
            ListFile = listFile,
            ListMinLength = listMinLength,
            Backend = backend,
            Device = device,
            ListDevices = listDevices,
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

    private static int NextInt(string[] args, ref int i, string option)
    {
        string value = Next(args, ref i, option);
        if (!int.TryParse(value, out int result))
        {
            throw new ArgumentException($"Option '{option}' requires a whole number, but got '{value}'.");
        }

        return result;
    }
}
