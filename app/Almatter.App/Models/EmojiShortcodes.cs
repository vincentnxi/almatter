using System;
using System.Collections.Generic;
using System.Linq;

namespace Almatter.App.Models;

/// <summary>
/// Mattermost reactions carry an emoji *shortcode* (e.g. "+1"), not the
/// actual character. This covers a broad slice of the standard set (not
/// Mattermost's full ~1900-entry table — just what's realistically common in
/// everyday reactions) plus skin-tone variants of anything in it; anything
/// still unmapped is presumed to be a server custom emoji, resolved
/// separately via its image, and falls back to showing the shortcode itself
/// if that fails too.
/// </summary>
public static class EmojiShortcodes
{
    private static readonly Dictionary<string, string> Map = new()
    {
        // Faces
        ["+1"] = "👍",
        ["thumbsup"] = "👍",
        ["-1"] = "👎",
        ["thumbsdown"] = "👎",
        ["smile"] = "😄",
        ["smiley"] = "😃",
        ["grinning"] = "😀",
        ["laughing"] = "😆",
        ["joy"] = "😂",
        ["rofl"] = "🤣",
        ["rolling_on_the_floor_laughing"] = "🤣",
        ["slightly_smiling_face"] = "🙂",
        ["wink"] = "😉",
        ["blush"] = "😊",
        ["heart_eyes"] = "😍",
        ["heart"] = "❤️",
        ["hearts"] = "💕",
        ["thinking_face"] = "🤔",
        ["thinking"] = "🤔",
        ["neutral_face"] = "😐",
        ["confused"] = "😕",
        ["cry"] = "😢",
        ["sob"] = "😭",
        ["scream"] = "😱",
        ["astonished"] = "😲",
        ["flushed"] = "😳",
        ["sweat_smile"] = "😅",
        ["relaxed"] = "☺️",
        ["smirk"] = "😏",
        ["unamused"] = "😒",
        ["disappointed"] = "😞",
        ["worried"] = "😟",
        ["frowning"] = "🙁",
        ["persevere"] = "😣",
        ["triumph"] = "😤",
        ["angry"] = "😠",
        ["rage"] = "😡",
        ["innocent"] = "😇",
        ["sunglasses"] = "😎",
        ["nerd_face"] = "🤓",
        ["hushed"] = "😲",
        ["open_mouth"] = "😮",
        ["grimacing"] = "😬",
        ["rolling_eyes"] = "🙄",
        ["expressionless"] = "😑",
        ["no_mouth"] = "😶",
        ["star_struck"] = "🤩",
        ["partying_face"] = "🥳",
        ["exploding_head"] = "🤯",
        ["clown_face"] = "🤡",
        ["ghost"] = "👻",
        ["alien"] = "👽",
        ["robot"] = "🤖",
        ["jack_o_lantern"] = "🎃",
        ["see_no_evil"] = "🙈",
        ["hear_no_evil"] = "🙉",
        ["speak_no_evil"] = "🙊",
        ["kiss"] = "💋",
        ["kissing_heart"] = "😘",
        ["yum"] = "😋",
        ["stuck_out_tongue"] = "😛",
        ["sleepy"] = "😪",
        ["sleeping"] = "😴",
        ["dizzy_face"] = "😵",
        ["mask"] = "😷",
        ["nauseated_face"] = "🤢",
        ["lying_face"] = "🤥",
        ["cold_sweat"] = "😰",
        ["fearful"] = "😨",
        ["yawning_face"] = "🥱",
        ["sweat"] = "😓",

        // Gestures / body
        ["raised_hand"] = "✋",
        ["vulcan_salute"] = "🖖",
        ["metal"] = "🤘",
        ["call_me_hand"] = "🤙",
        ["crossed_fingers"] = "🤞",
        ["punch"] = "👊",
        ["fist"] = "✊",
        ["v"] = "✌️",
        ["writing_hand"] = "✍️",
        ["selfie"] = "🤳",

        // Animals / nature
        ["dog"] = "🐶",
        ["cat"] = "🐱",
        ["mouse"] = "🐭",
        ["rabbit"] = "🐰",
        ["bear"] = "🐻",
        ["panda_face"] = "🐼",
        ["koala"] = "🐨",
        ["tiger"] = "🐯",
        ["lion"] = "🦁",
        ["cow"] = "🐮",
        ["pig"] = "🐷",
        ["frog"] = "🐸",
        ["monkey_face"] = "🐵",
        ["chicken"] = "🐔",
        ["penguin"] = "🐧",
        ["bird"] = "🐦",
        ["duck"] = "🦆",
        ["eagle"] = "🦅",
        ["owl"] = "🦉",
        ["bat"] = "🦇",
        ["wolf"] = "🐺",
        ["horse"] = "🐴",
        ["unicorn"] = "🦄",
        ["bee"] = "🐝",
        ["butterfly"] = "🦋",
        ["snail"] = "🐌",
        ["snake"] = "🐍",
        ["dragon"] = "🐉",
        ["turtle"] = "🐢",
        ["tropical_fish"] = "🐠",
        ["fish"] = "🐟",
        ["dolphin"] = "🐬",
        ["whale"] = "🐳",
        ["octopus"] = "🐙",
        ["crab"] = "🦀",
        ["camel"] = "🐫",
        ["elephant"] = "🐘",
        ["giraffe_face"] = "🦒",
        ["hedgehog"] = "🦔",
        ["sloth"] = "🦥",
        ["deer"] = "🦌",
        ["llama"] = "🦙",
        ["four_leaf_clover"] = "🍀",
        ["cherry_blossom"] = "🌸",
        ["tulip"] = "🌷",
        ["rose"] = "🌹",
        ["sunflower"] = "🌻",
        ["maple_leaf"] = "🍁",
        ["evergreen_tree"] = "🌲",
        ["palm_tree"] = "🌴",
        ["cactus"] = "🌵",
        ["seedling"] = "🌱",
        ["rainbow"] = "🌈",
        ["cloud"] = "☁️",
        ["umbrella"] = "☔",
        ["snowflake"] = "❄️",
        ["snowman"] = "⛄",
        ["droplet"] = "💧",
        ["ocean"] = "🌊",

        // Food / drink
        ["apple"] = "🍎",
        ["tangerine"] = "🍊",
        ["lemon"] = "🍋",
        ["banana"] = "🍌",
        ["watermelon"] = "🍉",
        ["grapes"] = "🍇",
        ["strawberry"] = "🍓",
        ["cherries"] = "🍒",
        ["peach"] = "🍑",
        ["pineapple"] = "🍍",
        ["avocado"] = "🥑",
        ["tomato"] = "🍅",
        ["eggplant"] = "🍆",
        ["corn"] = "🌽",
        ["carrot"] = "🥕",
        ["bread"] = "🍞",
        ["croissant"] = "🥐",
        ["cheese"] = "🧀",
        ["egg"] = "🥚",
        ["bacon"] = "🥓",
        ["pancakes"] = "🥞",
        ["fries"] = "🍟",
        ["hamburger"] = "🍔",
        ["hotdog"] = "🌭",
        ["taco"] = "🌮",
        ["burrito"] = "🌯",
        ["popcorn"] = "🍿",
        ["spaghetti"] = "🍝",
        ["ramen"] = "🍜",
        ["sushi"] = "🍣",
        ["curry"] = "🍛",
        ["ice_cream"] = "🍨",
        ["icecream"] = "🍦",
        ["doughnut"] = "🍩",
        ["cookie"] = "🍪",
        ["chocolate_bar"] = "🍫",
        ["candy"] = "🍬",
        ["lollipop"] = "🍭",
        ["tea"] = "🍵",
        ["sake"] = "🍶",
        ["wine_glass"] = "🍷",
        ["cocktail"] = "🍸",
        ["tropical_drink"] = "🍹",
        ["champagne"] = "🍾",

        // Activities / objects
        ["soccer"] = "⚽",
        ["basketball"] = "🏀",
        ["football"] = "🏈",
        ["baseball"] = "⚾",
        ["tennis"] = "🎾",
        ["volleyball"] = "🏐",
        ["golf"] = "⛳",
        ["running"] = "🏃",
        ["swimming"] = "🏊",
        ["trophy"] = "🏆",
        ["medal_sports"] = "🏅",
        ["dart"] = "🎯",
        ["video_game"] = "🎮",
        ["game_die"] = "🎲",
        ["guitar"] = "🎸",
        ["microphone"] = "🎤",
        ["headphones"] = "🎧",
        ["musical_note"] = "🎵",
        ["notes"] = "🎶",
        ["art"] = "🎨",
        ["movie_camera"] = "🎥",
        ["camera"] = "📷",
        ["tv"] = "📺",
        ["telephone"] = "☎️",
        ["iphone"] = "📱",
        ["keyboard"] = "⌨️",
        ["battery"] = "🔋",
        ["calendar"] = "📅",
        ["alarm_clock"] = "⏰",
        ["hourglass"] = "⌛",
        ["watch"] = "⌚",
        ["gear"] = "⚙️",
        ["wrench"] = "🔧",
        ["hammer"] = "🔨",
        ["bomb"] = "💣",
        ["pill"] = "💊",
        ["syringe"] = "💉",
        ["key"] = "🔑",
        ["lock"] = "🔒",
        ["unlock"] = "🔓",
        ["bell"] = "🔔",
        ["mega"] = "📣",
        ["envelope"] = "✉️",
        ["email"] = "📧",
        ["package"] = "📦",
        ["memo"] = "📝",
        ["book"] = "📖",
        ["books"] = "📚",
        ["bookmark"] = "🔖",
        ["paperclip"] = "📎",
        ["link"] = "🔗",
        ["dollar"] = "💵",
        ["credit_card"] = "💳",
        ["gem"] = "💎",
        ["crown"] = "👑",
        ["ring"] = "💍",
        ["briefcase"] = "💼",
        ["airplane"] = "✈️",
        ["car"] = "🚗",
        ["taxi"] = "🚕",
        ["bus"] = "🚌",
        ["train"] = "🚆",
        ["ship"] = "🚢",
        ["anchor"] = "⚓",
        ["house"] = "🏠",
        ["office"] = "🏢",
        ["hospital"] = "🏥",
        ["school"] = "🏫",
        ["bank"] = "🏦",
        ["hotel"] = "🏨",
        ["mountain"] = "⛰️",
        ["city_sunset"] = "🌇",
        ["night_with_stars"] = "🌃",
        ["milky_way"] = "🌌",

        // Symbols
        ["infinity"] = "♾️",
        ["recycle"] = "♻️",
        ["red_circle"] = "🔴",
        ["green_circle"] = "🟢",
        ["blue_circle"] = "🔵",
        ["yellow_circle"] = "🟡",
        ["arrow_up"] = "⬆️",
        ["arrow_down"] = "⬇️",
        ["arrow_left"] = "⬅️",
        ["arrow_right"] = "➡️",
        ["top"] = "🔝",
        ["new"] = "🆕",
        ["free"] = "🆓",
        ["sos"] = "🆘",
        ["ok"] = "🆗",
        ["cool"] = "🆒",
        ["copyright"] = "©️",
        ["registered"] = "®️",
        ["trademark"] = "™️",
        ["information_source"] = "ℹ️",

        // Original set (kept)
        ["tada"] = "🎉",
        ["confetti_ball"] = "🎊",
        ["fire"] = "🔥",
        ["100"] = "💯",
        ["rocket"] = "🚀",
        ["eyes"] = "👀",
        ["clap"] = "👏",
        ["raised_hands"] = "🙌",
        ["pray"] = "🙏",
        ["wave"] = "👋",
        ["ok_hand"] = "👌",
        ["muscle"] = "💪",
        ["point_up"] = "☝️",
        ["point_right"] = "👉",
        ["point_left"] = "👈",
        ["white_check_mark"] = "✅",
        ["heavy_check_mark"] = "✔️",
        ["x"] = "❌",
        ["warning"] = "⚠️",
        ["question"] = "❓",
        ["exclamation"] = "❗",
        ["bulb"] = "💡",
        ["star"] = "⭐",
        ["sparkles"] = "✨",
        ["zap"] = "⚡",
        ["coffee"] = "☕",
        ["pizza"] = "🍕",
        ["beers"] = "🍻",
        ["cake"] = "🎂",
        ["gift"] = "🎁",
        ["computer"] = "💻",
        ["bug"] = "🐛",
        ["shipit"] = "🚢",
        ["moneybag"] = "💰",
        ["skull"] = "💀",
        ["poop"] = "💩",
    };

