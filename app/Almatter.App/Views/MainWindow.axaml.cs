using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Almatter.App.Models;
using Almatter.App.Services;
using Almatter.App.ViewModels;

namespace Almatter.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Drag-transfer format carrying the dragged row's channel/DM id — the same id either kind of row uses (see IChannelListItem).</summary>
    private static readonly DataFormat<string> ChannelDragFormat = DataFormat.CreateInProcessFormat<string>("almatter-channel-id");

    private PointerPressedEventArgs? _dragStartArgs;
    private Point? _dragStartPoint;
    private IChannelListItem? _dragCandidateItem;
    private DispatcherTimer? _longPressTimer;

    /// <summary>
    /// A reorder-within-Favoris drag, started by a long-press rather than
    /// DoDragDropAsync — that's OS-level drag-drop, which doesn't give an
    /// easy way to paint a custom "drop here" line between rows, and (going
    /// through a DispatcherTimer rather than a live pointer event to start
    /// it) turned out unreliable to kick off at all. This is just plain
    /// pointer tracking instead, fully under our own control.
    /// </summary>
    private bool _isReorderDragging;
    private IChannelListItem? _reorderItem;
    private IChannelListItem? _reorderTargetItem;
    private bool _reorderPlaceAfter;
    private bool _reorderMoveToRegular;

    private IDesktopNotifier? _notifier;
    private (string ChannelId, string PostId)? _pendingNotificationTarget;

    public MainWindow()
    {
        InitializeComponent();

        // Once the window is open, so it has the native handle the Windows
        // notifier routes its click messages through.
        Opened += (_, _) =>
        {
            _notifier ??= DesktopNotifier.Create(this);
            _notifier.Clicked += OnNotificationClicked;
        };
        Closed += (_, _) => _notifier?.Dispose();

        // Remembers the window size across restarts — read once on the way
        // in, written back on the way out. Closing (not Closed) fires while
        // Width/Height are still the real, current values.
        Closing += (_, _) =>
        {
            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            // Size and position are only meaningful in the normal state: a
            // maximised window reports the screen, and restoring THAT as a
            // normal window would leave no way to tell the two apart next
            // launch. The maximised flag carries that on its own, and the
            // stored size stays the one to un-maximise back to.
            vm.Settings.WindowMaximised = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                vm.Settings.WindowWidth = Width;
                vm.Settings.WindowHeight = Height;
                vm.Settings.WindowX = Position.X;
                vm.Settings.WindowY = Position.Y;
            }

            // Saved whatever the window state: a sidebar dragged wider while
            // maximised is still a deliberate preference.
            vm.Settings.SidebarWidth = AppGrid.ColumnDefinitions[0].ActualWidth;
        };

        // The native handle doesn't exist yet at construction time (before
        // the window is actually shown), so the very first title-bar paint
        // has to wait for Opened — later changes (theme switch, Windows'
        // own light/dark setting flipping) are handled where DataContext
        // wires up ThemeResourcesChanged/ColorValuesChanged instead.
        Opened += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                ApplyTitleBarTheme(vm);
            }
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm)
            {
                return;
            }

            Width = vm.Settings.WindowWidth;
            Height = vm.Settings.WindowHeight;
            RestoreWindowPlacement(vm);

            // Clamped rather than trusted: a settings.json edited by hand, or
            // written by a build whose bounds differed, shouldn't be able to
            // start the app with a 12px or a 2000px sidebar.
            var sidebarColumn = AppGrid.ColumnDefinitions[0];
            sidebarColumn.Width = new GridLength(
                Math.Clamp(vm.Settings.SidebarWidth, sidebarColumn.MinWidth, sidebarColumn.MaxWidth));

            ApplyThemeResources(vm);
            vm.ThemeResourcesChanged += (_, _) => ApplyThemeResources(vm);
            vm.ThemeResourcesChanged += (_, _) => ApplyTitleBarTheme(vm);
            // Windows flipping light/dark while the app runs: under the
            // "Système" preference this re-resolves the whole palette (which
            // raises ThemeResourcesChanged, repainting everything including
            // the title bar); under an explicit choice it does nothing.
            if (Avalonia.Application.Current?.PlatformSettings is { } platformSettings)
            {
                platformSettings.ColorValuesChanged += (_, _) => vm.RefreshSystemTheme();
            }

            vm.LoggedOut += (_, _) => PerformLogout();

            // Posted rather than called inline: the message list has just
            // been rebuilt when this fires, and the ScrollViewer's content
            // needs a layout pass first to know where "the end" actually is.
            vm.ScrollMessagesToEndRequested += (_, _) =>
                Dispatcher.UIThread.Post(() => MessagesScroller.ScrollToEnd(), DispatcherPriority.Background);

            vm.ScrollToMessageRequested += (_, postId) =>
                Dispatcher.UIThread.Post(() => ScrollToMessage(postId), DispatcherPriority.Background);

            vm.MessagesAppended += (_, _) => FollowIfAtBottom(MessagesScroller);

            // Drives the floating "back to the latest message" button. Also
            // covers the content growing underneath a reader who has scrolled
            // up, since that changes the extent and raises this too.
            MessagesScroller.ScrollChanged += (_, _) =>
                vm.IsAwayFromLiveTail = DistanceFromBottom(MessagesScroller) > AwayFromTailThreshold;
            vm.ThreadRepliesAppended += (_, _) => FollowIfAtBottom(ThreadScroller);

            // Images are decoded for the screen's physical pixels; follow the
            // window when it moves to a monitor with a different scale.
            vm.DisplayScaling = RenderScaling;
            ScalingChanged += (_, _) => vm.DisplayScaling = RenderScaling;

            // Not needed for the thread panel: it isn't virtualized, so a row
            // growing there just pushes what's below it, as it should.
            var readingPosition = new ReadingPositionKeeper(MessagesScroller, MessagesItemsControl, StickToBottomSlack);
            vm.MessageRowsChanging += (_, _) => readingPosition.Capture();

            vm.MentionReceived += (_, notification) =>
                Dispatcher.UIThread.Post(() => ShowMentionNotification(notification));

            vm.UnreadBadgeStateChanged += (_, _) => Dispatcher.UIThread.Post(() => UpdateTaskbarBadge(vm));
            UpdateTaskbarBadge(vm);

            // ComposeText has just been loaded with the message to edit —
            // focus the composer and put the caret at the end so the user
            // can start adjusting the text immediately.
            vm.EditComposerRequested += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                ComposerBox.Focus();
                ComposerBox.CaretIndex = ComposerBox.Text?.Length ?? 0;
            }, DispatcherPriority.Background);

            // Selecting a channel/DM, or opening a thread, should drop the
            // caret straight into the relevant composer — otherwise it's an
            // extra click before typing does anything.
            vm.ComposerFocusRequested += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                ComposerBox.Focus();
                ComposerBox.CaretIndex = ComposerBox.Text?.Length ?? 0;
            }, DispatcherPriority.Background);

            vm.ThreadComposerFocusRequested += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                ThreadComposerBox.Focus();
                ThreadComposerBox.CaretIndex = ThreadComposerBox.Text?.Length ?? 0;
            }, DispatcherPriority.Background);

            // The ViewModel knows what to type but not where the caret is —
            // that only exists on the TextBox, which lives here.
            vm.EmojiInsertRequested += (_, request) =>
                Dispatcher.UIThread.Post(() => InsertEmoji(request.Text, request.IsThread), DispatcherPriority.Background);

            // Opening the picker should leave the keyboard usable: start
            // typing to filter, Enter to take the first match.
            vm.EmojiSearchFocusRequested += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                EmojiSearchBox.Focus();
                EmojiSearchBox.CaretIndex = 0;
            }, DispatcherPriority.Background);

            // The ViewModel only knows which username was picked — replacing
            // the "@partial" token with it needs the composer's actual
            // caret/text, which lives here, not on the ViewModel.
            vm.MentionSuggestionAccepted += (_, username) =>
                Dispatcher.UIThread.Post(() => ApplyMentionSuggestion(username, vm));

            // Opening any of the three find-something panels should drop the
            // caret straight into its box — otherwise it's an extra click
            // before typing does anything. Posted at Background priority so
            // the panel has actually been laid out (it's collapsed until
            // the property flips) by the time focus is set.
            vm.PropertyChanged += (_, e) =>
            {
                TextBox? box = e.PropertyName switch
                {
                    nameof(MainViewModel.IsSearchOpen) when vm.IsSearchOpen => SearchBox,
                    nameof(MainViewModel.IsBrowseChannelsOpen) when vm.IsBrowseChannelsOpen => BrowseChannelsBox,
                    nameof(MainViewModel.IsNewConversationOpen) when vm.IsNewConversationOpen => NewConversationBox,

                    // Closing the emoji picker hands the caret back to the
                    // composer. Not cosmetic: the picker took focus into its
                    // search box, and on close focus would otherwise settle
                    // on whatever comes next in the tree — landing inside the
                    // message list scrolls the list to it, which reads as the
                    // conversation jumping. The composer sits outside the
                    // scroller, so focusing it can't move the conversation.
                    nameof(MainViewModel.IsEmojiPickerOpen) when !vm.IsEmojiPickerOpen =>
                        vm.EmojiPickerReturnsToThread ? ThreadComposerBox : ComposerBox,

                    _ => null,
                };
                if (box is not null)
                {
                    Dispatcher.UIThread.Post(() => box.Focus(), DispatcherPriority.Background);
                }
            };

            // The ViewModel can't reach IStorageProvider itself — it's tied
            // to this window (TopLevel), not the DataContext.
            vm.PickFilesAsync = PickFilesAsync;
        };

        // Tunnel phase: fires before a row's own Button consumes the pointer
        // press for its click, and — attached once on the scroller rather
        // than per-row — works for every row in every section, present or
        // future, without wiring anything into the DataTemplates themselves.
        SidebarScroller.AddHandler(PointerPressedEvent, OnRowPointerPressed, RoutingStrategies.Tunnel);
        SidebarScroller.AddHandler(PointerMovedEvent, OnRowPointerMoved, RoutingStrategies.Tunnel);
        SidebarScroller.AddHandler(PointerReleasedEvent, OnRowPointerReleased, RoutingStrategies.Tunnel);
        SidebarScroller.AddHandler(PointerCaptureLostEvent, OnRowPointerCaptureLost, RoutingStrategies.Tunnel);

        DragDrop.AddDragOverHandler(FavoritesDropZone, OnZoneDragOver);
        DragDrop.AddDropHandler(FavoritesDropZone, OnFavoritesDrop);
        DragDrop.AddDragOverHandler(RegularDropZone, OnZoneDragOver);
        DragDrop.AddDropHandler(RegularDropZone, OnRegularZoneDrop);

        // Tunnel phase: TextBox's own class handler inserts the newline
        // during the bubble phase, so a bubble-routed KeyDown (the plain
        // XAML "KeyDown=" attribute) never even sees a plain Enter press —
        // TextBox already marked it handled by the time it would fire.
        ComposerBox.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        ThreadComposerBox.AddHandler(KeyDownEvent, OnThreadComposerKeyDown, RoutingStrategies.Tunnel);
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        EmojiSearchBox.AddHandler(KeyDownEvent, OnEmojiSearchKeyDown, RoutingStrategies.Tunnel);

        ComposerBox.TextChanged += (_, _) => UpdateMentionAutocomplete(ComposerBox, isThread: false);
        ThreadComposerBox.TextChanged += (_, _) => UpdateMentionAutocomplete(ThreadComposerBox, isThread: true);
    }

    /// <summary>
    /// DataContext is inherited down the whole visual subtree of a row, so
    /// every descendant (down to the TextBlock actually under the pointer)
    /// reports the same item — walking up and stopping at the FIRST match
    /// would return whatever tiny inner element was hit, not the row
    /// itself. This keeps climbing until the item stops matching, so the
    /// result is the outermost element for that row — the one whose Bounds
    /// actually correspond to "this row" for position/height math.
    /// </summary>
    private static Control? FindChannelListRow(Control? control)
    {
        Control? match = null;
        while (control is not null)
        {
            if (control.DataContext is IChannelListItem)
            {
                match = control;
            }
            control = control.Parent as Control;
        }
        return match;
    }

    private static IChannelListItem? FindChannelListItem(Control? control) =>
        FindChannelListRow(control)?.DataContext as IChannelListItem;

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SidebarScroller).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragCandidateItem = FindChannelListItem(e.Source as Control);
        if (_dragCandidateItem is null)
        {
            _dragStartArgs = null;
            _dragStartPoint = null;
            return;
        }

        _dragStartArgs = e;
        _dragStartPoint = e.GetPosition(SidebarScroller);

        // A favorite row needs the long-press below before it starts
        // moving at all — see the comment in OnRowPointerMoved for why.
        if (_dragCandidateItem.IsFavorite)
        {
            StartLongPressTimer();
        }
    }

    private void StartLongPressTimer()
    {
        StopLongPressTimer();
        _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _longPressTimer.Tick += OnLongPressElapsed;
        _longPressTimer.Start();
    }

    private void StopLongPressTimer()
    {
        if (_longPressTimer is null)
        {
            return;
        }
        _longPressTimer.Stop();
        _longPressTimer.Tick -= OnLongPressElapsed;
        _longPressTimer = null;
    }

    /// <summary>Fires after holding a favorite row still for a moment — enters reorder-within-Favoris mode, distinct from the plain short-drag used to move a row in/out of Favoris.</summary>
    private void OnLongPressElapsed(object? sender, EventArgs e)
    {
        StopLongPressTimer();

        if (_dragCandidateItem is not { } item)
        {
            return;
        }
        _dragCandidateItem = null;
        _dragStartPoint = null;
        _dragStartArgs = null;

        _isReorderDragging = true;
        _reorderItem = item;
        _reorderTargetItem = null;
    }

    /// <summary>
    /// A short move while the button is down promotes the gesture from "an
    /// ordinary click on this row" into a drag — DoDragDropAsync takes over
    /// pointer capture from there, so the row's own click never fires.
    /// </summary>
    private async void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isReorderDragging)
        {
            HandleReorderPointerMoved(e);
            return;
        }

        if (_dragCandidateItem is null || _dragStartPoint is null || _dragStartArgs is null)
        {
            return;
        }
        if (!e.GetCurrentPoint(SidebarScroller).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // A real press-then-drag motion moves the mouse almost immediately —
        // well before the 450ms long-press timer below ever gets a chance
        // to fire — so without this, the short drag would win the race
        // every single time and the long-press would never be reachable.
        // A favorite row's drag (either kind — see FinishReorderDrag) only
        // ever starts once OnLongPressElapsed has actually fired.
        if (_dragCandidateItem.IsFavorite)
        {
            return;
        }

        var delta = e.GetPosition(SidebarScroller) - _dragStartPoint.Value;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
        {
            return;
        }

        StopLongPressTimer();

        var item = _dragCandidateItem;
        var startArgs = _dragStartArgs;
        _dragCandidateItem = null;
        _dragStartPoint = null;
        _dragStartArgs = null;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(ChannelDragFormat, item.Id));
        await DragDrop.DoDragDropAsync(startArgs, transfer, DragDropEffects.Move);
    }

    /// <summary>
    /// Tracks what's under the pointer during a favorite's long-press drag:
    /// hovering another favorite (of the same kind) lights up an insertion
    /// line above/below it; hovering the regular Canaux/Messages privés
    /// zone instead means "drop here to un-favorite", same destination the
    /// plain short-drag already sends a non-favorite row to.
    ///
    /// Deliberately hit-tests the pointer's current position (InputHitTest)
    /// rather than using e.Source: the row that was originally pressed
    /// implicitly captured the pointer, and a captured PointerMoved's
    /// Source stays pinned to that same original element for its whole
    /// subtree, no matter where the cursor actually is on screen — so
    /// e.Source here would always report the drag's own starting row.
    /// </summary>
    private void HandleReorderPointerMoved(PointerEventArgs e)
    {
        if (_reorderItem is null || DataContext is not MainViewModel vm)
        {
            return;
        }

        var hit = this.InputHitTest(e.GetPosition(this)) as Control;
        var targetRow = FindChannelListRow(hit);
        if (targetRow?.DataContext is IChannelListItem targetItem &&
            targetItem.Id != _reorderItem.Id &&
            targetItem.IsFavorite &&
            SameKind(targetItem, _reorderItem))
        {
            _reorderTargetItem = targetItem;
            _reorderPlaceAfter = e.GetPosition(targetRow).Y > targetRow.Bounds.Height / 2;
            _reorderMoveToRegular = false;
        }
        else
        {
            _reorderTargetItem = null;
            _reorderMoveToRegular = IsDescendantOf(hit, RegularDropZone);
        }

        UpdateDropIndicators(vm);
    }

    private static bool SameKind(IChannelListItem a, IChannelListItem b) => (a is ChannelItem) == (b is ChannelItem);

    private static bool IsDescendantOf(Control? control, Control ancestor)
    {
        while (control is not null)
        {
            if (ReferenceEquals(control, ancestor))
            {
                return true;
            }
            control = control.Parent as Control;
        }
        return false;
    }

    private void UpdateDropIndicators(MainViewModel vm)
    {
        foreach (var row in vm.FavoriteChannels.Cast<IChannelListItem>().Concat(vm.FavoriteDirectMessages))
        {
            var isTarget = ReferenceEquals(row, _reorderTargetItem);
            row.ShowDropIndicatorAbove = isTarget && !_reorderPlaceAfter;
            row.ShowDropIndicatorBelow = isTarget && _reorderPlaceAfter;
        }
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        FinishReorderDrag();
        StopLongPressTimer();
        _dragCandidateItem = null;
        _dragStartPoint = null;
        _dragStartArgs = null;
    }

    private void OnRowPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        FinishReorderDrag();
        StopLongPressTimer();
        _dragCandidateItem = null;
        _dragStartPoint = null;
        _dragStartArgs = null;
    }

    private void FinishReorderDrag()
    {
        if (!_isReorderDragging)
        {
            return;
        }

        if (DataContext is MainViewModel vm)
        {
            if (_reorderItem is not null && _reorderTargetItem is not null)
            {
                vm.ReorderFavorite(_reorderItem.Id, _reorderTargetItem.Id, _reorderPlaceAfter);
            }
            else if (_reorderItem is not null && _reorderMoveToRegular)
            {
                _ = vm.SetFavoriteAsync(_reorderItem.Id, isFavorite: false);
            }
            foreach (var row in vm.FavoriteChannels.Cast<IChannelListItem>().Concat(vm.FavoriteDirectMessages))
            {
                row.ShowDropIndicatorAbove = false;
                row.ShowDropIndicatorBelow = false;
            }
        }

        _isReorderDragging = false;
        _reorderItem = null;
        _reorderTargetItem = null;
        _reorderMoveToRegular = false;
    }

    /// <summary>Enter sends the message; Shift+Enter inserts a newline like every other chat client.</summary>
    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (TryHandleLinkAwarePaste(e, sender as TextBox))
        {
            return;
        }

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (vm.IsMainMentionPopupOpen && TryHandleMentionPopupKey(e, vm))
        {
            return;
        }

        // Same shortcut the official client uses: pressing up in an empty
        // composer jumps straight into editing your own last message,
        // rather than doing nothing (there's no history to navigate here).
        if (e.Key == Key.Up && vm.ComposeText.Length == 0)
        {
            e.Handled = true;
            vm.EditLastOwnMessage();
            return;
        }

        if (e.Key == Key.Escape && vm.IsEditingMessage)
        {
            e.Handled = true;
            vm.CancelEditMessageCommand.Execute(null);
            return;
        }

        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }
        e.Handled = true;
        vm.SendMessageCommand.Execute(null);
    }

    private void OnThreadComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (TryHandleLinkAwarePaste(e, sender as TextBox))
        {
            return;
        }

        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (vm.IsThreadMentionPopupOpen && TryHandleMentionPopupKey(e, vm))
        {
            return;
        }

        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }
        e.Handled = true;
        vm.SendThreadReplyCommand.Execute(null);
    }

    /// <summary>Up/Down move the highlighted suggestion, Enter/Tab accept it, Escape dismisses the popup without touching the composer text — all instead of that key's usual composer behavior while the popup is open.</summary>
    private static bool TryHandleMentionPopupKey(KeyEventArgs e, MainViewModel vm)
    {
        switch (e.Key)
        {
            case Key.Down:
                e.Handled = true;
                vm.MoveMentionSelection(1);
                return true;
            case Key.Up:
                e.Handled = true;
                vm.MoveMentionSelection(-1);
                return true;
            case Key.Enter:
            case Key.Tab:
                e.Handled = true;
                vm.AcceptSelectedMentionSuggestion();
                return true;
            case Key.Escape:
                e.Handled = true;
                vm.CloseMentionPopup();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Checks whether the caret just moved in or out of an "@partial"
    /// token and, if so, kicks off (or closes) the mention search — called
    /// after every keystroke in either composer via TextChanged.
    /// </summary>
    private void UpdateMentionAutocomplete(TextBox textBox, bool isThread)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var text = textBox.Text ?? "";
        var caret = textBox.CaretIndex;

        if (MentionTextHelper.TryGetMentionQuery(text, caret, out var start, out var query))
        {
            _mentionTokenStart = start;
            _ = vm.UpdateMentionSuggestionsAsync(query, isThread);
        }
        else
        {
            _mentionTokenStart = -1;
            vm.CloseMentionPopup();
        }
    }

    /// <summary>Set by UpdateMentionAutocomplete to the "@" index of whichever composer currently has the popup open — consumed once by ApplyMentionSuggestion.</summary>
    private int _mentionTokenStart = -1;

    private void ApplyMentionSuggestion(string username, MainViewModel vm)
    {
        if (_mentionTokenStart < 0)
        {
            return;
        }
        var textBox = vm.MentionPopupIsForThread ? ThreadComposerBox : ComposerBox;
        var text = textBox.Text ?? "";
        var caret = Math.Clamp(textBox.CaretIndex, _mentionTokenStart, text.Length);
        if (_mentionTokenStart > text.Length)
        {
            _mentionTokenStart = -1;
            return;
        }

        textBox.Text = MentionTextHelper.ApplyMention(text, _mentionTokenStart, caret, username, out var newCaretIndex);
        textBox.CaretIndex = newCaretIndex;
        textBox.SelectionStart = newCaretIndex;
        textBox.SelectionEnd = newCaretIndex;
        _mentionTokenStart = -1;
    }

    /// <summary>
    /// A plain Ctrl+V paste only ever gets the clipboard's plain-text slot,
    /// but copying a hyperlinked phrase (e.g. "click here" from a web page
    /// or an email) commonly puts just that visible text there — the actual
    /// URL only exists in the clipboard's HTML slot, as the anchor's href,
    /// and would otherwise be silently lost, needing a second paste-and-
    /// send just for the link. This recovers it: if the clipboard's HTML
    /// content has a link, this pastes Markdown "[text](url)" instead of
    /// just "text" (or the bare url if the two are already the same) —
    /// Mattermost renders that as just "text", clickable, same as the
    /// original. Anything without a link in it pastes exactly as before —
    /// this only ever adds information, never removes any.
    /// </summary>
    private bool TryHandleLinkAwarePaste(KeyEventArgs e, TextBox? textBox)
    {
        if (textBox is null || e.Key != Key.V || e.KeyModifiers != KeyModifiers.Control || Clipboard is not { } clipboard)
        {
            return false;
        }

        // Avalonia's clipboard is asynchronous, and whether the key press is
        // handled has to be decided now, before anything can be read. So every
        // Ctrl+V is taken over here, and the paste finishes a moment later:
        // with the link recovered when there is one, and as the TextBox's
        // own ordinary paste when there isn't.
        e.Handled = true;
        _ = PasteKeepingLinksAsync(clipboard, textBox);
        return true;
    }

    private static async Task PasteKeepingLinksAsync(Avalonia.Input.Platform.IClipboard clipboard, TextBox textBox)
    {
        string? withLinks = null;
        try
        {
            withLinks = await LinkAwarePaste.TryReadTextWithLinksAsync(clipboard);
        }
        catch (Exception ex)
        {
            // Clipboard access can transiently fail (another app briefly
            // holding it, an unexpected format) — the plain paste below still happens.
            Diagnostics.CrashLogger.Write("paste: reading the HTML clipboard failed", ex);
        }

        if (withLinks is null)
        {
            textBox.Paste();
            return;
        }
        InsertTextAtCaret(textBox, withLinks);
    }

    private static void InsertTextAtCaret(TextBox textBox, string text)
    {
        var currentText = textBox.Text ?? "";
        var start = Math.Clamp(Math.Min(textBox.SelectionStart, textBox.SelectionEnd), 0, currentText.Length);
        var end = Math.Clamp(Math.Max(textBox.SelectionStart, textBox.SelectionEnd), 0, currentText.Length);
        textBox.Text = currentText[..start] + text + currentText[end..];
        textBox.CaretIndex = start + text.Length;
        textBox.SelectionStart = textBox.CaretIndex;
        textBox.SelectionEnd = textBox.CaretIndex;
    }

    private void ShowMentionNotification(MentionNotification notification)
    {
        if (_notifier is null)
        {
            return;
        }
        _pendingNotificationTarget = (notification.ChannelId, notification.PostId);
        _notifier.Show(notification.Title, notification.Text);
    }

    /// <summary>Clicking the notification brings the window to front and jumps straight to the mentioned message.</summary>
    private void OnNotificationClicked(object? sender, EventArgs e)
    {
        if (_pendingNotificationTarget is not { } target || DataContext is not MainViewModel vm)
        {
            return;
        }
        _pendingNotificationTarget = null;

        WindowState = WindowState.Normal;
        Activate();
        _ = vm.JumpToMessageAsync(target.ChannelId, target.PostId);
    }

    /// <summary>Opens the native file picker (multi-select) for a composer's paperclip button — the ViewModel's own PickFilesAsync callback.</summary>
    private async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return [];
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Joindre un ou plusieurs fichiers",
            AllowMultiple = true,
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(path => path is not null)
            .Select(path => path!)
            .ToList();
    }

    /// <summary>
    /// Scrolls a specific message row into view (roughly centered) after a
    /// "jump to this message" from a search result — the row may be
    /// anywhere in the currently painted list, not necessarily at the end.
    /// </summary>
    private int _settlingScrollIndex = -1;
    private int _settlingScrollAttemptsLeft;

    private void ScrollToMessage(string postId)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        var target = vm.Messages.FirstOrDefault(m => m.Id == postId);
        if (target is null)
        {
            return;
        }
        var index = vm.Messages.IndexOf(target);
        if (index < 0)
        {
            return;
        }

        // The message list is virtualized, so a target far from the current
        // scroll position isn't realized yet — ScrollIntoView forces that
        // (and a rough scroll) first. Its jump, and the first centering pass
        // right after, are still only based on the panel's ESTIMATED height
        // for not-yet-realized neighboring items; as those get realized with
        // their real (often quite different) heights over the next few
        // layout passes, the panel keeps correcting the scroll extent —
        // visible as the target message drifting out of view a moment after
        // it was first centered. Re-centering across a few more layout
        // passes, instead of just once, rides that out instead of fighting it.
        MessagesItemsControl.ScrollIntoView(index);
        _settlingScrollIndex = index;
        _settlingScrollAttemptsLeft = 8;
        MessagesScroller.LayoutUpdated -= OnSettlingScrollLayoutUpdated;
        MessagesScroller.LayoutUpdated += OnSettlingScrollLayoutUpdated;
        CenterMessageContainer(index);
    }

    private void OnSettlingScrollLayoutUpdated(object? sender, EventArgs e)
    {
        if (_settlingScrollAttemptsLeft <= 0 || _settlingScrollIndex < 0)
        {
            MessagesScroller.LayoutUpdated -= OnSettlingScrollLayoutUpdated;
            return;
        }
        _settlingScrollAttemptsLeft--;
        var moved = CenterMessageContainer(_settlingScrollIndex);
        if (!moved || _settlingScrollAttemptsLeft <= 0)
        {
            MessagesScroller.LayoutUpdated -= OnSettlingScrollLayoutUpdated;
        }
    }

    /// <summary>Re-centers the given message in the viewport; returns false once the position has already settled (nothing worth correcting), so the caller can stop re-running this on every subsequent layout pass.</summary>
    private bool CenterMessageContainer(int index)
    {
        if (MessagesItemsControl.ContainerFromIndex(index) is not { } container)
        {
            return true;
        }
        var topLeft = container.TranslatePoint(new Point(0, 0), MessagesScroller);
        if (topLeft is not { } point)
        {
            return true;
        }

        var targetY = Math.Max(0, MessagesScroller.Offset.Y + point.Y - (MessagesScroller.Viewport.Height / 2) + (container.Bounds.Height / 2));
        if (Math.Abs(targetY - MessagesScroller.Offset.Y) < 1)
        {
            return false;
        }
        MessagesScroller.Offset = new Vector(MessagesScroller.Offset.X, targetY);
        return true;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel vm)
        {
            return;
        }
        e.Handled = true;
        vm.RunSearchCommand.Execute(null);
    }

    private void OnEmojiSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.CloseEmojiPickerCommand.Execute(null);
        }
        else if (e.Key == Key.Enter)
        {
            // A search box where Enter does nothing reads as broken: typing
            // "tada" and pressing Enter should take the obvious match.
            e.Handled = true;
            vm.PickFirstEmojiResultCommand.Execute(null);
        }
    }

    /// <summary>
    /// Types a picked emoji into a composer at the caret rather than tacking
    /// it onto the end — the picker is often opened mid-sentence, and landing
    /// the emoji somewhere else would mean going back to move it. It gets a
    /// space in front when it would otherwise run into the previous word, and
    /// one after it so typing can carry straight on.
    /// </summary>
    private void InsertEmoji(string emoji, bool isThread)
    {
        var textBox = isThread ? ThreadComposerBox : ComposerBox;
        var text = textBox.Text ?? "";
        var caret = Math.Clamp(textBox.CaretIndex, 0, text.Length);

        var prefix = caret > 0 && !char.IsWhiteSpace(text[caret - 1]) ? " " : "";
        var inserted = prefix + emoji + " ";

        textBox.Text = text[..caret] + inserted + text[caret..];

        var newCaret = caret + inserted.Length;
        textBox.CaretIndex = newCaret;
        textBox.SelectionStart = newCaret;
        textBox.SelectionEnd = newCaret;
        textBox.Focus();
    }

    /// <summary>The MenuItem inherits its DataContext from the link Button whose ContextFlyout it is declared in — that is the LinkSegment carrying the actual destination.</summary>
    private async void OnCopyLinkClick(object? sender, RoutedEventArgs e)
    {
        // The window's clipboard rather than the menu's: the menu lives in a
        // popup, which is a separate top level that may not offer one.
        if (sender is not MenuItem { DataContext: LinkSegment segment } || Clipboard is not { } clipboard)
        {
            return;
        }
        try
        {
            await clipboard.SetTextAsync(segment.LinkUrl);
        }
        catch
        {
            // Not worth an error banner just for a failed copy.
        }
    }

    private static void OnZoneDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(ChannelDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
    }

    private async void OnFavoritesDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(ChannelDragFormat) is string channelId && DataContext is MainViewModel vm)
        {
            await vm.SetFavoriteAsync(channelId, isFavorite: true);
        }
    }

    private async void OnRegularZoneDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(ChannelDragFormat) is string channelId && DataContext is MainViewModel vm)
        {
            await vm.SetFavoriteAsync(channelId, isFavorite: false);
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>
    /// Tints the native title bar to match the active palette, so the
    /// window frame doesn't stay bright white above a dark app. Follows the
    /// app's own palette rather than Windows' setting: picking Sombre on a
    /// light desktop is a deliberate choice, and a light title bar on it
    /// would read as the glitch. Best-effort — an older Windows build
    /// without this DWM attribute just keeps the default title bar.
    /// </summary>
    private void ApplyTitleBarTheme(MainViewModel vm)
    {
        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var useDark = ColorTokens.Theme.IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int));
        }
        catch
        {
            // Cosmetic only — never worth crashing over.
        }
    }

    /// <summary>
    /// Mirrors the ViewModel's aggregate unread state onto the taskbar
    /// button's overlay badge — a numbered badge takes priority (mentions
    /// and DMs are the more important case), a plain dot otherwise, nothing
    /// when everything's read.
    /// </summary>
    private void UpdateTaskbarBadge(MainViewModel vm)
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (vm.TotalPriorityUnreadCount > 0)
        {
            Services.TaskbarBadge.Update(handle, Services.TaskbarBadgeKind.Number, vm.TotalPriorityUnreadCount);
        }
        else if (vm.HasUnreadRegularMessages)
        {
            Services.TaskbarBadge.Update(handle, Services.TaskbarBadgeKind.Dot);
        }
        else
        {
            Services.TaskbarBadge.Update(handle, Services.TaskbarBadgeKind.None);
        }
    }

    /// <summary>
    /// Puts the window back where it was closed — but only if that place
    /// still exists. A position saved on a monitor that has since been
    /// unplugged (or an external screen the laptop is no longer docked to)
    /// would otherwise reopen the window off in coordinates with nothing to
    /// display them, where it cannot be seen, moved or closed. Anything that
    /// no longer overlaps an attached screen falls back to centring.
    /// </summary>
    private void RestoreWindowPlacement(MainViewModel vm)
    {
        var x = vm.Settings.WindowX;
        var y = vm.Settings.WindowY;

        if (!double.IsNaN(x) && !double.IsNaN(y) && IsOnAnAttachedScreen(x, y))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)x, (int)y);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (vm.Settings.WindowMaximised)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    /// True when the window's title bar would land somewhere visible. Tests
    /// a band along the top edge rather than the whole window: a window
    /// mostly off-screen is still perfectly usable as long as its title bar
    /// can be grabbed, which is what a user would do to bring it back.
    /// </summary>
    private bool IsOnAnAttachedScreen(double x, double y)
    {
        var screens = Screens?.All;
        if (screens is null || screens.Count == 0)
        {
            return false;
        }

        const int TitleBarBand = 40;
        var titleBar = new PixelRect((int)x, (int)y, (int)Math.Max(1, Width), TitleBarBand);

        foreach (var screen in screens)
        {
            if (screen.WorkingArea.Intersects(titleBar))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// How close to the bottom still counts as "reading the end of the
    /// conversation". A few pixels of slack absorbs a partially-scrolled
    /// last row, which would otherwise leave the view stuck one message
    /// behind for good.
    /// </summary>
    private const double StickToBottomSlack = 24;

    /// <summary>
    /// Keeps the conversation pinned to the newest message when the reader
    /// is already there — otherwise an arriving message lands just below the
    /// visible area and has to be scrolled to by hand.
    ///
    /// The decision is made HERE, synchronously, and not in the posted
    /// callback below: the messages have been added to the collection but
    /// the layout pass has not run yet, so the scroller still reports the
    /// extent it had before they arrived. That is exactly the "was the
    /// reader at the bottom a moment ago?" question. Asking after layout
    /// would compare against an extent that already grew, and the answer
    /// would always be no.
    /// </summary>
    /// <summary>
    /// How far from the newest message counts as "reading back through the
    /// conversation". Roughly a message and a half: far enough that the
    /// button doesn't blink in and out while the last row is half-scrolled.
    /// </summary>
    private const double AwayFromTailThreshold = 120;

    private static double DistanceFromBottom(ScrollViewer scroller) =>
        scroller.Extent.Height - scroller.Viewport.Height - scroller.Offset.Y;

    private static void FollowIfAtBottom(ScrollViewer scroller)
    {
        var distanceFromBottom = DistanceFromBottom(scroller);
        if (distanceFromBottom > StickToBottomSlack)
        {
            // Reading further up — leave the view where the reader put it.
            return;
        }

        // Posted so the new rows have been measured and laid out; scrolling
        // to the end before that would aim at the old, shorter extent.
        Dispatcher.UIThread.Post(() => scroller.ScrollToEnd(), DispatcherPriority.Background);
    }

    /// <summary>Reopens a fresh login screen (the remembered session is already cleared by the time this fires) and closes this window.</summary>
    private void PerformLogout()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            App.ShowLoginWindow(desktop);
        }
        Close();
    }

    /// <summary>Window.Resources entries bound via DynamicResource pick up the new palette live — see ThemeResources.</summary>
    private void ApplyThemeResources(MainViewModel vm) => ThemeResources.Apply(this, vm.Settings);
}
