using Discord;
using Discord.Net;
using Discord.WebSocket;
using GrepoBot.Discord.Logic;
using GrepoBot.Discord.State;
using Microsoft.Extensions.Logging;

namespace GrepoBot.Discord.Services;

/// <summary>
/// <c>/server setup</c>: brings the server into the <see cref="ServerLayout"/>. Roles and channels that exist are
/// renamed and moved, missing ones are created, other public channels move into a "Delete me" category.
/// Nothing is ever deleted; staff (PRIVATE), voice and ticket channels stay as they are. Then it sets up the
/// features: permanent invite, rules, both forums, tickets, welcome, status message and links card.
/// Running it again only fixes what differs. Without <c>apply</c> it only describes what it would do.
/// </summary>
public sealed class ServerSetupService
{
    #region Private Fields

    private const string LINKS_TITLE = "🔗 GrepoBot links";

    private static readonly RequestOptions _audit = new() { AuditLogReason = "GrepoBot server setup" };

    private readonly StateStore _store;
    private readonly RulesService _rules;
    private readonly ForumService _forum;
    private readonly TicketService _tickets;
    private readonly AppStatusService _status;
    private readonly ILogger<ServerSetupService> _logger;

    #endregion

    #region Constructor

    public ServerSetupService(StateStore store, RulesService rules, ForumService forum, TicketService tickets, AppStatusService status, ILogger<ServerSetupService> logger)
    {
        _store = store;
        _rules = rules;
        _forum = forum;
        _tickets = tickets;
        _status = status;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <summary>Applies the layout (or only describes it). Returns one line per change, problems start with ⚠️.</summary>
    public async Task<List<string>> RunAsync(SocketGuild guild, bool apply)
    {
        var log = new List<string>();
        var roles = await SetupRolesAsync(guild, apply, log);
        var layout = await SetupChannelsAsync(guild, roles, apply, log);

        if (apply)
        {
            await SetupFeaturesAsync(guild, roles, layout, log);
        }
        else
        {
            log.Add("⚙️ Then: permanent invite, rules, both forums (guidelines and tags), tickets, welcome, status message and links card.");
        }

        _logger.LogInformation("Server setup {Mode} on {Guild}: {Count} step(s).", apply ? "applied" : "previewed", guild.Name, log.Count);
        return log;
    }

    #endregion

    #region Private Methods – Roles

    private async Task<Dictionary<string, IRole>> SetupRolesAsync(SocketGuild guild, bool apply, List<string> log)
    {
        var result = new Dictionary<string, IRole>();
        var botRole = guild.CurrentUser.Roles.Where(r => r.Id != guild.EveryoneRole.Id).MaxBy(r => r.Position);
        var botTop = guild.CurrentUser.Hierarchy;

        if (botRole is null || guild.Roles.Any(r => r.Position > botRole.Position) || !botRole.IsHoisted)
        {
            log.Add("⚠️ **Do this by hand** (Discord does not let a bot move or edit its own role): Server Settings → Roles → drag **GrepoBot** to the very top " +
                "and turn on *Display role members separately from online members*.");
        }

        if (botRole is not null && botRole.Colors.PrimaryColor.RawValue != ServerLayout.BOT_ROLE_COLOR)
        {
            log.Add($"⚠️ **Do this by hand**: Server Settings → Roles → **{botRole.Name}** → color `#{ServerLayout.BOT_ROLE_COLOR:X6}` (the icon's cyan).");
        }

        var used = new HashSet<ulong>();
        foreach (var spec in ServerLayout.Roles)
        {
            var role = guild.Roles
                .Where(r => !r.IsManaged && r.Id != guild.EveryoneRole.Id && !used.Contains(r.Id))
                .FirstOrDefault(r => ServerLayout.Matches(r.Name, spec.Aliases));
            var permissions = spec.AddPermissions.Select(Enum.Parse<GuildPermission>).ToArray();

            if (role is null)
            {
                log.Add($"➕ role **{spec.Name}**");
                if (apply)
                {
                    var created = await guild.CreateRoleAsync(spec.Name, new GuildPermissions(Mask(permissions)), new Color(spec.Color), isHoisted: true, isMentionable: false, _audit);
                    result[spec.Key] = created;
                    used.Add(created.Id);
                }

                continue;
            }

            used.Add(role.Id);
            result[spec.Key] = role;
            var missing = permissions.Where(p => !role.Permissions.Has(p)).ToArray();
            var changes = Describe(
                (role.Name != spec.Name, $"rename to **{spec.Name}**"),
                (role.Colors.PrimaryColor.RawValue != spec.Color, "new color"),
                (!role.IsHoisted, "shown separately"),
                (missing.Length > 0, $"+{string.Join(", ", missing)}"));
            if (changes is null)
            {
                continue;
            }

            if (role.Position >= botTop)
            {
                log.Add($"⚠️ role **{role.Name}** ({changes}) is above GrepoBot, move GrepoBot to the top first.");
                continue;
            }

            log.Add($"✏️ role **{role.Name}**: {changes}");
            if (apply)
            {
                var newPermissions = new GuildPermissions(role.Permissions.RawValue | Mask(missing));
                await role.ModifyAsync(p =>
                {
                    p.Name = spec.Name;
                    p.Colors = RoleColors.Solid(new Color(spec.Color));
                    p.Hoist = true;
                    p.Permissions = newPermissions;
                }, _audit);
            }
        }

        await DeleteObsoleteRolesAsync(guild, used, apply, log);

        if (apply)
        {
            await ReorderRolesAsync(guild, result, log);
        }

        return result;
    }

    /// <summary>Deletes the old roles of <see cref="ServerLayout.ObsoleteRoles"/> (never managed or layout roles).</summary>
    private static async Task DeleteObsoleteRolesAsync(SocketGuild guild, HashSet<ulong> layoutRoles, bool apply, List<string> log)
    {
        var obsolete = guild.Roles
            .Where(r => !r.IsManaged && r.Id != guild.EveryoneRole.Id && !layoutRoles.Contains(r.Id)
                && ServerLayout.ObsoleteRoles.Contains(ServerLayout.Slug(r.Name)))
            .ToList();

        foreach (var role in obsolete)
        {
            if (role.Position >= guild.CurrentUser.Hierarchy)
            {
                log.Add($"⚠️ role **{role.Name}** is above GrepoBot and cannot be deleted, move GrepoBot to the top first.");
                continue;
            }

            var members = role.Members.Count();
            log.Add($"🗑️ delete role **{role.Name}**" + (members > 0 ? $" ({members} member{(members == 1 ? string.Empty : "s")} lose it)" : string.Empty));
            if (apply)
            {
                await role.DeleteAsync(_audit);
            }
        }
    }

    /// <summary>Team, Moderator and Supporter right below the bot, every other role keeps its order below them.</summary>
    private static async Task ReorderRolesAsync(SocketGuild guild, Dictionary<string, IRole> roles, List<string> log)
    {
        var botTop = guild.CurrentUser.Hierarchy;
        var top = ServerLayout.Roles
            .Select(s => roles.GetValueOrDefault(s.Key))
            .OfType<IRole>()
            .Where(r => r.Position < botTop)
            .ToList();
        var topIds = top.Select(r => r.Id).ToHashSet();

        var below = guild.Roles
            .Where(r => r.Id != guild.EveryoneRole.Id && r.Position < botTop)
            .ToList();
        var rest = below.Where(r => !topIds.Contains(r.Id)).OrderByDescending(r => r.Position).Cast<IRole>();
        var order = top.Concat(rest).ToList();

        var current = below.OrderByDescending(r => r.Position).Select(r => r.Id);
        if (current.SequenceEqual(order.Select(r => r.Id)))
        {
            return;
        }

        var position = order.Count;
        await guild.ReorderRolesAsync(order.Select(r => new ReorderRoleProperties(r.Id, position--)), _audit);
        log.Add("↕️ roles ordered: " + string.Join(" › ", top.Select(r => r.Name)));
    }

    #endregion

    #region Private Methods – Channels

    private async Task<Dictionary<string, IGuildChannel>> SetupChannelsAsync(SocketGuild guild, Dictionary<string, IRole> roles, bool apply, List<string> log)
    {
        var result = new Dictionary<string, IGuildChannel>();
        var used = new HashSet<ulong>();
        var categories = guild.CategoryChannels.ToList();
        var statusState = _store.Read(s => s.Status.State);

        var position = 0;
        foreach (var categorySpec in ServerLayout.Categories)
        {
            ICategoryChannel? category = categories.FirstOrDefault(c => !used.Contains(c.Id) && ServerLayout.Matches(c.Name, categorySpec.Aliases));
            if (category is null)
            {
                log.Add($"➕ category **{categorySpec.Name}**");
                if (apply)
                {
                    category = await guild.CreateCategoryChannelAsync(categorySpec.Name, p => p.Position = position, _audit);
                }
            }
            else
            {
                if (category.Name != categorySpec.Name)
                {
                    log.Add($"✏️ category {category.Name} → **{categorySpec.Name}**");
                }

                if (apply)
                {
                    var categoryPosition = position;
                    await category.ModifyAsync(p =>
                    {
                        p.Name = categorySpec.Name;
                        p.Position = categoryPosition;
                    }, _audit);
                }
            }

            if (category is not null)
            {
                used.Add(category.Id);
                result["category:" + categorySpec.Aliases[0]] = category;
                if (apply && categorySpec.Access == CategoryAccess.Supporters)
                {
                    await LockToSupportersAsync(guild, category, roles);
                }
            }

            position++;
            var channelPosition = 0;
            foreach (var spec in categorySpec.Channels)
            {
                var wantedName = spec.Key == "status" ? ServerLayout.StatusChannelName(AppStatus.Look(statusState).Emoji) : spec.Name;
                var existing = guild.Channels
                    .Where(c => c is not ICategoryChannel and not IThreadChannel and not IVoiceChannel && !used.Contains(c.Id))
                    .FirstOrDefault(c => ServerLayout.Matches(c.Name, spec.Aliases));

                IGuildChannel? channel = existing;
                if (existing is null)
                {
                    log.Add($"➕ {Kind(spec.Kind)} **{wantedName}**" + (spec.ReadOnly ? " (read-only)" : string.Empty));
                    if (apply && category is not null)
                    {
                        channel = await CreateChannelAsync(guild, spec, wantedName, category.Id, channelPosition, log);
                    }
                }
                else
                {
                    used.Add(existing.Id);
                    var moves = existing is INestedChannel nested && nested.CategoryId != category?.Id;
                    if (existing.Name != wantedName || moves)
                    {
                        log.Add($"✏️ {existing.Name} → **{wantedName}**" + (moves ? $" (into {categorySpec.Name})" : string.Empty));
                    }

                    if (apply)
                    {
                        await ModifyChannelAsync(existing, spec, wantedName, category?.Id, channelPosition, moves);
                    }
                }

                if (channel is not null)
                {
                    used.Add(channel.Id);
                    result[spec.Key] = channel;
                    if (apply && spec.ReadOnly)
                    {
                        var writer = categorySpec.Access == CategoryAccess.Supporters ? roles.GetValueOrDefault(ServerLayout.SUPPORTER_ROLE) : guild.EveryoneRole;
                        if (writer is not null)
                        {
                            await MakeReadOnlyAsync(channel, writer);
                        }
                    }
                }

                channelPosition++;
            }
        }

        await MoveLeftoversAsync(guild, categories, used, position, apply, log);
        return result;
    }

    private static async Task<IGuildChannel> CreateChannelAsync(SocketGuild guild, ChannelSpec spec, string name, ulong categoryId, int position, List<string> log)
    {
        IGuildChannel created;
        switch (spec.Kind)
        {
            case ChannelKind.Forum:
                created = await guild.CreateForumChannelAsync(name, p =>
                {
                    p.CategoryId = categoryId;
                    p.Position = position;
                }, _audit);
                break;
            case ChannelKind.Announcement when guild.Features.HasFeature(GuildFeature.News):
                created = await guild.CreateNewsChannelAsync(name, p =>
                {
                    p.CategoryId = categoryId;
                    p.Position = position;
                    p.Topic = spec.Topic;
                }, _audit);
                break;
            default:
                if (spec.Kind == ChannelKind.Announcement)
                {
                    log.Add($"ℹ️ {name} is a text channel, announcement channels need a Community server.");
                }

                created = await guild.CreateTextChannelAsync(name, p =>
                {
                    p.CategoryId = categoryId;
                    p.Position = position;
                    p.Topic = spec.Topic;
                }, _audit);
                break;
        }

        if (created is INestedChannel nested)
        {
            await nested.SyncPermissionsAsync(_audit);
        }

        return created;
    }

    private static async Task ModifyChannelAsync(IGuildChannel channel, ChannelSpec spec, string name, ulong? categoryId, int position, bool moves)
    {
        if (channel is ITextChannel text and not IThreadChannel)
        {
            await text.ModifyAsync(p =>
            {
                p.Name = name;
                p.CategoryId = categoryId;
                p.Position = position;
                if (spec.Topic is not null)
                {
                    p.Topic = spec.Topic;
                }
            }, _audit);
        }
        else
        {
            await channel.ModifyAsync(p =>
            {
                p.Name = name;
                p.CategoryId = categoryId;
                p.Position = position;
            }, _audit);
        }

        // A channel that comes from another category takes over the permissions of its new one.
        if (moves && channel is INestedChannel nested)
        {
            await nested.SyncPermissionsAsync(_audit);
        }
    }

    /// <summary>Members can read and react but not write (the team still can, through its own permissions).</summary>
    private static async Task MakeReadOnlyAsync(IGuildChannel channel, IRole writer)
    {
        var current = channel.GetPermissionOverwrite(writer) ?? OverwritePermissions.InheritAll;
        var wanted = current.Modify(
            sendMessages: PermValue.Deny,
            sendMessagesInThreads: PermValue.Deny,
            createPublicThreads: PermValue.Deny,
            createPrivateThreads: PermValue.Deny);
        if (wanted.AllowValue != current.AllowValue || wanted.DenyValue != current.DenyValue)
        {
            await channel.AddPermissionOverwriteAsync(writer, wanted, _audit);
        }
    }

    /// <summary>Only supporters and the team see the category.</summary>
    private static async Task LockToSupportersAsync(SocketGuild guild, ICategoryChannel category, Dictionary<string, IRole> roles)
    {
        await category.AddPermissionOverwriteAsync(guild.EveryoneRole, new OverwritePermissions(viewChannel: PermValue.Deny), _audit);

        var member = new OverwritePermissions(
            viewChannel: PermValue.Allow,
            sendMessages: PermValue.Allow,
            readMessageHistory: PermValue.Allow,
            addReactions: PermValue.Allow,
            attachFiles: PermValue.Allow,
            embedLinks: PermValue.Allow,
            connect: PermValue.Allow,
            speak: PermValue.Allow);
        foreach (var key in new[] { ServerLayout.SUPPORTER_ROLE, ServerLayout.TEAM_ROLE, ServerLayout.MODERATOR_ROLE })
        {
            if (roles.GetValueOrDefault(key) is { } role)
            {
                await category.AddPermissionOverwriteAsync(role, member, _audit);
            }
        }
    }

    /// <summary>
    /// Public channels the layout does not know move into the "Delete me" category for an admin to check and delete.
    /// Untouched categories (staff, voice) go below the layout, the Delete me category last.
    /// </summary>
    private static async Task MoveLeftoversAsync(SocketGuild guild, List<SocketCategoryChannel> categories, HashSet<ulong> used, int position, bool apply, List<string> log)
    {
        var untouched = categories
            .Where(c => !used.Contains(c.Id) && ServerLayout.UntouchedCategoryWords.Any(w => ServerLayout.Slug(c.Name).Contains(w)))
            .OrderBy(c => c.Position)
            .ToList();
        var untouchedIds = untouched.Select(c => c.Id).ToHashSet();

        var leftovers = guild.Channels
            .OfType<INestedChannel>()
            .Where(c => c is not ICategoryChannel and not IThreadChannel and not IVoiceChannel
                && !used.Contains(c.Id)
                && !(c.CategoryId is { } id && untouchedIds.Contains(id))
                && !ServerLayout.Slug(c.Name).StartsWith("ticket-")
                && !ServerLayout.Slug(c.Name).StartsWith("closed-"))
            .ToList();

        if (apply)
        {
            foreach (var category in untouched)
            {
                var categoryPosition = position++;
                if (category.Position != categoryPosition)
                {
                    await category.ModifyAsync(p => p.Position = categoryPosition, _audit);
                }
            }
        }

        var bin = categories.FirstOrDefault(c => c.Name == ServerLayout.LEFTOVER_CATEGORY);
        var toMove = leftovers.Where(c => c.CategoryId != bin?.Id).ToList();
        if (toMove.Count == 0)
        {
            return;
        }

        ICategoryChannel? target = bin;
        if (target is null)
        {
            log.Add($"➕ category **{ServerLayout.LEFTOVER_CATEGORY}**");
            if (apply)
            {
                target = await guild.CreateCategoryChannelAsync(ServerLayout.LEFTOVER_CATEGORY, p => p.Position = 99, _audit);
            }
        }

        foreach (var channel in toMove)
        {
            log.Add($"🗑️ {channel.Name} → Delete me (check and delete it by hand)");
            if (apply && target is not null)
            {
                await channel.ModifyAsync(p => p.CategoryId = target.Id, _audit);
            }
        }
    }

    #endregion

    #region Private Methods – Features

    private async Task SetupFeaturesAsync(SocketGuild guild, Dictionary<string, IRole> roles, Dictionary<string, IGuildChannel> channels, List<string> log)
    {
        var invite = await Step(log, "invite", () => EnsureInviteAsync(guild, channels));
        if (invite is not null)
        {
            log.Add($"🔗 permanent invite: {invite}");
        }

        await Step(log, "rules", async () =>
        {
            if (channels.GetValueOrDefault("rules") is not ITextChannel rulesChannel)
            {
                return false;
            }

            var saved = _store.Read(s => new { s.Rules.Title, s.Rules.Text, s.Rules.ChannelId });
            var mentions = new[] { "help", "feedback", "tickets", "templates" }
                .Where(channels.ContainsKey)
                .ToDictionary(k => k, k => channels[k].Id);
            var isDefault = saved.ChannelId is null || saved.Text == RulesSettings.DEFAULT_TEXT || saved.Title == RulesSettings.DEFAULT_TITLE
                || saved.Text == ServerLayout.RenderRules(mentions);
            var title = isDefault ? ServerLayout.RULES_TITLE : saved.Title;
            var text = isDefault ? ServerLayout.RenderRules(mentions) : saved.Text;
            await _rules.PublishAsync(rulesChannel, null, title, text);
            log.Add(isDefault ? "📜 rules published" : "📜 rules published (your edited text was kept)");
            return true;
        });

        await Step(log, "feedback forum", async () =>
        {
            if (channels.GetValueOrDefault("feedback") is not IForumChannel forum)
            {
                return false;
            }

            await _forum.SetupAsync(forum, _store.Read(s => s.Forum.DeleteClosedAfterDays));
            log.Add($"💡 {forum.Name}: guidelines, tags and votes");
            return true;
        });

        await Step(log, "templates forum", async () =>
        {
            if (channels.GetValueOrDefault("templates") is not IForumChannel forum)
            {
                return false;
            }

            await SetupTemplatesForumAsync(forum);
            log.Add($"🏗️ {forum.Name}: guidelines and tags");
            return true;
        });

        await Step(log, "tickets", async () =>
        {
            if (channels.GetValueOrDefault("category:support") is not ICategoryChannel category
                || channels.GetValueOrDefault("tickets") is not ITextChannel panelChannel)
            {
                return false;
            }

            var staff = new[] { ServerLayout.TEAM_ROLE, ServerLayout.MODERATOR_ROLE }
                .Select(k => roles.GetValueOrDefault(k)?.Id)
                .OfType<ulong>()
                .ToList();
            var logChannel = guild.TextChannels.FirstOrDefault(c => ServerLayout.Slug(c.Name) == "ticket-logs");
            _store.Update(s =>
            {
                s.Tickets.CategoryId = category.Id;
                s.Tickets.StaffRoleIds = s.Tickets.StaffRoleIds.Concat(staff).Distinct().ToList();
                s.Tickets.LogChannelId ??= logChannel?.Id;
            });

            var panel = _store.Read(s => new { s.Tickets.PanelTitle, s.Tickets.PanelDescription });
            await _tickets.PublishPanelAsync(panelChannel, panel.PanelTitle, panel.PanelDescription);
            log.Add($"🎫 tickets in {category.Name}, panel in {panelChannel.Name}");
            return true;
        });

        await Step(log, "welcome", () =>
        {
            if (channels.GetValueOrDefault("welcome") is not ITextChannel welcome)
            {
                return Task.FromResult(false);
            }

            var changed = _store.Update(s =>
            {
                if (s.Welcome.ChannelId is not null)
                {
                    return false;
                }

                s.Welcome.ChannelId = welcome.Id;
                return true;
            });
            if (changed)
            {
                log.Add($"👋 welcome cards in {welcome.Name}");
            }

            return Task.FromResult(true);
        });

        await Step(log, "status", async () =>
        {
            if (channels.GetValueOrDefault("status") is not ITextChannel statusChannel)
            {
                return false;
            }

            await _status.PublishAsync(statusChannel);
            log.Add($"📡 status message in {statusChannel.Name} (change it with `/status set`)");
            return true;
        });

        await Step(log, "links", async () =>
        {
            if (channels.GetValueOrDefault("links") is not ITextChannel links || invite is null)
            {
                return false;
            }

            await PublishLinksAsync(links, invite);
            log.Add($"🔗 links card in {links.Name}");
            return true;
        });
    }

    /// <summary>Runs one feature step; a failure is logged as a ⚠️ line and the next steps still run.</summary>
    private async Task<T?> Step<T>(List<string> log, string name, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Server setup step {Step} failed.", name);
            log.Add($"⚠️ {name}: {(ex is HttpException http ? http.Reason ?? http.Message : ex.Message)}");
            return default;
        }
    }

