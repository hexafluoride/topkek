namespace ChatterNew;

public class ConfigBlock
{
    public int TrimStart { get; set; } = 1500;
    public int TrimTarget { get; set; }  = 400;
    public double Temperature { get; set; } = 0.4;
    public double RepetitionPenalty { get; set; } = 1.25;
    public string AssignedNick { get; set; } = "SmartGenius";
    public int NickTokens { get; set; } = 4;
    public bool PrintTimings { get; set; }
    public bool NotifyCompaction { get; set; }
    public int PonderancesPerReply { get; set; } = 0;
    public int RepetitionPenaltySustain { get; set; } = 256;
    public int RepetitionPenaltyDecay { get; set; } = 128;
    public bool AllowPromptOverride { get; set; } = false;
    public string? Prompt { get; set; } = null;
    public string? PromptAuthor { get; set; } = null;
    public int NoRepeatLines { get; set; } = 5;
}