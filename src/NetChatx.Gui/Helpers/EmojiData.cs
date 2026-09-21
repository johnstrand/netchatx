using System;
using System.Collections.Generic;
using System.Linq;

namespace NetChatx.Gui.Helpers;

public sealed class EmojiCategory
{
    public string Name { get; }
    public string Icon { get; }
    public IReadOnlyList<string> Emojis { get; }

    public EmojiCategory(string name, string icon, IReadOnlyList<string> emojis)
    {
        Name = name;
        Icon = icon;
        Emojis = emojis;
    }
}

public static class EmojiData
{
    public static readonly IReadOnlyList<string> DefaultQuickEmojis = ["👍", "❤️", "😂", "😮", "😢", "🎉"];

    public static readonly IReadOnlyList<EmojiCategory> Categories =
    [
        new EmojiCategory("Smileys", "😀", [
            "😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣", "🥲", "🥹", "😊", "😇", "🙂", "🙃", "😉", "😌",
            "😍", "🥰", "😘", "😗", "😙", "😚", "😋", "😛", "😝", "😜", "🤪", "🤨", "🧐", "🤓", "😎", "🥸",
            "🤩", "🥳", "😏", "😒", "😞", "😔", "😟", "😕", "🙁", "☹️", "😣", "😖", "😫", "😩", "🥺", "😢",
            "😭", "😮‍💨", "😤", "😠", "😡", "🤬", "🤯", "😳", "🥵", "🥶", "😱", "😨", "😰", "😥", "😓", "🫣",
            "🤗", "🫡", "🤔", "🫢", "🤭", "🤫", "🤥", "😶", "😐", "😑", "😬", "🫠", "🙄", "😯", "😦", "😧",
            "😮", "😲", "🥱", "😴", "🤤", "😪", "😵", "😵‍💫", "🤐", "🥴", "🤢", "🤮", "🤧", "😷", "🤒", "🤕",
            "🤑", "🤠", "😈", "👿", "👹", "👺", "🤡", "💩", "👻", "💀", "☠️", "👽", "👾", "🤖", "🎃"
        ]),

        new EmojiCategory("People", "👋", [
            "👋", "🤚", "🖐️", "✋", "🖖", "👌", "🤌", "🤏", "✌️", "🤞", "🫰", "🤟", "🤘", "🤙", "👈", "👉",
            "👆", "🖕", "👇", "☝️", "🫵", "👍", "👎", "✊", "👊", "🤛", "🤜", "👏", "🙌", "🫶", "👐", "🤲",
            "🤝", "🙏", "✍️", "💅", "🤳", "💪", "🦾", "🦿", "🦵", "🦶", "👂", "🦻", "👃", "🧠", "🫀", "🫁",
            "🦷", "🦴", "👀", "👁️", "👅", "👄", "👶", "👧", "🧒", "👦", "👩", "🧑", "👨", "👵", "🧓", "👴"
        ]),

        new EmojiCategory("Symbols", "❤️", [
            "❤️", "🧡", "💛", "💚", "💙", "💜", "🖤", "🤍", "🤎", "💔", "❤️‍🔥", "❤️‍🩹", "❣️", "💕", "💞", "💓",
            "💗", "💖", "💘", "💝", "💟", "☮️", "✝️", "☪️", "🕉️", "☸️", "✡️", "🔯", "🕎", "☯️", "☦️", "🛐",
            "⛎", "♈", "♉", "⭐", "🌟", "✨", "💥", "💯", "💢", "💬", "🗯️", "💭", "💤", "♨️", "🛑", "⛔",
            "⭕", "❌", "❓", "❗", "🔥", "🌈", "☀️", "🌙", "⚡", "❄️", "🔔", "💡", "🔑", "🔒", "🎉", "🏆"
        ]),

        new EmojiCategory("Nature", "🐶", [
            "🐶", "🐱", "🐭", "🐹", "🐰", "🦊", "🐻", "🐼", "🐻‍❄️", "🐨", "🐯", "🦁", "🐮", "🐷", "🐸", "🐵",
            "🙈", "🙉", "🙊", "🐒", "🐔", "🐧", "🐦", "🐤", "🐣", "🐥", "🦆", "🦅", "🦉", "🦇", "🐺", "🐗",
            "🐴", "🦄", "🐝", "🪱", "🐛", "🦋", "🐌", "🐞", "🐜", "🪰", "🪲", "🪳", "🪴", "🌲", "🌳", "🌴",
            "🌵", "🌾", "🌿", "☘️", "🍀", "🍁", "🍂", "🍃", "🍄", "🌰", "🦀", "🦞", "🦐", "🦑", "🐙", "🐬"
        ]),

        new EmojiCategory("Food", "🍔", [
            "🍏", "🍎", "🍐", "🍊", "🍋", "🍌", "🍉", "🍇", "🍓", "🫐", "🍈", "🍒", "🍑", "🥭", "🍍", "🥥",
            "🥝", "🍅", "🍆", "🥑", "🥦", "🥬", "🥒", "🌶️", "🫑", "🌽", "🥕", "🫒", "🧄", "🧅", "🥔", "🍠",
            "🥐", "🥯", "🍞", "🥖", "🥨", "🧀", "🥚", "🍳", "🧈", "🥞", "🧇", "🥓", "🥩", "🍗", "🍖", "🌭",
            "🍔", "🍟", "🍕", "🥪", "🥙", "🌮", "🌯", "🫔", "🥗", "🥘", "🫕", "🥫", "🍝", "🍜", "🍲", "🍛",
            "🍣", "🍱", "🥟", "🦪", "🍤", "🍙", "🍚", "🍰", "🎂", "🧁", "🍫", "🍩", "🍪", "☕", "🍺", "🍷"
        ]),

        new EmojiCategory("Activities", "⚽", [
            "⚽", "🏀", "🏈", "⚾", "🥎", "🎾", "🏐", "🏉", "🥏", "🎱", "🪀", "🏓", "🏸", "🏒", "🏑", "🥍",
            "🏏", "🪃", "🥅", "⛳", "🪁", "🏹", "🎣", "🤿", "🥊", "🥋", "🎽", "🛹", "🛼", "🛷", "⛸️", "🥌",
            "🎿", "⛷️", "🏂", "🪂", "🏋️", "🤸", "🚴", "🥇", "🥈", "🥉", "🎫", "🎭", "🎨", "🎬", "🎤", "🎧",
            "🎼", "🎹", "🥁", "🎷", "🎺", "🎸", "🎮", "🚗", "🚕", "✈️", "🚀", "🛸", "⛵", "⏰", "📱", "💻"
        ])
    ];