    /// <summary>The bot's invite into the welcome channel: never expires, unlimited uses, reused when it exists.</summary>
    private async Task<string> EnsureInviteAsync(SocketGuild guild, Dictionary<string, IGuildChannel> channels)
    {
        var existing = (await guild.GetInvitesAsync())
            .FirstOrDefault(i => i.Inviter?.Id == guild.CurrentUser.Id && i.MaxAge is null or 0 && i.MaxUses is null or 0 && i.IsTemporary != true);

        string url;
        if (existing is not null)
        {
            url = existing.Url;
        }
        else
        {
            var channel = channels.GetValueOrDefault("welcome") as ITextChannel
                ?? throw new InvalidOperationException("The welcome channel for the invite is missing.");
            var invite = await channel.CreateInviteAsync(maxAge: null, maxUses: null, isTemporary: false, isUnique: true, _audit);
            url = invite.Url;
        }

        _store.Update(s => s.InviteUrl = url);
        return url;
    }

    private static async Task SetupTemplatesForumAsync(IForumChannel forum)
    {
        var byName = forum.Tags.GroupBy(t => t.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Id);
        var tags = ServerLayout.TemplateTags
            .Select(t => new ForumTagBuilder(t.Name, byName.TryGetValue(t.Name.ToLowerInvariant(), out var id) ? id : null, t.Moderated, new Emoji(t.Emoji)).Build())
            .ToList();

        // The tags first: Discord only accepts "require a tag" once the forum has tags members can pick.
        await forum.ModifyAsync(p => p.Tags = tags, _audit);
        await forum.ModifyAsync(p =>
        {
            p.Topic = ServerLayout.TEMPLATES_GUIDELINES;
            p.DefaultReactionEmoji = new Emoji("👍");
            p.ThreadCreationInterval = FeedbackForum.POST_SLOWMODE_SECONDS;
            p.AutoArchiveDuration = ThreadArchiveDuration.OneWeek;
            p.DefaultSortOrder = ForumSortOrder.LatestActivity;
            p.Flags = ChannelFlags.RequireTag;
        }, _audit);
    }

