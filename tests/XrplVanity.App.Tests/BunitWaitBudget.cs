using Bunit;
using XrplVanity.App.Tests;
using Xunit;

[assembly: AssemblyFixture(typeof(BunitWaitBudget))]

namespace XrplVanity.App.Tests;

/// <summary>
/// Sets the budget for every bUnit wait. The 1 s default is shorter than the work some waits cover
/// on a loaded machine: one 600 000 iteration PBKDF2 build took 1.0 s, and the first backup build and
/// the first QR drawing in a process took 1.3 s each while they were JIT compiled. A wait returns
/// as soon as its assertion passes, so this only changes how long a real failure takes to report.
/// </summary>
public sealed class BunitWaitBudget
{
    public BunitWaitBudget() => BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(30);
}
