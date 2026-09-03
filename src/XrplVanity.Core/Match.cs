namespace XrplVanity.Core;

public readonly record struct Match(string Address, string Seed, string Pattern, MatchPosition Position);
