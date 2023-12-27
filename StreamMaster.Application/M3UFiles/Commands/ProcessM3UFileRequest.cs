﻿using FluentValidation;

using Microsoft.EntityFrameworkCore;

using StreamMaster.Application.ChannelGroups.Commands;

using System.Collections.Concurrent;
using System.Diagnostics;

namespace StreamMaster.Application.M3UFiles.Commands;

public record ProcessM3UFileRequest(int Id) : IRequest<M3UFile?> { }

public class ProcessM3UFileRequestValidator : AbstractValidator<ProcessM3UFileRequest>
{
    public ProcessM3UFileRequestValidator()
    {
        _ = RuleFor(v => v.Id)
            .NotNull()
            .GreaterThanOrEqualTo(0);
    }
}


public class ProcessM3UFileRequestHandler(ILogger<ProcessM3UFileRequest> logger, IRepositoryWrapper repository, IMapper mapper, ISettingsService settingsService, IPublisher publisher, ISender sender, IHubContext<StreamMasterHub, IStreamMasterHub> hubContext, IMemoryCache memoryCache) : BaseMediatorRequestHandler(logger, repository, mapper, settingsService, publisher, sender, hubContext, memoryCache), IRequestHandler<ProcessM3UFileRequest, M3UFile?>
{
    private SimpleIntList existingChannels;

    [LogExecutionTimeAspect]
    public async Task<M3UFile?> Handle(ProcessM3UFileRequest request, CancellationToken cancellationToken)
    {
        try
        {
            M3UFile? m3uFile = await Repository.M3UFile.GetM3UFileById(request.Id).ConfigureAwait(false);
            if (m3uFile == null)
            {
                Logger.LogCritical("Could not find M3U file");
                return null;
            }

            (List<VideoStream>? streams, int streamCount) = await ProcessStreams(m3uFile).ConfigureAwait(false);
            if (streams == null)
            {
                Logger.LogCritical("Error while processing M3U file, bad format");
                return null;
            }

            if (streams.Count == 0)
            {
                return m3uFile;
            }

            if (!ShouldUpdate(m3uFile))
            {
                return m3uFile;
            }

            await ProcessAndUpdateStreams(m3uFile, streams, streamCount).ConfigureAwait(false);
            await UpdateChannelGroups(streams, cancellationToken).ConfigureAwait(false);
            await NotifyUpdates(m3uFile, cancellationToken).ConfigureAwait(false);

            return m3uFile;
        }
        catch (Exception ex)
        {
            Logger.LogCritical(ex, "Error while processing M3U file");
            return null;
        }
    }

    [LogExecutionTimeAspect]
    private async Task<(List<VideoStream>? streams, int streamCount)> ProcessStreams(M3UFile m3uFile)
    {
        Stopwatch sw = Stopwatch.StartNew();

        List<VideoStream>? streams = await m3uFile.GetM3U(logger, CancellationToken.None).ConfigureAwait(false);

        int streamsCount = 0;
        if (streams != null)
        {
            streamsCount = GetRealStreamCount(streams);
            streams = await RemoveIgnoredStreams(streams);
            streams = RemoveDuplicates(streams);
        }

        Logger.LogInformation($"Processing M3U streams took {sw.Elapsed.TotalSeconds} seconds");
        return (streams, streamsCount);
    }

    private static bool ShouldUpdate(M3UFile m3uFile)
    {
        return m3uFile.LastWrite() >= m3uFile.LastUpdated;
    }

    [LogExecutionTimeAspect]
    private async Task ProcessAndUpdateStreams(M3UFile m3uFile, List<VideoStream> streams, int streamCount)
    {
        await RemoveMissing(streams, m3uFile.Id);

        List<VideoStream> existing = await Repository.VideoStream.GetVideoStreamQuery().Where(a => a.M3UFileId == m3uFile.Id).ToListAsync().ConfigureAwait(false);
        existingChannels = new SimpleIntList(m3uFile.StartingChannelNumber < 0 ? 0 : m3uFile.StartingChannelNumber - 1);

        int ch = m3uFile.StartingChannelNumber < 0 ? 0 : m3uFile.StartingChannelNumber - 1;

        List<ChannelGroup> groups = await Repository.ChannelGroup.GetChannelGroups();

        ProcessStreamsConcurrently(streams, existing, groups, m3uFile);

        _ = await Repository.SaveAsync().ConfigureAwait(false);

        m3uFile.LastUpdated = DateTime.Now;
        if (m3uFile.StationCount != streamCount)
        {
            m3uFile.StationCount = streamCount;
        }
        Repository.M3UFile.UpdateM3UFile(m3uFile);
        _ = await Repository.SaveAsync().ConfigureAwait(false);
    }