    /// <summary>
    /// Longest suffix first — "_medium_dark_skin_tone" ends with
    /// "_dark_skin_tone" as a plain substring, so checking the shorter form
    /// first would strip the wrong amount off names like
    /// "ok_hand_medium_dark_skin_tone".
    /// </summary>
    private static readonly (EmojiSkinTone Tone, string Suffix, string Modifier)[] SkinTones =
    [
        (EmojiSkinTone.MediumLight, "_medium_light_skin_tone", "\U0001F3FC"),
        (EmojiSkinTone.MediumDark, "_medium_dark_skin_tone", "\U0001F3FE"),
        (EmojiSkinTone.Medium, "_medium_skin_tone", "\U0001F3FD"),
        (EmojiSkinTone.Light, "_light_skin_tone", "\U0001F3FB"),
        (EmojiSkinTone.Dark, "_dark_skin_tone", "\U0001F3FF"),
    ];

    /// <summary>
    /// The emoji in this table that actually accept a skin-tone modifier —
    /// every one of them depicts a person or a body part. Appending a
    /// modifier to anything else (🍕🏆) is not a valid sequence: it renders
    /// as the emoji followed by a stray colored square, and Mattermost has
    /// no such emoji name to store it under either.
    ///
    /// Listed by base name, so the aliases resolve too: "+1" and "thumbsup"
    /// are the same glyph and both belong here.
    /// </summary>
    private static readonly HashSet<string> SkinToneCapable =
    [
        "+1", "thumbsup", "-1", "thumbsdown",
        "clap", "raised_hands", "pray", "wave", "ok_hand", "muscle",
        "point_up", "point_right", "point_left",
        "raised_hand", "vulcan_salute", "metal", "call_me_hand",
        "crossed_fingers", "punch", "fist", "v", "writing_hand", "selfie",
        "running", "swimming",
    ];

