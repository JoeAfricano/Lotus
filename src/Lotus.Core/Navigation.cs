using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lotus.Core;

public sealed record Section(string Id, string Title, string Glyph, string Description, string NextStep);

public static class Sections
{
    public static IReadOnlyList<Section> All { get; } = Array.AsReadOnly(new[]
    {
        new Section("dashboard", "Dashboard", "\uE80F", "Seu ponto de partida para uma experiência melhor em jogos.", "Visão geral do computador e resultados verificados."),
        new Section("analysis", "Análise", "\uE9D9", "Entenda o sistema antes de decidir o que mudar.", "Diagnóstico com critérios objetivos e fontes identificadas."),
        new Section("optimizations", "Otimizações", "\uE713", "Cada mudança começa com uma decisão informada.", "Recomendações com benefício esperado, riscos e confirmação."),
        new Section("monitoring", "Monitoramento", "\uE9D2", "Observe o comportamento do computador.", "Leituras reais de hardware, com indisponibilidades explícitas."),
        new Section("benchmark", "Benchmark", "\uE9D9", "Compare resultados, não promessas.", "Sessões reais de FPS, frametime e comparação antes/depois."),
        new Section("games", "Jogos", "\uE7FC", "Um espaço para sua biblioteca e seus perfis.", "Detecção autorizada de instalações e perfis individuais."),
        new Section("history", "Histórico", "\uE81C", "Saiba o que mudou e o que foi medido.", "Registro de alterações e resultados anteriores."),
        new Section("restore", "Restaurar", "\uE777", "Controle também significa poder voltar.", "Reversão baseada no estado anterior de cada alteração."),
        new Section("settings", "Configurações", "\uE115", "O Lotus com suas preferências.", "Personalização e preferências persistentes."),
        new Section("logs", "Logs", "\uE8A5", "Transparência desde a primeira execução.", "Um visualizador de registros técnicos será adicionado nesta seção.")
    });
}

public interface IAppEvents { void Navigated(string sectionId); }

public sealed class ShellViewModel : ObservableObject
{
    private readonly IAppEvents events;
    private Section selectedSection = Sections.All[0];
    public IReadOnlyList<Section> Items => Sections.All;
    public Section SelectedSection
    {
        get => selectedSection;
        private set => SetProperty(ref selectedSection, value);
    }
    public IRelayCommand<string> NavigateCommand { get; }
    public ShellViewModel(IAppEvents events)
    {
        this.events = events;
        NavigateCommand = new RelayCommand<string>(Navigate);
    }
    private void Navigate(string? id)
    {
        var section = Items.FirstOrDefault(item => item.Id == id);
        if (section is null || section == SelectedSection) return;
        SelectedSection = section;
        events.Navigated(section.Id);
    }
}
