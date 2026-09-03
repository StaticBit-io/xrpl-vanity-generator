using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using XrplVanity.Core;

namespace XrplVanity.Cli;

public static class SearchRunner
{
    public static PatternSet BuildPatternSet(CliOptions options)
    {
        PatternSet.Builder builder = new();
        foreach ((MatchPosition position, string pattern) in options.Patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            builder.Add(position, pattern, ignoreCase: !options.CaseSensitive);
        }

        if (options.ListFile is not null)
        {
            foreach (string line in File.ReadLines(options.ListFile))
            {
                string word = line.Trim();
                if (word.Length == 0)
                {
                    continue;
                }

                if (word.Length >= options.ListMinLength)
                {
                    builder.Add(MatchPosition.Starts, word, ignoreCase: !options.CaseSensitive);
                }
            }
        }

        return builder.Build();
    }

    public static Match? SearchDeterministic(CliOptions options, IEnumerable<byte[]> entropySource)
    {
        PatternSet patterns = BuildPatternSet(options);
        CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);
        foreach (byte[] entropy in entropySource)
        {
            if (evaluator.TryEvaluate(entropy, out Match match))
            {
                return match;
            }
        }

        return null;
    }
}

public static class Program
{
    public static int Main(string[] args)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        if (options.Count is int c && c <= 0)
        {
            Console.Error.WriteLine("Option '--count' must be a positive number.");
            return 2;
        }

        PatternSet patterns = SearchRunner.BuildPatternSet(options);
        foreach (NormalizedPattern u in patterns.Unreachable)
        {
            Console.Error.WriteLine(
                $"warning: pattern '{u.Original}' cannot occur in an XRPL address; skipped.");
        }

        if (!patterns.HasPatterns)
        {
            Console.Error.WriteLine("error: no reachable patterns to search for.");
            return 2;
        }

        Console.Error.WriteLine(
            "WARNING: a seed is a secret. Anyone who holds it controls the account. " +
            "Keep matches offline and verify this tool before using generated keys for real funds.");

        long found = 0;
        long total = 0;
        bool countSatisfied = false;
        object gate = new();
        using CancellationTokenSource cts = new();
        TextWriter? outFile = options.OutFile is null ? null : new StreamWriter(options.OutFile, append: true);

        void Worker()
        {
            CandidateEvaluator evaluator = new(new BouncyCastleDeriver(), patterns);
            byte[] pool = new byte[16 * 512];
            int poolPos = pool.Length;
            long local = 0;

            while (!cts.IsCancellationRequested)
            {
                if (poolPos >= pool.Length)
                {
                    RandomNumberGenerator.Fill(pool);
                    poolPos = 0;
                }

                ReadOnlySpan<byte> entropy = pool.AsSpan(poolPos, 16);
                poolPos += 16;
                local++;

                if (evaluator.TryEvaluate(entropy, out Match match))
                {
                    lock (gate)
                    {
                        if (options.Count is int reached && found >= reached)
                        {
                            // Another thread already hit --count; drop this surplus
                            // match instead of writing an extra secret seed.
                            break;
                        }

                        Console.WriteLine($"Address: {match.Address}\tSeed: {match.Seed}");
                        outFile?.WriteLine($"{match.Address}\t{match.Seed}");
                        outFile?.Flush();
                        found++;
                        if (options.Count is int limit && found >= limit)
                        {
                            countSatisfied = true;
                            cts.Cancel();
                        }
                    }
                }

                if ((local & 0xFFFF) == 0)
                {
                    Interlocked.Add(ref total, 0x10000);
                    local = 0;
                }
            }
        }

        Thread[] workers = new Thread[Math.Max(1, options.Threads)];
        for (int t = 0; t < workers.Length; t++)
        {
            workers[t] = new Thread(Worker) { IsBackground = true };
            workers[t].Start();
        }

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        outFile?.Dispose();

        // 0 only when a --count target was fully satisfied; otherwise the run ended
        // via Ctrl+C (or a --count-less search the user stopped), reported as 130.
        return countSatisfied ? 0 : 130;
    }
}