    private async Task RemoveMissing(List<VideoStream> streams, int m3uFileId)
    {
        List<string> streamIds = streams.Select(a => a.Id).ToList();

        IQueryable<VideoStream> toDelete = Repository.VideoStream.FindByCondition(a => a.M3UFileId == m3uFileId && !streamIds.Contains(a.Id));
        if (toDelete.Any())
        {
            List<string> ids = [.. toDelete.Select(a => a.Id)];

            IQueryable<VideoStreamLink> toVideoStreamLinkDel = Repository.VideoStreamLink.FindByCondition(a => ids.Contains(a.ChildVideoStreamId) || ids.Contains(a.ParentVideoStreamId));
            if (toVideoStreamLinkDel.Any())
            {
                await Repository.VideoStreamLink.BulkDeleteAsync(toVideoStreamLinkDel);
            }

            IQueryable<StreamGroupVideoStream> toStreamGroupVideoStreamDel = Repository.StreamGroupVideoStream.FindByCondition(a => ids.Contains(a.ChildVideoStreamId));
            if (toStreamGroupVideoStreamDel.Any())
            {
                await Repository.StreamGroupVideoStream.BulkDeleteAsync(toStreamGroupVideoStreamDel);
            }

            await Repository.VideoStream.BulkDeleteAsync(toDelete);
        }
    }

    private void ProcessStreamsConcurrently(List<VideoStream> streams, List<VideoStream> existing, List<ChannelGroup> groups, M3UFile m3uFile)
    {
        int totalCount = streams.Count;
        var existingLookup = existing.ToDictionary(a => a.Id, a => a);
        var groupLookup = groups.ToDictionary(g => g.Name, g => g);
        //var processed = new HashSet<string>(); // Assuming this should be filled with existing IDs
        var processed = new ConcurrentDictionary<string, bool>();

        var toWrite = new ConcurrentBag<VideoStream>();
        var toUpdate = new ConcurrentBag<VideoStream>();

        int processedCount = 0;


        Parallel.ForEach(streams, stream =>
        {
            if (processed.TryAdd(stream.Id, true))
            {
                groupLookup.TryGetValue(stream.Tvg_group, out ChannelGroup? group);

                if (!existingLookup.TryGetValue(stream.Id, out VideoStream? existingStream))
                {
                    ProcessNewStream(stream, group?.IsHidden ?? false, m3uFile.Name).Wait();
                    toWrite.Add(stream);
                }
                else
                {
                    if (ProcessExistingStream(stream, existingStream, group?.IsHidden ?? false, m3uFile.Name).Result)
                    {
                        existingStream.M3UFileId = m3uFile.Id;
                        toUpdate.Add(existingStream);
                    }
                }

                Interlocked.Increment(ref processedCount);
                if (processedCount % 20000 == 0)
                {
                    Logger.LogInformation($"Processed {processedCount}/{totalCount} streams, adding {toWrite.Count}, updating: {toUpdate.Count}");
                }
            }
        });


        // Final batch processing
        ProcessBatches(toWrite, toUpdate);
        Logger.LogInformation($"Finished Found {processedCount} streams, inserted: {toWrite.Count}, updated: {toUpdate.Count}");
    }

    private void ProcessBatches(ConcurrentBag<VideoStream> toWrite, ConcurrentBag<VideoStream> toUpdate)
    {
        int totalToWrite = toWrite.Count;
        int totalToUpdate = toUpdate.Count;
        int batchWriteCount = 0;
        int batchUpdateCount = 0;

        // Convert to a list and process in chunks
        List<VideoStream> writeList = toWrite.ToList();
        List<VideoStream> updateList = toUpdate.ToList();

        int batchSize = 500;

        if (writeList.Any())
        {
            Logger.LogInformation($"Inserting {writeList.Count} new streams in the DB");

            for (int i = 0; i < writeList.Count; i += batchSize)
            {

                var batch = writeList.Skip(i).Take(batchSize).ToList();
                Repository.VideoStream.BulkInsert(batch);
                batchWriteCount += batch.Count;

                if (batchWriteCount % 5000 == 0)
                {
                    Logger.LogInformation($"Inserted {batchWriteCount}/{totalToWrite} new streams into DB");
                }
            }
        }

        if (updateList.Any())
        {
            Logger.LogInformation($"Updating {updateList.Count} streams in DB");
            for (int i = 0; i < updateList.Count; i += batchSize)
            {
                var batch = updateList.Skip(i).Take(batchSize).ToList();
                Repository.VideoStream.BulkUpdate(batch);
                batchUpdateCount += batch.Count;

                if (batchUpdateCount % 5000 == 0)
                {
                    Logger.LogInformation($"Updated {batchUpdateCount}/{totalToUpdate} streams in DB");
                }
            }
        }
    }



