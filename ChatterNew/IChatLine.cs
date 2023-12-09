namespace ChatterNew;

public interface IChatLine
{
    string Nick { get; }
    string Contents { get; }
    List<int> Tokens { get; }
    string Origin { get; set; }
}

public record ChatLine : IChatLine
{
    public string Nick { get; } = "";
    public string Contents { get; } = "";
    public List<int> Tokens { get; } = new();
    public string Origin { get; set; } = "";

    public ChatLine(string nick, string contents, List<int> tokens)
    {
        Nick = nick;
        Contents = contents;
        Tokens = tokens;
    }

    public void SetTokens(List<int> tokens)
    {
        Tokens.Clear();
        Tokens.AddRange(tokens);
    }
    
    public override string ToString()
    {
        return $"<{Nick}> {Contents}";
    }

    public static ChatLine? Parse(string line, List<int> tokens)
    {
        if (!line.StartsWith('<'))
        {
            return null;
        }

        int rightBracketIndex = line.IndexOf('>');
        if (rightBracketIndex < 0)
        {
            return null;
        }

        string nick = line.Substring(1, rightBracketIndex - 1);
        string contents = line.Substring(rightBracketIndex + 1);

        if (!contents.StartsWith(' '))
        {
            return null;
        }
        
        contents = contents.Substring(1);
        return new ChatLine(nick, contents, tokens);
    }
}