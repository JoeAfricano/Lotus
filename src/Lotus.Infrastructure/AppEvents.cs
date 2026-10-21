using Lotus.Core;
using Serilog;

namespace Lotus.Infrastructure;

public sealed class AppEvents(ILogger logger) : IAppEvents
{
    public void Navigated(string sectionId) => logger.Information("Navigation to {SectionId}", sectionId);
}