    private async Task UpdateChannelGroups(List<VideoStream> streams, CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();

        //var badStreams = streams.Where(a => a.User_Tvg_group == null || a.User_Tvg_group == "").ToList();

        List<string> newGroups = streams.Where(a => a.User_Tvg_group != null && a.User_Tvg_group != "").Select(a => a.User_Tvg_group).Distinct().ToList();
        List<ChannelGroup> channelGroups = await Repository.ChannelGroup.GetChannelGroups();

        await CreateNewChannelGroups(newGroups, channelGroups, cancellationToken);

        Logger.LogInformation($"Updating channel groups took {sw.Elapsed.TotalSeconds} seconds");
    }

    private async Task CreateNewChannelGroups(List<string> newGroups, List<ChannelGroup> existingGroups, CancellationToken cancellationToken)
    {
        foreach (string? group in newGroups)
        {
            if (!existingGroups.Any(a => a.Name == group))
            {
                await Sender.Send(new CreateChannelGroupRequest(group, true), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task NotifyUpdates(M3UFile m3uFile, CancellationToken cancellationToken)
    {
        await Publisher.Publish(new M3UFileProcessedEvent(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<VideoStream>> RemoveIgnoredStreams(List<VideoStream> streams)
    {
        Setting setting = await GetSettingsAsync();
        if (setting.NameRegex.Any())
        {
            foreach (string regex in setting.NameRegex)
            {
                List<VideoStream> toIgnore = ListHelper.GetMatchingProperty(streams, "Tvg_name", regex);
                _ = streams.RemoveAll(toIgnore.Contains);
            }
        }

        return streams;
    }

    private static int GetRealStreamCount(List<VideoStream> streams)
    {
        List<string> ids = streams.Select(a => a.Id).Distinct().ToList();
        return ids.Count;
    }

    private static List<VideoStream> RemoveDuplicates(List<VideoStream> streams)
    {
        List<VideoStream> cleanStreams = streams.GroupBy(s => s.Id)
                  .Select(g => g.First())
                  .ToList();

        //List<VideoStream> dupes = Repository.VideoStream.FindByCondition(a => ids.Contains(a.Id)).ToList();

        //List<VideoStream> duplicateIds = streams.GroupBy(x => x.Id).Where(g => g.Count() > 1).First().ToList();

        //if (ids.Any())
        //{
        //    List<string> dupeIds = dupes.Select(a => a.Id).Distinct().ToList();

        //    //LogDuplicatesToCSV(dupes);
        //    streams = streams.Where(a => !dupeIds.Contains(a.Id)).ToList();
        //}

        return cleanStreams;
    }

    private void LogDuplicatesToCSV(List<VideoStream> dupes)
    {
        string fileName = $"dupes.csv";
        List<string> lines = [VideoStream.GetCsvHeader(), .. dupes.Select(a => a.ToString())];

        using StreamWriter file = new(fileName);
        foreach (string line in lines)
        {
            file.WriteLine(line);
        }

        Logger.LogError($"Found duplicate streams. Details logged to {fileName}");
    }

    private async Task<bool> ProcessExistingStream(VideoStream stream, VideoStream dbStream, bool isHidden, string mu3FileName)
    {
        bool changed = false;
        if (dbStream.IsHidden != isHidden)
        {
            changed = true;
            dbStream.IsHidden = isHidden;
        }

        if (string.IsNullOrEmpty(dbStream.M3UFileName) || dbStream.M3UFileName != mu3FileName)
        {
            changed = true;
            dbStream.M3UFileName = mu3FileName;
        }

        Setting setting = await GetSettingsAsync();

        if (setting.OverWriteM3UChannels || (stream.Tvg_chno != 0 && dbStream.Tvg_chno != stream.Tvg_chno))
        {
            int localNextChno = setting.OverWriteM3UChannels ? existingChannels.GetNextInt() : existingChannels.GetNextInt(stream.Tvg_chno);
            if (dbStream.Tvg_chno != localNextChno)
            {
                changed = true;
                if (dbStream.Tvg_chno == dbStream.User_Tvg_chno)
                {
                    dbStream.User_Tvg_chno = localNextChno;
                }
                dbStream.Tvg_chno = localNextChno;
            }
        }

        if (dbStream.Tvg_group != stream.Tvg_group)
        {
            changed = true;
            if (dbStream.Tvg_group == dbStream.User_Tvg_group)
            {
                dbStream.User_Tvg_group = stream.Tvg_group;
            }
            dbStream.Tvg_group = stream.Tvg_group;
        }

        if (dbStream.Tvg_ID != stream.Tvg_ID)
        {
            changed = true;
            if (dbStream.Tvg_ID == dbStream.User_Tvg_ID)
            {
                dbStream.User_Tvg_ID = stream.Tvg_ID;
            }
            dbStream.Tvg_ID = stream.Tvg_ID;
        }

        if (dbStream.Tvg_logo != stream.Tvg_logo)
        {
            changed = true;
            if (dbStream.Tvg_logo == dbStream.User_Tvg_logo)
            {
                dbStream.User_Tvg_logo = stream.Tvg_logo;
            }
            dbStream.Tvg_logo = stream.Tvg_logo;
        }

        if (dbStream.Tvg_name != stream.Tvg_name)
        {
            changed = true;
            if (dbStream.Tvg_name == dbStream.User_Tvg_name)
            {
                dbStream.User_Tvg_name = stream.Tvg_name;
            }
            dbStream.Tvg_name = stream.Tvg_name;
        }

        return changed;
    }

    private async Task<bool> ProcessExistingUserStream(VideoStream stream, VideoStream dbStream, bool isHidden, string mu3FileName)
    {
        bool changed = false;
        //if (dbStream.IsHidden != isHidden)
        //{
        //    changed = true;
        //    dbStream.IsHidden = isHidden;
        //}

        if (dbStream.User_Tvg_group != stream.Tvg_group)
        {
            changed = true;
            dbStream.User_Tvg_group = stream.Tvg_group;
        }

        if (string.IsNullOrEmpty(dbStream.M3UFileName) || dbStream.M3UFileName != mu3FileName)
        {
            changed = true;
            dbStream.M3UFileName = mu3FileName;
        }

        Setting setting = await GetSettingsAsync();
        if (setting.OverWriteM3UChannels || dbStream.User_Tvg_chno != stream.Tvg_chno)
        {
            int localNextChno = setting.OverWriteM3UChannels ? existingChannels.GetNextInt() : existingChannels.GetNextInt(stream.Tvg_chno);
            if (dbStream.User_Tvg_chno != localNextChno)
            {
                changed = true;
                dbStream.User_Tvg_chno = localNextChno;
            }
        }

        if (dbStream.User_Tvg_ID != stream.Tvg_ID)
        {
            changed = true;
            dbStream.User_Tvg_ID = stream.Tvg_ID;
        }

        if (dbStream.User_Tvg_logo != stream.Tvg_logo)
        {
            changed = true;

            dbStream.User_Tvg_logo = stream.Tvg_logo;
        }

        if (dbStream.User_Tvg_name != stream.Tvg_name)
        {
            changed = true;

            dbStream.User_Tvg_name = stream.Tvg_name;
        }

        return changed;
    }

    private async Task ProcessNewStream(VideoStream stream, bool IsHidden, string mu3FileName)
    {
        stream.IsHidden = IsHidden;

        Setting setting = await GetSettingsAsync();

        if (setting.OverWriteM3UChannels || stream.User_Tvg_chno == 0 || existingChannels.ContainsInt(stream.Tvg_chno))
        {
            int localNextChno = setting.OverWriteM3UChannels ? existingChannels.GetNextInt() : existingChannels.GetNextInt(stream.User_Tvg_chno);

            stream.User_Tvg_chno = localNextChno;
            stream.Tvg_chno = localNextChno;
        }
        stream.M3UFileName = mu3FileName;
    }
}