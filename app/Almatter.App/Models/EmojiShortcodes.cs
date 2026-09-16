using System;
using System.Collections.Generic;
using System.Linq;

namespace Almatter.App.Models;

/// <summary>
/// Mattermost carries an emoji as a *shortcode* — ":+1:", ":smirk_cat:" —
/// both in a reaction and in the body of a message, never as the character
/// itself. This resolves the whole standard set: every name the server
/// knows, generated into <see cref="EmojiShortcodesData"/> from the same
/// reference list Mattermost itself is built on, plus skin-tone variants of
/// anything that takes one.
///
/// A name still unmapped after that is taken to be a server custom emoji and
/// resolved separately via its image; if that fails too, the shortcode stays
/// on screen as typed. That last fallback used to catch far more than it
/// should: the table here was a hand-written few hundred names, so a
/// standard emoji outside it was hunted for among the server's custom ones,
/// never found, and left showing as ":smirk_cat:" mid-sentence.
/// </summary>
public static class EmojiShortcodes
{
    /// <summary>
    /// Names this app has always answered to that the reference list doesn't
    /// carry — mostly GitHub's spellings, which people do type. They fill
    /// gaps only: where the standard set defines a name, it wins, so a
    /// message reads the same here as in the official client.
    /// </summary>
    private const string LegacyAliases = """
        rofl 🤣
        thinking 🤔
        rolling_eyes 🙄
        star_struck 🤩
        robot 🤖
        vulcan_salute 🖖
        metal 🤘
        lion 🦁
        unicorn 🦄
        cheese 🧀
        swimming 🏊
        medal_sports 🏅
        green_circle 🟢
        blue_circle 🔵
        yellow_circle 🟡
        trademark ™️
        shipit 🚢
        """;

    /// <summary>
    /// What the picker shows with an empty search box: the everyday set,
    /// grouped by hand (faces, then gestures, then animals…) rather than
    /// strewn through Unicode order.
    ///
    /// The full set is deliberately not all drawn at once. The grid builds a
    /// real button per emoji with no virtualisation, so putting nineteen
    /// hundred of them up front would cost six times the controls — and the
    /// lag of building them — for a wall nobody scrolls to the end of.
    /// Everything left out is still one search away: see
    /// <see cref="PickerEntries"/>.
    /// </summary>
    private const string CommonEmoji = """
        +1
        -1
        smile
        smiley
        grinning
        laughing
        joy
        rofl
        slightly_smiling_face
        wink
        blush
        heart_eyes
        heart
        hearts
        thinking_face
        neutral_face
        confused
        cry
        sob
        scream
        astonished
        flushed
        sweat_smile
        relaxed
        smirk
        unamused
        disappointed
        worried
        frowning
        persevere
        triumph
        angry
        rage
        innocent
        sunglasses
        nerd_face
        hushed
        open_mouth
        grimacing
        rolling_eyes
        expressionless
        no_mouth
        star_struck
        partying_face
        exploding_head
        clown_face
        ghost
        alien
        robot
        jack_o_lantern
        see_no_evil
        hear_no_evil
        speak_no_evil
        kiss
        kissing_heart
        yum
        stuck_out_tongue
        sleepy
        sleeping
        dizzy_face
        mask
        nauseated_face
        lying_face
        cold_sweat
        fearful
        yawning_face
        sweat
        raised_hand
        vulcan_salute
        metal
        call_me_hand
        crossed_fingers
        punch
        fist
        v
        writing_hand
        selfie
        dog
        cat
        mouse
        rabbit
        bear
        panda_face
        koala
        tiger
        lion
        cow
        pig
        frog
        monkey_face
        chicken
        penguin
        bird
        duck
        eagle
        owl
        bat
        wolf
        horse
        unicorn
        bee
        butterfly
        snail
        snake
        dragon
        turtle
        tropical_fish
        fish
        dolphin
        whale
        octopus
        crab
        camel
        elephant
        giraffe_face
        hedgehog
        sloth
        deer
        llama
        four_leaf_clover
        cherry_blossom
        tulip
        rose
        sunflower
        maple_leaf
        evergreen_tree
        palm_tree
        cactus
        seedling
        rainbow
        cloud
        umbrella
        snowflake
        snowman
        droplet
        ocean
        apple
        tangerine
        lemon
        banana
        watermelon
        grapes
        strawberry
        cherries
        peach
        pineapple
        avocado
        tomato
        eggplant
        corn
        carrot
        bread
        croissant
        cheese
        egg
        bacon
        pancakes
        fries
        hamburger
        hotdog
        taco
        burrito
        popcorn
        spaghetti
        ramen
        sushi
        curry
        ice_cream
        icecream
        doughnut
        cookie
        chocolate_bar
        candy
        lollipop
        tea
        sake
        wine_glass
        cocktail
        tropical_drink
        champagne
        soccer
        basketball
        football
        baseball
        tennis
        volleyball
        golf
        running
        swimming
        trophy
        medal_sports
        dart
        video_game
        game_die
        guitar
        microphone
        headphones
        musical_note
        notes
        art
        movie_camera
        camera
        tv
        telephone
        iphone
        keyboard
        battery
        calendar
        alarm_clock
        hourglass
        watch
        gear
        wrench
        hammer
        bomb
        pill
        syringe
        key
        lock
        unlock
        bell
        mega
        envelope
        package
        memo
        book
        books
        bookmark
        paperclip
        link
        dollar
        credit_card
        gem
        crown
        ring
        briefcase
        airplane
        car
        taxi
        bus
        train
        ship
        anchor
        house
        office
        hospital
        school
        bank
        hotel
        mountain
        city_sunset
        night_with_stars
        milky_way
        infinity
        recycle
        red_circle
        green_circle
        blue_circle
        yellow_circle
        arrow_up
        arrow_down
        arrow_left
        arrow_right
        top
        new
        free
        sos
        ok
        cool
        copyright
        registered
        trademark
        information_source
        tada
        confetti_ball
        fire
        100
        rocket
        eyes
        clap
        raised_hands
        pray
        wave
        ok_hand
        muscle
        point_up
        point_right
        point_left
        white_check_mark
        heavy_check_mark
        x
        warning
        question
        exclamation
        bulb
        star
        sparkles
        zap
        coffee
        pizza
        beers
        cake
        gift
        computer
        bug
        moneybag
        skull
        poop
        """;

