namespace XrplVanity.App.Core.ViewModels;

/// <summary>
/// What one <c>load list…</c> did: how many rules it added, how many lines named a rule the set
/// already held, and how many fell outside <see cref="PatternListFormat"/>'s grammar. Blank lines
/// are counted in none of the three.
/// </summary>
public readonly record struct ListLoadResult(int Added, int Duplicates, int Rejected);
