using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Deletes messages. Discord only bulk-deletes messages younger than 14 days (100 per request),
/// older ones go one by one, which is slow (about one per second) but works for any channel.
/// </summary>
public sealed class PurgeService
{
    #region Private Fields

    /// <summary>A little under Discord's 14 days, so a message never ages out while a purge runs.</summary>
    private static readonly TimeSpan _bulkDeleteMaxAge = TimeSpan.FromDays(13.9);

    private const int PAGE_SIZE = 100;

    /// <summary>How long after a purge its delete events are still expected from the gateway.</summary>
    private static readonly TimeSpan _purgeGracePeriod = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _purgingUntil = new();
    private readonly StateStore _store;
    private readonly ILogger<PurgeService> _logger;

    #endregion

    #region Constructor

    public PurgeService(StateStore store, ILogger<PurgeService> logger)
    {
        _store = store;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>True while (and shortly after) the bot purges the channel, so the mod log skips those deletions.</summary>
    public bool IsBeingPurged(ulong channelId)
    {
        return _purgingUntil.TryGetValue(channelId, out var until) && until > DateTimeOffset.UtcNow;
    }

    /// <summary>Deletes the last <paramref name="count"/> messages, optionally only those of one user.</summary>
    public async Task<int> PurgeLatestAsync(ITextChannel channel, int count, ulong? userId)
    {
        var toDelete = new List<IMessage>();
        ulong? before = null;

        // Filtering by user may need many pages; stop after 5000 scanned messages.
        for (var scanned = 0; toDelete.Count < count && scanned < 5000; scanned += PAGE_SIZE)
        {
            var page = before is null
                ? await channel.GetMessagesAsync(PAGE_SIZE).FlattenAsync()
                : await channel.GetMessagesAsync(before.Value, Direction.Before, PAGE_SIZE).FlattenAsync();

            var messages = page.ToList();
            if (messages.Count == 0)
            {
                break;
            }

            before = messages.Min(m => m.Id);
            toDelete.AddRange(messages
                .Where(m => userId is null || m.Author.Id == userId)
                .Take(count - toDelete.Count));
        }

        return await DeleteAsync(channel, toDelete);
    }

    /// <summary>Deletes <paramref name="fromMessageId"/> and every newer message in the channel.</summary>
    public async Task<int> PurgeFromAsync(ITextChannel channel, ulong fromMessageId)
    {
        var deleted = 0;
        var after = fromMessageId;

        while (true)
        {
            var page = (await channel.GetMessagesAsync(after, Direction.After, PAGE_SIZE).FlattenAsync()).ToList();
            if (page.Count == 0)
            {
                break;
            }

            after = page.Max(m => m.Id);
            deleted += await DeleteAsync(channel, page);
        }

        MarkPurging(channel.Id);
        await channel.DeleteMessageAsync(fromMessageId);
        MarkPurging(channel.Id);
        return deleted + 1;
    }

    /// <summary>Deletes every message and keeps the channel (its ID, settings, webhooks and links stay).</summary>
    public async Task<int> PurgeAllAsync(ITextChannel channel)
    {
        var deleted = 0;
        ulong? before = null;

        // Walk backwards with a cursor, so a message that cannot be deleted is not fetched again forever.
        while (true)
        {
            var page = before is null
                ? (await channel.GetMessagesAsync(PAGE_SIZE).FlattenAsync()).ToList()
                : (await channel.GetMessagesAsync(before.Value, Direction.Before, PAGE_SIZE).FlattenAsync()).ToList();
            if (page.Count == 0)
            {
                break;
            }

            before = page.Min(m => m.Id);
            deleted += await DeleteAsync(channel, page);
        }

        return deleted;
    }

    /// <summary>
    /// Replaces the channel with an empty copy (same name, topic, category, position, permissions, slow mode).
    /// Instant for any number of messages, but the channel ID changes, so links, webhooks and pins are lost.
    /// </summary>
    public async Task<ITextChannel> RecreateAsync(SocketTextChannel channel)
    {
        var guild = channel.Guild;
        var isAnnouncement = channel is INewsChannel;
        var overwrites = channel.PermissionOverwrites.ToList();

        var copy = await guild.CreateTextChannelAsync(channel.Name, properties =>
        {
            properties.CategoryId = channel.CategoryId;
            properties.Topic = channel.Topic;
            properties.IsNsfw = channel.IsNsfw;
            properties.SlowModeInterval = channel.SlowModeInterval;
            properties.PermissionOverwrites = overwrites;
            properties.Position = channel.Position;
            if (isAnnouncement)
            {
                properties.ChannelType = ChannelType.News;
            }
        });

        RemapChannel(channel.Id, copy.Id);
        await channel.DeleteAsync(new RequestOptions { AuditLogReason = "Channel cleared with /purge all" });

        _logger.LogInformation("Recreated #{Channel} ({Old} -> {New}).", channel.Name, channel.Id, copy.Id);
        return copy;
    }

    #endregion

    #region Private Methods

    private async Task<int> DeleteAsync(ITextChannel channel, IReadOnlyCollection<IMessage> messages)
    {
        var limit = DateTimeOffset.UtcNow - _bulkDeleteMaxAge;
        var recent = messages.Where(m => m.Timestamp > limit).ToList();
        var old = messages.Where(m => m.Timestamp <= limit).ToList();
        var deleted = 0;
        MarkPurging(channel.Id);

        foreach (var chunk in recent.Chunk(PAGE_SIZE))
        {
            if (chunk.Length > 1)
            {
                try
                {
                    await channel.DeleteMessagesAsync(chunk);
                    deleted += chunk.Length;
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Bulk delete in #{Channel} failed, deleting one by one.", channel.Name);
                }
            }

            deleted += await DeleteOneByOneAsync(channel.Id, chunk);
        }

        deleted += await DeleteOneByOneAsync(channel.Id, old);
        MarkPurging(channel.Id);
        return deleted;
    }

    private async Task<int> DeleteOneByOneAsync(ulong channelId, IEnumerable<IMessage> messages)
    {
        var deleted = 0;
        foreach (var message in messages)
        {
            try
            {
                MarkPurging(channelId);
                await message.DeleteAsync();
                deleted++;
            }
            catch (Exception ex)
            {
                // Some system messages cannot be deleted, skip them instead of aborting the whole purge.
                _logger.LogDebug(ex, "Message {Message} could not be deleted.", message.Id);
            }
        }

        return deleted;
    }

    private void MarkPurging(ulong channelId)
    {
        _purgingUntil[channelId] = DateTimeOffset.UtcNow + _purgeGracePeriod;
    }

    /// <summary>Points settings at the new channel, the rules message has to be posted again.</summary>
    private void RemapChannel(ulong oldId, ulong newId)
    {
        _store.Update(state =>
        {
            if (state.Welcome.ChannelId == oldId)
            {
                state.Welcome.ChannelId = newId;
            }

            if (state.LogChannelId == oldId)
            {
                state.LogChannelId = newId;
            }

            if (state.Rules.ChannelId == oldId)
            {
                state.Rules.ChannelId = newId;
                state.Rules.MessageId = null;
            }

            var ignored = state.AutoReply.IgnoredChannelIds.IndexOf(oldId);
            if (ignored >= 0)
            {
                state.AutoReply.IgnoredChannelIds[ignored] = newId;
            }
        });
    }

    #endregion
}