    private static readonly Dictionary<string, string> Map = BuildMap();

    private static Dictionary<string, string> BuildMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, glyph) in ParsePairs(EmojiShortcodesData.Table))
        {
            map[name] = glyph;
        }
        foreach (var (name, glyph) in ParsePairs(LegacyAliases))
        {
            map.TryAdd(name, glyph);
        }
        return map;
    }

    /// <summary>Reads a "shortcode glyph" block. No shortcode contains a space, so the first one separates the two.</summary>
    private static IEnumerable<(string Name, string Glyph)> ParsePairs(string block)
    {
        foreach (var line in ParseNames(block))
        {
            var space = line.IndexOf(' ');
            yield return (line[..space], line[(space + 1)..]);
        }
    }

    /// <summary>Reads a one-name-per-line block, skipping blank lines and the carriage returns a CRLF source file leaves behind.</summary>
    private static IEnumerable<string> ParseNames(string block) =>
        block.Split('\n').Select(line => line.Trim()).Where(line => line.Length != 0);

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
    /// The emoji that accept a skin-tone modifier — every one of them depicts
    /// a person or a body part. Appending a modifier to anything else (🍕🏆)
    /// is not a valid sequence: it draws as the emoji followed by a stray
    /// coloured square, and Mattermost has no such name to store it under
    /// either.
    /// </summary>
    private static readonly HashSet<string> SkinToneCapable = BuildSkinToneCapable();

    private static HashSet<string> BuildSkinToneCapable()
    {
        var set = new HashSet<string>(ParseNames(EmojiShortcodesData.ToneCapable), StringComparer.Ordinal);
        // The three legacy aliases above that do depict a hand or a person.
        set.UnionWith(["vulcan_salute", "metal", "swimming"]);
        return set;
    }

    /// <summary>Whether appending a skin-tone modifier to this emoji produces a real emoji rather than a stray coloured square.</summary>
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
            if (!shortcode.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            // Only where the emoji actually takes a tone. Composing one onto
            // anything else invents a glyph nobody sent: ":pizza_light_skin_tone:"
            // would come out as 🍕 trailed by a loose coloured square. There
            // is no such emoji on the server either, so the honest answer is
            // that this name is unknown, and it stays on screen as typed.
            var baseName = shortcode[..^suffix.Length];
            if (SupportsSkinTone(baseName) && Map.TryGetValue(baseName, out var baseGlyph))
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
    /// loose coloured square.
    /// </summary>
    private static string Compose(string baseGlyph, string modifier) =>
        (baseGlyph.EndsWith('️') ? baseGlyph[..^1] : baseGlyph) + modifier;

    /// <summary>One swatch in the emoji picker. <c>Common</c> marks the everyday set — what the grid shows before anything is typed in the search box.</summary>
    public readonly record struct PickerEntry(string Shortcode, string Glyph, bool Common);

    /// <summary>
    /// One entry per distinct glyph — aliases ("+1" and "thumbsup") collapse
    /// into a single swatch rather than sitting side by side twice. The
    /// everyday set leads, in its hand-grouped order; the rest of the
    /// standard set follows in Unicode order, and turns up on searching.
    /// </summary>
    public static readonly IReadOnlyList<PickerEntry> PickerEntries = BuildPickerEntries();

    private static List<PickerEntry> BuildPickerEntries()
    {
        var entries = new List<PickerEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in ParseNames(CommonEmoji))
        {
            if (Map.TryGetValue(name, out var glyph) && seen.Add(glyph))
            {
                entries.Add(new PickerEntry(name, glyph, Common: true));
            }
        }

        foreach (var (name, glyph) in ParsePairs(EmojiShortcodesData.Table))
        {
            if (seen.Add(glyph))
            {
                entries.Add(new PickerEntry(name, glyph, Common: false));
            }
        }

        return entries;
    }
}
