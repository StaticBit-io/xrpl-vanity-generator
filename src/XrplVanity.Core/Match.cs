namespace XrplVanity.Core;

public readonly record struct Match(string Address, string Seed, string Pattern, MatchPosition Position)
{
    // The generated ToString prints every member, and a seed that reaches a log or a debugger
    // string is a leaked wallet key.
    public override string ToString() => $"Match {{ Address = {Address}, Pattern = {Pattern}, Position = {Position} }}";
}
