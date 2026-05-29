using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Kavita.API.Database;
using Kavita.API.Services;
using Kavita.API.Services.Helpers;
using Kavita.API.Services.SignalR;
using Kavita.Common.Extensions;
using Kavita.Common.Helpers;
using Kavita.Models.DTOs.Settings;
using Kavita.Models.DTOs.SignalR;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Models.Entities.Interfaces;
using Kavita.Services;
using Kavita.Services.Comparators;
using Kavita.Services.Extensions;
using Microsoft.Extensions.Logging;

namespace Kavita.Services;

public class MetadataServiceGds : IMetadataServiceGds
{
    public const string Name = "MetadataServiceGDS";

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MetadataServiceGds> _logger;
    private readonly IEventHub _eventHub;
    private readonly ICacheHelper _cacheHelper;
    private readonly IReadingItemService _readingItemService;
    private readonly IDirectoryService _directoryService;
    private readonly IImageService _imageService;
    private readonly IList<SignalRMessage> _updateEvents = new List<SignalRMessage>();

    public MetadataServiceGds(IUnitOfWork unitOfWork, ILogger<MetadataServiceGds> logger, IEventHub eventHub,
        ICacheHelper cacheHelper, IReadingItemService readingItemService, IDirectoryService directoryService,
        IImageService imageService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _eventHub = eventHub;
        _cacheHelper = cacheHelper;
        _readingItemService = readingItemService;
        _directoryService = directoryService;
        _imageService = imageService;
    }

    private Task<bool> UpdateChapterCoverImage(Chapter chapter, bool forceUpdate, EncodeFormat encodeFormat, CoverImageSize coverImageSize,
        bool forceColorScape = false, GdsInfo? gdsInfo = null)
    {
        if (chapter == null)
        {
            return Task.FromResult(false);
        }

        var mangaFile = chapter.Files.MinBy(x => x.Chapter);
        if (mangaFile == null)
        {
            return Task.FromResult(false);
        }

        if (!_cacheHelper.ShouldUpdateCoverImage(_directoryService.FileSystem.Path.Join(_directoryService.CoverImageDirectory, chapter.CoverImage),
                mangaFile, chapter.Created, forceUpdate, chapter.CoverImageLocked))
        {
            if (NeedsColorSpace(chapter, forceColorScape))
            {
                _imageService.UpdateColorScape(chapter);
                _unitOfWork.ChapterRepository.Update(chapter);
                _updateEvents.Add(MessageFactory.CoverUpdateEvent(chapter.Id, MessageFactoryEntityTypes.Chapter));
            }

            return Task.FromResult(false);
        }

        _logger.LogDebug("[MetadataServiceGDS] Generating cover image for {File}", mangaFile.FilePath);
        var handledByGds = false;
        var useFirstCover = false;
        if (gdsInfo != null)
        {
            var fileName = Path.GetFileName(mangaFile.FilePath);
            var gdsFile = GdsUtil.GetGdsFile(gdsInfo, fileName);
            if (gdsFile != null)
            {
                if (gdsFile.Cover == "FIRST")
                {
                    useFirstCover = true;
                }
                else if (gdsFile.Cover == "TEXT")
                {
                    chapter.CoverImage = "text.png";
                    handledByGds = true;
                    _imageService.UpdateColorScape(chapter);
                    _unitOfWork.ChapterRepository.Update(chapter);
                }
                else if (!string.IsNullOrEmpty(gdsFile.Cover))
                {
                    var filepath = Path.Join(_directoryService.CoverImageDirectory,
                        ImageService.GetChapterFormat(chapter.Id, chapter.VolumeId)) + ".png";
                    handledByGds = GdsUtil.SaveCover(filepath, gdsFile.Cover);
                    _logger.LogDebug("[MetadataServiceGDS] Saved GDS cover for {Key} ({Handled})", fileName, handledByGds);
                    if (handledByGds)
                    {
                        chapter.CoverImage = ImageService.GetChapterFormat(chapter.Id, chapter.VolumeId) + ".png";
                        _imageService.UpdateColorScape(chapter);
                        _unitOfWork.ChapterRepository.Update(chapter);
                    }
                }
            }
        }

        if (!useFirstCover)
        {
            if (!handledByGds)
            {
                _logger.LogWarning("[MetadataServiceGDS] Fallback cover generation for {File}", mangaFile.FilePath);
                chapter.CoverImage = _readingItemService.GetCoverImage(mangaFile.FilePath,
                    ImageService.GetChapterFormat(chapter.Id, chapter.VolumeId), mangaFile.Format, encodeFormat, coverImageSize);
            }

            _imageService.UpdateColorScape(chapter);
            _unitOfWork.ChapterRepository.Update(chapter);
            _updateEvents.Add(MessageFactory.CoverUpdateEvent(chapter.Id, MessageFactoryEntityTypes.Chapter));
        }

        return Task.FromResult(true);
    }

