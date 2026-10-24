using System.Diagnostics;

namespace Lotus.Core.Telemetry;

// One ref-counted worker across all consumers, including rapid release/reacquire.
public sealed class HardwareMonitoringService : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IHardwareTelemetryProvider provider;
    private readonly TimeSpan interval;
    private readonly Action<string, Exception?>? diagnostic;
    private readonly Dictionary<long, Action<HardwareTelemetrySnapshot>> consumers = [];
    private CancellationTokenSource? cancellation;
    private Task worker = Task.CompletedTask;
    private long nextId;
    private bool running, disposed;
    private long samples;
    public long SampleCount => Interlocked.Read(ref samples);
    public int ConsumerCount { get { lock (gate) return consumers.Count; } }
    public bool IsRunning { get { lock (gate) return running; } }

    public HardwareMonitoringService(IHardwareTelemetryProvider provider, TimeSpan? interval = null, Action<string, Exception?>? diagnostic = null)
    {
        this.provider = provider;
        this.interval = interval ?? TimeSpan.FromSeconds(1);
        if (this.interval < TimeSpan.FromMilliseconds(10)) throw new ArgumentOutOfRangeException(nameof(interval));
        this.diagnostic = diagnostic;
    }
    public IDisposable Subscribe(Action<HardwareTelemetrySnapshot> consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var id = ++nextId;
            consumers.Add(id, consumer);
            if (!running) StartWorker();
            return new Lease(this, id);
        }
    }
    private void StartWorker()
    {
        running = true;
        cancellation = new();
        var token = cancellation.Token;
        worker = Task.Run(() => RunAsync(token));
    }
    private void Release(long id)
    {
        lock (gate)
        {
            consumers.Remove(id);
            if (consumers.Count == 0) cancellation?.Cancel();
        }
    }
    private async Task RunAsync(CancellationToken token)
    {
        var failed = false;
        try
        {
            diagnostic?.Invoke("Monitoring started", null);
            try { provider.Reset(); }
            catch (Exception ex) { diagnostic?.Invoke("Monitoring baseline reset failed", ex); }
            using var timer = new PeriodicTimer(interval);
            do
            {
                token.ThrowIfCancellationRequested();
                HardwareTelemetrySnapshot snapshot;
                try
                {
                    snapshot = await provider.ReadAsync(token).ConfigureAwait(false);
                    failed = false;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    if (!failed) diagnostic?.Invoke("Monitoring provider failed", ex);
                    failed = true;
                    snapshot = HardwareTelemetrySnapshot.Missing(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(0), TelemetryStatus.Error);
                }
                token.ThrowIfCancellationRequested();
                Interlocked.Increment(ref samples);
                KeyValuePair<long, Action<HardwareTelemetrySnapshot>>[] targets;
                lock (gate) targets = consumers.ToArray();
                foreach (var target in targets)
                {
                    lock (gate)
                    {
                        if (token.IsCancellationRequested || !consumers.ContainsKey(target.Key)) continue;
                        // Consumers only enqueue UI work. Release cannot race this notification.
                        try { target.Value(snapshot); }
                        catch (Exception ex) { diagnostic?.Invoke("Monitoring consumer failed", ex); }
                    }
                }
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { diagnostic?.Invoke("Monitoring worker failed", ex); }
        finally
        {
            try { (provider as IMonitoringSessionProvider)?.Stop(); }
            catch (Exception ex) { diagnostic?.Invoke("Monitoring provider cleanup failed", ex); }
            lock (gate)
            {
                cancellation?.Dispose(); cancellation = null;
                running = false;
                diagnostic?.Invoke("Monitoring stopped", null);
                if (!disposed && consumers.Count > 0) StartWorker();
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (gate)
        {
            disposed = true;
            consumers.Clear(); cancellation?.Cancel(); pending = worker;
        }
        await pending.ConfigureAwait(false);
    }
    private sealed class Lease(HardwareMonitoringService owner, long id) : IDisposable
    {
        private HardwareMonitoringService? owner = owner;
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release(id);
    }
}
