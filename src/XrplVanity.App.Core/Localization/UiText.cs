namespace XrplVanity.App.Core.Localization;

/// <summary>One named value of a <see cref="UiText"/>.</summary>
/// <param name="Value">
/// A <see cref="string"/> (shown as is), a <see cref="UiText"/> or <see cref="TextId"/> (rendered in the
/// same language), an integer (grouped in the language's locale), another number or a date (formatted
/// in the language's locale with the placeholder's format).
/// </param>
public readonly record struct UiArg(string Name, object Value);

/// <summary>
/// A phrase to show, not yet in any language: an id and its named values. Models hand these to the
/// screens instead of finished strings, so a text already on screen is re-rendered when the language
/// changes, and a model's decisions read the same whatever the language.
/// </summary>
public sealed class UiText : IEquatable<UiText>
{
    private UiText(TextId id, IReadOnlyList<UiArg> args)
    {
        Id = id;
        Args = args;
    }

    public TextId Id { get; }

    public IReadOnlyList<UiArg> Args { get; }

    public static UiText Of(TextId id) => new(id, []);

    public static UiText Of(TextId id, params (string Name, object Value)[] args) =>
        new(id, Array.ConvertAll(args, arg => new UiArg(arg.Name, arg.Value)));

    public static implicit operator UiText(TextId id) => Of(id);

    /// <summary>The value named <paramref name="name"/>, or null.</summary>
    public object? Arg(string name)
    {
        foreach (UiArg arg in Args)
        {
            if (string.Equals(arg.Name, name, StringComparison.Ordinal)) return arg.Value;
        }

        return null;
    }

    public bool Equals(UiText? other) =>
        other is not null && Id == other.Id && Args.SequenceEqual(other.Args);

    public override bool Equals(object? obj) => Equals(obj as UiText);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Id);
        foreach (UiArg arg in Args) hash.Add(arg);
        return hash.ToHashCode();
    }

    /// <summary>The id and values, for test failure output; never shown to a person.</summary>
    public override string ToString() =>
        Args.Count == 0 ? Id.Key : $"{Id.Key}({string.Join(", ", Args.Select(arg => $"{arg.Name}={arg.Value}"))})";
}
