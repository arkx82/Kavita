using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Kavita.Services;

namespace Kavita.Services.Metadata;

public interface IWordCountAnalyzerServiceGds
{
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60 * 60)]
    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    Task ScanLibrary(int libraryId, bool forceUpdate = false, CancellationToken ct = default);

    Task ScanSeries(int libraryId, int seriesId, bool forceUpdate = true, GdsInfo? gdsInfo = null,
        CancellationToken ct = default);
}
