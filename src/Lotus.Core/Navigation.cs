using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lotus.Core;

public sealed record Section(string Id, string Title, string Glyph, string Description, string NextStep);

public static class Sections
{
    public static IReadOnlyList<Section> All { get; } = Create(new TextCatalog());
    public static IReadOnlyList<Section> Create(ITextCatalog text) => Array.AsReadOnly(Definitions.Select(item =>
        new Section(item.Id, text[item.Id + "Title"], item.Glyph, text[item.Id + "Description"], text[item.Id + "Next"])).ToArray());
    private static IEnumerable<(string Id, string Glyph)> Definitions => new[]
    {
        ("dashboard", "\uE80F"), ("analysis", "\uE9D9"), ("optimizations", "\uE713"),
        ("monitoring", "\uE9D2"), ("benchmark", "\uE9D9"), ("games", "\uE7FC"),
        ("history", "\uE81C"), ("restore", "\uE777"), ("settings", "\uE115"), ("logs", "\uE8A5")
    };
}

public interface IAppEvents { void Navigated(string sectionId); }

public sealed class ShellViewModel : ObservableObject
{
    private readonly IAppEvents events;
    private Section selectedSection;
    public ITextCatalog Text { get; }
    public IReadOnlyList<Section> Items { get; private set; }
    public Section SelectedSection
    {
        get => selectedSection;
        private set => SetProperty(ref selectedSection, value);
    }
    public IRelayCommand<string> NavigateCommand { get; }
    public ShellViewModel(IAppEvents events, ITextCatalog? text = null)
    {
        this.events = events;
        Text = text ?? new TextCatalog();
        Items = Sections.Create(Text);
        selectedSection = Items[0];
        NavigateCommand = new RelayCommand<string>(Navigate);
    }
    public void RefreshLanguage()
    {
        var id = SelectedSection.Id;
        Items = Sections.Create(Text);
        OnPropertyChanged(nameof(Items));
        SelectedSection = Items.Single(section => section.Id == id);
    }
    private void Navigate(string? id)
    {
        var section = Items.FirstOrDefault(item => item.Id == id);
        if (section is null || section == SelectedSection) return;
        SelectedSection = section;
        events.Navigated(section.Id);
    }
}
