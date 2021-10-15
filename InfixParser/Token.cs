namespace InfixParser
{
    public class Token
    {
        public Token(string content, TokenType type)
        {
            Content = content;
            Type = type;
        }

        public TokenType Type { get; set; }
        public string Content { get; set; }

        public override string ToString()
        {
            return string.Format("[{0} Token: \"{1}\"]", Type, Content);
        }
    }

    public enum TokenType
    {
        None,
        Input,
        Operator,
        Brackets,
        FunctionCall
    }
}