using System.Text;
using HeimdallBase;
using Lanbridge;

namespace ChatterNew;

public class ChatterUtil
{
    public Dictionary<string, IChatSession> Sessions = new();

    public Dictionary<string, NetworkedGptInstance> Instances = new();
    public readonly ChatterNew ChatterNew;

    public ChatterUtil(ChatterNew chatterNew)
    {
        ChatterNew = chatterNew;
    }

    public bool ChannelEnrolled(string source) => !Config.GetArray<string>("chatter.disabled").Contains(source);

    public NetworkedGptInstance GetInstanceForSource(string source)
    {
        Console.WriteLine($"0!!");
        NetworkedGptInstance? instance;
        // default to single instance
        if (Instances.ContainsKey(string.Empty))
        {
            instance = Instances[string.Empty];
            Console.WriteLine($"Model dead: {instance.ModelDead}");

            if (!instance.ModelDead)
            {
                Console.WriteLine($"1!!");
                return instance;
            }
            
            Console.WriteLine($"Model is dead");
            instance.Kill();
        }

        instance =
            new NetworkedGptInstance(Config.GetString("chatter.host"), Config.GetInt("chatter.port"));

        try
        {
            instance.Tokens = NetworkedGptInstance.GetTokens(instance);
            Console.WriteLine($"2!!");
            return Instances[string.Empty] = instance;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to create instance: {e}");
            Instances.Remove(string.Empty);
            Console.WriteLine($"3!!");
            throw;
        }
    }

    private IChatSession CreateChatSession(string source)
    {
        NetworkedGptInstance instance = GetInstanceForSource(source);
        var savePath = GetSavePathForSource(source);
        ChatSession? session = null;

        if (File.Exists(savePath))
        {
            try
            {
                session = ChatSession.FromFile(savePath);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                session = null;
            }
        }

        if (session is null)
        {
            session = new ChatSession(source);
        }

        session.UseGptInstance(instance);
        return session;
    }

    public string GetSavePathForSource(string source)
    {
        return $"source_states/{Convert.ToBase64String(Encoding.UTF8.GetBytes(source))}.json";
    }
    
    public IChatSession? GetChatSession(string source, string? nick = null, string? args = null)
    {
        if (Sessions.ContainsKey(source))
        {
            IChatSession? session = Sessions[source];

            if (session.Context.ModelDead)
            {
                try
                {
                    Console.WriteLine($"Using new instance");
                    NetworkedGptInstance instance = GetInstanceForSource(source);
                    session.UseGptInstance(instance);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }
            return Sessions[source];
        }

        if (ChannelEnrolled(source))
        {
            return Sessions[source] = CreateChatSession(source);
        }

        return null;
    }

    public void SaveSession(string source)
    {
        GetChatSession(source)?.Save(GetSavePathForSource(source));
    }

    public int CountTokens(string source, string args)
    {
        // TODO: caching? dedicated counting message?
        return GetChatSession(source).Context.Tokenize(args).Count;
    }

    public bool ShouldRecordLine(string args, string source, string n)
    {
        // TODO: Check for spam
        if (args.StartsWith("!!"))
        {
            return false;
        }
        
        foreach ((var commandKey, _) in ChatterNew.Commands)
        {
            if (commandKey.StartsWith('.') && args.StartsWith(commandKey))
                return false;
        }

        return true;
    }
}