    private void UpdateChapterLastModified(Chapter chapter, bool forceUpdate)
    {
        var mangaFile = chapter.Files.MinBy(x => x.Chapter);
        if (mangaFile != null && !_cacheHelper.IsFileUnmodifiedSinceCreationOrLastScan(chapter, forceUpdate, mangaFile))
        {
            mangaFile.UpdateLastModified();
        }
    }

    private static bool NeedsColorSpace(IHasCoverImage? entity, bool force)
    {
        if (entity == null)
        {
            return false;
        }

        if (force)
        {
            return true;
        }

        return !string.IsNullOrEmpty(entity.CoverImage)
               && (string.IsNullOrEmpty(entity.PrimaryColor) || string.IsNullOrEmpty(entity.SecondaryColor));
    }

    private Task<bool> UpdateVolumeCoverImage(Volume? volume, bool forceUpdate, bool forceColorScape = false)
    {
        if (volume == null)
        {
            return Task.FromResult(false);
        }

        if (!_cacheHelper.ShouldUpdateCoverImage(_directoryService.FileSystem.Path.Join(_directoryService.CoverImageDirectory, volume.CoverImage),
                null, volume.Created, forceUpdate))
        {
            if (NeedsColorSpace(volume, forceColorScape))
            {
                _imageService.UpdateColorScape(volume);
                _unitOfWork.VolumeRepository.Update(volume);
                _updateEvents.Add(MessageFactory.CoverUpdateEvent(volume.Id, MessageFactoryEntityTypes.Volume));
            }

            return Task.FromResult(false);
        }

        volume.Chapters ??= new List<Chapter>();
        var chapter = volume.Chapters.FirstOrDefault(x => x.MinNumber.Is(1f));
        chapter ??= volume.Chapters.MinBy(x => x.SortOrder, ChapterSortComparerDefaultFirst.Default);
        if (chapter == null)
        {
            return Task.FromResult(false);
        }

        volume.CoverImage = chapter.CoverImage;
        _imageService.UpdateColorScape(volume);
        _updateEvents.Add(MessageFactory.CoverUpdateEvent(volume.Id, MessageFactoryEntityTypes.Volume));
        return Task.FromResult(true);
    }

