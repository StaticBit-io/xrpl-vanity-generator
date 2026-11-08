using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace XrplVanity.App.Core.Localization;

/// <summary>
/// The phrases of one language and how to render them. Immutable and shared: one instance per language
/// for the life of the process, so the screens can cascade it and a changed language is a changed
/// reference.
///
/// A catalog is a flat JSON object of key to phrase, embedded as <c>Localization/Text/&lt;code&gt;.json</c>.
/// A phrase is a string with named placeholders, <c>{name}</c> or <c>{name:format}</c> (<c>{{</c> and
/// <c>}}</c> for literal braces), or an object of CLDR plural forms chosen by the <c>count</c> value.
/// Formatting uses <see cref="UiLanguage.Culture"/> passed explicitly; the ambient culture is never read.
/// </summary>
public sealed class TextCatalog
{
    private static readonly ConcurrentDictionary<string, TextCatalog> Loaded = new(StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, Phrase> _phrases;
    private readonly TextCatalog? _fallback;

    private TextCatalog(UiLanguage language, IReadOnlyDictionary<string, Phrase> phrases, TextCatalog? fallback)
    {
        Language = language;
        _phrases = phrases;
        _fallback = fallback;
    }

    public UiLanguage Language { get; }

    /// <summary>The keys of this catalog, for the completeness tests.</summary>
    internal IReadOnlyDictionary<string, Phrase> Phrases => _phrases;

    /// <summary>A phrase with no values.</summary>
    public string this[TextId id] => Render(UiText.Of(id));

    public string this[UiText? text] => Render(text);

    /// <summary>The catalog of <paramref name="language"/>, loaded once.</summary>
    public static TextCatalog For(UiLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        return Loaded.GetOrAdd(language.Code, _ => Load(language));
    }

    /// <summary>A phrase with its values; null renders as nothing, so an absent message needs no check in markup.</summary>
    public string Render(UiText? text)
    {
        if (text is null) return string.Empty;

        if (!_phrases.TryGetValue(text.Id.Key, out Phrase? phrase))
        {
            // The completeness test makes this unreachable for a shipped catalog; a missing phrase is
            // shown in English rather than taking the screen down. English itself has no fallback:
            // an id English lacks is a programming error the id test catches.
            return _fallback is not null
                ? _fallback.Render(text)
                : throw new KeyNotFoundException($"No phrase '{text.Id.Key}' in the {Language.Code} catalog.");
        }

        return Fill(phrase.Pick(Language, text), text, html: null);
    }

    /// <summary>
    /// A phrase as HTML for a sentence that carries inline markup around some of its values ("Publishing
    /// <c>&lt;strong&gt;{name}&lt;/strong&gt;</c> from …"): every literal and value is HTML-encoded, and
    /// the value named in <paramref name="wrap"/> is put inside that element. The element is named by the
    /// code, never by a catalog, so a translation cannot inject markup.
    /// </summary>
    /// <param name="wrap">Value name to element, such as <c>("name", "strong")</c>; one of <c>strong</c>, <c>em</c>, <c>code</c>, <c>kbd</c>.</param>
    public string RenderHtml(UiText text, params (string Value, string Element)[] wrap)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach ((string _, string element) in wrap)
        {
            if (element is not ("strong" or "em" or "code" or "kbd")) throw new ArgumentException($"'{element}' is not an inline element this method writes.", nameof(wrap));
        }

        if (!_phrases.TryGetValue(text.Id.Key, out Phrase? phrase))
        {
            return _fallback is not null
                ? _fallback.RenderHtml(text, wrap)
                : throw new KeyNotFoundException($"No phrase '{text.Id.Key}' in the {Language.Code} catalog.");
        }

        return Fill(phrase.Pick(Language, text), text, wrap.ToDictionary(item => item.Value, item => item.Element, StringComparer.Ordinal));
    }

    /// <summary>An integer grouped in the language's locale: "12,345", "12 345", "12.345".</summary>
    public string Number(long value) => value.ToString("N0", Language.Culture);

    /// <summary>
    /// A catalog of <paramref name="language"/> whose phrases are the English ones run through
    /// <paramref name="phrase"/>. Tests use it to tell languages apart while the shipped catalogs still
    /// hold English text; it is never cached and never reaches a screen.
    /// </summary>
    internal static TextCatalog Transformed(UiLanguage language, Func<string, string> phrase)
    {
        TextCatalog english = For(UiLanguage.English);
        Dictionary<string, Phrase> phrases = english._phrases.ToDictionary(
            entry => entry.Key,
            entry => new Phrase(
                entry.Value.Text is null ? null : phrase(entry.Value.Text),
                entry.Value.Forms?.ToDictionary(form => form.Key, form => phrase(form.Value))),
            StringComparer.Ordinal);
        return new TextCatalog(language, phrases, english);
    }

