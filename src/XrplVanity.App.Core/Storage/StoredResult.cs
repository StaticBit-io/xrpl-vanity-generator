namespace XrplVanity.App.Core.Storage;

public sealed record StoredResult(string Address, string Seed, string Pattern, string Position, DateTimeOffset FoundAt, string Target);