    private Task UpdateSeriesCoverImage(Series? series, bool forceUpdate, bool forceColorScape = false)
    {
        if (series == null)
        {
            return Task.CompletedTask;
        }

        if (!_cacheHelper.ShouldUpdateCoverImage(_directoryService.FileSystem.Path.Join(_directoryService.CoverImageDirectory, series.CoverImage),
                null, series.Created, forceUpdate, series.CoverImageLocked))
        {
            if (NeedsColorSpace(series, forceColorScape))
            {
                _imageService.UpdateColorScape(series);
                _updateEvents.Add(MessageFactory.CoverUpdateEvent(series.Id, MessageFactoryEntityTypes.Series));
            }

            return Task.CompletedTask;
        }

        series.Volumes ??= new List<Volume>();
        var firstFile = SeriesService.GetFirstChapterForMetadata(series)?.Files.FirstOrDefault();
        if (firstFile == null)
        {
            return Task.CompletedTask;
        }

        var coverPath = Path.Join(Path.GetDirectoryName(firstFile.FilePath), "cover.jpg");
        var coverName = $"_s{series.Id}.jpg";
        if (!File.Exists(coverPath))
        {
            coverPath = Path.Join(Path.GetDirectoryName(firstFile.FilePath), "cover.png");
            coverName = $"_s{series.Id}.png";
            if (!File.Exists(coverPath))
            {
                coverPath = Path.Join(Path.GetDirectoryName(firstFile.FilePath), "cover.webp");
                coverName = $"_s{series.Id}.webp";
            }
        }

        var outputPath = Path.Join(_directoryService.CoverImageDirectory, coverName);
        var hasCover = false;
        if (File.Exists(coverPath) && !File.Exists(outputPath))
        {
            File.Copy(coverPath, outputPath, overwrite: true);
        }
        else if (File.Exists(coverPath) && File.Exists(outputPath)
                 && new FileInfo(coverPath).Length != new FileInfo(outputPath).Length)
        {
            File.Copy(coverPath, outputPath, overwrite: true);
        }

        if (!File.Exists(coverPath) && File.Exists(outputPath))
        {
            File.Delete(outputPath);
            hasCover = false;
        }

        if (File.Exists(outputPath))
        {
            hasCover = true;
            series.CoverImage = coverName;
        }

        if (!hasCover)
        {
            series.CoverImage = series.GetCoverImage();
        }

        _imageService.UpdateColorScape(series);
        _updateEvents.Add(MessageFactory.CoverUpdateEvent(series.Id, MessageFactoryEntityTypes.Series));
        return Task.CompletedTask;
    }

