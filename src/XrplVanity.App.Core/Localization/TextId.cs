namespace XrplVanity.App.Core.Localization;

/// <summary>
/// One phrase of the catalogs. Ids are declared as static fields of one class per area
/// (<c>AccountsText.Fund</c>), never written as strings at a call site, so a misspelt phrase is a
/// compile error. The key is <c>"&lt;Area&gt;.&lt;Field&gt;"</c>; <c>CatalogTests</c> holds every
/// declared id to that rule and to the English catalog.
/// </summary>
public readonly record struct TextId(string Key)
{
    /// <summary>The id <c>"<paramref name="area"/>.<paramref name="name"/>"</c>; area classes call it with <c>[CallerMemberName]</c>.</summary>
    public static TextId Of(string area, string name) => new($"{area}.{name}");

    public override string ToString() => Key;
}