    /// <summary>Whether appending a skin-tone modifier to this emoji produces a real emoji rather than a stray colored square.</summary>
    public static bool SupportsSkinTone(string shortcode) => SkinToneCapable.Contains(shortcode);

    /// <summary>
    /// The name to actually send for <paramref name="shortcode"/> at the
    /// chosen tone — unchanged for the default tone, and for any emoji that
    /// doesn't take one.
    /// </summary>
    public static string ApplyTone(string shortcode, EmojiSkinTone tone)
    {
        if (tone == EmojiSkinTone.Default || !SupportsSkinTone(shortcode))
        {
            return shortcode;
        }

        foreach (var (candidate, suffix, _) in SkinTones)
        {
            if (candidate == tone)
            {
                return shortcode + suffix;
            }
        }
        return shortcode;
    }

    /// <summary>
    /// The base name behind a possibly skin-toned one. Usage counts are kept
    /// per base emoji so that changing your tone preference doesn't split
    /// your own history in two and empty the "most used" row.
    /// </summary>
    public static string StripTone(string shortcode)
    {
        foreach (var (_, suffix, _) in SkinTones)
        {
            if (shortcode.EndsWith(suffix, StringComparison.Ordinal))
            {
                return shortcode[..^suffix.Length];
            }
        }
        return shortcode;
    }

