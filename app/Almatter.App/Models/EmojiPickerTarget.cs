namespace Almatter.App.Models;

/// <summary>
/// What the emoji picker is currently being used for. It's one panel serving
/// both jobs — the grid, the search, the most-used row and the tone strip are
/// the same either way, and the only difference is what a click does.
/// </summary>
public enum EmojiPickerTarget
{
    /// <summary>Adds the emoji as a reaction to the message that opened the picker.</summary>
    Reaction,

    /// <summary>Inserts the emoji into the channel composer, at the caret.</summary>
    Composer,

    /// <summary>Inserts the emoji into the thread panel's reply box, at the caret.</summary>
    ThreadComposer,
}
