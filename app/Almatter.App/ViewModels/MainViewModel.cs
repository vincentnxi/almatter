using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Almatter.App.Diagnostics;
using Almatter.App.Interop;
using Almatter.App.Models;
using Almatter.App.Services;

namespace Almatter.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly string[] AvatarPalette =
        ["#8752A3", "#3B7A57", "#B0554B", "#4A6FA5", "#8A6D3B", "#5C6BC0"];

    private readonly MattermostService _service = new();
    private readonly Session _session;

    /// <summary>Every channel (public/private/direct) loaded for the active team, so a click can find any of them by id.</summary>
    private List<ChannelDto> _loadedChannels = [];
    /// <summary>Direct-message channel id -> the other participant's display name (DM channels don't carry a useful display_name of their own).</summary>
    private Dictionary<string, string> _dmDisplayNames = [];
    /// <summary>Channel ids favorited via the server's own preferences store — mirrors the official clients rather than being an Almatter-only flag.</summary>
    private HashSet<string> _favoriteChannelIds = [];
    private string? _activeChannelId;
    /// <summary>Set once the team is known (cache or network) — search needs it, since Mattermost search is scoped per team.</summary>
    private string? _teamId;
    private List<string> _lastRenderedPostIds = [];
    private List<string> _lastRenderedOutboxIds = [];
    private bool _pollingStarted;
    private CancellationTokenSource? _pollingCts;
    /// <summary>
    /// True right after jumping to a message from search — the list is showing
    /// a window around that (possibly old) message rather than the channel's
    /// live tail, so the polling loop below must not silently swap it back to
    /// "the most recent messages" a couple of seconds later. Cleared on the
    /// next real channel switch.
    /// </summary>
    private bool _viewingJumpedMessage;

    [ObservableProperty]
    public partial string TeamName { get; set; } = "";

    [ObservableProperty]
    public partial string CoreVersion { get; set; } = "?";

    [ObservableProperty]
    public partial string ActiveChannelName { get; set; } = "";

    [ObservableProperty]
    public partial string ActiveChannelTopic { get; set; } = "";

    /// <summary>"X est en train d'écrire…" above the composer — empty when nobody currently is. Refreshed by the poll loop, cleared immediately on channel switch so it doesn't briefly show the previous channel's typers.</summary>
    [ObservableProperty]
    public partial string TypingIndicatorText { get; set; } = "";

    /// <summary>Only true while there is nothing at all to show yet — a cache hit skips straight past this.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; } = true;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Bound to the main composer's TextBox.</summary>
    [ObservableProperty]
    public partial string ComposeText { get; set; } = "";

    /// <summary>Bound to the thread panel's reply TextBox — kept separate so switching threads doesn't lose a half-typed main-composer message.</summary>
    [ObservableProperty]
    public partial string ThreadComposeText { get; set; } = "";

    private DateTime _lastComposerTypingSentAt = DateTime.MinValue;
    private DateTime _lastThreadTypingSentAt = DateTime.MinValue;

    /// <summary>Matches how often Mattermost's own clients re-send a typing ping while you keep typing — see GetTypingUsers' TYPING_TTL_MILLIS on the receive side for the corresponding expiry window.</summary>
    private static readonly TimeSpan TypingResendInterval = TimeSpan.FromSeconds(2);

    partial void OnComposeTextChanged(string value)
    {
        if (string.IsNullOrEmpty(value) || _activeChannelId is not { } channelId
            || DateTime.UtcNow - _lastComposerTypingSentAt < TypingResendInterval)
        {
            return;
        }
        _lastComposerTypingSentAt = DateTime.UtcNow;
        _ = PingTypingAsync(channelId, parentId: "");
    }

    partial void OnThreadComposeTextChanged(string value)
    {
        if (string.IsNullOrEmpty(value) || _activeChannelId is not { } channelId || _openThreadRootId is not { } rootId
            || DateTime.UtcNow - _lastThreadTypingSentAt < TypingResendInterval)
        {
            return;
        }
        _lastThreadTypingSentAt = DateTime.UtcNow;
        _ = PingTypingAsync(channelId, rootId);
    }

    private async Task PingTypingAsync(string channelId, string parentId)
    {
        try
        {
            await _service.SendTypingAsync(channelId, parentId);
        }
        catch
        {
            // Best-effort — the only thing that depends on this is someone else's "is typing" indicator, nothing on this end.
        }
    }

    /// <summary>Files already uploaded and waiting to go out with the main composer's next message.</summary>
    public ObservableCollection<PendingAttachmentItem> PendingAttachments { get; } = [];

    /// <summary>Same as <see cref="PendingAttachments"/>, for the thread reply composer.</summary>
    public ObservableCollection<PendingAttachmentItem> ThreadPendingAttachments { get; } = [];

    /// <summary>
    /// Set by MainWindow's code-behind to the platform file picker — the
    /// ViewModel can't reach <c>IStorageProvider</c> itself, since that's
    /// tied to the window (<c>TopLevel</c>), not the DataContext. Returns
    /// the chosen local paths, empty if the user cancelled.
    /// </summary>
    public Func<Task<IReadOnlyList<string>>>? PickFilesAsync { get; set; }

    /// <summary>Search panel — opened via the magnifying-glass icon in the channel header.</summary>
    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    /// <summary>True once a non-empty search has actually run — distinguishes "nothing typed yet" from "searched, found nothing" for the empty-state message.</summary>
    [ObservableProperty]
    public partial bool HasSearched { get; set; }

    [ObservableProperty]
    public partial bool HasSearchResults { get; set; }

    /// <summary>Drives the "Aucun résultat" empty state — distinct from simply "not searched yet".</summary>
    public bool NoSearchResults => HasSearched && !HasSearchResults;

    partial void OnHasSearchedChanged(bool value) => OnPropertyChanged(nameof(NoSearchResults));
    partial void OnHasSearchResultsChanged(bool value) => OnPropertyChanged(nameof(NoSearchResults));

    public ObservableCollection<SearchResultItem> SearchResults { get; } = [];

    /// <summary>Browse-and-join panel — opened via the + next to the CANAUX section header.</summary>
    [ObservableProperty]
    public partial bool IsBrowseChannelsOpen { get; set; }

    [ObservableProperty]
    public partial string BrowseChannelsQuery { get; set; } = "";

    /// <summary>True while the public-channel list is being fetched.</summary>
    [ObservableProperty]
    public partial bool IsBrowsingChannels { get; set; }

    [ObservableProperty]
    public partial string? BrowseChannelsError { get; set; }

    /// <summary>The filtered view of <see cref="_browsableChannels"/> that the panel actually lists.</summary>
    public ObservableCollection<BrowseChannelItem> BrowseChannelResults { get; } = [];

    /// <summary>Every joinable channel fetched this time the panel was opened, before the text filter.</summary>
    private List<BrowseChannelItem> _browsableChannels = [];

    /// <summary>
    /// The empty state. Deliberately false while loading or on an error, so
    /// "aucun canal" never shows on top of a spinner or an error message —
    /// three different situations that would otherwise read the same.
    /// </summary>
    public bool NoBrowseChannelResults =>
        !IsBrowsingChannels && BrowseChannelsError is null && BrowseChannelResults.Count == 0;

    partial void OnBrowseChannelsQueryChanged(string value) => ApplyBrowseChannelsFilter();
    partial void OnIsBrowsingChannelsChanged(bool value) => OnPropertyChanged(nameof(NoBrowseChannelResults));
    partial void OnBrowseChannelsErrorChanged(string? value) => OnPropertyChanged(nameof(NoBrowseChannelResults));

    /// <summary>"Start a conversation" panel — opened via the + next to the MESSAGES PRIVÉS section header.</summary>
    [ObservableProperty]
    public partial bool IsNewConversationOpen { get; set; }

    [ObservableProperty]
    public partial string NewConversationQuery { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSearchingPeople { get; set; }

    [ObservableProperty]
    public partial string? NewConversationError { get; set; }

    public ObservableCollection<DirectoryUserItem> PeopleResults { get; } = [];

    /// <summary>Same three-way distinction as the channel browser: never show "personne" over a spinner or an error.</summary>
    public bool NoPeopleResults =>
        !IsSearchingPeople && NewConversationError is null && PeopleResults.Count == 0;

    partial void OnNewConversationQueryChanged(string value) => _ = SearchPeopleAsync();
    partial void OnIsSearchingPeopleChanged(bool value) => OnPropertyChanged(nameof(NoPeopleResults));
    partial void OnNewConversationErrorChanged(string? value) => OnPropertyChanged(nameof(NoPeopleResults));

    /// <summary>Whether the thread panel is showing a thread right now — opened by clicking a message's reply count.</summary>
    [ObservableProperty]
    public partial bool IsThreadOpen { get; set; }

    [ObservableProperty]
    public partial MessageItem? ThreadRootMessage { get; set; }

    /// <summary>Collapsible sidebar sections — collapsing "Canaux" gives "Messages privés" more room, and vice versa.</summary>
    [ObservableProperty]
    public partial bool IsChannelsSectionExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDirectMessagesSectionExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsFavoritesSectionExpanded { get; set; } = true;

    /// <summary>Settings panel — opened via the gear icon at the bottom of the sidebar.</summary>
    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    /// <summary>Status picker — opened by clicking your own name/avatar at the bottom of the sidebar.</summary>
    [ObservableProperty]
    public partial bool IsStatusPickerOpen { get; set; }

    /// <summary>
    /// This user's own presence — defaults to Online since that's what a
    /// freshly-authenticated Mattermost session starts as server-side; the
    /// real value is fetched once the session is up (see LoadAsync) and
    /// updated locally the moment the user picks a new one, without waiting
    /// on a round trip.
    /// </summary>
    [ObservableProperty]
    public partial PresenceStatus MyStatus { get; set; } = PresenceStatus.Online;

    public string MyStatusLabel => MyStatus switch
    {
        PresenceStatus.Online => "Disponible",
        PresenceStatus.Away => "Absent",
        PresenceStatus.DoNotDisturb => "Ne pas déranger",
        _ => "Hors ligne",
    };

    public IBrush MyPresenceBrush => ColorTokens.Presence(MyStatus);

    partial void OnMyStatusChanged(PresenceStatus value)
    {
        OnPropertyChanged(nameof(MyStatusLabel));
        OnPropertyChanged(nameof(MyPresenceBrush));
    }

    /// <summary>Reaction picker — opened via the "+" button next to a message's reactions.</summary>
    [ObservableProperty]
    public partial bool IsEmojiPickerOpen { get; set; }

    private string? _emojiPickerTargetPostId;
    private Task? _customEmojiLoadTask;
    private readonly Dictionary<string, string> _customEmojiIdByName = [];
    private readonly Dictionary<string, Bitmap> _customEmojiImageCache = [];
    private readonly Dictionary<string, Bitmap> _avatarImageCache = [];
    private readonly Dictionary<string, Bitmap> _linkPreviewImageCache = [];

    private string? _openThreadRootId;

    public string CurrentUserDisplayName => _session.User.DisplayName;
    public string CurrentUserInitials => _session.User.Initials;

    /// <summary>Own avatar shown at the bottom of the sidebar — same fetch-once-and-cache mechanism as message authors' avatars.</summary>
    [ObservableProperty]
    public partial IBrush? MyAvatarImageBrush { get; set; }

    public AppSettings Settings { get; }

    /// <summary>
    /// Raised whenever the active channel's message list has just been
    /// (re)painted from a channel switch or the initial load — never from
    /// the background poll picking up new messages in an already-open
    /// channel, so scrolling to the newest message doesn't yank the view
    /// out from under someone who's scrolled up reading history.
    /// MainWindow's code-behind scrolls the messages ScrollViewer to the
    /// end in response, since the ViewModel has no view to scroll itself.
    /// </summary>
    public event EventHandler? ScrollMessagesToEndRequested;

    /// <summary>
    /// Raised after jumping to a specific message (from a search result) —
    /// carries the message's id. MainWindow's code-behind scrolls that
    /// specific row into view, since it may be nowhere near the end of
    /// what's currently painted.
    /// </summary>
    public event EventHandler<string>? ScrollToMessageRequested;

    /// <summary>
    /// Raised when the poll loop drains a mention notification for a
    /// channel that isn't currently open. MainWindow's code-behind shows an
    /// actual OS-level notification, since the ViewModel has no way to do
    /// that itself.
    /// </summary>
    public event EventHandler<MentionNotification>? MentionReceived;

    /// <summary>Raised right after ComposeText is loaded with a message to edit, so MainWindow's code-behind can focus the composer and put the caret at the end.</summary>
    public event EventHandler? EditComposerRequested;

    /// <summary>Raised right after selecting a channel/DM, so MainWindow's code-behind can focus the main composer — no extra click needed before typing.</summary>
    public event EventHandler? ComposerFocusRequested;

    /// <summary>Raised right after opening a thread (via "X réponses" or the reply-arrow button), so MainWindow's code-behind can focus the thread reply composer.</summary>
    public event EventHandler? ThreadComposerFocusRequested;

    /// <summary>
    /// Raised when a @mention suggestion is accepted (click or Enter/Tab)
    /// — carries the chosen username. Replacing the "@partial" token with
    /// it needs the composer TextBox's actual caret/text, which the
    /// ViewModel doesn't have direct access to, so MainWindow's code-behind
    /// does the text surgery in response to this.
    /// </summary>
    public event EventHandler<string>? MentionSuggestionAccepted;

    public ObservableCollection<ChannelItem> Channels { get; } = [];
    public ObservableCollection<DirectMessageItem> DirectMessages { get; } = [];
    public ObservableCollection<ChannelItem> FavoriteChannels { get; } = [];
    public ObservableCollection<DirectMessageItem> FavoriteDirectMessages { get; } = [];

    /// <summary>Drives the Favoris zone's "drop something here" empty-state hint.</summary>
    public bool HasFavorites => FavoriteChannels.Count > 0 || FavoriteDirectMessages.Count > 0;
    public ObservableCollection<MessageItem> Messages { get; } = [];
    public ObservableCollection<MessageItem> ThreadReplies { get; } = [];

    /// <summary>The composer's @mention autocomplete popup — shared between the main and thread composers (only one can be focused at a time); see MentionPopupIsForThread for which one is currently showing it.</summary>
    public ObservableCollection<MentionSuggestionItem> MentionSuggestions { get; } = [];

    public ObservableCollection<FontChoiceOption> FontChoiceOptions { get; } =
    [
        new()
        {
            Choice = AppFontChoice.Mattermost,
            Name = "Mattermost",
            Preview = AppFonts.Resolve(AppFontChoice.Mattermost),
        },
        new()
        {
            Choice = AppFontChoice.System,
            Name = "Système",
            Preview = AppFonts.Resolve(AppFontChoice.System),
        },
    ];

    public ObservableCollection<FontSizeOption> FontSizeOptions { get; } =
    [
        new() { Label = "Petite", Size = 13 },
        new() { Label = "Normale", Size = 14.5 },
        new() { Label = "Grande", Size = 16.5 },
    ];

    public ObservableCollection<ThemeOption> ThemeOptions { get; } =
    [
        new()
        {
            Mode = AppThemeMode.System,
            Name = "Système",
            IconData = "M12 3a9 9 0 0 0 0 18zM12 3a9 9 0 0 1 0 18",
        },
        new()
        {
            Mode = AppThemeMode.Light,
            Name = "Clair",
            IconData = "M12 7.5a4.5 4.5 0 1 0 0 9 4.5 4.5 0 0 0 0-9zM12 2v2.5M12 19.5V22M2 12h2.5M19.5 12H22M4.9 4.9l1.8 1.8M17.3 17.3l1.8 1.8M19.1 4.9l-1.8 1.8M6.7 17.3l-1.8 1.8",
        },
        new()
        {
            Mode = AppThemeMode.Dark,
            Name = "Sombre",
            IconData = "M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5z",
        },
    ];

    /// <summary>
    /// Raised whenever the theme/accent changes — MainWindow's code-behind
    /// applies the full window-resource palette, font and shape tokens in
    /// response, since the ViewModel has no view to touch itself.
    /// </summary>
    public event EventHandler? ThemeResourcesChanged;

    /// <summary>
    /// Raised after the remembered session is cleared — MainWindow's
    /// code-behind reopens a fresh login screen and closes itself in
    /// response, since the ViewModel has no way to manage windows itself.
    /// </summary>
    public event EventHandler? LoggedOut;

    public ObservableCollection<EmojiPickerItem> StandardEmojiOptions { get; } = new(
        EmojiShortcodes.PickerEntries.Select(e => new EmojiPickerItem { Name = e.Shortcode, Glyph = e.Glyph }));

    /// <summary>The server's custom emoji — loaded (cache-first, then network) the first time the picker opens.</summary>
    public ObservableCollection<EmojiPickerItem> CustomEmojiOptions { get; } = [];

    public MainViewModel(Session session)
    {
        _session = session;
        try
        {
            CoreVersion = NativeCore.GetCoreVersion();
        }
        catch
        {
            CoreVersion = "indisponible";
        }

        Settings = SettingsStore.Load();
        ApplyTheme();
        RefreshFontChoiceSelection();
        RefreshFontSizeSelection();
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.ThemeMode))
            {
                ApplyTheme();
            }
            else if (e.PropertyName == nameof(AppSettings.FontChoice))
            {
                RefreshFontChoiceSelection();
                // Same channel as a palette swap: MainWindow re-pushes the
                // window resources, AppFontFamily among them.
                ThemeResourcesChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (e.PropertyName == nameof(AppSettings.MessageFontSize))
            {
                RefreshFontSizeSelection();
            }
            SettingsStore.Save(Settings);
        };
    }

    /// <summary>
    /// Cache-first: paints instantly from whatever's already in SQLite (if
    /// anything), then refreshes from the network — which also keeps the
    /// cache warm for next time, courtesy of almatter-core's dispatcher.
    /// </summary>
    public async Task LoadAsync()
    {
        ErrorMessage = null;

        var paintedFromCache = await TryLoadFromCacheAsync();
        IsLoading = !paintedFromCache;

        try
        {
            await RefreshFromNetworkAsync();
        }
        catch (MattermostServiceException ex)
        {
            if (!paintedFromCache)
            {
                ErrorMessage = ex.Message;
            }
            // else: keep showing what the cache had; the network refresh can be retried later.
        }
        catch (Exception ex)
        {
            if (!paintedFromCache)
            {
                ErrorMessage = $"Erreur inattendue : {ex.Message}";
            }
        }
        finally
        {
            IsLoading = false;
        }

        _ = ResolveMyAvatarAsync();

        // A dropped connection just means the app keeps working off what's
        // in the cache — but the *start* call itself is awaited (not fired
        // and forgotten) and logged, since a swallowed exception here would
        // otherwise look identical to "the socket connected but nothing is
        // arriving" from the outside.
        try
        {
            await _service.StartWebSocketAsync(_session.BaseUrl, _session.Token, _session.User.Id);
            CrashLogger.Write("websocket", "start_web_socket command returned ok");
        }
        catch (Exception ex)
        {
            CrashLogger.Write("websocket", ex);
        }

        // Best-effort — MyStatus just keeps its Online default if this fails.
        try
        {
            var statuses = await _service.GetStatusesAsync(_session.BaseUrl, _session.Token, [_session.User.Id]);
            if (statuses.FirstOrDefault(s => s.UserId == _session.User.Id) is { } mine)
            {
                MyStatus = mine.Status switch
                {
                    "online" => PresenceStatus.Online,
                    "away" => PresenceStatus.Away,
                    "dnd" => PresenceStatus.DoNotDisturb,
                    _ => PresenceStatus.Offline,
                };
            }
        }
        catch
        {
        }

        StartPollingLoop();
    }

    /// <summary>
    /// The WebSocket keeps the cache warm in the background; this is what
    /// notices and repaints. Polling a local SQLite read every couple of
    /// seconds is not push-instant, but it's simple, safe, and avoids
    /// building a callback-based FFI push channel for a first cut.
    /// </summary>
    private void StartPollingLoop()
    {
        if (_pollingStarted)
        {
            return;
        }
        _pollingStarted = true;
        _pollingCts = new CancellationTokenSource();
        var cancellationToken = _pollingCts.Token;

        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var outbox = new List<OutboxItemDto>();
                try
                {
                    outbox = await _service.GetCachedOutboxAsync();
                }
                catch
                {
                    // Transient cache read hiccup — try again next tick.
                }

                if (outbox.Count > 0)
                {
                    try
                    {
                        // No-op server-side if we're still offline; a genuine
                        // rejection is already dropped from the queue core-side.
                        await _service.FlushOutboxAsync(_session.BaseUrl, _session.Token);
                    }
                    catch
                    {
                        // Still offline — try again next tick.
                    }
                }

                try
                {
                    await RefreshChannelBadgesFromCacheAsync();
                }
                catch
                {
                    // Transient cache read hiccup — try again next tick.
                }

                try
                {
                    var mentions = await _service.GetAndClearMentionEventsAsync();
                    if (mentions.Count > 0)
                    {
                        CrashLogger.Write("mentions", $"drained {mentions.Count} mention(s): {string.Join(", ", mentions.Select(m => $"{m.PostId}@{m.ChannelId}"))}, activeChannel={_activeChannelId}");
                    }
                    foreach (var mention in mentions)
                    {
                        // Already looking at that channel — no need to interrupt.
                        // Muted — the whole point is no notification for it.
                        var isMuted = _loadedChannels.FirstOrDefault(c => c.Id == mention.ChannelId)?.IsMuted ?? false;
                        if (mention.ChannelId != _activeChannelId && !isMuted)
                        {
                            await RaiseMentionNotificationAsync(mention);
                        }
                    }
                }
                catch (Exception ex)
                {
                    CrashLogger.Write("mentions", ex);
                }

                var channelId = _activeChannelId;
                if (channelId is null || _viewingJumpedMessage)
                {
                    continue;
                }

                try
                {
                    var typingUserIds = await _service.GetTypingUsersAsync(channelId);
                    var typingNames = typingUserIds.Count > 0
                        ? (await _service.GetCachedUsersAsync(typingUserIds)).Select(u => u.DisplayName).ToList()
                        : [];
                    var text = BuildTypingIndicatorText(typingNames);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (channelId == _activeChannelId)
                        {
                            TypingIndicatorText = text;
                        }
                    });
                }
                catch
                {
                    // Transient cache read hiccup — try again next tick.
                }

                try
                {
                    var posts = await _service.GetCachedPostsAsync(channelId);
                    var ids = posts.OrderBy(p => p.CreateAt).Select(p => p.Id).ToList();
                    var pendingIds = outbox
                        .Where(o => o.ChannelId == channelId && o.RootId is null)
                        .OrderBy(o => o.CreatedAt)
                        .Select(o => o.LocalId)
                        .ToList();

                    if (channelId != _activeChannelId ||
                        (ids.SequenceEqual(_lastRenderedPostIds) && pendingIds.SequenceEqual(_lastRenderedOutboxIds)))
                    {
                        continue;
                    }

                    var authorIds = posts.Select(p => p.UserId).Distinct();
                    var authors = (await _service.GetCachedUsersAsync(authorIds)).ToDictionary(u => u.Id);

                    // ObservableCollection mutations must happen on the UI thread;
                    // this loop runs on a background thread pool thread throughout.
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        if (channelId == _activeChannelId)
                        {
                            await PopulateMessagesWithOutboxAsync(channelId, posts, authors);
                            // A message arriving live while this channel is
                            // already open has effectively been seen — without
                            // this, the sidebar badge refresh above would show
                            // it as unread until the channel is re-opened.
                            MarkChannelViewed(channelId);
                        }
                    });
                }
                catch
                {
                    // Transient cache read hiccup — try again next tick.
                }

                var openRootId = _openThreadRootId;
                if (openRootId is null)
                {
                    continue;
                }
                try
                {
                    var threadPosts = await _service.GetCachedThreadAsync(openRootId);
                    if (openRootId != _openThreadRootId || threadPosts.Count == 0)
                    {
                        continue;
                    }
                    var currentRealIds = new[] { ThreadRootMessage?.Id }
                        .Concat(ThreadReplies.Where(r => !r.IsPending).Select(r => r.Id));
                    var pendingReplyIds = outbox
                        .Where(o => o.RootId == openRootId)
                        .OrderBy(o => o.CreatedAt)
                        .Select(o => o.LocalId)
                        .ToList();
                    var currentPendingIds = ThreadReplies.Where(r => r.IsPending).Select(r => r.Id).ToList();

                    if (threadPosts.Select(p => p.Id).SequenceEqual(currentRealIds) &&
                        pendingReplyIds.SequenceEqual(currentPendingIds))
                    {
                        continue;
                    }
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        if (openRootId == _openThreadRootId)
                        {
                            await PopulateThreadAsync(threadPosts);
                            await AppendPendingOutboxAsync(ThreadReplies, threadPosts[0].ChannelId, openRootId);
                        }
                    });
                }
                catch
                {
                    // Transient cache read hiccup — try again next tick.
                }
            }
        });
    }

    /// <summary>
    /// Stops the background polling loop — called on logout so the old
    /// session's ViewModel doesn't keep polling (and, worse, flushing the
    /// outbox and drawing tray notifications) forever with a token that no
    /// longer belongs to whoever is using the app next.
    /// </summary>
    private void StopPollingLoop()
    {
        _pollingCts?.Cancel();
        _pollingCts = null;
        _pollingStarted = false;
    }

    /// <summary>Resolves an author/channel name for a queued mention and raises MentionReceived for MainWindow to actually show.</summary>
    private async Task RaiseMentionNotificationAsync(MentionEventDto mention)
    {
        string authorName;
        try
        {
            var users = await _service.GetCachedUsersAsync([mention.AuthorId]);
            authorName = users.FirstOrDefault()?.DisplayName ?? "Quelqu'un";
        }
        catch
        {
            authorName = "Quelqu'un";
        }

        // A DM's "channel label" is the other participant's name — the same
        // person as the author, so appending it would just repeat itself.
        var channel = _loadedChannels.FirstOrDefault(c => c.Id == mention.ChannelId);
        var channelLabel = channel is null || channel.Type == "D" ? null : ChannelDisplayName(channel);
        var title = channelLabel is null ? authorName : $"{authorName} · {channelLabel}";
        var text = mention.Message.Length > 140 ? mention.Message[..140] + "…" : mention.Message;

        var notification = new MentionNotification
        {
            Title = title,
            Text = text,
            ChannelId = mention.ChannelId,
            PostId = mention.PostId,
        };

        CrashLogger.Write("mentions", $"raising MentionReceived: title='{title}', hasSubscribers={MentionReceived is not null}");
        await Dispatcher.UIThread.InvokeAsync(() => MentionReceived?.Invoke(this, notification));
    }

    /// <summary>
    /// Bound to a click on a channel row. Switching is meant to feel instant:
    /// selection updates immediately, the cache (if warm for this channel)
    /// paints right away, and the network refresh happens quietly after.
    /// </summary>
    [RelayCommand]
    private async Task SelectChannelAsync(string channelId)
    {
        if (string.IsNullOrEmpty(channelId) || channelId == _activeChannelId)
        {
            return;
        }

        var channel = _loadedChannels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return;
        }

        _activeChannelId = channelId;
        _viewingJumpedMessage = false;
        foreach (var item in Channels.Concat(FavoriteChannels))
        {
            item.IsSelected = item.Id == channelId;
        }
        foreach (var item in DirectMessages.Concat(FavoriteDirectMessages))
        {
            item.IsSelected = item.Id == channelId;
        }
        ActiveChannelName = ChannelDisplayName(channel);
        ActiveChannelTopic = "";
        TypingIndicatorText = "";
        ErrorMessage = null;
        MarkChannelViewed(channelId);
        ComposerFocusRequested?.Invoke(this, EventArgs.Empty);

        var paintedFromCache = await TryPaintMessagesFromCacheAsync(channelId);

        try
        {
            var posts = await _service.GetPostsAsync(_session.BaseUrl, _session.Token, channelId);
            var authorIds = posts.Select(p => p.UserId).Distinct();
            var authors = (await _service.GetUsersAsync(_session.BaseUrl, _session.Token, authorIds))
                .ToDictionary(u => u.Id);

            // The user may have already clicked away to another channel while this was in flight.
            if (channelId == _activeChannelId)
            {
                await PopulateMessagesWithOutboxAsync(channelId, posts, authors);
                ScrollMessagesToEndRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (MattermostServiceException ex)
        {
            if (!paintedFromCache && channelId == _activeChannelId)
            {
                ErrorMessage = ex.Message;
            }
        }
    }

    /// <summary>
    /// Bound to "Envoyer un message" in a message's avatar popover — opens
    /// (or resolves the existing) 1:1 DM with that author and switches to
    /// it. The server call is idempotent (returns the existing channel if
    /// one's already there), so this doesn't need to check first.
    /// </summary>
    [RelayCommand]
    private async Task OpenDirectMessageWithAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId) || userId == _session.User.Id)
        {
            return;
        }

        ChannelDto channel;
        try
        {
            channel = await _service.OpenDirectMessageAsync(_session.BaseUrl, _session.Token, _session.User.Id, userId);
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        if (_loadedChannels.All(c => c.Id != channel.Id))
        {
            _loadedChannels.Add(channel);
        }

        // A brand-new DM's own display_name is useless (see
        // PopulateDirectMessagesAsync's doc comment) — resolve the real one
        // straight from the already-known author, rather than waiting for
        // the next full channel refresh to populate it.
        var user = (await _service.GetCachedUsersAsync([userId])).FirstOrDefault();
        if (user is not null)
        {
            _dmDisplayNames[channel.Id] = user.DisplayName;
        }

        await SelectChannelAsync(channel.Id);
    }

    /// <summary>Bound to a message's "X réponses" link. Same cache-first-then-network shape as everything else here.</summary>
    [RelayCommand]
    private async Task OpenThreadAsync(string rootId)
    {
        if (string.IsNullOrEmpty(rootId))
        {
            return;
        }

        _openThreadRootId = rootId;
        IsThreadOpen = true;
        ThreadComposerFocusRequested?.Invoke(this, EventArgs.Empty);
        ThreadRootMessage = null;
        ThreadReplies.Clear();

        var paintedFromCache = await TryPaintThreadFromCacheAsync(rootId);

        try
        {
            var posts = await _service.GetThreadAsync(_session.BaseUrl, _session.Token, rootId);
            // Empty means the root was deleted (or otherwise no longer
            // available) server-side — nothing to paint over whatever the
            // cache already showed (or the empty panel, if it didn't).
            if (rootId == _openThreadRootId && posts.Count > 0)
            {
                await PopulateThreadAsync(posts);
                await AppendPendingOutboxAsync(ThreadReplies, posts[0].ChannelId, rootId);
            }
        }
        catch (MattermostServiceException ex)
        {
            if (!paintedFromCache && rootId == _openThreadRootId)
            {
                ErrorMessage = ex.Message;
            }
        }
    }

    /// <summary>
    /// Bound to the main composer's send button / Enter key — doubles as
    /// "save" when the composer is currently loaded with an edit (see
    /// StartEditMessage) rather than a new message.
    /// </summary>
    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var channelId = _activeChannelId;
        if (channelId is null)
        {
            return;
        }

        if (_editingMessageId is { } editingId)
        {
            var editedText = ComposeText.Trim();
            if (editedText.Length == 0)
            {
                return;
            }
            try
            {
                var updated = await _service.EditMessageAsync(_session.BaseUrl, _session.Token, editingId, editedText);
                ApplyEditedTextEverywhere(editingId, updated.Message);
                ClearIsBeingEdited(editingId);
                _editingMessageId = null;
                IsEditingMessage = false;
                ComposeText = "";
            }
            catch (MattermostServiceException ex)
            {
                // Left in edit mode on failure — ComposeText keeps the edit, the user can just retry.
                ErrorMessage = ex.Message;
            }
            return;
        }

        var text = ComposeText.Trim();
        if (text.Length == 0 && PendingAttachments.Count == 0)
        {
            return;
        }

        var fileIds = PendingAttachments.Select(a => a.FileId).ToList();
        ComposeText = "";
        PendingAttachments.Clear();
        await SendAsync(channelId, rootId: null, text, fileIds);
    }

    /// <summary>Bound to the thread panel's reply composer send button / Enter key.</summary>
    [RelayCommand]
    private async Task SendThreadReplyAsync()
    {
        var channelId = _activeChannelId;
        var rootId = _openThreadRootId;
        var text = ThreadComposeText.Trim();
        if (channelId is null || rootId is null || (text.Length == 0 && ThreadPendingAttachments.Count == 0))
        {
            return;
        }

        var fileIds = ThreadPendingAttachments.Select(a => a.FileId).ToList();
        ThreadComposeText = "";
        ThreadPendingAttachments.Clear();
        await SendAsync(channelId, rootId, text, fileIds);
    }

    /// <summary>Bound to the main composer's paperclip button.</summary>
    [RelayCommand]
    private Task AttachFileAsync() => AttachFilesAsync(PendingAttachments);

    /// <summary>Bound to the thread reply composer's paperclip button.</summary>
    [RelayCommand]
    private Task AttachThreadFileAsync() => AttachFilesAsync(ThreadPendingAttachments);

    [RelayCommand]
    private void RemovePendingAttachment(PendingAttachmentItem item) => PendingAttachments.Remove(item);

    [RelayCommand]
    private void RemoveThreadPendingAttachment(PendingAttachmentItem item) => ThreadPendingAttachments.Remove(item);

    /// <summary>
    /// Opens the platform file picker (via the code-behind-supplied
    /// callback) and uploads whatever was chosen right away — before the
    /// message itself is sent — so the composer can show a real filename
    /// chip and the eventual send is just attaching an id that already
    /// exists server-side, not waiting on the upload too.
    /// </summary>
    private async Task AttachFilesAsync(ObservableCollection<PendingAttachmentItem> target)
    {
        var channelId = _activeChannelId;
        if (channelId is null || PickFilesAsync is null)
        {
            return;
        }

        IReadOnlyList<string> paths;
        try
        {
            paths = await PickFilesAsync();
        }
        catch
        {
            return;
        }

        foreach (var path in paths)
        {
            try
            {
                var file = await _service.UploadFileAsync(_session.BaseUrl, _session.Token, channelId, path);
                target.Add(new PendingAttachmentItem { FileId = file.Id, FileName = file.Name });
            }
            catch (MattermostServiceException ex)
            {
                ErrorMessage = ex.Message;
            }
        }
    }

    /// <summary>
    /// Fires the send at the core, which either posts it right away or
    /// queues it in the offline outbox — either way this repaints
    /// immediately from the cache rather than waiting for the next
    /// 2-second poll tick, so a pending bubble (or the confirmed post)
    /// shows up instantly. A genuine server-side rejection (not a
    /// connectivity failure) surfaces as a real error instead.
    /// </summary>
    private async Task SendAsync(string channelId, string? rootId, string message, IReadOnlyList<string>? fileIds = null)
    {
        var localId = "local-" + Guid.NewGuid().ToString("N");
        try
        {
            await _service.SendMessageAsync(_session.BaseUrl, _session.Token, channelId, rootId, localId, message, fileIds);
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
        }

        if (rootId is null && channelId == _activeChannelId)
        {
            await TryPaintMessagesFromCacheAsync(channelId);
            ScrollMessagesToEndRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (rootId is not null && rootId == _openThreadRootId)
        {
            await TryPaintThreadFromCacheAsync(rootId);
        }
    }

    /// <summary>
    /// Bound to the up-arrow key in the (empty) main composer — the same
    /// shortcut the official client uses to jump straight into editing the
    /// last thing you sent, without hunting for it in the list.
    /// </summary>
    public void EditLastOwnMessage()
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i].IsMine && !Messages[i].IsPending)
            {
                StartEditMessage(Messages[i]);
                return;
            }
        }
    }

    /// <summary>Loaded into the composer while editing — see StartEditMessage.</summary>
    [ObservableProperty]
    public partial bool IsEditingMessage { get; set; }

    private string? _editingMessageId;

    /// <summary>
    /// Loads a message into the main composer for editing, in place of
    /// typing a new one — SendMessageCommand notices IsEditingMessage and
    /// saves instead of sending. Editing happens in the composer rather
    /// than inline in the conversation on purpose: an inline edit box used
    /// to change that row's height, and with the message list virtualized,
    /// that visibly jumped/scrolled the whole conversation the moment you
    /// clicked "modifier".
    /// </summary>
    [RelayCommand]
    private void StartEditMessage(MessageItem item)
    {
        if (!item.IsMine)
        {
            return;
        }
        CancelEditMessage();
        _editingMessageId = item.Id;
        IsEditingMessage = true;
        ComposeText = item.Text;
        item.IsBeingEdited = true;
        EditComposerRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CancelEditMessage()
    {
        if (_editingMessageId is { } id)
        {
            ClearIsBeingEdited(id);
        }
        _editingMessageId = null;
        IsEditingMessage = false;
        ComposeText = "";
    }

    /// <summary>A reply shows both inline in the main list and in the thread panel, each its own separate MessageItem for the same post — this touches whichever of those exist, not just one.</summary>
    private void ApplyEditedTextEverywhere(string postId, string newText)
    {
        if (Messages.FirstOrDefault(m => m.Id == postId) is { } inMain)
        {
            inMain.ApplyEditedText(newText);
        }
        if (ThreadReplies.FirstOrDefault(m => m.Id == postId) is { } inThread)
        {
            inThread.ApplyEditedText(newText);
        }
        if (ThreadRootMessage?.Id == postId)
        {
            ThreadRootMessage.ApplyEditedText(newText);
        }
    }

    private void ClearIsBeingEdited(string postId)
    {
        if (Messages.FirstOrDefault(m => m.Id == postId) is { } inMain)
        {
            inMain.IsBeingEdited = false;
        }
        if (ThreadReplies.FirstOrDefault(m => m.Id == postId) is { } inThread)
        {
            inThread.IsBeingEdited = false;
        }
        if (ThreadRootMessage?.Id == postId)
        {
            ThreadRootMessage.IsBeingEdited = false;
        }
    }

    /// <summary>The message a delete is about to be confirmed for — set by RequestDeleteMessage, read by the confirmation panel.</summary>
    [ObservableProperty]
    public partial bool IsDeleteConfirmOpen { get; set; }

    private MessageItem? _pendingDeleteMessage;

    [RelayCommand]
    private void RequestDeleteMessage(MessageItem item)
    {
        if (!item.IsMine)
        {
            return;
        }
        _pendingDeleteMessage = item;
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private void CancelDeleteMessage()
    {
        _pendingDeleteMessage = null;
        IsDeleteConfirmOpen = false;
    }

    [RelayCommand]
    private async Task ConfirmDeleteMessageAsync()
    {
        var item = _pendingDeleteMessage;
        _pendingDeleteMessage = null;
        IsDeleteConfirmOpen = false;
        if (item is null)
        {
            return;
        }

        try
        {
            await _service.DeleteMessageAsync(_session.BaseUrl, _session.Token, item.Id);
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        if (_editingMessageId == item.Id)
        {
            // Nothing left to save the composer's edit onto.
            _editingMessageId = null;
            IsEditingMessage = false;
            ComposeText = "";
        }

        // A reply shows both inline in the main list and in the thread
        // panel — each holds its own separate MessageItem instance for the
        // same underlying post, so this removes by id from both rather than
        // just the one instance that was actually clicked.
        if (Messages.FirstOrDefault(m => m.Id == item.Id) is { } inMain)
        {
            Messages.Remove(inMain);
        }
        if (ThreadReplies.FirstOrDefault(m => m.Id == item.Id) is { } inThread)
        {
            ThreadReplies.Remove(inThread);
        }
        if (ThreadRootMessage?.Id == item.Id)
        {
            // The rest of the thread has nothing to attach to any more.
            CloseThread();
        }
    }

    /// <summary>Open while there's an @mention query in flight or with results to show — see MentionPopupIsForThread for which composer it belongs to.</summary>
    [ObservableProperty]
    public partial bool IsMentionPopupOpen { get; set; }

    [ObservableProperty]
    public partial int MentionSelectedIndex { get; set; }

    /// <summary>True while the open popup belongs to the thread reply composer rather than the main one — only one composer is ever focused at a time, so this is what tells the two popup regions in XAML apart.</summary>
    [ObservableProperty]
    public partial bool MentionPopupIsForThread { get; set; }

    public bool IsMainMentionPopupOpen => IsMentionPopupOpen && !MentionPopupIsForThread;
    public bool IsThreadMentionPopupOpen => IsMentionPopupOpen && MentionPopupIsForThread;

    partial void OnIsMentionPopupOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMainMentionPopupOpen));
        OnPropertyChanged(nameof(IsThreadMentionPopupOpen));
    }

    partial void OnMentionPopupIsForThreadChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMainMentionPopupOpen));
        OnPropertyChanged(nameof(IsThreadMentionPopupOpen));
    }

    partial void OnMentionSelectedIndexChanged(int value)
    {
        for (var i = 0; i < MentionSuggestions.Count; i++)
        {
            MentionSuggestions[i].IsSelected = i == value;
        }
    }

    private CancellationTokenSource? _mentionSearchCts;

    /// <summary>
    /// Called by MainWindow's code-behind whenever the composer's text or
    /// caret moves in or out of an "@partial" token (see
    /// MentionTextHelper.TryGetMentionQuery) — searches the active
    /// channel's members matching query and populates MentionSuggestions.
    /// A cancellation token guards against an earlier, slower search
    /// resolving after a newer one and clobbering its (more current) results.
    /// </summary>
    public async Task UpdateMentionSuggestionsAsync(string query, bool isThread)
    {
        _mentionSearchCts?.Cancel();
        if (_activeChannelId is not { } channelId)
        {
            CloseMentionPopup();
            return;
        }
        var cts = new CancellationTokenSource();
        _mentionSearchCts = cts;
        MentionPopupIsForThread = isThread;

        List<UserDto> results;
        try
        {
            results = await _service.SearchUsersAsync(_session.BaseUrl, _session.Token, channelId, query);
        }
        catch (MattermostServiceException)
        {
            // Best-effort — a transient failure just means no suggestions this keystroke, not a disruption to typing.
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        MentionSuggestions.Clear();
        foreach (var user in results.Take(8))
        {
            MentionSuggestions.Add(new MentionSuggestionItem
            {
                UserId = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Initials = user.Initials,
                AvatarHex = AvatarColorFor(user.Id),
            });
        }
        MentionSelectedIndex = 0;
        IsMentionPopupOpen = MentionSuggestions.Count > 0;
    }

    public void CloseMentionPopup()
    {
        _mentionSearchCts?.Cancel();
        IsMentionPopupOpen = false;
        MentionSuggestions.Clear();
    }

    public void MoveMentionSelection(int delta)
    {
        if (MentionSuggestions.Count == 0)
        {
            return;
        }
        var next = (MentionSelectedIndex + delta + MentionSuggestions.Count) % MentionSuggestions.Count;
        MentionSelectedIndex = next;
    }

    /// <summary>Enter/Tab while the popup is open accepts whichever row the arrow keys last highlighted.</summary>
    public void AcceptSelectedMentionSuggestion()
    {
        if (MentionSelectedIndex < 0 || MentionSelectedIndex >= MentionSuggestions.Count)
        {
            CloseMentionPopup();
            return;
        }
        PickMentionSuggestion(MentionSuggestions[MentionSelectedIndex]);
    }

    [RelayCommand]
    private void PickMentionSuggestion(MentionSuggestionItem item)
    {
        MentionSuggestionAccepted?.Invoke(this, item.Username);
        CloseMentionPopup();
    }

    [RelayCommand]
    private void ToggleChannelsSection() => IsChannelsSectionExpanded = !IsChannelsSectionExpanded;

    [RelayCommand]
    private void ToggleDirectMessagesSection() => IsDirectMessagesSectionExpanded = !IsDirectMessagesSectionExpanded;

    [RelayCommand]
    private void ToggleFavoritesSection() => IsFavoritesSectionExpanded = !IsFavoritesSectionExpanded;

    /// <summary>
    /// Called from MainWindow's code-behind when a channel/DM row is
    /// dragged onto the Favoris zone (isFavorite: true) or back onto the
    /// regular Canaux/Messages privés zone (isFavorite: false) — drag-and-drop
    /// replaced the earlier star-button toggle, which is why this takes an
    /// explicit target state rather than flipping the current one.
    /// </summary>
    public async Task SetFavoriteAsync(string channelId, bool isFavorite)
    {
        if (_favoriteChannelIds.Contains(channelId) == isFavorite)
        {
            return;
        }

        try
        {
            await _service.SetFavoriteAsync(_session.BaseUrl, _session.Token, _session.User.Id, channelId, isFavorite);
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        if (isFavorite)
        {
            _favoriteChannelIds.Add(channelId);
        }
        else
        {
            _favoriteChannelIds.Remove(channelId);
        }

        // A cheap local repaint from what's already loaded — no network round trip needed.
        var visibleChannels = _loadedChannels.Where(c => c.IsPublicOrPrivate).OrderBy(c => c.DisplayName).ToList();
        PopulateChannels(visibleChannels, _activeChannelId ?? "");
        await PopulateDirectMessagesAsync(_loadedChannels, allowNetwork: false);
    }

    /// <summary>
    /// Called from MainWindow's code-behind after a long-press-drag within
    /// Favoris — moves the dragged item to sit right before/after the drop
    /// target, in whichever of FavoriteChannels/FavoriteDirectMessages both
    /// ids actually belong to (dragging a channel onto a DM row, or vice
    /// versa, is simply a no-op). The resulting order is saved locally,
    /// since Mattermost's favorite preference is just a set with no order
    /// of its own.
    /// </summary>
    internal void ReorderFavorite(string draggedId, string targetId, bool placeAfter)
    {
        if (draggedId == targetId)
        {
            return;
        }

        var moved = MoveWithinCollection(FavoriteChannels, draggedId, targetId, placeAfter)
            || MoveWithinCollection(FavoriteDirectMessages, draggedId, targetId, placeAfter);
        if (!moved)
        {
            return;
        }

        // Setting this triggers an automatic save via the Settings.PropertyChanged hook in the constructor.
        Settings.FavoriteOrder = FavoriteChannels.Select(c => c.Id)
            .Concat(FavoriteDirectMessages.Select(d => d.Id))
            .ToList();
    }

    private static bool MoveWithinCollection<T>(ObservableCollection<T> collection, string draggedId, string targetId, bool placeAfter)
        where T : IChannelListItem
    {
        var draggedIndex = IndexOfId(collection, draggedId);
        var targetIndex = IndexOfId(collection, targetId);
        if (draggedIndex < 0 || targetIndex < 0)
        {
            return false;
        }

        var item = collection[draggedIndex];
        collection.RemoveAt(draggedIndex);

        // The target may have shifted left by one now that the dragged item is gone.
        var adjustedTargetIndex = IndexOfId(collection, targetId);
        var insertIndex = Math.Clamp(placeAfter ? adjustedTargetIndex + 1 : adjustedTargetIndex, 0, collection.Count);
        collection.Insert(insertIndex, item);
        return true;
    }

    private static int IndexOfId<T>(ObservableCollection<T> collection, string id) where T : IChannelListItem
    {
        for (var i = 0; i < collection.Count; i++)
        {
            if (collection[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Favorites follow Settings.FavoriteOrder; anything not yet in that list keeps its natural (display-name/recency) order, appended at the end — OrderBy is a stable sort, so ties preserve input order.</summary>
    private IEnumerable<T> OrderByFavoritePosition<T>(List<T> favorites) where T : IChannelListItem
    {
        var order = Settings.FavoriteOrder;
        return favorites.OrderBy(item =>
        {
            var index = order.IndexOf(item.Id);
            return index < 0 ? int.MaxValue : index;
        });
    }

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void ToggleStatusPicker() => IsStatusPickerOpen = !IsStatusPickerOpen;

    [RelayCommand]
    private void CloseStatusPicker() => IsStatusPickerOpen = false;

    /// <summary>
    /// Bound to each option in the status picker — <paramref name="status"/>
    /// is one of Mattermost's own status strings ("online"/"away"/"dnd"/
    /// "offline"), so no extra mapping is needed at the call site. Updates
    /// locally right away rather than waiting on the network round trip —
    /// presence is a low-stakes, purely cosmetic value, and an optimistic
    /// update reads as instant the same way the official client's does.
    /// </summary>
    [RelayCommand]
    private async Task SetStatusAsync(string status)
    {
        IsStatusPickerOpen = false;
        MyStatus = status switch
        {
            "online" => PresenceStatus.Online,
            "away" => PresenceStatus.Away,
            "dnd" => PresenceStatus.DoNotDisturb,
            _ => PresenceStatus.Offline,
        };
        try
        {
            await _service.SetStatusAsync(_session.BaseUrl, _session.Token, _session.User.Id, status);
        }
        catch (MattermostServiceException)
        {
            // Best-effort — the local UI already reflects the choice; a
            // failed round trip just means it could revert to the server's
            // idea of the status next time presence is refreshed.
        }
    }

    /// <summary>Clears the remembered session — MainWindow's code-behind takes it from here (fresh login screen, closes this window).</summary>
    [RelayCommand]
    private async Task LogOutAsync()
    {
        StopPollingLoop();
        try
        {
            await _service.StopWebSocketAsync();
        }
        catch (MattermostServiceException)
        {
            // Best-effort — worst case the old connection lingers until it
            // drops on its own; nothing else depends on this succeeding.
        }
        SessionStore.Clear();
        LoggedOut?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenSearch() => IsSearchOpen = true;

    [RelayCommand]
    private void CloseSearch() => IsSearchOpen = false;

    /// <summary>
    /// Opens the browse panel and fetches the team's public channels.
    /// Network-only and re-fetched on every open: this is a discovery list,
    /// and offering a channel that has since been archived (or hiding one
    /// just created) is worse than a brief spinner.
    /// </summary>
    [RelayCommand]
    private async Task OpenBrowseChannelsAsync()
    {
        IsBrowseChannelsOpen = true;
        BrowseChannelsQuery = "";
        BrowseChannelsError = null;
        _browsableChannels = [];
        BrowseChannelResults.Clear();

        if (_teamId is null)
        {
            BrowseChannelsError = "Équipe inconnue — reconnectez-vous.";
            return;
        }

        IsBrowsingChannels = true;
        try
        {
            var channels = await _service.GetPublicChannelsAsync(_session.BaseUrl, _session.Token, _teamId);

            // The server lists every public channel, joined or not; only the
            // client knows which ones are already in the sidebar.
            var joinedIds = _loadedChannels.Select(c => c.Id).ToHashSet();
            _browsableChannels = channels
                .Where(c => !joinedIds.Contains(c.Id))
                .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(c => new BrowseChannelItem
                {
                    Id = c.Id,
                    DisplayName = c.DisplayName,
                    Name = c.Name,
                    Purpose = c.Purpose,
                })
                .ToList();
            ApplyBrowseChannelsFilter();
        }
        catch (MattermostServiceException ex)
        {
            BrowseChannelsError = ex.Message;
        }
        catch (Exception ex)
        {
            BrowseChannelsError = $"Erreur inattendue : {ex.Message}";
        }
        finally
        {
            IsBrowsingChannels = false;
        }
    }

    [RelayCommand]
    private void CloseBrowseChannels() => IsBrowseChannelsOpen = false;

    /// <summary>Opens the "start a conversation" panel with the team already listed, so it's useful before a single keystroke.</summary>
    [RelayCommand]
    private async Task OpenNewConversationAsync()
    {
        IsNewConversationOpen = true;
        NewConversationError = null;
        PeopleResults.Clear();

        // Setting this fires SearchPeopleAsync via OnNewConversationQueryChanged —
        // but only if the value actually changes, so an already-empty query
        // (the usual case) needs the explicit call below.
        if (NewConversationQuery.Length == 0)
        {
            await SearchPeopleAsync();
        }
        else
        {
            NewConversationQuery = "";
        }
    }

    [RelayCommand]
    private void CloseNewConversation() => IsNewConversationOpen = false;

    private CancellationTokenSource? _peopleSearchCts;

    /// <summary>
    /// Searches the whole team, server-side, on every keystroke — unlike the
    /// channel browser, the directory is too big to hold locally and filter.
    /// A cancellation token guards against a slower earlier search landing
    /// after a newer one and replacing more current results.
    /// </summary>
    private async Task SearchPeopleAsync()
    {
        if (!IsNewConversationOpen || _teamId is not { } teamId)
        {
            return;
        }

        _peopleSearchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _peopleSearchCts = cts;

        IsSearchingPeople = true;
        NewConversationError = null;
        List<UserDto> results;
        try
        {
            results = await _service.SearchTeamUsersAsync(_session.BaseUrl, _session.Token, teamId, NewConversationQuery.Trim());
        }
        catch (MattermostServiceException ex)
        {
            if (!cts.IsCancellationRequested)
            {
                NewConversationError = ex.Message;
                IsSearchingPeople = false;
            }
            return;
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                NewConversationError = $"Erreur inattendue : {ex.Message}";
                IsSearchingPeople = false;
            }
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        PeopleResults.Clear();
        foreach (var user in results.Where(u => u.Id != _session.User.Id).Take(50))
        {
            PeopleResults.Add(new DirectoryUserItem
            {
                UserId = user.Id,
                DisplayName = user.DisplayName,
                Username = user.Username,
                Initials = user.Initials,
                AvatarHex = AvatarColorFor(user.Id),
            });
        }
        IsSearchingPeople = false;
        OnPropertyChanged(nameof(NoPeopleResults));
    }

    /// <summary>Closes the panel and hands off to the same open-or-create-DM path the avatar popover uses.</summary>
    [RelayCommand]
    private async Task StartConversationAsync(DirectoryUserItem item)
    {
        IsNewConversationOpen = false;
        await OpenDirectMessageWithAsync(item.UserId);
    }

    /// <summary>Filters the already-fetched list as the user types — no round trip, the whole list is in memory.</summary>
    private void ApplyBrowseChannelsFilter()
    {
        var query = BrowseChannelsQuery.Trim();
        BrowseChannelResults.Clear();
        foreach (var channel in _browsableChannels)
        {
            var matches = query.Length == 0
                || channel.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || channel.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            if (matches)
            {
                BrowseChannelResults.Add(channel);
            }
        }
        OnPropertyChanged(nameof(NoBrowseChannelResults));
    }

    /// <summary>
    /// Joins a channel, then reloads the sidebar from the network and opens
    /// it. The full reload is deliberate: joining changes this user's
    /// membership list server-side, and re-running the normal load path is
    /// what keeps read state, favorites and ordering consistent rather than
    /// hand-patching a row into the list.
    /// </summary>
    [RelayCommand]
    private async Task JoinChannelAsync(BrowseChannelItem item)
    {
        if (item.IsJoining)
        {
            return;
        }

        item.IsJoining = true;
        try
        {
            await _service.JoinChannelAsync(_session.BaseUrl, _session.Token, item.Id, _session.User.Id);
            IsBrowseChannelsOpen = false;
            await RefreshFromNetworkAsync();
            await SelectChannelAsync(item.Id);
        }
        catch (MattermostServiceException ex)
        {
            BrowseChannelsError = ex.Message;
            IsBrowseChannelsOpen = true;
        }
        catch (Exception ex)
        {
            BrowseChannelsError = $"Erreur inattendue : {ex.Message}";
            IsBrowseChannelsOpen = true;
        }
        finally
        {
            item.IsJoining = false;
        }
    }

    /// <summary>
    /// Bound to the search box's Enter key. Cache-first (instant, but only
    /// covers whatever's already been cached) then a real server-side
    /// search across the whole team's full history — same shape as every
    /// other cache-then-network read in this class.
    /// </summary>
    [RelayCommand]
    private async Task RunSearchAsync()
    {
        var query = SearchQuery.Trim();
        SearchResults.Clear();
        HasSearchResults = false;
        HasSearched = query.Length > 0;
        if (query.Length == 0)
        {
            return;
        }

        try
        {
            await PopulateSearchResultsAsync(await _service.SearchCachedMessagesAsync(query));
        }
        catch
        {
            // A cold cache / read glitch just means we wait for the network result below.
        }

        if (_teamId is null)
        {
            return;
        }

        try
        {
            var results = await _service.SearchMessagesAsync(_session.BaseUrl, _session.Token, _teamId, query);
            // The user may have already changed the search box while this was in flight.
            if (query == SearchQuery.Trim())
            {
                await PopulateSearchResultsAsync(results);
            }
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task PopulateSearchResultsAsync(List<PostDto> posts)
    {
        if (posts.Count == 0)
        {
            return;
        }

        var authorIds = posts.Select(p => p.UserId).Distinct();
        var authors = (await _service.GetCachedUsersAsync(authorIds)).ToDictionary(u => u.Id);
        var missing = authorIds.Where(id => !authors.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            try
            {
                foreach (var user in await _service.GetUsersAsync(_session.BaseUrl, _session.Token, missing))
                {
                    authors[user.Id] = user;
                }
            }
            catch
            {
                // A missing author just shows as "Utilisateur inconnu" below — not worth failing the whole search over.
            }
        }

        SearchResults.Clear();
        foreach (var post in posts.OrderByDescending(p => p.CreateAt))
        {
            var channel = _loadedChannels.FirstOrDefault(c => c.Id == post.ChannelId);
            authors.TryGetValue(post.UserId, out var author);
            SearchResults.Add(new SearchResultItem
            {
                PostId = post.Id,
                ChannelId = post.ChannelId,
                ChannelLabel = channel is null ? "Canal inconnu" : ChannelDisplayName(channel),
                AuthorName = author?.DisplayName ?? "Utilisateur inconnu",
                AuthorInitials = author?.Initials ?? "?",
                AvatarHex = AvatarColorFor(post.UserId),
                TimeLabel = FormatSearchResultTime(post.CreateAt),
                Text = post.Message,
            });
        }
        HasSearchResults = SearchResults.Count > 0;
    }

    /// <summary>Bound to a search result row — jumps to that exact message and closes the search panel.</summary>
    [RelayCommand]
    private async Task OpenSearchResultAsync(SearchResultItem result)
    {
        IsSearchOpen = false;
        await JumpToMessageAsync(result.ChannelId, result.PostId);
    }

    /// <summary>Bound to a reply's quoted-root preview — same jump/highlight behavior as a search result, just already knowing which channel and post.</summary>
    [RelayCommand]
    private Task JumpToQuotedMessage(MessageItem item)
    {
        if (item.ThreadRootId is not { } rootId || _activeChannelId is not { } channelId)
        {
            return Task.CompletedTask;
        }
        return JumpToMessageAsync(channelId, rootId);
    }

    /// <summary>
    /// Unlike SelectChannelAsync, this always re-fetches even if the target
    /// channel is already open — "already open" doesn't mean this specific
    /// message is currently painted, since the channel view normally only
    /// shows the most recent page.
    /// </summary>
    internal async Task JumpToMessageAsync(string channelId, string postId)
    {
        var channel = _loadedChannels.FirstOrDefault(c => c.Id == channelId);
        if (channel is null)
        {
            return;
        }

        _activeChannelId = channelId;
        foreach (var item in Channels.Concat(FavoriteChannels))
        {
            item.IsSelected = item.Id == channelId;
        }
        foreach (var item in DirectMessages.Concat(FavoriteDirectMessages))
        {
            item.IsSelected = item.Id == channelId;
        }
        ActiveChannelName = ChannelDisplayName(channel);
        ActiveChannelTopic = "";
        TypingIndicatorText = "";
        ErrorMessage = null;
        MarkChannelViewed(channelId);

        try
        {
            var posts = await _service.GetPostsAroundMessageAsync(_session.BaseUrl, _session.Token, channelId, postId);
            var authorIds = posts.Select(p => p.UserId).Distinct();
            var authors = (await _service.GetUsersAsync(_session.BaseUrl, _session.Token, authorIds))
                .ToDictionary(u => u.Id);

            if (channelId != _activeChannelId)
            {
                return;
            }

            await PopulateMessagesWithOutboxAsync(channelId, posts, authors);
            _viewingJumpedMessage = true;

            var target = Messages.FirstOrDefault(m => m.Id == postId);
            if (target is not null)
            {
                ScrollToMessageRequested?.Invoke(this, target.Id);
                target.IsHighlighted = true;
                _ = ClearHighlightAfterDelayAsync(target);
            }
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static async Task ClearHighlightAfterDelayAsync(MessageItem item)
    {
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        item.IsHighlighted = false;
    }

    [RelayCommand]
    private void SetFontSize(double size) => Settings.MessageFontSize = size;

    /// <summary>Opens a clicked message link in the user's default browser.</summary>
    [RelayCommand]
    private void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // A malformed or unsupported URL just doesn't open — no crash.
        }
    }

    /// <summary>Bound to a click on a message attachment: downloads it (once — cached to disk after that), then opens it with the OS's own default handler for that file type.</summary>
    [RelayCommand]
    private async Task OpenAttachmentAsync(AttachmentItem attachment)
    {
        if (attachment.IsDownloading)
        {
            return;
        }

        attachment.IsDownloading = true;
        try
        {
            var path = await _service.GetFilePathAsync(_session.BaseUrl, _session.Token, attachment.Id, attachment.FileName);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch
        {
            // No app associated with this file type, or the OS declined to open it — not worth an app-wide error banner.
        }
        finally
        {
            attachment.IsDownloading = false;
        }
    }

    /// <summary>Toggles the current user's own reaction on a message — clicking a reaction pill you already reacted with removes it.</summary>
    [RelayCommand]
    private async Task ToggleReactionAsync(ReactionItem reaction)
    {
        try
        {
            var updated = reaction.ReactedByMe
                ? await _service.RemoveReactionAsync(_session.BaseUrl, _session.Token, _session.User.Id, reaction.PostId, reaction.EmojiName)
                : await _service.AddReactionAsync(_session.BaseUrl, _session.Token, _session.User.Id, reaction.PostId, reaction.EmojiName);
            ApplyUpdatedReactions(reaction.PostId, updated);
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Bound to a message's "+" button — opens the emoji picker targeting that specific message.</summary>
    [RelayCommand]
    private async Task OpenEmojiPickerAsync(string postId)
    {
        _emojiPickerTargetPostId = postId;
        IsEmojiPickerOpen = true;
        await EnsureCustomEmojiLoadedAsync();
    }

    [RelayCommand]
    private void CloseEmojiPicker() => IsEmojiPickerOpen = false;

    /// <summary>Bound to a swatch in the emoji picker — adds that reaction to whichever message opened the picker.</summary>
    [RelayCommand]
    private async Task PickEmojiAsync(string emojiName)
    {
        var postId = _emojiPickerTargetPostId;
        IsEmojiPickerOpen = false;
        if (postId is null)
        {
            return;
        }

        try
        {
            var updated = await _service.AddReactionAsync(_session.BaseUrl, _session.Token, _session.User.Id, postId, emojiName);
            ApplyUpdatedReactions(postId, updated);
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Rebuilds one message's reactions from a freshly re-fetched post, wherever that message is currently shown.</summary>
    private void ApplyUpdatedReactions(string postId, PostDto updatedPost)
    {
        var fresh = BuildReactions(updatedPost);

        void Apply(MessageItem? item)
        {
            if (item is null || item.Id != postId)
            {
                return;
            }
            item.Reactions.Clear();
            foreach (var r in fresh)
            {
                item.Reactions.Add(r);
            }
            item.NotifyReactionsChanged();
        }

        foreach (var message in Messages)
        {
            Apply(message);
        }
        foreach (var reply in ThreadReplies)
        {
            Apply(reply);
        }
        Apply(ThreadRootMessage);
    }

    /// <summary>
    /// Cache-first, then network — loaded once per session, the first time
    /// either the picker opens or a message with a custom-emoji reaction is
    /// built. The task itself (not just a bool flag) is cached so that
    /// several callers racing to trigger the first load all await the same
    /// in-flight work instead of some of them reading name/id data that
    /// isn't there yet.
    /// </summary>
    private Task EnsureCustomEmojiLoadedAsync() => _customEmojiLoadTask ??= LoadCustomEmojiListAsync();

    private async Task LoadCustomEmojiListAsync()
    {
        try
        {
            PopulateCustomEmojiOptions(await _service.GetCachedCustomEmojiAsync());
        }
        catch (Exception ex)
        {
            // A cold cache just means the list starts empty until the network call below lands.
            CrashLogger.Write("custom emoji: cached list read failed", ex);
        }

        try
        {
            var emoji = await _service.GetCustomEmojiAsync(_session.BaseUrl, _session.Token);
            CrashLogger.Write("custom emoji: network list", $"{emoji.Count} emoji: {string.Join(", ", emoji.Select(e => e.Name))}");
            PopulateCustomEmojiOptions(emoji);
        }
        catch (Exception ex)
        {
            // Offline or the server rejected it — the cached (possibly empty) list stands.
            CrashLogger.Write("custom emoji: network list fetch failed", ex);
        }
    }

    private void PopulateCustomEmojiOptions(List<CustomEmojiDto> emoji)
    {
        CustomEmojiOptions.Clear();
        _customEmojiIdByName.Clear();
        foreach (var e in emoji)
        {
            _customEmojiIdByName[e.Name] = e.Id;
            var item = new EmojiPickerItem { Name = e.Name, EmojiId = e.Id };
            CustomEmojiOptions.Add(item);
            _ = LoadCustomEmojiImageAsync(item);
        }
    }

    /// <summary>Downloads (or reads back from disk, once cached by the core) one custom emoji's image off the UI thread.</summary>
    private async Task LoadCustomEmojiImageAsync(EmojiPickerItem item)
    {
        if (item.EmojiId is not null)
        {
            item.Image = await GetCustomEmojiBitmapAsync(item.EmojiId);
        }
    }

    /// <summary>
    /// Resolves a message reaction's emoji, if it's not one of the standard
    /// set — almost always a server custom emoji, whose image is fetched
    /// (and shared with the picker's own swatch, via the same bitmap cache)
    /// rather than the ":name:" text fallback staying up forever.
    /// </summary>
    private async Task ResolveReactionImageAsync(ReactionItem reaction)
    {
        if (EmojiShortcodes.IsKnown(reaction.EmojiName))
        {
            return;
        }

        await EnsureCustomEmojiLoadedAsync();
        // A reaction not found here is completely normal — most reactions use
        // a standard emoji EmojiShortcodes doesn't happen to list by name,
        // not a real custom-server one — so it stays as the ":name:" text
        // fallback with nothing logged. (This used to log unconditionally,
        // rebuilding and disk-writing the full ~200-name known list on every
        // occurrence — for a channel with many such reactions, that alone
        // was a significant, entirely avoidable source of per-render lag.)
        if (_customEmojiIdByName.TryGetValue(reaction.EmojiName, out var emojiId))
        {
            reaction.Image = await GetCustomEmojiBitmapAsync(emojiId);
        }
    }

    /// <summary>
    /// Shared by every kind of image fetch (custom emoji, avatars): opening
    /// a channel with a lot of reaction/author variety used to fire one
    /// fetch per distinct image all at once — dozens of simultaneous new
    /// connections, each paying its own connection-setup cost, which in
    /// aggregate was slow enough to delay unrelated concurrent work sharing
    /// the same pool (the channel's own message fetch included). Capping how
    /// many run at a time keeps images loading, just a little more gradually,
    /// without that contention.
    /// </summary>
    private readonly SemaphoreSlim _imageFetchThrottle = new(3);

    /// <summary>Shared by the picker's swatches and message reactions so the same custom emoji's image is only ever downloaded/decoded once per session.</summary>
    private async Task<Bitmap?> GetCustomEmojiBitmapAsync(string emojiId)
    {
        if (_customEmojiImageCache.TryGetValue(emojiId, out var cached))
        {
            return cached;
        }
        await _imageFetchThrottle.WaitAsync();
        try
        {
            // Another waiter may have fetched this same emoji while we were queued.
            if (_customEmojiImageCache.TryGetValue(emojiId, out cached))
            {
                return cached;
            }
            var path = await _service.GetEmojiImagePathAsync(_session.BaseUrl, _session.Token, emojiId);
            Bitmap bitmap;
            try
            {
                bitmap = await Task.Run(() => new Bitmap(path));
            }
            catch (Exception ex)
            {
                // A cached file that fails to decode is almost certainly left over
                // from an earlier race between two concurrent downloads of the same
                // emoji (fixed on the core side, but an already-corrupted file on
                // disk stays corrupted forever otherwise) — delete it and let the
                // core fetch a fresh copy once rather than failing on this emoji
                // for the rest of the session.
                CrashLogger.Write($"custom emoji: decode failed for {emojiId} at {path}, retrying once", ex);
                TryDeleteFile(path);
                path = await _service.GetEmojiImagePathAsync(_session.BaseUrl, _session.Token, emojiId);
                bitmap = await Task.Run(() => new Bitmap(path));
            }
            _customEmojiImageCache[emojiId] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            // That one swatch/reaction just stays blank — not worth surfacing as an app-wide error.
            CrashLogger.Write($"custom emoji: gave up on {emojiId}", ex);
            return null;
        }
        finally
        {
            _imageFetchThrottle.Release();
        }
    }

    /// <summary>Same shape as GetCustomEmojiBitmapAsync, for a user's profile picture — shared cache/throttle, keyed by user id instead of emoji id.</summary>
    private async Task<Bitmap?> GetAvatarBitmapAsync(string userId)
    {
        if (_avatarImageCache.TryGetValue(userId, out var cached))
        {
            return cached;
        }
        await _imageFetchThrottle.WaitAsync();
        try
        {
            if (_avatarImageCache.TryGetValue(userId, out cached))
            {
                return cached;
            }
            var path = await _service.GetUserAvatarPathAsync(_session.BaseUrl, _session.Token, userId);
            Bitmap bitmap;
            try
            {
                bitmap = await Task.Run(() => new Bitmap(path));
            }
            catch (Exception ex)
            {
                // Same reasoning as GetCustomEmojiBitmapAsync: a corrupt cached
                // file from an earlier interrupted download stays corrupt
                // forever otherwise — delete it and retry once.
                CrashLogger.Write($"avatar: decode failed for {userId} at {path}, retrying once", ex);
                TryDeleteFile(path);
                path = await _service.GetUserAvatarPathAsync(_session.BaseUrl, _session.Token, userId);
                bitmap = await Task.Run(() => new Bitmap(path));
            }
            _avatarImageCache[userId] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            // That one avatar just stays the colored-initials fallback.
            CrashLogger.Write($"avatar: gave up on {userId}", ex);
            return null;
        }
        finally
        {
            _imageFetchThrottle.Release();
        }
    }

    /// <summary>Same shape as GetAvatarBitmapAsync, for a link preview's og:image — cached per source URL rather than per user id.</summary>
    private async Task<Bitmap?> GetLinkPreviewImageBitmapAsync(string url)
    {
        if (_linkPreviewImageCache.TryGetValue(url, out var cached))
        {
            return cached;
        }
        await _imageFetchThrottle.WaitAsync();
        try
        {
            if (_linkPreviewImageCache.TryGetValue(url, out cached))
            {
                return cached;
            }
            var path = await _service.GetLinkPreviewImagePathAsync(url);
            Bitmap bitmap;
            try
            {
                bitmap = await Task.Run(() => new Bitmap(path));
            }
            catch (Exception ex)
            {
                CrashLogger.Write($"link preview: decode failed for {url} at {path}, retrying once", ex);
                TryDeleteFile(path);
                path = await _service.GetLinkPreviewImagePathAsync(url);
                bitmap = await Task.Run(() => new Bitmap(path));
            }
            _linkPreviewImageCache[url] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            // That one preview just shows without an image.
            CrashLogger.Write($"link preview: gave up on {url}", ex);
            return null;
        }
        finally
        {
            _imageFetchThrottle.Release();
        }
    }

    /// <summary>
    /// A message's first "opengraph" embed (its data.title/description/
    /// image), if the server generated one — null for a message with no
    /// link, or one whose link had nothing useful to show. The image (if
    /// any) resolves asynchronously afterward; the card itself always
    /// reserves the same fixed-height area for it up front (see
    /// MainWindow.axaml) so that arriving later never changes this row's
    /// height — the same virtualized-list jump concern as everywhere else
    /// something resolves after the initial paint.
    /// </summary>
    private LinkPreviewItem? BuildLinkPreview(PostDto post)
    {
        var embed = post.Metadata.Embeds.FirstOrDefault(e => e.Type == "opengraph" && e.Data is not null);
        if (embed?.Data is not { } data)
        {
            return null;
        }
        var image = data.Images.FirstOrDefault();
        var imageUrl = image is null ? "" : (string.IsNullOrEmpty(image.SecureUrl) ? image.Url : image.SecureUrl);
        if (data.Title.Length == 0 && data.Description.Length == 0 && imageUrl.Length == 0)
        {
            return null;
        }

        var item = new LinkPreviewItem
        {
            Url = embed.Url,
            Title = data.Title,
            Description = data.Description,
            SiteName = data.SiteName,
            ImageUrl = imageUrl,
        };
        if (item.HasImage)
        {
            _ = ResolveLinkPreviewImageAsync(item);
        }
        return item;
    }

    private async Task ResolveLinkPreviewImageAsync(LinkPreviewItem item)
    {
        if (await GetLinkPreviewImageBitmapAsync(item.ImageUrl) is { } bitmap)
        {
            item.ImageSource = bitmap;
        }
    }

    [RelayCommand]
    private void ToggleLinkPreviews() => Settings.ShowLinkPreviews = !Settings.ShowLinkPreviews;

    /// <summary>Fire-and-forget from a message's construction — resolves the author's real avatar in the background and applies it once ready, without holding up painting the message itself.</summary>
    private async Task ResolveMessageAvatarAsync(MessageItem item, string userId)
    {
        if (await GetAvatarBitmapAsync(userId) is { } bitmap)
        {
            item.AvatarImageBrush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        }
    }

    private async Task ResolveMyAvatarAsync()
    {
        if (await GetAvatarBitmapAsync(_session.User.Id) is { } bitmap)
        {
            MyAvatarImageBrush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        }
    }

    /// <summary>Same as ResolveMessageAvatarAsync, for a DM sidebar row's other participant.</summary>
    private async Task ResolveDirectMessageAvatarAsync(DirectMessageItem item, string userId)
    {
        if (await GetAvatarBitmapAsync(userId) is { } bitmap)
        {
            item.AvatarImageBrush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup — if this fails, the retry below will just fail too, same as before.
        }
    }

    [RelayCommand]
    private void SetTheme(AppThemeMode mode) => Settings.ThemeMode = mode;

    /// <summary>
    /// Re-resolves the palette against the OS setting. Called when Windows
    /// flips light/dark underneath a "System" preference — a no-op for an
    /// explicit Clair/Sombre choice, so it's safe to wire unconditionally.
    /// </summary>
    public void RefreshSystemTheme()
    {
        if (Settings.ThemeMode == AppThemeMode.System)
        {
            ApplyTheme();
        }
    }

    /// <summary>
    /// Applies the active palette: updates the shared color tokens, the
    /// picker selection states, and repaints every already-built item
    /// (channels, DMs, messages, reactions) so the change is visible
    /// immediately rather than only on the next natural refresh. Also tells
    /// MainWindow to swap the window-resource palette and shape tokens.
    /// </summary>
    private void ApplyTheme()
    {
        ColorTokens.ApplyTheme(ThemeDefinition.Resolve(Settings.ThemeMode, SystemTheme.PrefersDark));

        foreach (var option in ThemeOptions)
        {
            option.IsSelected = option.Mode == Settings.ThemeMode;
            option.RefreshColors();
        }

        OnPropertyChanged(nameof(MyPresenceBrush));

        foreach (var channel in Channels.Concat(FavoriteChannels))
        {
            channel.RefreshColors();
        }
        foreach (var dm in DirectMessages.Concat(FavoriteDirectMessages))
        {
            dm.RefreshColors();
        }
        foreach (var message in Messages)
        {
            RefreshMessageColors(message);
        }
        foreach (var reply in ThreadReplies)
        {
            RefreshMessageColors(reply);
        }
        if (ThreadRootMessage is not null)
        {
            RefreshMessageColors(ThreadRootMessage);
        }
        foreach (var fontOption in FontSizeOptions)
        {
            fontOption.RefreshColors();
        }
        foreach (var fontChoice in FontChoiceOptions)
        {
            fontChoice.RefreshColors();
        }

        ThemeResourcesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The row's own tint is a DynamicResource on a style class, so only the reaction pills (which hold real brushes) need repainting.</summary>
    private static void RefreshMessageColors(MessageItem message)
    {
        foreach (var reaction in message.Reactions)
        {
            reaction.RefreshColors();
        }
    }

    private void RefreshFontSizeSelection()
    {
        foreach (var option in FontSizeOptions)
        {
            option.IsSelected = option.Size == Settings.MessageFontSize;
        }
    }

    private void RefreshFontChoiceSelection()
    {
        foreach (var option in FontChoiceOptions)
        {
            option.IsSelected = option.Choice == Settings.FontChoice;
        }
    }

    [RelayCommand]
    private void SetFontChoice(AppFontChoice choice) => Settings.FontChoice = choice;

    [RelayCommand]
    private void CloseThread()
    {
        _openThreadRootId = null;
        IsThreadOpen = false;
        ThreadRootMessage = null;
        ThreadReplies.Clear();
    }

    private async Task<bool> TryPaintThreadFromCacheAsync(string rootId)
    {
        try
        {
            var posts = await _service.GetCachedThreadAsync(rootId);
            if (posts.Count == 0)
            {
                return false;
            }
            await PopulateThreadAsync(posts);
            await AppendPendingOutboxAsync(ThreadReplies, posts[0].ChannelId, rootId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>`posts` is the root plus its replies, oldest first — the root sorts first since replies must come after it.</summary>
    private async Task PopulateThreadAsync(List<PostDto> posts)
    {
        var authorIds = posts.Select(p => p.UserId).Distinct();
        var authors = (await _service.GetCachedUsersAsync(authorIds)).ToDictionary(u => u.Id);
        if (authors.Count < authorIds.Count())
        {
            // Cache didn't have everyone yet (e.g. this thread was never fetched before) — resolve for real.
            var missing = authorIds.Where(id => !authors.ContainsKey(id));
            foreach (var user in await _service.GetUsersAsync(_session.BaseUrl, _session.Token, missing))
            {
                authors[user.Id] = user;
            }
        }

        MessageItem ToMessageItem(PostDto post, MessageItem? previous)
        {
            authors.TryGetValue(post.UserId, out var author);
            var item = new MessageItem
            {
                Id = post.Id,
                AuthorUserId = post.UserId,
                AuthorName = author?.DisplayName ?? "Utilisateur inconnu",
                AuthorInitials = author?.Initials ?? "?",
                AvatarHex = AvatarColorFor(post.UserId),
                TimeLabel = FormatTime(post.CreateAt),
                CreateAtMillis = post.CreateAt,
                Text = post.Message,
                IsMine = post.UserId == _session.User.Id,
                IsEdited = post.EditAt > 0,
                LinkPreview = BuildLinkPreview(post),
                IsContinuation = IsContinuationOf(previous, post.UserId, post.CreateAt),
                Reactions = BuildReactions(post),
                Attachments = BuildAttachments(post),
            };
            _ = ResolveMessageAvatarAsync(item, post.UserId);
            return item;
        }

        // The root never counts as a continuation of anything — it's always
        // shown with its own full header, visually separate from the reply list.
        ThreadRootMessage = ToMessageItem(posts[0], previous: null);
        ThreadReplies.Clear();
        MessageItem? previousReply = null;
        foreach (var reply in posts.Skip(1))
        {
            var item = ToMessageItem(reply, previousReply);
            ThreadReplies.Add(item);
            previousReply = item;
        }
    }

    private async Task<bool> TryLoadFromCacheAsync()
    {
        try
        {
            var team = (await _service.GetCachedTeamsAsync()).FirstOrDefault();
            if (team is null)
            {
                return false;
            }
            _teamId = team.Id;

            try
            {
                _favoriteChannelIds = (await _service.GetCachedFavoritesAsync()).ToHashSet();
            }
            catch
            {
                // A cold cache just starts empty — the network refresh below fills it in.
            }

            var allChannels = await _service.GetCachedChannelsAsync(team.Id);
            _loadedChannels = allChannels;

            var visibleChannels = allChannels.Where(c => c.IsPublicOrPrivate).OrderBy(c => c.DisplayName).ToList();
            var firstChannel = visibleChannels.FirstOrDefault();
            if (firstChannel is null)
            {
                return false;
            }

            TeamName = team.DisplayName;
            PopulateChannels(visibleChannels, firstChannel.Id);
            await PopulateDirectMessagesAsync(allChannels, allowNetwork: false);
            ActiveChannelName = ChannelDisplayName(firstChannel);
            ActiveChannelTopic = "";
            TypingIndicatorText = "";
            _activeChannelId = firstChannel.Id;
            MarkChannelViewed(firstChannel.Id);

            return await TryPaintMessagesFromCacheAsync(firstChannel.Id);
        }
        catch
        {
            // A cold cache (or a read glitch) just means we fall through to the network path.
            return false;
        }
    }

    private async Task<bool> TryPaintMessagesFromCacheAsync(string channelId)
    {
        try
        {
            // Posts and outbox are independent reads — fetch them concurrently
            // instead of paying for two sequential FFI round trips.
            var postsTask = _service.GetCachedPostsAsync(channelId);
            var outboxTask = _service.GetCachedOutboxAsync();
            await Task.WhenAll(postsTask, outboxTask);
            var posts = postsTask.Result;
            var outbox = outboxTask.Result;
            var hasPending = outbox.Any(o => o.ChannelId == channelId && o.RootId is null);
            if (posts.Count == 0 && !hasPending)
            {
                return false;
            }
            var authorIds = posts.Select(p => p.UserId).Distinct();
            var authors = (await _service.GetCachedUsersAsync(authorIds)).ToDictionary(u => u.Id);
            await PopulateMessagesWithOutboxAsync(channelId, posts, authors, outbox);
            ScrollMessagesToEndRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RefreshFromNetworkAsync()
    {
        var teams = await _service.GetTeamsAsync(_session.BaseUrl, _session.Token);
        var team = teams.FirstOrDefault();
        if (team is null)
        {
            throw new MattermostServiceException("Aucune équipe trouvée pour ce compte.");
        }
        _teamId = team.Id;
        TeamName = team.DisplayName;

        try
        {
            _favoriteChannelIds = (await _service.GetFavoritesAsync(_session.BaseUrl, _session.Token, _session.User.Id)).ToHashSet();
        }
        catch
        {
            // Favorites are a nice-to-have; don't let a failure here block loading channels themselves.
        }

        var allChannels = await _service.GetChannelsAsync(_session.BaseUrl, _session.Token, team.Id);
        _loadedChannels = allChannels;

        var visibleChannels = allChannels.Where(c => c.IsPublicOrPrivate).OrderBy(c => c.DisplayName).ToList();
        var firstChannel = visibleChannels.FirstOrDefault();
        if (firstChannel is null)
        {
            throw new MattermostServiceException("Aucun canal trouvé pour cette équipe.");
        }

        // Keep whatever channel the user is already looking at (they may have
        // clicked away from the first channel while this refresh was in flight) —
        // checked against every channel, not just public/private, since that
        // "current" channel could well be a DM.
        var targetChannelId = _activeChannelId is not null && allChannels.Any(c => c.Id == _activeChannelId)
            ? _activeChannelId
            : firstChannel.Id;
        var targetChannel = allChannels.First(c => c.Id == targetChannelId);

        PopulateChannels(visibleChannels, targetChannelId);
        await PopulateDirectMessagesAsync(allChannels, allowNetwork: true);
        ActiveChannelName = ChannelDisplayName(targetChannel);
        ActiveChannelTopic = "";
        TypingIndicatorText = "";
        _activeChannelId = targetChannelId;
        MarkChannelViewed(targetChannelId);

        var posts = await _service.GetPostsAsync(_session.BaseUrl, _session.Token, targetChannelId);
        var authorIds = posts.Select(p => p.UserId).Distinct();
        var authors = (await _service.GetUsersAsync(_session.BaseUrl, _session.Token, authorIds))
            .ToDictionary(u => u.Id);

        if (targetChannelId == _activeChannelId)
        {
            await PopulateMessagesWithOutboxAsync(targetChannelId, posts, authors);
            ScrollMessagesToEndRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Keeps the sidebar's bold/badge state live: a message arriving via the
    /// WebSocket for a channel that isn't open right now only ever updates
    /// the cache (see almatter-core's ws.rs), never the sidebar directly —
    /// this is what actually notices and repaints it, cheaply skipped when
    /// nothing about any channel's counts has actually changed.
    /// </summary>
    private async Task RefreshChannelBadgesFromCacheAsync()
    {
        if (_teamId is null)
        {
            return;
        }

        var allChannels = await _service.GetCachedChannelsAsync(_teamId);
        var changed = allChannels.Count != _loadedChannels.Count || allChannels.Exists(c =>
        {
            var previous = _loadedChannels.FirstOrDefault(p => p.Id == c.Id);
            return previous is null
                || previous.TotalMsgCount != c.TotalMsgCount
                || previous.MsgCount != c.MsgCount
                || previous.MentionCount != c.MentionCount;
        });
        if (!changed)
        {
            return;
        }

        _loadedChannels = allChannels;
        var visibleChannels = allChannels.Where(c => c.IsPublicOrPrivate).OrderBy(c => c.DisplayName).ToList();

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            PopulateChannels(visibleChannels, _activeChannelId ?? "");
            await PopulateDirectMessagesAsync(allChannels, allowNetwork: false);
        });
    }

    private void PopulateChannels(List<ChannelDto> visibleChannels, string selectedChannelId)
    {
        Channels.Clear();
        var favorites = new List<ChannelItem>();
        foreach (var c in visibleChannels)
        {
            var isFavorite = _favoriteChannelIds.Contains(c.Id);
            var unreadCount = Math.Max(0, c.TotalMsgCount - c.MsgCount);
            var item = new ChannelItem
            {
                Id = c.Id,
                Name = c.DisplayName,
                Kind = c.IsPrivate ? ChannelKind.Private : ChannelKind.Public,
                IsSelected = c.Id == selectedChannelId,
                IsFavorite = isFavorite,
                IsMuted = c.IsMuted,
                HasUnread = unreadCount > 0,
                MentionCount = (int)c.MentionCount,
            };
            if (isFavorite)
            {
                favorites.Add(item);
            }
            else
            {
                Channels.Add(item);
            }
        }

        FavoriteChannels.Clear();
        foreach (var item in OrderByFavoritePosition(favorites))
        {
            FavoriteChannels.Add(item);
        }
        OnPropertyChanged(nameof(HasFavorites));
        RefreshUnreadAggregates();
    }

    /// <summary>
    /// Clears a channel's unread badge the moment it's opened: zeroes it
    /// out locally (in the visible items and in the retained DTO, so a
    /// later repaint from already-loaded data — e.g. a favorite drag —
    /// doesn't resurrect the old count) immediately, then tells the server
    /// in the background so the next real fetch agrees.
    /// </summary>
    private void MarkChannelViewed(string channelId)
    {
        var dto = _loadedChannels.FirstOrDefault(c => c.Id == channelId);
        if (dto is not null)
        {
            dto.MsgCount = dto.TotalMsgCount;
            dto.MentionCount = 0;
        }

        foreach (var item in Channels.Concat(FavoriteChannels))
        {
            if (item.Id == channelId)
            {
                item.HasUnread = false;
                item.MentionCount = 0;
            }
        }
        foreach (var item in DirectMessages.Concat(FavoriteDirectMessages))
        {
            if (item.Id == channelId)
            {
                item.HasUnread = false;
                item.MentionCount = 0;
            }
        }

        RefreshUnreadAggregates();
        _ = MarkChannelViewedOnServerAsync(channelId);
    }

    /// <summary>Raised whenever aggregate unread state (see HasUnreadRegularMessages/TotalPriorityUnreadCount) might have changed — MainWindow's code-behind uses this to update the taskbar overlay badge, since that's a Win32 handle the ViewModel has no access to.</summary>
    public event EventHandler? UnreadBadgeStateChanged;

    /// <summary>True when some channel/DM has an unread message that isn't itself a mention/DM — those get the numbered badge instead, this one just a plain dot.</summary>
    public bool HasUnreadRegularMessages { get; private set; }

    /// <summary>Total unread mentions + DM messages across every channel/DM (Favoris included) — drives the taskbar's numbered overlay badge.</summary>
    public int TotalPriorityUnreadCount { get; private set; }

    private void RefreshUnreadAggregates()
    {
        var items = new List<IChannelListItem>();
        items.AddRange(Channels);
        items.AddRange(FavoriteChannels);
        items.AddRange(DirectMessages);
        items.AddRange(FavoriteDirectMessages);

        // A muted channel/DM keeps its own sidebar unread state (still
        // visible if you go look), it just never contributes to the
        // taskbar badge.
        var unmuted = items.Where(i => !i.IsMuted).ToList();
        HasUnreadRegularMessages = unmuted.Any(i => i.HasUnread && i.MentionCount == 0);
        TotalPriorityUnreadCount = unmuted.Sum(i => i.MentionCount);
        UnreadBadgeStateChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ToggleMuteAsync(IChannelListItem item)
    {
        var muted = !item.IsMuted;
        try
        {
            await _service.SetChannelMutedAsync(_session.BaseUrl, _session.Token, _session.User.Id, item.Id, muted);
        }
        catch (MattermostServiceException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        item.IsMuted = muted;
        var dto = _loadedChannels.FirstOrDefault(c => c.Id == item.Id);
        if (dto is not null)
        {
            dto.IsMuted = muted;
        }
        RefreshUnreadAggregates();
    }

    private async Task MarkChannelViewedOnServerAsync(string channelId)
    {
        try
        {
            await _service.MarkChannelViewedAsync(_session.BaseUrl, _session.Token, channelId);
        }
        catch
        {
            // Best-effort — the badge already cleared locally; a failure here
            // just means it could reappear on the next real fetch, no worse
            // than not having this call at all.
        }
    }

    private string ChannelDisplayName(ChannelDto channel) =>
        _dmDisplayNames.TryGetValue(channel.Id, out var name) ? name : channel.DisplayName;

    /// <summary>
    /// Mattermost direct-message channels don't carry a useful display_name
    /// of their own — a 1:1 DM channel's `name` is literally
    /// "{userId1}__{userId2}" (sorted), so the other participant's id is
    /// derived from that rather than an extra per-channel members call. A
    /// group DM's `name` isn't parseable this way, so its participant list
    /// comes from a separate `GetChannelMembers` call instead (cache-first,
    /// refreshed over the network when allowed).
    /// </summary>
    private async Task PopulateDirectMessagesAsync(List<ChannelDto> allChannels, bool allowNetwork)
    {
        // Mattermost creates the DM/GM channel record the moment a
        // conversation is opened, before any message is actually sent —
        // total_msg_count is what tells an unused one apart from a real one.
        // Most recent activity first, like every real chat client.
        var dmChannels = allChannels
            .Where(c => (c.Type == "D" || c.Type == "G") && c.TotalMsgCount > 0)
            .OrderByDescending(c => c.LastPostAt)
            .ToList();
        if (dmChannels.Count == 0)
        {
            DirectMessages.Clear();
            FavoriteDirectMessages.Clear();
            OnPropertyChanged(nameof(HasFavorites));
            return;
        }

        var otherUserIdByChannel = new Dictionary<string, string>();
        var participantIdsByChannel = new Dictionary<string, List<string>>();
        foreach (var c in dmChannels)
        {
            if (c.Type == "D")
            {
                var parts = c.Name.Split("__");
                if (parts.Length != 2)
                {
                    continue;
                }
                otherUserIdByChannel[c.Id] = parts[0] == _session.User.Id ? parts[1] : parts[0];
            }
            else
            {
                List<string> memberIds;
                if (allowNetwork)
                {
                    try
                    {
                        memberIds = await _service.GetChannelMembersAsync(_session.BaseUrl, _session.Token, c.Id);
                    }
                    catch
                    {
                        memberIds = await _service.GetCachedChannelMembersAsync(c.Id);
                    }
                }
                else
                {
                    memberIds = await _service.GetCachedChannelMembersAsync(c.Id);
                }
                participantIdsByChannel[c.Id] = memberIds.Where(id => id != _session.User.Id).ToList();
            }
        }

        var otherUserIds = otherUserIdByChannel.Values
            .Concat(participantIdsByChannel.Values.SelectMany(ids => ids))
            .Distinct()
            .ToList();
        var users = (await _service.GetCachedUsersAsync(otherUserIds)).ToDictionary(u => u.Id);

        if (allowNetwork)
        {
            var missing = otherUserIds.Where(id => !users.ContainsKey(id)).ToList();
            if (missing.Count > 0)
            {
                foreach (var user in await _service.GetUsersAsync(_session.BaseUrl, _session.Token, missing))
                {
                    users[user.Id] = user;
                }
            }
        }

        var statuses = new Dictionary<string, string>();
        if (allowNetwork)
        {
            try
            {
                statuses = (await _service.GetStatusesAsync(_session.BaseUrl, _session.Token, otherUserIds))
                    .ToDictionary(s => s.UserId, s => s.Status);
            }
            catch
            {
                // Presence is a nice-to-have; don't let it block the DM list itself.
            }
        }

        _dmDisplayNames.Clear();
        var items = new List<DirectMessageItem>();
        foreach (var c in dmChannels)
        {
            var unreadCount = Math.Max(0, c.TotalMsgCount - c.MsgCount);

            if (c.Type == "G")
            {
                if (!participantIdsByChannel.TryGetValue(c.Id, out var participantIds) || participantIds.Count == 0)
                {
                    continue;
                }
                var participantNames = participantIds
                    .Select(id => users.TryGetValue(id, out var u) ? u.DisplayName : "Utilisateur inconnu")
                    .OrderBy(name => name)
                    .ToList();
                var displayName = string.Join(", ", participantNames);
                _dmDisplayNames[c.Id] = displayName;

                items.Add(new DirectMessageItem
                {
                    Id = c.Id,
                    DisplayName = displayName,
                    Initials = "",
                    AvatarHex = AvatarColorFor(c.Id),
                    Presence = PresenceStatus.Offline,
                    IsGroup = true,
                    IsSelected = c.Id == _activeChannelId,
                    IsFavorite = _favoriteChannelIds.Contains(c.Id),
                    IsMuted = c.IsMuted,
                    HasUnread = unreadCount > 0,
                    MentionCount = (int)c.MentionCount,
                });
                continue;
            }

            if (!otherUserIdByChannel.TryGetValue(c.Id, out var otherId))
            {
                continue;
            }
            users.TryGetValue(otherId, out var user);
            var otherDisplayName = user?.DisplayName ?? "Utilisateur inconnu";
            _dmDisplayNames[c.Id] = otherDisplayName;

            var presence = statuses.TryGetValue(otherId, out var status)
                ? status switch
                {
                    "online" => PresenceStatus.Online,
                    "away" => PresenceStatus.Away,
                    "dnd" => PresenceStatus.DoNotDisturb,
                    _ => PresenceStatus.Offline,
                }
                : PresenceStatus.Offline;

            var dmItem = new DirectMessageItem
            {
                Id = c.Id,
                DisplayName = otherDisplayName,
                Initials = user?.Initials ?? "?",
                AvatarHex = AvatarColorFor(otherId),
                Presence = presence,
                IsSelected = c.Id == _activeChannelId,
                IsFavorite = _favoriteChannelIds.Contains(c.Id),
                IsMuted = c.IsMuted,
                HasUnread = unreadCount > 0,
                MentionCount = (int)c.MentionCount,
            };
            _ = ResolveDirectMessageAvatarAsync(dmItem, otherId);
            items.Add(dmItem);
        }

        // `items` is already in dmChannels' order (most recent activity first).
        DirectMessages.Clear();
        var favorites = new List<DirectMessageItem>();
        foreach (var item in items)
        {
            if (item.IsFavorite)
            {
                favorites.Add(item);
            }
            else
            {
                DirectMessages.Add(item);
            }
        }

        FavoriteDirectMessages.Clear();
        foreach (var item in OrderByFavoritePosition(favorites))
        {
            FavoriteDirectMessages.Add(item);
        }
        OnPropertyChanged(nameof(HasFavorites));
        RefreshUnreadAggregates();
    }

    /// <summary>"Less than two minutes" per an explicit request — the same window every grouping site below uses.</summary>
    private const long ContinuationWindowMillis = 2 * 60 * 1000;

    /// <summary>True when a message immediately follows one from the same author less than two minutes ago — the header (avatar/name) is skipped for it, official-client style.</summary>
    private static bool IsContinuationOf(MessageItem? previous, string authorUserId, long createAtMillis) =>
        previous is not null
        && previous.AuthorUserId == authorUserId
        && createAtMillis - previous.CreateAtMillis < ContinuationWindowMillis;

    private void PopulateMessages(List<PostDto> posts, Dictionary<string, UserDto> authors)
    {
        // Thread replies are shown inline too (not just in the thread panel)
        // so nothing gets missed just by not having a thread open — marked
        // with an accent bar so their thread membership is still visible.
        var ordered = posts.OrderBy(p => p.CreateAt).ToList();
        // For resolving a reply's quoted-root preview below — only from
        // what's already in this same batch (see QuotedAuthorName's doc
        // comment for why this is never resolved later/asynchronously).
        var postsById = ordered.ToDictionary(p => p.Id);

        _lastRenderedPostIds = ordered.Select(p => p.Id).ToList();

        Messages.Clear();
        MessageItem? previous = null;
        foreach (var post in ordered)
        {
            authors.TryGetValue(post.UserId, out var author);

            string? quotedAuthorName = null;
            string? quotedText = null;
            if (!string.IsNullOrEmpty(post.RootId) && postsById.TryGetValue(post.RootId, out var rootPost))
            {
                authors.TryGetValue(rootPost.UserId, out var rootAuthor);
                quotedAuthorName = rootAuthor?.DisplayName ?? "Utilisateur inconnu";
                quotedText = TruncateQuote(rootPost.Message);
            }

            var item = new MessageItem
            {
                Id = post.Id,
                AuthorUserId = post.UserId,
                AuthorName = author?.DisplayName ?? "Utilisateur inconnu",
                AuthorInitials = author?.Initials ?? "?",
                AvatarHex = AvatarColorFor(post.UserId),
                TimeLabel = FormatTime(post.CreateAt),
                CreateAtMillis = post.CreateAt,
                Text = post.Message,
                IsMine = post.UserId == _session.User.Id,
                IsEdited = post.EditAt > 0,
                ThreadReplyCount = (int)post.ReplyCount,
                ThreadRootId = string.IsNullOrEmpty(post.RootId) ? null : post.RootId,
                QuotedAuthorName = quotedAuthorName,
                QuotedText = quotedText,
                LinkPreview = BuildLinkPreview(post),
                IsContinuation = IsContinuationOf(previous, post.UserId, post.CreateAt),
                Reactions = BuildReactions(post),
                Attachments = BuildAttachments(post),
            };
            _ = ResolveMessageAvatarAsync(item, post.UserId);
            Messages.Add(item);
            previous = item;
        }
    }

    /// <summary>Paints the confirmed posts, then appends any still-queued outbox messages for this channel so a pending send doesn't vanish until it's actually flushed.</summary>
    private async Task PopulateMessagesWithOutboxAsync(string channelId, List<PostDto> posts, Dictionary<string, UserDto> authors, List<OutboxItemDto>? prefetchedOutbox = null)
    {
        PopulateMessages(posts, authors);
        await AppendPendingOutboxAsync(Messages, channelId, rootId: null, prefetchedOutbox);
        _lastRenderedOutboxIds = Messages.Where(m => m.IsPending).Select(m => m.Id).ToList();
    }

    /// <summary>Appends queued-but-not-yet-sent outbox messages (oldest first) to an already-painted message list, as pending bubbles. Pass a prefetchedOutbox when the caller already has it, to avoid a redundant round trip.</summary>
    private async Task AppendPendingOutboxAsync(ObservableCollection<MessageItem> target, string channelId, string? rootId, List<OutboxItemDto>? prefetchedOutbox = null)
    {
        List<OutboxItemDto> outbox;
        if (prefetchedOutbox is not null)
        {
            outbox = prefetchedOutbox;
        }
        else
        {
            try
            {
                outbox = await _service.GetCachedOutboxAsync();
            }
            catch
            {
                return;
            }
        }

        foreach (var item in outbox
            .Where(o => o.ChannelId == channelId && o.RootId == rootId)
            .OrderBy(o => o.CreatedAt))
        {
            var previous = target.Count > 0 ? target[^1] : null;
            target.Add(ToPendingMessageItem(item, previous));
        }
    }

    private MessageItem ToPendingMessageItem(OutboxItemDto item, MessageItem? previous) => new()
    {
        Id = item.LocalId,
        AuthorUserId = _session.User.Id,
        AuthorName = _session.User.DisplayName,
        AuthorInitials = _session.User.Initials,
        AvatarHex = AvatarColorFor(_session.User.Id),
        TimeLabel = FormatTime(item.CreatedAt),
        CreateAtMillis = item.CreatedAt,
        Text = item.Message,
        IsMine = true,
        IsPending = true,
        IsContinuation = IsContinuationOf(previous, _session.User.Id, item.CreatedAt),
    };

    private ObservableCollection<ReactionItem> BuildReactions(PostDto post)
    {
        var items = post.Metadata.Reactions
            .GroupBy(r => r.EmojiName)
            .Select(g => new ReactionItem
            {
                PostId = post.Id,
                EmojiName = g.Key,
                Emoji = EmojiShortcodes.ToGlyph(g.Key),
                Count = g.Count(),
                ReactedByMe = g.Any(r => r.UserId == _session.User.Id),
            })
            .ToList();
        foreach (var reaction in items)
        {
            _ = ResolveReactionImageAsync(reaction);
        }
        return new ObservableCollection<ReactionItem>(items);
    }

    private static ObservableCollection<AttachmentItem> BuildAttachments(PostDto post)
    {
        var items = post.Metadata.Files.Select(f => new AttachmentItem
        {
            Id = f.Id,
            FileName = f.Name,
            SizeLabel = FormatFileSize(f.Size),
        });
        return new ObservableCollection<AttachmentItem>(items);
    }

    private static string FormatFileSize(long bytes)
    {
        double size = bytes;
        string[] units = ["o", "Ko", "Mo", "Go"];
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return unitIndex == 0 ? $"{size:0} {units[unitIndex]}" : $"{size:0.#} {units[unitIndex]}";
    }

    private static string FormatTime(long createAtMillis) =>
        DateTimeOffset.FromUnixTimeMilliseconds(createAtMillis).ToLocalTime().ToString("HH:mm");

    private static string BuildTypingIndicatorText(List<string> names) => names.Count switch
    {
        0 => "",
        1 => $"{names[0]} est en train d'écrire…",
        2 => $"{names[0]} et {names[1]} sont en train d'écrire…",
        _ => "Plusieurs personnes sont en train d'écrire…",
    };

    /// <summary>Search results can span months, so — unlike the plain message list — the date matters, not just the time.</summary>
    private static string FormatSearchResultTime(long createAtMillis) =>
        DateTimeOffset.FromUnixTimeMilliseconds(createAtMillis).ToLocalTime().ToString("dd/MM/yy HH:mm");

    /// <summary>A single-line, length-capped preview for a reply's quoted-root block — a citation, not the full message.</summary>
    private static string TruncateQuote(string text)
    {
        const int maxLength = 120;
        var singleLine = string.Join(' ', text.Split('\n', StringSplitOptions.RemoveEmptyEntries)).Trim();
        return singleLine.Length > maxLength ? singleLine[..maxLength].TrimEnd() + "…" : singleLine;
    }

    private static string AvatarColorFor(string userId)
    {
        var hash = 0;
        foreach (var ch in userId)
        {
            hash = hash * 31 + ch;
        }
        return AvatarPalette[Math.Abs(hash) % AvatarPalette.Length];
    }
}