    /// <summary>The raised hand in each tone — what the tone selector shows as its swatches.</summary>
    public static string ToneSwatch(EmojiSkinTone tone) => ToGlyph(ApplyTone("raised_hand", tone));

    public static string ToGlyph(string shortcode) =>
        TryResolve(shortcode, out var glyph) ? glyph : $":{shortcode}:";

    /// <summary>False for anything not in the standard set (skin-tone variants included) — almost always a server custom emoji, resolved separately via its image.</summary>
    public static bool IsKnown(string shortcode) => TryResolve(shortcode, out _);

    private static bool TryResolve(string shortcode, out string glyph)
    {
        if (Map.TryGetValue(shortcode, out glyph!))
        {
            return true;
        }

        foreach (var (_, suffix, modifier) in SkinTones)
        {
            if (shortcode.EndsWith(suffix, StringComparison.Ordinal)
                && Map.TryGetValue(shortcode[..^suffix.Length], out var baseGlyph))
            {
                glyph = Compose(baseGlyph, modifier);
                return true;
            }
        }

        glyph = "";
        return false;
    }

    /// <summary>
    /// A skin-tone modifier replaces the emoji presentation selector U+FE0F
    /// rather than following it: ☝️ is U+261D U+FE0F, and the toned form is
    /// U+261D U+1F3FB, not U+261D U+FE0F U+1F3FB. Leaving the selector in
    /// breaks the sequence, so it shows as the plain sign followed by a
    /// loose colored square. Only ☝️ ✌️ ✍️ in this table are affected, but
    /// they are exactly the kind of emoji people apply a tone to.
    /// </summary>
    private static string Compose(string baseGlyph, string modifier) =>
        (baseGlyph.EndsWith('\uFE0F') ? baseGlyph[..^1] : baseGlyph) + modifier;

    /// <summary>One (shortcode, glyph) pair per distinct glyph — some shortcodes are aliases
    /// of each other ("+1"/"thumbsup"), so the reaction picker shows each emoji only once.</summary>
    public static readonly IReadOnlyList<(string Shortcode, string Glyph)> PickerEntries =
        Map.GroupBy(kv => kv.Value).Select(g => (g.First().Key, g.Key)).ToList();
}
