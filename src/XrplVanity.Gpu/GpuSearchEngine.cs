using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using XrplVanity.Core;

namespace XrplVanity.Gpu;

/// <summary>
/// Runs the device search and re-verifies every device hit with the CPU reference before
/// yielding it. Not thread-safe; one engine per device/thread.
/// </summary>
public sealed class GpuSearchEngine : IDisposable
{
    public const int HitCapacity = 4096;
    public const int CandidatesPerLaunch = 1 << 20;
    public const int SubLaunchSize = 1 << 16;

    /// <summary>
    /// Candidates each device thread derives before one shared field inversion serves the
    /// whole group. Must equal SEARCH_BATCH in the kernel body; KernelSourceTests pins them
    /// together, because a mismatch would silently skip or repeat candidates.
    /// </summary>
    public const int CandidateBatch = 4;
    private const int HitRecordSize = 52;
    private const int BlockSize = 128;
    private const int TableBytes = 256 * 96;

    private readonly IGpuDevice _device;
    private readonly IGpuKernel _search;
    private readonly IGpuBuffer _key;
    private readonly IGpuBuffer _groups;
    private readonly IGpuBuffer _chars;
    private readonly IGpuBuffer _pairs;
    private readonly IGpuBuffer _hits;
    private readonly IGpuBuffer _hitCount;
    private readonly IGpuBuffer _table;
    private readonly int _groupCount;
    private readonly int _pairCount;
    private readonly CandidateEvaluator _verifier;
    private ulong _counter;
    private bool _disposed;

    public GpuSearchEngine(IGpuDevice device, PatternSet patterns)
    {
        _device = device;
        TargetTable table = TargetTable.Build(patterns);
        if (table.GroupCount == 0)
        {
            throw new ArgumentException("The pattern set has no reachable targets to search for.", nameof(patterns));
        }

        _search = device.GetKernel("k_search");
        _groupCount = table.GroupCount;
        _pairCount = table.PairCount;

        _table = device.Alloc(TableBytes);
        device.Launch(device.GetKernel("k_build_base_table"), 256, 128, _table);
        device.Synchronize();

        _groups = device.Alloc(table.Groups.Length * 4);
        _groups.CopyFrom(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(table.Groups)));
        _chars = device.Alloc(Math.Max(1, table.Chars.Length));
        if (table.Chars.Length > 0)
        {
            _chars.CopyFrom(table.Chars);
        }

        // Always allocated, even with no paired rules, so the launch signature never varies.
        _pairs = device.Alloc(Math.Max(4, table.Pairs.Length * 4));
        if (table.Pairs.Length > 0)
        {
            _pairs.CopyFrom(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(table.Pairs)));
        }

        _key = device.Alloc(32);
        _hits = device.Alloc(HitCapacity * HitRecordSize);
        _hitCount = device.Alloc(4);
        _verifier = new CandidateEvaluator(new BouncyCastleDeriver(), patterns);
    }

    public string DeviceName => $"{_device.Backend}:{_device.Name}";
    public long CandidatesSearched { get; private set; }
    public long ConsistencyErrors { get; private set; }
    public long OverflowDrops { get; private set; }

    /// <summary>
    /// Searches <see cref="CandidatesPerLaunch"/> candidates under a fresh CSPRNG key, in
    /// TDR-safe sub-launches, and returns only the hits the CPU reference reproduces.
    /// </summary>
    public List<Match> RunLaunch()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        _key.CopyFrom(key);
        _hitCount.Clear();

        ulong start = _counter;
        for (int offset = 0; offset < CandidatesPerLaunch; offset += SubLaunchSize)
        {
            int count = Math.Min(SubLaunchSize, CandidatesPerLaunch - offset);
            int threads = (count + CandidateBatch - 1) / CandidateBatch;
            _device.Launch(_search, threads, BlockSize,
                _key, start + (ulong)offset, count, _groups, _groupCount, _chars, _pairs, _pairCount, _hits, _hitCount, HitCapacity, _table);
            _device.Synchronize();
        }

        _counter += CandidatesPerLaunch;
        CandidatesSearched += CandidatesPerLaunch;

        byte[] countBytes = new byte[4];
        _hitCount.CopyTo(countBytes);
        uint hitCount = BitConverter.ToUInt32(countBytes, 0);
        int usable = (int)Math.Min(hitCount, (uint)HitCapacity);
        if (hitCount > HitCapacity)
        {
            OverflowDrops += hitCount - HitCapacity;
        }

        List<Match> matches = new(usable);
        if (usable == 0)
        {
            return matches;
        }

        byte[] raw = new byte[usable * HitRecordSize];
        _hits.CopyTo(raw);
        for (int i = 0; i < usable; i++)
        {
            int at = i * HitRecordSize;
            ReadOnlySpan<byte> entropy = raw.AsSpan(at, 16);
            int len = raw[at + 16];
            if (len < 1 || len > 35)
            {
                ConsistencyErrors++;
                continue;
            }

            string gpuAddress = Encoding.ASCII.GetString(raw, at + 17, len);

            // The gate: print nothing the CPU does not independently reproduce.
            if (_verifier.TryEvaluate(entropy, out Match match) && match.Address == gpuAddress)
            {
                matches.Add(match);
            }
            else
            {
                ConsistencyErrors++;
            }
        }

        return matches;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hitCount.Dispose();
        _hits.Dispose();
        _key.Dispose();
        _pairs.Dispose();
        _chars.Dispose();
        _groups.Dispose();
        _table.Dispose();
    }
}
