using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using XrplVanity.Core;

namespace XrplVanity.Gpu;

/// <summary>
/// Runs the device search and re-verifies every device hit with the CPU reference before
/// yielding it. Not thread-safe; one engine per context/thread.
/// </summary>
public sealed class GpuSearchEngine : IDisposable
{
    public const int HitCapacity = 4096;
    public const int CandidatesPerLaunch = 1 << 20;
    public const int SubLaunchSize = 1 << 16;
    private const int HitRecordSize = 52;
    private const int BlockSize = 128;

    private readonly CudaContext _context;
    private readonly CudaFunction _search;
    private readonly DeviceBuffer _key;
    private readonly DeviceBuffer _groups;
    private readonly DeviceBuffer _chars;
    private readonly DeviceBuffer _hits;
    private readonly DeviceBuffer _hitCount;
    private readonly int _groupCount;
    private readonly CandidateEvaluator _verifier;
    private ulong _counter;
    private bool _disposed;

    public GpuSearchEngine(CudaContext context, PatternSet patterns)
    {
        _context = context;
        TargetTable table = TargetTable.Build(patterns);
        if (table.GroupCount == 0)
        {
            throw new ArgumentException("The pattern set has no reachable targets to search for.", nameof(patterns));
        }

        CudaModule module = context.LoadModule(GpuKernels.ReadPtx());
        _search = module.GetFunction("k_search");
        _groupCount = table.GroupCount;

        _groups = context.Alloc(table.Groups.Length * 4);
        _groups.CopyFrom(MemoryMarshal.AsBytes(new ReadOnlySpan<uint>(table.Groups)));
        _chars = context.Alloc(Math.Max(1, table.Chars.Length));
        if (table.Chars.Length > 0)
        {
            _chars.CopyFrom(table.Chars);
        }

        _key = context.Alloc(32);
        _hits = context.Alloc(HitCapacity * HitRecordSize);
        _hitCount = context.Alloc(4);
        _verifier = new CandidateEvaluator(new BouncyCastleDeriver(), patterns);
    }

    public string DeviceName => _context.DeviceName;
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
            _context.Launch(_search, (count + BlockSize - 1) / BlockSize, BlockSize,
                _key, start + (ulong)offset, count, _groups, _groupCount, _chars, _hits, _hitCount, HitCapacity);
            _context.Synchronize();
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
        _chars.Dispose();
        _groups.Dispose();
    }
}
