using System.Globalization;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core;

public interface ITextCatalog
{
    string Language { get; }
    string this[string key] { get; }
    string Format(string key, params object[] arguments);
    void SetLanguage(string language);
}

public sealed class TextCatalog : ObservableObject, ITextCatalog
{
    private static readonly ResourceManager Resources = new("Lotus.Core.Localization.Strings", typeof(TextCatalog).Assembly);
    private CultureInfo culture;
    public string Language => culture.Name;
    public TextCatalog(string language = "pt-BR") => culture = CultureInfo.GetCultureInfo(language == "en-US" ? "en-US" : "pt-BR");
    public string this[string key] => Resources.GetString(key, culture) ?? throw new MissingManifestResourceException($"Missing resource: {key}");
    public string Format(string key, params object[] arguments) => string.Format(culture, this[key], arguments);
    public void SetLanguage(string language)
    {
        var next = CultureInfo.GetCultureInfo(language == "en-US" ? "en-US" : "pt-BR");
        if (next.Name == culture.Name) return;
        culture = next;
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged("Item[]");
    }
}
