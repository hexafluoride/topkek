using ChatterNew;
using Lanbridge;

public interface IChatSession
{
    ConfigBlock Config { get; set; }
    GenerationContext Context { get; }

    DateTime LastNotified { get; set; }
    int HistoryLength { get; }
    IList<IChatLine> History { get; }

    void WipeHistory();
    void AddHistoryLine(IChatLine line, bool decodeImmediately);
    IChatLine? SimulateChatFromPerson(string person);
    void RollbackHistory(int n);
    IChatLine? ProcessChatLine(string args, string source, string nick, string ownNick);
    void UseGptInstance(NetworkedGptInstance instance);
    void UpdateConfig();
    int[]? GetPromptTokens();
    string GetPromptTemplate();
    void Save(string filename);
}