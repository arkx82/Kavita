using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using HtmlAgilityPack;
using Kavita.API.Database;
using Kavita.API.Services;
using Kavita.API.Services.Helpers;
using Kavita.API.Services.SignalR;
using Kavita.Common.Helpers;
using Kavita.Models.Entities;
using Kavita.Models.Entities.Enums;
using Kavita.Services.Reading;
using Microsoft.Extensions.Logging;
using VersOne.Epub;

namespace Kavita.Services.Metadata;

public class WordCountAnalyzerServiceGds : IWordCountAnalyzerServiceGds
{
    private readonly ILogger<WordCountAnalyzerServiceGds> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventHub _eventHub;
    private readonly ICacheHelper _cacheHelper;
    private readonly IMediaErrorService _mediaErrorService;

    public WordCountAnalyzerServiceGds(ILogger<WordCountAnalyzerServiceGds> logger, IUnitOfWork unitOfWork,
        IEventHub eventHub, ICacheHelper cacheHelper, IMediaErrorService mediaErrorService)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
        _eventHub = eventHub;
        _cacheHelper = cacheHelper;
        _mediaErrorService = mediaErrorService;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60 * 60)]
    [AutomaticRetry(Attempts = 2, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public async Task ScanLibrary(int libraryId, bool forceUpdate = false, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var library = await _unitOfWork.LibraryRepository.GetLibraryForIdAsync(libraryId, ct: ct);
        if (library == null || library.Type != LibraryType.GDS)
        {
            return;
        }

        await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
            MessageFactory.WordCountAnalyzerProgressEvent(libraryId, 0F, ProgressEventType.Started, string.Empty), ct: ct);

        var chunkInfo = await _unitOfWork.SeriesRepository.GetChunkInfoAsync(library.Id, ct);
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[MetadataService] Refreshing Library {LibraryName}. Total Items: {TotalSize}. Total Chunks: {TotalChunks} with {ChunkSize} size",
            library.Name, chunkInfo.TotalSize, chunkInfo.TotalChunks, chunkInfo.ChunkSize);

        await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
            MessageFactory.WordCountAnalyzerProgressEvent(library.Id, 0F, ProgressEventType.Started, $"Starting {library.Name}"), ct: ct);

        for (var chunk = 1; chunk <= chunkInfo.TotalChunks; chunk++)
        {
            if (chunkInfo.TotalChunks == 0) continue;
            stopwatch.Restart();

            _logger.LogInformation("[MetadataService] Processing chunk {ChunkNumber} / {TotalChunks} with size {ChunkSize}. Series ({SeriesStart} - {SeriesEnd}",
                chunk, chunkInfo.TotalChunks, chunkInfo.ChunkSize, chunk * chunkInfo.ChunkSize, (chunk + 1) * chunkInfo.ChunkSize);

            var nonLibrarySeries = await _unitOfWork.SeriesRepository.GetFullSeriesForLibraryIdAsync(library.Id,
                new UserParams { PageNumber = chunk, PageSize = chunkInfo.ChunkSize }, ct);
            _logger.LogDebug("[MetadataService] Fetched {SeriesCount} series for refresh", nonLibrarySeries.Count);

            var seriesIndex = 0;
            foreach (var series in nonLibrarySeries)
            {
                var index = chunk * seriesIndex;
                var progress = Math.Max(0F, Math.Min(1F, index * 1F / chunkInfo.TotalSize));
                await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
                    MessageFactory.WordCountAnalyzerProgressEvent(library.Id, progress, ProgressEventType.Updated, series.Name), ct: ct);

                try
                {
                    var gdsInfo = GdsUtil.GetGdsInfoBySeries(series);
                    await ProcessSeries(series, forceUpdate, false, gdsInfo, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[MetadataServiceGDS] There was an exception during metadata refresh for {SeriesName}", series.Name);
                }

                seriesIndex++;
            }

            if (_unitOfWork.HasChanges())
            {
                await _unitOfWork.CommitAsync(ct);
            }

            _logger.LogInformation("[MetadataService] Processed {SeriesStart} - {SeriesEnd} out of {TotalSeries} series in {ElapsedScanTime} milliseconds for {LibraryName}",
                chunk * chunkInfo.ChunkSize, chunk * chunkInfo.ChunkSize + nonLibrarySeries.Count, chunkInfo.TotalSize, stopwatch.ElapsedMilliseconds, library.Name);
        }

        await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
            MessageFactory.WordCountAnalyzerProgressEvent(library.Id, 1F, ProgressEventType.Ended, "Complete"), ct: ct);
        _logger.LogInformation("[WordCountAnalyzerService] Updated metadata for {LibraryName} in {ElapsedMilliseconds} milliseconds",
            library.Name, sw.ElapsedMilliseconds);
    }

    public async Task ScanSeries(int libraryId, int seriesId, bool forceUpdate = true, GdsInfo? gdsInfo = null,
        CancellationToken ct = default)
    {
        var library = await _unitOfWork.LibraryRepository.GetLibraryForIdAsync(libraryId, ct: ct);
        if (library == null || library.Type != LibraryType.GDS)
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        var series = await _unitOfWork.SeriesRepository.GetFullSeriesForSeriesIdAsync(seriesId, ct);
        if (series == null)
        {
            _logger.LogError("[WordCountAnalyzerService] Series {SeriesId} was not found on Library {LibraryId}", seriesId, libraryId);
            return;
        }

        await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
            MessageFactory.WordCountAnalyzerProgressEvent(libraryId, 0F, ProgressEventType.Started, series.Name), ct: ct);

        await ProcessSeries(series, forceUpdate, true, gdsInfo, ct);

        if (_unitOfWork.HasChanges())
        {
            await _unitOfWork.CommitAsync(ct);
        }

        await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
            MessageFactory.WordCountAnalyzerProgressEvent(libraryId, 1F, ProgressEventType.Ended, series.Name), ct: ct);

        _logger.LogInformation("[WordCountAnalyzerService] Updated metadata for {SeriesName} in {ElapsedMilliseconds} milliseconds",
            series.Name, sw.ElapsedMilliseconds);
    }

    private async Task ProcessSeries(Series series, bool forceUpdate = false, bool useFileName = true, GdsInfo? gdsInfo = null,
        CancellationToken ct = default)
    {
        var isEpub = series.Format is MangaFormat.Epub or MangaFormat.Text;
        var existingWordCount = series.WordCount;
        series.WordCount = 0;
        foreach (var volume in series.Volumes)
        {
            volume.WordCount = 0;
            foreach (var chapter in volume.Chapters)
            {
                var firstFile = chapter.Files.FirstOrDefault();
                if (firstFile == null || !_cacheHelper.HasFileChangedSinceLastScan(firstFile.LastFileAnalysis, forceUpdate, firstFile))
                {
                    volume.WordCount += chapter.WordCount;
                    series.WordCount += chapter.WordCount;
                    continue;
                }

                var usedGdsInfo = false;
                if (gdsInfo != null)
                {
                    var key = Path.GetFileName(firstFile.FilePath);
                    var gdsFile = GdsUtil.GetGdsFile(gdsInfo, key);
                    if (gdsFile != null)
                    {
                        chapter.WordCount = gdsFile.WordCount;
                        series.WordCount += chapter.WordCount;
                        volume.WordCount += chapter.WordCount;
                        usedGdsInfo = true;
                    }
                }

                if (!usedGdsInfo && series.Format == MangaFormat.Epub)
                {
                    long sum = 0;
                    var fileCounter = 1;
                    foreach (var file in chapter.Files)
                    {
                        var filePath = file.FilePath;
                        var pageCounter = 1;
                        try
                        {
                            using var book = await EpubReader.OpenBookAsync(filePath, BookService.LenientBookReaderOptions);
                            ReadOnlyCollection<EpubLocalTextContentFileRef> totalPages = book.Content.Html.Local;
                            foreach (var bookPage in totalPages)
                            {
                                var progress = Math.Max(0F, Math.Min(1F,
                                    (fileCounter * pageCounter) * 1F / (chapter.Files.Count * totalPages.Count)));
                                await _eventHub.SendMessageAsync(MessageFactory.NotificationProgress,
                                    MessageFactory.WordCountAnalyzerProgressEvent(series.LibraryId, progress,
                                        ProgressEventType.Updated, useFileName ? filePath : series.Name), ct: ct);
                                sum += await GetWordCountFromHtml(bookPage, filePath);
                                pageCounter++;
                            }

                            fileCounter++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "There was an error reading an epub file for word count, series skipped");
                            await _eventHub.SendMessageAsync(MessageFactory.Error,
                                MessageFactory.ErrorEvent("There was an issue counting words on an epub",
                                    $"{series.Name} - {file.FilePath}"), ct: ct);
                            return;
                        }

                        UpdateFileAnalysis(file);
                    }

                    chapter.WordCount = sum;
                    series.WordCount += sum;
                    volume.WordCount += sum;
                }
                else if (!usedGdsInfo && series.Format == MangaFormat.Text)
                {
                    try
                    {
                        var sum = 0;
                        foreach (var file in chapter.Files)
                        {
                            var all = File.ReadAllText(file.FilePath);
                            sum += all.Length;
                        }

                        chapter.WordCount = sum;
                        series.WordCount += sum;
                        volume.WordCount += sum;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Unable to read text file for word count in {SeriesName}", series.Name);
                    }
                }

                var est = ReaderService.GetTimeEstimate(chapter.WordCount, chapter.Pages, isEpub);
                chapter.MinHoursToRead = est.MinHours;
                chapter.MaxHoursToRead = est.MaxHours;
                chapter.AvgHoursToRead = est.AvgHours;
                foreach (var file in chapter.Files)
                {
                    UpdateFileAnalysis(file);
                }

                _unitOfWork.ChapterRepository.Update(chapter);
            }

            var volumeEst = ReaderService.GetTimeEstimate(volume.WordCount, volume.Pages, isEpub);
            volume.MinHoursToRead = volumeEst.MinHours;
            volume.MaxHoursToRead = volumeEst.MaxHours;
            volume.AvgHoursToRead = volumeEst.AvgHours;
            _unitOfWork.VolumeRepository.Update(volume);
        }

        if (series.WordCount == 0 && existingWordCount != 0) series.WordCount = existingWordCount;
        var seriesEstimate = ReaderService.GetTimeEstimate(series.WordCount, series.Pages, isEpub);
        series.MinHoursToRead = seriesEstimate.MinHours;
        series.MaxHoursToRead = seriesEstimate.MaxHours;
        series.AvgHoursToRead = seriesEstimate.AvgHours;
        _unitOfWork.SeriesRepository.Update(series);
    }

    private void UpdateFileAnalysis(MangaFile file)
    {
        file.UpdateLastFileAnalysis();
        _unitOfWork.MangaFileRepository.Update(file);
    }

    private async Task<int> GetWordCountFromHtml(EpubLocalTextContentFileRef bookFile, string filePath)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(await bookFile.ReadContentAsync());
            var textNodes = doc.DocumentNode.SelectNodes("//body//text()[not(parent::script)]");
            var characterCount = textNodes?.Sum(node => node.InnerText.Count(char.IsLetter)) ?? 0;
            return WordCountAnalyzerService.GetWordCount(characterCount);
        }
        catch (EpubContentException ex)
        {
            _logger.LogError(ex, "Error when counting words in epub {EpubPath}", filePath);
            await _mediaErrorService.ReportMediaIssueAsync(filePath, MediaErrorProducer.BookService,
                $"Invalid Epub Metadata, {bookFile.FilePath} does not exist", ex.Message);
            return 0;
        }
    }
}
