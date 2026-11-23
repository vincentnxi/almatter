using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Almatter.App.Models;

namespace Almatter.App.Views;

/// <summary>
/// Pushes the active palette into a window's resource dictionary, where
/// DynamicResource references pick it up live. Shared by both windows so
/// the login screen and the main window can never drift apart.
/// </summary>
internal static class ThemeResources
{
    /// <summary>The strip at the top of the main window when Ark draws the title bar into it: the height of the system's caption buttons.</summary>
    public const double IntegratedTitleBarHeight = 32;

    /// <summary>Salon's thinner strip, as in its mockup.</summary>
    public const double SalonTitleBarHeight = 28;

    /// <summary>Salon's mention badge: a red that white text reads on at 4,6:1, on both palettes.</summary>
    public const string SalonBadgeHex = "#DA373C";

    /// <summary>
    /// Also sets Application.RequestedThemeVariant, which is what makes
    /// Avalonia's own Fluent chrome — scrollbars, flyout and menu
    /// backgrounds, tooltips, text selection, the caret, TextBox borders —
    /// follow the app instead of staying stubbornly light. Default hands
    /// the decision back to the OS, which is what AppThemeMode.System
    /// wants; ColorTokens has already resolved the same thing for our own
    /// palette, so the two stay in step.
    /// </summary>
    /// <param name="hasPanels">True for the main window, the one whose columns become floating cards under a theme that has them.</param>
    /// <summary>WCAG contrast of white text on a "#RRGGBB" colour.</summary>
    private static double ContrastWithWhite(string hex)
    {
        static double Channel(string h, int i)
        {
            var v = System.Convert.ToInt32(h.Substring(1 + i * 2, 2), 16) / 255.0;
            return v <= 0.03928 ? v / 12.92 : System.Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        var luminance = 0.2126 * Channel(hex, 0) + 0.7152 * Channel(hex, 1) + 0.0722 * Channel(hex, 2);
        return 1.05 / (luminance + 0.05);
    }

    public static void Apply(Window window, AppSettings settings, bool hasPanels = false)
    {
        if (Application.Current is { } app)
        {
            // A named theme is light or dark by nature, whatever the mode
            // setting says. That also gives context menus the right one of
            // their two looks: they stay system-styled on purpose (App.axaml).
            app.RequestedThemeVariant = NamedThemes.Find(settings.NamedTheme) is { } named
                ? (named.IsDark ? ThemeVariant.Dark : ThemeVariant.Light)
                : settings.ThemeMode switch
                {
                    AppThemeMode.Light => ThemeVariant.Light,
                    AppThemeMode.Dark => ThemeVariant.Dark,
                    _ => ThemeVariant.Default,
                };
        }

        var theme = ColorTokens.Theme;
        var resources = window.Resources;

        resources["BgAppBrush"] = ColorTokens.Solid(theme.BgApp);
        resources["BgPanelBrush"] = ColorTokens.Solid(theme.BgPanel);
        resources["BgRailBrush"] = ColorTokens.Solid(theme.BgRail);
        var gradient = theme.HasGradientGround;
        resources["BgHoverBrush"] = ColorTokens.Solid(gradient ? ColorTokens.GradientHoverHex : theme.BgHover);
        resources["DividerBrush"] = ColorTokens.Divider;

        // The three big areas. On a gradient theme GroundBackdrop paints the
        // gradient once behind everything; the sidebar lays a dark veil over
        // it and the thread panel lets it through, so it runs across the
        // whole window.
        resources["GroundTheme"] = theme;
        resources["SidebarGroundBrush"] = gradient ? ColorTokens.Solid(ColorTokens.GradientSidebarVeilHex) : ColorTokens.Solid(theme.BgPanel);
        resources["ThreadGroundBrush"] = gradient ? Brushes.Transparent : ColorTokens.Solid(theme.BgApp);
        resources["DividerStrongBrush"] = ColorTokens.Solid(theme.DividerStrong);
        resources["TextPrimaryBrush"] = ColorTokens.TextPrimary;
        resources["UnreadRowInkBrush"] = ColorTokens.UnreadRowInk;
        resources["TextSecondaryBrush"] = ColorTokens.TextSecondary;
        resources["TextTertiaryBrush"] = ColorTokens.Solid(theme.TextTertiary);
        resources["CardBgBrush"] = ColorTokens.CardBg;
        resources["AccentBrush"] = ColorTokens.Accent;
        resources["AccentSoftBrush"] = ColorTokens.AccentSoft;
        resources["AccentInkBrush"] = ColorTokens.AccentInk;
        resources["AccentTextBrush"] = ColorTokens.AccentText;

        // A pastel badge or unread dot on a light sidebar is 1,1 to 1,5:1
        // against it: a ring of the accent's deep shade, at 45 %, outlines it.
        // Salon's deep shade is for text only: its accent needs no ring.
        var pastelRing = theme.ArkStyle && theme.AccentDeepHex is not null;
        resources["AccentEdgeBrush"] = pastelRing ? ColorTokens.Solid("#73" + theme.AccentDeepHex![1..]) : Brushes.Transparent;
        resources["AccentEdgeThickness"] = new Thickness(pastelRing ? 1 : 0);
        resources["TextSelectionBrush"] = ColorTokens.TextSelection;
        resources["OnAccentBrush"] = ColorTokens.Solid(theme.OnAccentHex);
        resources["DangerBrush"] = ColorTokens.Solid(theme.DangerHex);
        resources["DangerSoftBrush"] = ColorTokens.Solid(theme.DangerSoftHex);
        resources["DangerBorderBrush"] = ColorTokens.Solid(theme.DangerBorderHex);
        resources["ScrimBrush"] = ColorTokens.Solid(theme.ScrimHex);
        resources["PresenceOnlineBrush"] = ColorTokens.Solid(theme.PresenceOnlineHex);
        resources["PresenceAwayBrush"] = ColorTokens.Solid(theme.PresenceAwayHex);
        resources["PresenceOfflineBrush"] = ColorTokens.Solid(theme.PresenceOfflineHex);
        resources["PopoverShadow"] = BoxShadows.Parse(theme.PopoverShadow);
        resources["CardShadow"] = BoxShadows.Parse(theme.CardShadow);
        resources["RowRadiusValue"] = new CornerRadius(theme.RowRadius);
        resources["SurfaceRadiusValue"] = new CornerRadius(theme.SurfaceRadius);

        // The three columns: edge to edge with a line between them, or (Ark)
        // rounded cards with a gap all round over the window's own ground.
        // No shadow under the cards on purpose: the app renders on the CPU,
        // and a blurred shadow the size of a panel is redrawn on every scroll.
        var floating = theme.FloatingPanels;
        const double gap = 10;
        var salon = theme.SalonStyle;

        // Ark and Salon on Windows draw the title bar into the window: the
        // panels start below a strip of the window's ground that drags the
        // window, with the system's own buttons at its right. Salon's strip
        // is thinner, in the sidebar's colour, with smaller buttons.
        var integratedTitle = hasPanels && (theme.ArkStyle || salon) && System.OperatingSystem.IsWindows();
        var stripHeight = !integratedTitle ? 0.0 : salon ? SalonTitleBarHeight : IntegratedTitleBarHeight;
        var top = integratedTitle ? stripHeight : gap;
        if (hasPanels)
        {
            window.ExtendClientAreaTitleBarHeightHint = integratedTitle ? stripHeight : -1;
            window.ExtendClientAreaToDecorationsHint = integratedTitle;
        }
        resources["TitleStripVisible"] = integratedTitle;
        resources["TitleStripHeight"] = stripHeight;
        resources["SplitterMargin"] = new Thickness(-3, stripHeight, 0, 0);
        resources["CaptionButtonWidth"] = salon ? 34.0 : 36.0;
        resources["CaptionButtonHeight"] = salon ? SalonTitleBarHeight : IntegratedTitleBarHeight;
        resources["WindowTitleOpacity"] = salon ? 0.0 : 1.0;

        // Salon's columns sit flush, without a line between sidebar and
        // conversation (their colours part them), below the title strip.
        // The conversation paints its own ground, since the window's is the
        // sidebar's colour for the strip.
        var flushTop = new Thickness(0, stripHeight, 0, 0);
        resources["PanelRadius"] = new CornerRadius(floating ? 12 : 0);
        resources["SidebarMargin"] = floating ? new Thickness(gap, top, 0, gap) : flushTop;
        resources["SidebarBorderThickness"] = floating ? new Thickness(1) : salon ? new Thickness(0) : new Thickness(0, 0, 1, 0);
        resources["ConversationMargin"] = floating ? new Thickness(gap, top, gap, gap) : flushTop;
        resources["ConversationBorderThickness"] = new Thickness(floating ? 1 : 0);
        resources["ConversationGroundBrush"] = floating || salon ? ColorTokens.Solid(theme.BgApp) : Brushes.Transparent;
        resources["ThreadMargin"] = floating ? new Thickness(0, top, gap, gap) : flushTop;
        resources["ThreadBorderThickness"] = floating ? new Thickness(1) : new Thickness(1, 0, 0, 0);

        // Ark's other touches. Every value in the "else" half is what the
        // window had before these resources existed, so no other theme moves.
        var ark = theme.ArkStyle;

        // Soft shadows under Ark's cards, when the user keeps them on: a
        // tight one for the edge and a wide, pulled-in one for the lift, as
        // in the mockup. A panel's shadow is drawn by a plate behind it (see
        // MainWindow.axaml): the panel clips its content to its rounded
        // corners, which would cut its own shadow off. Cost measured in
        // AppSettings.ArkShadows.
        var shadows = ark && theme.CastsShadows;
        resources["PanelShadow"] = !shadows ? default
            : BoxShadows.Parse(theme.IsDark ? "0 12 28 -12 #B3000000" : "0 1 2 0 #0D101114, 0 10 28 -12 #33101114");
        // The plates are filled (so the shadow has a shape to fall from) only
        // when they cast one; otherwise they would darken a gradient theme's
        // see-through sidebar a second time.
        resources["ShadowPlateBrush"] = shadows ? ColorTokens.Solid(theme.BgPanel) : Brushes.Transparent;
        resources["SmallCardShadow"] = !shadows ? default
            : BoxShadows.Parse(theme.IsDark ? "0 2 6 0 #66000000" : "0 1 2 0 #0D101114, 0 6 16 -8 #26101114");
        resources["SelectedRowBorderThickness"] = new Thickness(ark ? 1 : 0);

        // Section headings and the channel title.
        resources["SectionHeaderFontSize"] = ark ? 10.5 : salon ? 12.0 : 12.5;
        resources["SectionHeaderFontWeight"] = ark ? FontWeight.Medium : FontWeight.Bold;
        resources["SectionHeaderLetterSpacing"] = ark ? 1.2 : salon ? 0.3 : 0.0;
        resources["ChannelTitleFontSize"] = ark ? 22.0 : salon ? 16.0 : 15.0;
        resources["ChannelTitleFontWeight"] = ark ? FontWeight.Light : FontWeight.Bold;

        // Primary buttons (send, join, save): the accent, or ink on Ark —
        // black on the light palette, near-white on the dark one.
        resources["PrimaryButtonBrush"] = ark ? ColorTokens.TextPrimary : ColorTokens.Accent;
        resources["OnPrimaryButtonBrush"] = ColorTokens.Solid(ark ? theme.BgApp : theme.OnAccentHex);
        resources["PrimaryButtonRadius"] = new CornerRadius(ark ? 15 : 7);

        // The channel header's round, outlined icon buttons.
        resources["HeaderButtonBrush"] = ark ? ColorTokens.CardBg : Brushes.Transparent;
        resources["HeaderButtonBorderBrush"] = ark ? ColorTokens.Divider : Brushes.Transparent;
        resources["HeaderButtonBorderThickness"] = new Thickness(ark ? 1 : 0);
        resources["HeaderButtonRadius"] = new CornerRadius(ark ? 16 : theme.RowRadius);
        resources["HeaderButtonSize"] = ark ? 32.0 : 28.0;

        // Switches for the few elements that exist in only one of the two
        // looks (the dotted date rules, the "Ctrl K" chip, the reply icon).
        resources["ArkOn"] = ark;
        resources["ArkOff"] = !ark;
        resources["SalonOn"] = salon;
        resources["SalonOff"] = !salon;
        resources["UnreadBarWidth"] = salon ? 4.0 : 0.0;

        // Every theme: the red "Nouveau" rule (its label white, or near
        // black on a red too light for white text), and a message naming
        // the reader tinted with the palette's warm "away" colour.
        var markerHex = salon ? SalonBadgeHex : theme.DangerHex;
        resources["NewMarkerBrush"] = ColorTokens.Solid(markerHex);
        resources["OnNewMarkerBrush"] = ColorTokens.Solid(ContrastWithWhite(markerHex) >= 4.5 ? "#FFFFFF" : "#14161A");
        resources["MentionRowBrush"] = ColorTokens.Solid((theme.IsDark ? "#1A" : "#24") + theme.PresenceAwayHex[1..]);
        resources["MentionBarBrush"] = ColorTokens.Solid(theme.PresenceAwayHex);

        // Salon: mention badges in red, a small bar in the text colour left
        // of an unread channel instead of an accent dot, the sidebar's
        // search field sunk in the darkest grey, and mentions in messages on
        // a tint of the accent. White on the badge red is 4,6:1.
        resources["BadgeBrush"] = salon ? ColorTokens.Solid(SalonBadgeHex) : ColorTokens.Accent;
        resources["OnBadgeBrush"] = salon ? Brushes.White : ColorTokens.Solid(theme.OnAccentHex);
        resources["FilterBrush"] = salon ? ColorTokens.Solid(theme.BgRail) : ColorTokens.CardBg;
        resources["FilterBorderThickness"] = new Thickness(salon ? 0 : 1);
        resources["MentionBrush"] = salon ? ColorTokens.Solid(theme.IsDark ? "#C9CDFB" : theme.AccentDeepHex ?? theme.AccentHex) : ColorTokens.AccentText;
        resources["MentionBackgroundBrush"] = salon ? ColorTokens.Solid(theme.IsDark ? "#4D5865F2" : "#265865F2") : Brushes.Transparent;

        // Text: sidebar rows a touch smaller, names semi-bold rather than bold.
        resources["SidebarRowFontSize"] = ark ? 14.0 : 14.5;
        resources["AuthorFontWeight"] = ark || salon ? FontWeight.SemiBold : FontWeight.Bold;
        resources["PanelTitleFontWeight"] = ark ? FontWeight.Medium : FontWeight.Bold;

        // The date between two days: a pill on a rule, or (Ark) a small
        // capitalised label between dotted rules.
        // Salon: a bare label on a solid rule.
        var bareDate = ark || salon;
        resources["DateLabelPillBrush"] = bareDate ? Brushes.Transparent : ColorTokens.CardBg;
        resources["DateLabelPillBorderBrush"] = bareDate ? Brushes.Transparent : ColorTokens.Solid(theme.DividerStrong);
        resources["DateLabelPillPadding"] = bareDate ? new Thickness(4, 0) : new Thickness(12, 4);
        resources["DateLabelFontSize"] = ark ? 10.5 : salon ? 12.0 : 11.5;
        resources["DateLabelFontWeight"] = ark ? FontWeight.Medium : FontWeight.SemiBold;
        resources["DateLabelLetterSpacing"] = ark ? 1.2 : 0.0;

        // Reactions: outlined pills, or (Ark) tinted ones with a mono count.
        resources["ReactionCountFontWeight"] = ark ? FontWeight.Medium : FontWeight.Bold;

        // "3 replies": accent text, or (Ark) an outlined pill in ink.
        resources["ThreadPillBrush"] = ark ? ColorTokens.CardBg : Brushes.Transparent;
        resources["ThreadPillBorderBrush"] = ark ? ColorTokens.Divider : Brushes.Transparent;
        resources["ThreadPillBorderThickness"] = new Thickness(ark ? 1 : 0);
        resources["ThreadPillPadding"] = ark ? new Thickness(10, 5, 12, 5) : new Thickness(0);
        resources["ThreadPillRadius"] = new CornerRadius(ark ? 14 : 0);
        resources["ThreadReplyTextBrush"] = ark ? ColorTokens.TextPrimary : ColorTokens.AccentText;
        resources["ThreadReplyFontWeight"] = ark ? FontWeight.Medium : FontWeight.SemiBold;

        // Attachments and code blocks.
        resources["AttachmentBrush"] = ark ? ColorTokens.Solid(theme.BgPanel) : ColorTokens.CardBg;
        resources["AttachmentRadius"] = new CornerRadius(ark ? 10 : 8);
        resources["CodeBlockBrush"] = ColorTokens.Solid(ark ? theme.BgRail : theme.BgPanel);
        resources["CodeBlockRadius"] = new CornerRadius(ark ? 8 : 6);
        resources["CodeFontFamily"] = ark ? FontFamily.Parse(AppFonts.ArkMonoStack) : MessageInlineText.MonospaceFont;

        // The thread panel's opening message, set in a card on Ark.
        resources["ThreadRootBrush"] = ark ? ColorTokens.Solid(theme.BgPanel) : Brushes.Transparent;
        resources["ThreadRootBorderBrush"] = ark ? ColorTokens.Divider : Brushes.Transparent;
        resources["ThreadRootBorderThickness"] = new Thickness(ark ? 1 : 0);
        resources["ThreadRootPadding"] = new Thickness(ark ? 12 : 0);
        resources["ThreadRootRadius"] = new CornerRadius(ark ? 10 : 0);

        // The sidebar's foot (you, your status, the settings): a strip under
        // a line, or (Ark) a card with a round ink settings button.
        // Salon: a band of its own, a step darker than the sidebar, no line.
        resources["FooterBrush"] = ark ? ColorTokens.CardBg : theme.FooterHex is { } foot ? ColorTokens.Solid(foot) : Brushes.Transparent;
        resources["FooterBorderThickness"] = ark ? new Thickness(1) : salon ? new Thickness(0) : new Thickness(0, 1, 0, 0);
        resources["FooterMargin"] = ark ? new Thickness(10, 0, 10, 10) : new Thickness(0);
        resources["FooterPadding"] = ark ? new Thickness(10, 8) : salon ? new Thickness(10, 9) : new Thickness(12, 10);
        resources["FooterRadius"] = new CornerRadius(ark ? 12 : 0);
        resources["SettingsButtonBrush"] = ark ? ColorTokens.TextPrimary : Brushes.Transparent;
        resources["SettingsIconBrush"] = ark ? ColorTokens.Solid(theme.BgApp) : ColorTokens.TextSecondary;
        resources["SettingsButtonSize"] = ark ? 34.0 : 28.0;
        resources["SettingsButtonRadius"] = new CornerRadius(ark ? 17 : 0);

        // The sidebar's "+" buttons: bare, or (Ark) small outlined circles.
        resources["SectionButtonSize"] = ark ? 26.0 : 20.0;
        resources["SectionButtonRadius"] = new CornerRadius(ark ? 13 : 0);

        // The composer's layout: its buttons on a row under the text, or (Ark,
        // Salon) on the same line, right of the text and before the send
        // button, which makes the box one line shorter.
        var oneLine = theme.OneLineComposer;
        resources["ComposerToolsRow"] = oneLine ? 0 : 1;
        resources["ComposerToolsColumn"] = oneLine ? 1 : 0;
        resources["ComposerTextSpan"] = oneLine ? 1 : 2;
        resources["ComposerToolsAlignment"] = oneLine ? Avalonia.Layout.VerticalAlignment.Bottom : Avalonia.Layout.VerticalAlignment.Stretch;
        resources["ComposerTextPadding"] = oneLine ? new Thickness(14, 10, 6, 10) : new Thickness(12, 10, 12, 4);
        resources["ComposerToolsMargin"] = oneLine ? new Thickness(0, 5, 6, 5) : new Thickness(10, 4, 10, 10);
        resources["ThreadComposerToolsMargin"] = oneLine ? new Thickness(6, 0, 0, 0) : new Thickness(0);
        resources["ComposerTextAlignment"] = oneLine ? Avalonia.Layout.VerticalAlignment.Center : Avalonia.Layout.VerticalAlignment.Stretch;

        // The composer's box: a card with an outline, or (Salon) a filled
        // box with none.
        resources["ComposerBrush"] = theme.ComposerHex is { } composer ? ColorTokens.Solid(composer) : ColorTokens.CardBg;
        resources["ComposerFrameBorderThickness"] = new Thickness(salon ? 0 : 1);

        // "… is typing": a floating pill, or (Ark, Salon) bare text on the
        // conversation's ground. The ground-coloured fill stays so that,
        // scrolled up, messages don't run under the words.
        var bareTyping = ark || salon;
        resources["TypingPillBrush"] = bareTyping ? ColorTokens.Solid(theme.BgApp) : ColorTokens.CardBg;
        resources["TypingPillBorderBrush"] = bareTyping ? Brushes.Transparent : ColorTokens.Solid(theme.DividerStrong);
        resources["TypingPillShadow"] = bareTyping ? default : BoxShadows.Parse(theme.PopoverShadow);

        // The composer sits in a sunken well on Ark.
        resources["ComposerWellBrush"] = ark ? ColorTokens.Solid(theme.BgRail) : Brushes.Transparent;
        resources["ComposerWellBorderBrush"] = ark ? ColorTokens.Divider : Brushes.Transparent;
        resources["ComposerWellBorderThickness"] = new Thickness(ark ? 1 : 0);
        resources["ComposerWellPadding"] = new Thickness(ark ? 5 : 0);
        resources["ComposerWellRadius"] = new CornerRadius(ark ? theme.SurfaceRadius + 4 : 0);

        // Not a palette token, but it travels the same path: one resource
        // the whole window inherits from, swapped live when the choice changes.
        var appFont = AppFonts.Resolve(settings.FontChoice, settings.EmojiStyle);
        resources["AppFontFamily"] = appFont;

        // Small labels (headings, times, dates): monospaced on Ark, the
        // app's own font everywhere else.
        resources["LabelFontFamily"] = theme.ArkStyle ? FontFamily.Parse(AppFonts.ArkMonoStack) : appFont;

        // The window's own chrome-level colors aren't reachable through
        // DynamicResource on the Window element itself in XAML (they'd be
        // resolved against the dictionary being replaced), so set them here.
        // A gradient theme's gradient is drawn on top of this by the window's
        // GroundBackdrop (much cheaper than a gradient brush on the CPU);
        // the solid colour is what shows for every other theme.
        // Only the main window has panels to float: the settings and login
        // windows keep the plain ground under a floating-panel theme.
        window.Background = ColorTokens.Solid(hasPanels && theme.WindowGroundHex is { } ground ? ground : theme.BgApp);
        window.Foreground = ColorTokens.TextPrimary;
    }
}
