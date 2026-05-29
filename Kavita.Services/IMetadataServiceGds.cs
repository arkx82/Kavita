using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;

namespace Kavita.Services;

public interface IMetadataServiceGds
{
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60 * 60)]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    Task GenerateCoversForLibrary(int libraryId, bool forceUpdate = false, bool forceColorScape = false,
        CancellationToken ct = default);

    Task GenerateCoversForSeries(int libraryId, int seriesId, bool forceUpdate = true, bool forceColorScape = false,
        GdsInfo? gdsInfo = null, CancellationToken ct = default);

    Task GenerateCoversForSeries(Series series, EncodeFormat encodeFormat, CoverImageSize coverImageSize,
        bool forceUpdate = false, bool forceColorScape = true, GdsInfo? gdsInfo = null, CancellationToken ct = default);

    Task RemoveAbandonedMetadataKeys(CancellationToken ct = default);
}
