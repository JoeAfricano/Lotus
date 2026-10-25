using CommunityToolkit.Mvvm.ComponentModel;

namespace Lotus.Core;

// Progress of one user task (cleaning, testing, closing apps...): a percentage while it runs, then a "done" state
// with a short result. The view animates the transition; Report is safe to call through Progress<double>.
public sealed class TaskState : ObservableObject
{
    private bool running, done;
    private double percent;
    private string doneText = "";
    public bool Running { get => running; private set => SetProperty(ref running, value); }
    public bool Done { get => done; private set => SetProperty(ref done, value); }
    public double Percent { get => percent; private set { if (SetProperty(ref percent, value)) OnPropertyChanged(nameof(PercentText)); } }
    public string PercentText => $"{percent:0}%";
    public string DoneText { get => doneText; private set => SetProperty(ref doneText, value); }

    public void Start() { Done = false; DoneText = ""; Percent = 0; Running = true; }
    public void Report(double value) { if (Running) Percent = Math.Clamp(value, 0, 100); }
    public void Finish(string text) { Percent = 100; Running = false; DoneText = text; Done = true; }
    public void Fail() { Running = false; Done = false; }
    public void Reset() => Fail();
    public IProgress<double> Progress() => new Progress<double>(Report);
}
