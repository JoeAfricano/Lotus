using Microsoft.UI.Xaml;

namespace Lotus.App;

// Custom themes: the animated background follows the theme in use (or the one being edited in the studio).
public sealed partial class MainWindow
{
    // Theme Studio: "Cancelar" puts back the theme in use (built-in mode or saved custom theme).
    public void OpenThemeStudio(Lotus.Core.Themes.ThemeSpec start) =>
        Studio.Open(start, preferencesViewModel, palette, () => palette.Apply(preferences.Current.Theme, preferences.Current.ActiveCustom()));
    // Where "Criar tema" starts: the custom theme in use, or the gallery theme closest to the built-in mode.
    public Lotus.Core.Themes.ThemeSpec StudioStart() => palette.Custom
        ?? (Lotus.Core.Themes.ThemeGallery.Find(palette.IsLight ? "porcelain" : "lotus")!.Spec with { Name = text["TsNewName"] });

    private void ApplyBackdrop()
    {
        var custom = palette.Custom;
        var kind = custom?.Backdrop.Kind ?? "lotus";
        ThemeScene.Apply(custom is null ? null : custom.Backdrop, palette.IsLight);
        // The Lotus flower scene is the original ambient art; any other choice hides it.
        AmbientBackground.Visibility = kind == "lotus" ? Visibility.Visible : Visibility.Collapsed;
        UpdateAmbientState();
    }
}
