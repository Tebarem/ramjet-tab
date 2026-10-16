namespace Tab.Core;

/// <summary>Reads the placeholder tokens of a template ("Online: %tab_online% (%tab_ping%ms)"). The engine renders the tokens; this reads which ones a text
/// uses, for the dump command and for the checks of the settings. The syntax is the engine's: <c>%id%</c>, <c>%id:arg1:arg2%</c>, <c>%%</c> for a percent
/// sign. A percent sign with no closing one is text.</summary>
public static class PlaceholderTemplate
{
    /// <summary>The ids of the tokens in order, repeats included. Arguments are not returned.</summary>
    public static List<string> Ids(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ids = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] != '%')
            {
                i++;
                continue;
            }
            if (i + 1 < text.Length && text[i + 1] == '%')
            {
                i += 2;
                continue;
            }
            int close = text.IndexOf('%', i + 1);
            if (close < 0) break;
            string token = text[(i + 1)..close];
            int colon = token.IndexOf(':');
            string id = colon < 0 ? token : token[..colon];
            if (id.Length > 0) ids.Add(id);
            i = close + 1;
        }
        return ids;
    }

    /// <summary>The arguments of one token ("%id:a:b%" gives ["a", "b"]; "%id%" gives none).</summary>
    /// <param name="token">The token without its percent signs.</param>
    public static string[] Arguments(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        int colon = token.IndexOf(':');
        return colon < 0 ? [] : token[(colon + 1)..].Split(':');
    }
}
