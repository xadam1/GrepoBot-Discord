using Discord;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging;

namespace GrepoBot.Discord.Services;

/// <summary>
/// Posts a copy of an admin's draft message as GrepoBot: text, attachments (re-uploaded),
/// server stickers and rich embeds. Custom emojis keep working because the message text
/// carries them as <c>&lt;:name:id&gt;</c>.
/// </summary>
public sealed class RepostService
{
    #region Private Fields

    private static readonly AllowedMentions _postMentions =
        new(AllowedMentionTypes.Everyone | AllowedMentionTypes.Roles | AllowedMentionTypes.Users);

    private readonly HttpClient _http;
    private readonly StateStore _store;
    private readonly ILogger<RepostService> _logger;

    #endregion

    #region Constructor

    public RepostService(HttpClient http, StateStore store, ILogger<RepostService> logger)
    {
        _http = http;
        _store = store;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>Returns why the message cannot be reposted, or null when it can.</summary>
    public static string? GetRepostProblem(IMessage message)
    {
        if (message is not IUserMessage)
        {
            return "System messages cannot be reposted.";
        }

        if (string.IsNullOrWhiteSpace(message.Content)
            && message.Attachments.Count == 0
            && message.Stickers.Count == 0
            && !message.Embeds.Any(e => e.Type == EmbedType.Rich))
        {
            return "The message is empty, there is nothing to post.";
        }

        if (message.Flags?.HasFlag(MessageFlags.VoiceMessage) == true)
        {
            return "Voice messages cannot be reposted.";
        }

        return null;
    }

    /// <summary>Posts the copy and remembers it for later updates. Returns the first posted message.</summary>
    public async Task<IUserMessage> PostAsync(IUserMessage source, ITextChannel target, IRole? pingRole)
    {
        var parts = BuildTextParts(source.Content, pingRole?.Id);
        var files = await DownloadAttachmentsAsync(source, target.Guild);
        var stickers = FindServerStickers(source, target.Guild);
        var embeds = GetRichEmbeds(source);
        var flags = source.Flags?.HasFlag(MessageFlags.SuppressEmbeds) == true
            ? MessageFlags.SuppressEmbeds
            : MessageFlags.None;

        var posted = new List<IUserMessage>();
        try
        {
            // Every part but the last is plain text, the last one carries files, stickers and embeds.
            for (var i = 0; i < parts.Count - 1; i++)
            {
                posted.Add(await target.SendMessageAsync(parts[i], allowedMentions: _postMentions, flags: flags));
            }

            var last = parts.Count > 0 ? parts[^1] : null;
            posted.Add(files.Count > 0
                ? await target.SendFilesAsync(files, last, allowedMentions: _postMentions, stickers: stickers, embeds: embeds, flags: flags)
                : await target.SendMessageAsync(last, allowedMentions: _postMentions, stickers: stickers, embeds: embeds, flags: flags));
        }
        finally
        {
            DisposeAll(files);
        }

        await PublishIfAnnouncementAsync(target, posted);

        _store.Update(state => state.Posts.Add(new PostRecord
        {
            SourceChannelId = source.Channel.Id,
            SourceMessageId = source.Id,
            TargetChannelId = target.Id,
            MessageIds = posted.Select(m => m.Id).ToList(),
            PingRoleId = pingRole?.Id,
            PostedAt = DateTimeOffset.UtcNow,
        }));

        _logger.LogInformation("Reposted message {Source} to #{Channel} as {Count} message(s).", source.Id, target.Name, posted.Count);
        return posted[0];
    }

    /// <summary>
    /// Re-applies the draft's current text and attachments to every copy posted from it.
    /// Returns a line per copy describing the result.
    /// </summary>
    public async Task<List<string>> UpdateCopiesAsync(IUserMessage source, SocketGuild guild)
    {
        var records = _store.Read(state => state.Posts
            .Where(p => p.SourceMessageId == source.Id)
            .Select(Clone)
            .ToList());

        var results = new List<string>();
        foreach (var record in records)
        {
            var channel = guild.GetTextChannel(record.TargetChannelId);
            if (channel is null)
            {
                results.Add($"⚠️ Channel <#{record.TargetChannelId}> no longer exists.");
                continue;
            }

            results.Add(await UpdateCopyAsync(source, channel, record));
        }

        return results;
    }

    public bool HasCopies(ulong sourceMessageId)
    {
        return _store.Read(state => state.Posts.Any(p => p.SourceMessageId == sourceMessageId));
    }

    #endregion

    #region Private Methods

    private async Task<string> UpdateCopyAsync(IUserMessage source, ITextChannel channel, PostRecord record)
    {
        var parts = BuildTextParts(source.Content, record.PingRoleId);
        if (parts.Count == 0)
        {
            parts.Add(string.Empty);
        }

        if (parts.Count != record.MessageIds.Count)
        {
            return $"⚠️ {channel.Mention}: the draft now needs {parts.Count} message(s) instead of {record.MessageIds.Count}. Delete the old post and post it again.";
        }

        var files = await DownloadAttachmentsAsync(source, channel.Guild);
        var embeds = GetRichEmbeds(source);
        try
        {
            for (var i = 0; i < parts.Count; i++)
            {
                if (await channel.GetMessageAsync(record.MessageIds[i]) is not IUserMessage copy)
                {
                    return $"⚠️ {channel.Mention}: the posted message was deleted.";
                }

                var text = parts[i];
                var isLast = i == parts.Count - 1;
                await copy.ModifyAsync(properties =>
                {
                    properties.Content = text;
                    properties.AllowedMentions = _postMentions;
                    if (isLast)
                    {
                        properties.Attachments = files;
                        properties.Embeds = embeds;
                    }
                });
            }
        }
        finally
        {
            DisposeAll(files);
        }

        var link = $"https://discord.com/channels/{channel.GuildId}/{channel.Id}/{record.MessageIds[0]}";
        return $"✅ Updated {link}";
    }

    private static List<string> BuildTextParts(string content, ulong? pingRoleId)
    {
        var text = content ?? string.Empty;
        if (pingRoleId is not null)
        {
            text = $"<@&{pingRoleId}>\n{text}".TrimEnd();
        }

        return MessageSplitter.Split(text);
    }

    private async Task<List<FileAttachment>> DownloadAttachmentsAsync(IMessage source, IGuild guild)
    {
        var limit = guild.MaxUploadLimit;
        var total = source.Attachments.Sum(a => (long)a.Size);
        if (total > (long)limit)
        {
            throw new RepostException(
                $"The attachments have {total / 1024d / 1024d:0.#} MB, but the bot can upload at most {limit / 1024d / 1024d:0.#} MB per message on this server.");
        }

        var files = new List<FileAttachment>();
        try
        {
            foreach (var attachment in source.Attachments)
            {
                var bytes = await _http.GetByteArrayAsync(attachment.Url);
                files.Add(new FileAttachment(
                    new MemoryStream(bytes),
                    attachment.Filename,
                    attachment.Description,
                    attachment.IsSpoiler()));
            }
        }
        catch
        {
            DisposeAll(files);
            throw;
        }

        return files;
    }

    private static ISticker[] FindServerStickers(IMessage source, IGuild guild)
    {
        // Bots can only send stickers of the server they post in.
        if (guild is not SocketGuild socketGuild)
        {
            return [];
        }

        return source.Stickers
            .Select(s => socketGuild.Stickers.FirstOrDefault(gs => gs.Id == s.Id))
            .Where(s => s is not null)
            .Cast<ISticker>()
            .ToArray();
    }

    private static Embed[] GetRichEmbeds(IMessage source)
    {
        return source.Embeds
            .Where(e => e.Type == EmbedType.Rich)
            .OfType<Embed>()
            .ToArray();
    }

    private async Task PublishIfAnnouncementAsync(ITextChannel target, List<IUserMessage> posted)
    {
        if (target is not INewsChannel || !_store.Read(state => state.AutoPublish))
        {
            return;
        }

        foreach (var message in posted)
        {
            try
            {
                await message.CrosspostAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Publishing message {Message} in #{Channel} failed.", message.Id, target.Name);
            }
        }
    }

    private static PostRecord Clone(PostRecord record)
    {
        return new PostRecord
        {
            SourceChannelId = record.SourceChannelId,
            SourceMessageId = record.SourceMessageId,
            TargetChannelId = record.TargetChannelId,
            MessageIds = record.MessageIds.ToList(),
            PingRoleId = record.PingRoleId,
            PostedAt = record.PostedAt,
        };
    }

    private static void DisposeAll(IEnumerable<FileAttachment> files)
    {
        foreach (var file in files)
        {
            file.Dispose();
        }
    }

    #endregion
}

/// <summary>A repost problem the admin can fix (too large files, ...). The message is shown to them as is.</summary>
public sealed class RepostException(string message) : Exception(message);