    private static readonly Dictionary<string, string[]> KeywordMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["👍"] = ["thumbs up", "thumbsup", "like", "approve", "ok", "yes", "good"],
        ["👎"] = ["thumbs down", "thumbsdown", "dislike", "bad", "no"],
        ["❤️"] = ["heart", "love", "red heart", "like"],
        ["😂"] = ["joy", "laugh", "laughing", "tears", "crying with laughter", "lol", "haha", "rofl"],
        ["🤣"] = ["rofl", "rolling on the floor", "laugh", "lol", "haha"],
        ["😮"] = ["open mouth", "surprised", "wow", "gasp", "omg", "shocked"],
        ["😢"] = ["cry", "crying", "tear", "sad", "unhappy"],
        ["😭"] = ["sob", "crying", "sad", "loudly crying", "tears"],
        ["🎉"] = ["tada", "party", "celebration", "confetti", "congrats", "hooray"],
        ["🔥"] = ["fire", "flame", "hot", "lit"],
        ["👏"] = ["clap", "applause", "bravo", "hands"],
        ["🙏"] = ["pray", "pleased", "thank you", "thanks", "hope"],
        ["💯"] = ["100", "hundred", "perfect", "score"],
        ["🚀"] = ["rocket", "blastoff", "launch", "fast", "moon"],
        ["✨"] = ["sparkles", "stars", "shine", "magic", "clean"],
        ["⭐"] = ["star", "favorite"],
        ["😍"] = ["heart eyes", "love", "adore", "infatuated"],
        ["🥰"] = ["smiling face with hearts", "love", "affection", "cute"],
        ["😘"] = ["kiss", "blowing kiss", "love"],
        ["🤔"] = ["thinking", "hmm", "ponder", "wonder"],
        ["🤫"] = ["shh", "quiet", "secret"],
        ["😎"] = ["cool", "sunglasses", "chill"],
        ["🥳"] = ["party", "celebrating", "birthday", "hat"],
        ["🤩"] = ["star-struck", "excited", "wow"],
        ["😴"] = ["sleeping", "zzz", "tired"],
        ["🤮"] = ["vomit", "puke", "sick", "disgust"],
        ["💩"] = ["poop", "hankey", "crap"],
        ["💀"] = ["skull", "dead", "died", "skeleton"],
        ["👀"] = ["eyes", "look", "peek", "see"]
    };

    public static IReadOnlyList<string> SearchEmojis(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Categories.SelectMany(c => c.Emojis).ToList();
        }

        var trimmed = query.Trim();
        var results = new List<string>();

        // Exact emoji match
        foreach (var cat in Categories)
        {
            foreach (var emoji in cat.Emojis)
            {
                if (emoji == trimmed)
                {
                    results.Add(emoji);
                }
            }
        }

        // Search in keywords
        foreach (var (emoji, keywords) in KeywordMap)
        {
            if (keywords.Any(k => k.Contains(trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                if (!results.Contains(emoji))
                {
                    results.Add(emoji);
                }
            }
        }

        // Search category name match
        foreach (var cat in Categories)
        {
            if (cat.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var emoji in cat.Emojis)
                {
                    if (!results.Contains(emoji))
                    {
                        results.Add(emoji);
                    }
                }
            }
        }

        return results;
    }
}
