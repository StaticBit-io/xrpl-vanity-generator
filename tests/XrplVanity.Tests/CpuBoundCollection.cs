using Xunit;

namespace XrplVanity.Tests;

/// <summary>
/// Test classes that either saturate the CPU (real multi-threaded searches) or assert on
/// wall-clock timing. They run one class at a time with nothing else in parallel, so a
/// CPU-heavy class cannot distort a timing assertion in another.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CpuBoundCollection
{
    public const string Name = "cpu-bound";
}
