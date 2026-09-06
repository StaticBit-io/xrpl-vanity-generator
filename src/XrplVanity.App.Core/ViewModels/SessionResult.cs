namespace XrplVanity.App.Core.ViewModels;

public sealed record SessionResult(string Address, string Seed, string Pattern, string Position, DateTimeOffset FoundAt, string Target);