    internal static IReadOnlyDictionary<string, Phrase> Parse(Stream json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Dictionary<string, Phrase> phrases = new(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            phrases.Add(property.Name, property.Value.ValueKind switch
            {
                JsonValueKind.String => new Phrase(property.Value.GetString()!, null),
                JsonValueKind.Object => new Phrase(null, property.Value.EnumerateObject().ToDictionary(
                    form => Enum.Parse<PluralForm>(form.Name, ignoreCase: true),
                    form => form.Value.GetString()!)),
                _ => throw new InvalidDataException($"Phrase '{property.Name}' is neither a string nor plural forms."),
            });
        }

        return phrases;
    }

    private static TextCatalog Load(UiLanguage language)
    {
        string resource = $"XrplVanity.App.Core.Localization.Text.{language.Code}.json";
        using Stream stream = typeof(TextCatalog).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The {language.Code} catalog is not embedded ({resource}).");

        TextCatalog? fallback = language == UiLanguage.English ? null : For(UiLanguage.English);
        return new TextCatalog(language, Parse(stream), fallback);
    }

    /// <param name="html">Null for plain text; else HTML output, with the values to wrap and their elements.</param>
    private string Fill(string template, UiText text, Dictionary<string, string>? html)
    {
        if (html is null && template.IndexOf('{') < 0 && template.IndexOf('}') < 0) return template;

        StringBuilder result = new(template.Length + 16);
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c == '}')
            {
                result.Append('}');
                i += i + 1 < template.Length && template[i + 1] == '}' ? 2 : 1;
                continue;
            }

            if (c != '{')
            {
                int next = template.IndexOfAny(['{', '}'], i);
                string literal = template[i..(next < 0 ? template.Length : next)];
                result.Append(html is null ? literal : WebUtility.HtmlEncode(literal));
                i += literal.Length;
                continue;
            }

            if (i + 1 < template.Length && template[i + 1] == '{')
            {
                result.Append('{');
                i += 2;
                continue;
            }

            int close = template.IndexOf('}', i + 1);
            if (close < 0) throw new FormatException($"Unclosed placeholder in '{text.Id.Key}' ({Language.Code}).");

            ReadOnlySpan<char> placeholder = template.AsSpan(i + 1, close - i - 1);
            int colon = placeholder.IndexOf(':');
            string name = (colon < 0 ? placeholder : placeholder[..colon]).ToString();
            string? format = colon < 0 ? null : placeholder[(colon + 1)..].ToString();

            object value = text.Arg(name)
                ?? throw new FormatException($"Phrase '{text.Id.Key}' ({Language.Code}) needs a value for '{name}'.");
            string formatted = Format(value, format);
            if (html is null)
            {
                result.Append(formatted);
            }
            else if (html.TryGetValue(name, out string? element))
            {
                result.Append('<').Append(element).Append('>').Append(WebUtility.HtmlEncode(formatted)).Append("</").Append(element).Append('>');
            }
            else
            {
                result.Append(WebUtility.HtmlEncode(formatted));
            }

            i = close + 1;
        }

        return result.ToString();
    }

    private string Format(object value, string? format) => value switch
    {
        string s => s,
        UiText nested => Render(nested),
        TextId id => this[id],
        int or long or short or uint or ulong or ushort or byte => ((IFormattable)value).ToString(format ?? "N0", Language.Culture),
        IFormattable formattable => formattable.ToString(format, Language.Culture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>One catalog entry: a single text, or plural forms.</summary>
    internal sealed record Phrase(string? Text, IReadOnlyDictionary<PluralForm, string>? Forms)
    {
        /// <summary>The placeholder names of every text of this phrase, for the parity test.</summary>
        internal IEnumerable<string> Texts => Text is not null ? [Text] : Forms!.Values;

        internal string Pick(UiLanguage language, UiText text)
        {
            if (Text is not null) return Text;

            object count = text.Arg("count")
                ?? throw new FormatException($"Plural phrase '{text.Id.Key}' needs a 'count' value.");
            PluralForm form = PluralRules.FormOf(language, Convert.ToInt64(count, CultureInfo.InvariantCulture));
            return Forms!.TryGetValue(form, out string? chosen) ? chosen : Forms[PluralForm.Other];
        }
    }
}