    private async Task ProcessSeriesCoverGen(Series series, bool forceUpdate, EncodeFormat encodeFormat, CoverImageSize coverImageSize,
        bool forceColorScape = false, GdsInfo? gdsInfo = null)
    {
        _logger.LogDebug("[MetadataServiceGDS] Processing cover image generation for series: {SeriesName}", series.OriginalName);
        try
        {
            var volumeIndex = 0;
            var firstVolumeUpdated = false;
            foreach (var volume in series.Volumes)
            {
                var firstChapterUpdated = false;
                var index = 0;
                foreach (var chapter in volume.Chapters)
                {
                    var chapterUpdated = await UpdateChapterCoverImage(chapter, forceUpdate, encodeFormat, coverImageSize, forceColorScape, gdsInfo);
                    UpdateChapterLastModified(chapter, forceUpdate || chapterUpdated);
                    if (index == 0 && chapterUpdated)
                    {
                        firstChapterUpdated = true;
                    }

                    index++;
                }

                if (volumeIndex == 0 && await UpdateVolumeCoverImage(volume, firstChapterUpdated || forceUpdate, forceColorScape))
                {
                    firstVolumeUpdated = true;
                }

                volumeIndex++;
            }

            var useFirstCover = series.Format == MangaFormat.Text;
            if (gdsInfo?.Action != null && gdsInfo.Action.TryGetValue("first_cover", out var firstCover)
                && firstCover == "true")
            {
                useFirstCover = true;
            }

            if (!useFirstCover && (Path.Exists(Path.Join(Path.GetDirectoryName(Path.GetDirectoryName(series.FolderPath)), ".firstcover"))
                                   || Path.Exists(Path.Join(Path.GetDirectoryName(series.FolderPath), ".firstcover"))
                                   || Path.Exists(Path.Join(series.FolderPath, ".firstcover"))))
            {
                useFirstCover = true;
            }

            await UpdateSeriesCoverImage(series, firstVolumeUpdated || forceUpdate, forceColorScape);
            if (!useFirstCover)
            {
                return;
            }

            var coverImage = series.CoverImage;
            if (coverImage == null)
            {
                foreach (var volume in series.Volumes)
                {
                    foreach (var chapter in volume.Chapters)
                    {
                        if (chapter.CoverImage != null)
                        {
                            coverImage = chapter.CoverImage;
                            break;
                        }
                    }
                }
            }

            if (coverImage == null)
            {
                return;
            }

            foreach (var volume in series.Volumes)
            {
                volume.CoverImage = coverImage;
                foreach (var chapter in volume.Chapters)
                {
                    chapter.CoverImage = coverImage;
                }
            }

            series.CoverImage = coverImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MetadataServiceGDS] There was an exception during cover generation for {SeriesName} ", series.Name);
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60 * 60)]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task GenerateCoversForLibrary(int libraryId, bool forceUpdate = false, bool forceColorScape = false,
        CancellationToken ct = default)
    {
        var library = await _unitOfWork.LibraryRepository.GetLibraryForIdAsync(libraryId, ct: ct);
        if (library == null || library.Type != LibraryType.GDS)
        {
            return;
        }

        _logger.LogInformation("[MetadataServiceGDS] Beginning cover generation refresh of {LibraryName}", library.Name);
        _updateEvents.Clear();
        var chunkInfo = await _unitOfWork.SeriesRepository.GetChunkInfoAsync(library.Id, ct);
        var stopwatch = Stopwatch.StartNew();
        long totalTime = 0;
        _logger.LogInformation("[MetadataServiceGDS] Refreshing Library {LibraryName} for cover generation. Total Items: {TotalSize}. Total Chunks: {TotalChunks} with {ChunkSize} size",
            library.Name, chunkInfo.TotalSize, chunkInfo.TotalChunks, chunkInfo.ChunkSize);
        await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
            MessageFactory.CoverUpdateProgressEvent(library.Id, 0F, ProgressEventType.Started, $"Starting {library.Name}"), ct: ct);

        var settings = await _unitOfWork.SettingsRepository.GetSettingsDtoAsync(ct);
        var encodeFormat = settings.EncodeMediaAs;
        var coverImageSize = settings.CoverImageSize;
        for (var chunk = 1; chunk <= chunkInfo.TotalChunks; chunk++)
        {
            if (chunkInfo.TotalChunks == 0) continue;
            totalTime += stopwatch.ElapsedMilliseconds;
            stopwatch.Restart();

            _logger.LogDebug("[MetadataServiceGDS] Processing chunk {ChunkNumber} / {TotalChunks} with size {ChunkSize}. Series ({SeriesStart} - {SeriesEnd})",
                chunk, chunkInfo.TotalChunks, chunkInfo.ChunkSize, chunk * chunkInfo.ChunkSize, (chunk + 1) * chunkInfo.ChunkSize);

            var nonLibrarySeries = await _unitOfWork.SeriesRepository.GetFullSeriesForLibraryIdAsync(library.Id,
                new UserParams { PageNumber = chunk, PageSize = chunkInfo.ChunkSize }, ct);
            _logger.LogDebug("[MetadataServiceGDS] Fetched {SeriesCount} series for refresh", nonLibrarySeries.Count);

            var seriesIndex = 0;
            foreach (var series in nonLibrarySeries)
            {
                var index = (chunk - 1) * chunkInfo.ChunkSize + seriesIndex;
                var progress = Math.Max(0F, Math.Min(1F, index * 1F / chunkInfo.TotalSize));
                await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
                    MessageFactory.CoverUpdateProgressEvent(library.Id, progress, ProgressEventType.Updated, series.Name), ct: ct);

                try
                {
                    var gdsInfo = GdsUtil.GetGdsInfoBySeries(series);
                    await ProcessSeriesCoverGen(series, forceUpdate, encodeFormat, coverImageSize, forceColorScape, gdsInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[MetadataServiceGDS] There was an exception during cover generation refresh for {SeriesName}", series.Name);
                }

                seriesIndex++;
            }

            await _unitOfWork.CommitAsync(ct);
            await FlushEvents(ct);
            _logger.LogInformation("[MetadataServiceGDS] Processed {SeriesStart} - {SeriesEnd} out of {TotalSeries} series in {ElapsedScanTime} milliseconds for {LibraryName}",
                chunk * chunkInfo.ChunkSize, chunk * chunkInfo.ChunkSize + nonLibrarySeries.Count, chunkInfo.TotalSize, stopwatch.ElapsedMilliseconds, library.Name);
        }

        await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
            MessageFactory.CoverUpdateProgressEvent(library.Id, 1F, ProgressEventType.Ended, "Complete"), ct: ct);
        _logger.LogInformation("[MetadataServiceGDS] Updated covers for {SeriesNumber} series in library {LibraryName} in {ElapsedMilliseconds} milliseconds total",
            chunkInfo.TotalSize, library.Name, totalTime);
    }

    public async Task RemoveAbandonedMetadataKeys(CancellationToken ct = default)
    {
        await _unitOfWork.TagRepository.RemoveAllTagNoLongerAssociated();
        await _unitOfWork.PersonRepository.RemoveAllPeopleNoLongerAssociated();
        await _unitOfWork.GenreRepository.RemoveAllGenreNoLongerAssociated();
        await _unitOfWork.CollectionTagRepository.RemoveCollectionsWithoutSeries();
        await _unitOfWork.AppUserProgressRepository.CleanupAbandonedChapters();
    }

    public async Task GenerateCoversForSeries(int libraryId, int seriesId, bool forceUpdate = true, bool forceColorScape = true,
        GdsInfo? gdsInfo = null, CancellationToken ct = default)
    {
        var series = await _unitOfWork.SeriesRepository.GetFullSeriesForSeriesIdAsync(seriesId, ct);
        if (series == null)
        {
            _logger.LogError("[MetadataServiceGDS] Series {SeriesId} was not found on Library {LibraryId}", seriesId, libraryId);
            return;
        }

        gdsInfo ??= GdsUtil.GetGdsInfoBySeries(series);
        var settings = await _unitOfWork.SettingsRepository.GetSettingsDtoAsync(ct);
        var encodeFormat = settings.EncodeMediaAs;
        var coverImageSize = settings.CoverImageSize;
        await GenerateCoversForSeries(series, encodeFormat, coverImageSize, forceUpdate, forceColorScape, gdsInfo, ct);
    }

    public async Task GenerateCoversForSeries(Series series, EncodeFormat encodeFormat, CoverImageSize coverImageSize, bool forceUpdate = false,
        bool forceColorScape = true, GdsInfo? gdsInfo = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
            MessageFactory.CoverUpdateProgressEvent(series.LibraryId, 0F, ProgressEventType.Started, series.Name), ct: ct);

        await ProcessSeriesCoverGen(series, forceUpdate, encodeFormat, coverImageSize, forceColorScape, gdsInfo);
        if (_unitOfWork.HasChanges())
        {
            await _unitOfWork.CommitAsync(ct);
            _logger.LogInformation("[MetadataServiceGDS] Updated covers for {SeriesName} in {ElapsedMilliseconds} milliseconds",
                series.Name, sw.ElapsedMilliseconds);
        }

        await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
            MessageFactory.CoverUpdateProgressEvent(series.LibraryId, 1F, ProgressEventType.Ended, series.Name), ct: ct);
        await _eventHub.SendMessageAsync(MessageFactory.CoverUpdate,
            MessageFactory.CoverUpdateEvent(series.Id, MessageFactoryEntityTypes.Series), false, ct);
        await FlushEvents(ct);
    }

    private async Task FlushEvents(CancellationToken ct = default)
    {
        _logger.LogDebug("Dispatching {Count} update events", _updateEvents.Count);
        foreach (var updateEvent in _updateEvents)
        {
            await _eventHub.SendMessageAsync(MessageFactory.CoverUpdate, updateEvent, false, ct);
        }

        _updateEvents.Clear();
    }
}
