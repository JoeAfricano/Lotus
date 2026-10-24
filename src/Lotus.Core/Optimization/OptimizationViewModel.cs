using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lotus.Core.Optimization;

public sealed class OptimizationViewModel : ObservableObject
{
    private readonly OptimizationEngine engine;
    private readonly Action<Exception> log;
    private bool busy, confirmed;
    private OptimizationPlan? selected;
    private bool error;
    public ITextCatalog Text { get; }
    public ObservableCollection<OptimizationPlan> Plans { get; } = [];
    public OptimizationPlan? Selected { get => selected; set { SetProperty(ref selected, value); Confirmed = false; Notify(); } }
    public bool Confirmed { get => confirmed; set { SetProperty(ref confirmed, value); Notify(); } }
    public bool CanEdit => !busy;
    public string Status => error ? Text["OptError"] : Selected is null ? Text["OptReady"] : Text["OptStage" + Selected.Stage];
    public IAsyncRelayCommand AnalyzeCommand { get; }
    public IAsyncRelayCommand ApplyCommand { get; }
    public IAsyncRelayCommand RevertCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public OptimizationViewModel(OptimizationEngine engine, ITextCatalog text, Action<Exception> log)
    {
        this.engine = engine; Text = text; this.log = log;
        AnalyzeCommand = new AsyncRelayCommand(() => Run(async () => { await engine.RecoverAsync(); return await engine.AnalyzeAsync(); }), () => !busy);
        ApplyCommand = new AsyncRelayCommand(() => Run(async () => await engine.ApplyAsync(Selected!.Id, Confirmed)), () => !busy && Confirmed && Selected?.Stage == OperationStage.Planned);
        RevertCommand = new AsyncRelayCommand(() => Run(async () => await engine.RevertAsync(Selected!.Id, Confirmed)), () => !busy && Confirmed && Selected?.Stage == OperationStage.Applied);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !busy);
    }
    public Task RefreshAsync() => Run(async () => (await engine.RecoverAsync()).FirstOrDefault());
    private async Task Run(Func<Task<OptimizationPlan?>> action)
    {
        if (busy) return; busy = true; error = false; Notify();
        try
        {
            var result = await action(); var plans = await engine.RecoverAsync();
            Plans.Clear(); foreach (var p in plans.Take(100)) Plans.Add(p);
            Selected = result is null ? null : Plans.FirstOrDefault(p => p.Id == result.Id);
        }
        catch (Exception ex) { error = true; log(ex); }
        finally { busy = false; Confirmed = false; Notify(); }
    }
    public void RefreshLanguage() => Notify();
    private void Notify()
    {
        OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(CanEdit));
        AnalyzeCommand?.NotifyCanExecuteChanged(); ApplyCommand?.NotifyCanExecuteChanged();
        RevertCommand?.NotifyCanExecuteChanged(); RefreshCommand?.NotifyCanExecuteChanged();
    }
}