    /// <summary>The links card: every link with its button next to it, and the invite. Updated in place.</summary>
    private async Task PublishLinksAsync(ITextChannel channel, string invite)
    {
        var container = new ContainerBuilder()
            .WithAccentColor(BotBranding.Accent)
            .WithSection(MessageCards.WithLogo(MessageCards.Logo(channel.Guild),
                "# " + LINKS_TITLE,
                "Everything about GrepoBot in one place."))
            .WithSeparator(SeparatorSpacingSize.Small, true)
            .WithSection(MessageCards.WithButton(MessageCards.Link("Website", "https://grepobot.net/", "🌐"),
                "**🌐 Website**\nEverything about GrepoBot"))
            .WithSection(MessageCards.WithButton(MessageCards.Link("Download", "https://grepobot.net/download/", "⬇️"),
                "**⬇️ Download**\nAlways get GrepoBot from the official website"))
            .WithSection(MessageCards.WithButton(MessageCards.Link("Features", "https://grepobot.net/features/", "✨"),
                "**✨ Features**\nFarming, building, culture, scripts and more"))
            .WithSection(MessageCards.WithButton(MessageCards.Link("Changelog", "https://grepobot.net/changelog/", "📜"),
                "**📜 Changelog**\nWhat changed in every release"))
            .WithSeparator(SeparatorSpacingSize.Small, true)
            .WithSection(MessageCards.WithButton(MessageCards.Link("Invite", invite, "👋"),
                $"**👋 Invite your friends**\n{invite}"));
        var card = new ComponentBuilderV2().WithContainer(container).Build();

        var botId = ((SocketGuild)channel.Guild).CurrentUser.Id;
        var existing = (await channel.GetMessagesAsync(20).FlattenAsync())
            .OfType<IUserMessage>()
            .FirstOrDefault(m => m.Author.Id == botId
                && (m.Embeds.Any(e => e.Title == "🔗 GrepoBot links") || MessageCards.Texts(m.Components).Any(t => t.Contains(LINKS_TITLE))));
        await MessageCards.UpsertAsync(channel, existing, card);
    }

    #endregion

    #region Helpers

    private static string? Describe(params (bool Condition, string Text)[] parts)
    {
        var texts = parts.Where(p => p.Condition).Select(p => p.Text).ToList();
        return texts.Count == 0 ? null : string.Join(", ", texts);
    }

    private static ulong Mask(IEnumerable<GuildPermission> permissions)
    {
        return permissions.Aggregate(0UL, (mask, p) => mask | (ulong)p);
    }

    private static string Kind(ChannelKind kind)
    {
        return kind switch
        {
            ChannelKind.Forum => "forum",
            ChannelKind.Announcement => "announcement channel",
            _ => "channel",
        };
    }

    #endregion
}
