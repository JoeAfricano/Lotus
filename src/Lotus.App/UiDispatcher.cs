using Lotus.Core.Telemetry;
using Microsoft.UI.Dispatching;

namespace Lotus.App;

public sealed class UiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool Post(Action action) => queue.TryEnqueue(DispatcherQueuePriority.Low, () => action());
}
