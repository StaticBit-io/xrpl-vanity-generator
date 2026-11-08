using System.Reflection;
using System.Text.RegularExpressions;
using XrplVanity.App.Core.Localization;
using Xunit;

namespace XrplVanity.App.Tests.Localization;

/// <summary>
/// English is the contract: every other catalog carries exactly its phrases with the same placeholder
/// names, and the code declares exactly the phrases English has.
/// </summary>
public sealed partial class CatalogTests
{
    private static readonly string[] PluralNames = ["zero", "one", "two", "few", "many", "other"];

    public static TheoryData<string> AllLanguages() => new(UiLanguage.All.Select(language => language.Code));

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void Every_Catalog_Has_Exactly_The_English_Keys(string code)
    {
        TextCatalog english = TextCatalog.For(UiLanguage.English);
        TextCatalog other = TextCatalog.For(UiLanguage.FromCode(code)!);

        string[] missing = english.Phrases.Keys.Except(other.Phrases.Keys).Order(StringComparer.Ordinal).ToArray();
        string[] extra = other.Phrases.Keys.Except(english.Phrases.Keys).Order(StringComparer.Ordinal).ToArray();

        Assert.True(missing.Length == 0, $"{code} lacks: {string.Join(", ", missing)}");
        Assert.True(extra.Length == 0, $"{code} has keys English does not: {string.Join(", ", extra)}");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void Every_Catalog_Has_The_English_Placeholders(string code)
    {
        TextCatalog english = TextCatalog.For(UiLanguage.English);
        TextCatalog other = TextCatalog.For(UiLanguage.FromCode(code)!);

        List<string> wrong = [];
        foreach ((string key, TextCatalog.Phrase phrase) in other.Phrases)
        {
            if (phrase.Forms is not null)
            {
                string[] badForms = phrase.Forms.Keys.Select(form => form.ToString().ToLowerInvariant()).Except(PluralNames).ToArray();
                if (badForms.Length > 0) wrong.Add($"{key}: unknown plural forms {string.Join(",", badForms)}");
                if (!phrase.Forms.ContainsKey(PluralForm.Other)) wrong.Add($"{key}: plural without 'other'");
            }

            if (!english.Phrases.TryGetValue(key, out TextCatalog.Phrase? reference)) continue;

            string[] expected = Placeholders(reference).ToArray();
            string[] actual = Placeholders(phrase).ToArray();
            if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            {
                wrong.Add($"{key}: [{string.Join(",", actual)}] not [{string.Join(",", expected)}]");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void Every_Plural_Phrase_Has_The_Forms_Its_Language_Needs(string code)
    {
        UiLanguage language = UiLanguage.FromCode(code)!;
        IReadOnlyList<PluralForm> needed = PluralRules.FormsOf(language);

        string[] wrong = TextCatalog.For(language).Phrases
            .Where(entry => entry.Value.Forms is { } forms && !forms.Keys.Order().SequenceEqual(needed.Order()))
            .Select(entry => $"{entry.Key}: {string.Join("/", entry.Value.Forms!.Keys)} not {string.Join("/", needed)}")
            .ToArray();

        Assert.True(wrong.Length == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_Declared_Id_Exists_In_English()
    {
        (List<string> declared, List<string> misnamed) = DeclaredIds();
        HashSet<string> english = TextCatalog.For(UiLanguage.English).Phrases.Keys.ToHashSet(StringComparer.Ordinal);

        string[] unwritten = declared.Where(key => !english.Contains(key)).Order(StringComparer.Ordinal).ToArray();

        Assert.True(misnamed.Count == 0, string.Join(Environment.NewLine, misnamed));
        Assert.True(unwritten.Length == 0, $"Declared but not in en.json: {string.Join(", ", unwritten)}");
    }

    [Fact]
    public void No_English_Key_Is_Unused()
    {
        (List<string> declared, _) = DeclaredIds();

        string[] undeclared = TextCatalog.For(UiLanguage.English).Phrases.Keys
            .Except(declared).Order(StringComparer.Ordinal).ToArray();

        Assert.True(undeclared.Length == 0, $"In en.json but not declared: {string.Join(", ", undeclared)}");
    }

    [Fact]
    public void Russian_Plurals_Pick_Few_And_Many()
    {
        Assert.Equal(PluralForm.One, PluralRules.FormOf(UiLanguage.Russian, 1));
        Assert.Equal(PluralForm.One, PluralRules.FormOf(UiLanguage.Russian, 21));
        Assert.Equal(PluralForm.Few, PluralRules.FormOf(UiLanguage.Russian, 3));
        Assert.Equal(PluralForm.Many, PluralRules.FormOf(UiLanguage.Russian, 5));
        Assert.Equal(PluralForm.Many, PluralRules.FormOf(UiLanguage.Russian, 11));
        Assert.Equal(PluralForm.One, PluralRules.FormOf(UiLanguage.English, 1));
        Assert.Equal(PluralForm.Other, PluralRules.FormOf(UiLanguage.English, 2));
        Assert.Equal(PluralForm.Other, PluralRules.FormOf(UiLanguage.Chinese, 1));
    }

    /// <summary>The languages whose catalogs are written; the others still carry English text.</summary>
    public static TheoryData<string> Translated() => new("ru", "de", "es");

    // Words that read the same in every language; a phrase made only of these, placeholders and
    // punctuation is allowed to equal English.
    private static readonly string[] SameInEveryLanguage = ["XRPL Vanity", "OpenCL", "CUDA", "CPU", "GPU", "QR", "TSV"];

    // Unit symbols that several languages write as English does ("1 h 30 min", "< 1 s"); matched as
    // whole words only, so an English sentence never passes for one.
    private static readonly HashSet<string> SameUnits = new(["h", "min", "s"], StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(Translated))]
    public void Translated_Catalogs_Differ_From_English(string code)
    {
        TextCatalog english = TextCatalog.For(UiLanguage.English);
        TextCatalog other = TextCatalog.For(UiLanguage.FromCode(code)!);

        string[] untranslated = other.Phrases
            .Where(entry => english.Phrases.TryGetValue(entry.Key, out TextCatalog.Phrase? reference)
                            && entry.Value.Texts.Any(text => HasWords(text) && reference.Texts.Contains(text, StringComparer.Ordinal)))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(untranslated.Length == 0, $"{code} still has English: {string.Join(", ", untranslated)}");
    }

    private static bool HasWords(string text)
    {
        string rest = PlaceholderPattern().Replace(text, string.Empty);
        foreach (string word in SameInEveryLanguage) rest = rest.Replace(word, string.Empty, StringComparison.Ordinal);
        return WordPattern().Matches(rest).Any(word => !SameUnits.Contains(word.Value));
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordPattern();

    private static (List<string> Declared, List<string> Misnamed) DeclaredIds()
    {
        List<string> declared = [];
        List<string> misnamed = [];
        foreach (Type area in typeof(TextId).Assembly.GetTypes().Where(type =>
                     type.Namespace == typeof(TextId).Namespace && type.Name.EndsWith("Text", StringComparison.Ordinal)))
        {
            foreach (FieldInfo field in area.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(TextId)))
            {
                string key = ((TextId)field.GetValue(null)!).Key;
                declared.Add(key);
                string expected = $"{area.Name[..^"Text".Length]}.{field.Name}";
                if (!string.Equals(key, expected, StringComparison.Ordinal)) misnamed.Add($"{area.Name}.{field.Name} is '{key}', not '{expected}'");
            }
        }

        return (declared, misnamed);
    }

    private static IEnumerable<string> Placeholders(TextCatalog.Phrase phrase) =>
        phrase.Texts.SelectMany(Placeholders).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    private static IEnumerable<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text.Replace("{{", string.Empty, StringComparison.Ordinal))
            .Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)(?::[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
