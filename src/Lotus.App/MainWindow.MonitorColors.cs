using Lotus.Core.Display;
using Lotus.Infrastructure.Display;

namespace Lotus.App;

public sealed partial class MainWindow
{
    public IMonitorColors ColorDriver { get; } = new WindowsMonitorColors();
    public MonitorColorController? MonitorColors { get; private set; }
    public Task MonitorColorsReady { get; private set; } = Task.CompletedTask;
    public IDisplayCalibration CalibrationDriver { get; } = new WindowsDisplayCalibration();
    public DisplayCalibrationController? Calibration { get; private set; }
    private int calibrationOperations;
    internal bool CalibrationDraftOpen { get; set; }
    private bool CalibrationKeepsPage => CalibrationDraftOpen || Calibration?.HasPreview == true || Volatile.Read(ref calibrationOperations) > 0;
    internal void BeginCalibrationWork() { Interlocked.Increment(ref calibrationOperations); ResumePerformance(); }
    internal void EndCalibrationWork() => Interlocked.Decrement(ref calibrationOperations);
    private void InitializeMonitorColors()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lotus", "monitor-colors.json");
        MonitorColors = new(ColorDriver, new JsonMonitorColorStore(path), ex => Serilog.Log.Warning(ex, "Monitor colors recovery"));
        // Only unfinished previews are rolled back. Never impose a saved preference or poll while gaming.
        Calibration = new(CalibrationDriver, new JsonCalibrationStore(Path.Combine(Path.GetDirectoryName(path)!, "monitor-calibration.json")),
            ex => Serilog.Log.Warning(ex, "Display calibration recovery"));
        MonitorColorsReady = Task.Run(() =>
        {
            if (MonitorColors.HasPreview) MonitorColors.CancelPreview();
            if (Calibration.HasPreview) Calibration.CancelPreview();
        });
        Closed += (_, _) => { MonitorColors.Dispose(); Calibration.Dispose(); };
    }
}
