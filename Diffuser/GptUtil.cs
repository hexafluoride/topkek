using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using HeimdallBase;
using Newtonsoft.Json;

namespace Diffuser;

public class GptInstance
{
    public string Model { get; set; }
    public readonly Process Process;

    private readonly StreamReader Reader;
    private readonly StreamWriter Writer;

    public GptInstance(Process process)
    {
        Process = process ?? throw new ArgumentNullException(nameof(process));

        Reader = Process.StandardOutput;
        Writer = Process.StandardInput;
    }

    public string GetNextOverlappingOutput()
    {
        lock (Process)
        {
            var requestObj = new
            {
                type = "generate_overlapping"
            };

            Writer.WriteLine(JsonConvert.SerializeObject(requestObj));
            var response = JsonDocument.Parse(Reader.ReadLine());
            var result = response.RootElement.GetProperty("result").GetString();
            return result;
        }
    }

    public string GetOutputForPrompt(string prompt, int numTokens = 64)
    {
        lock (Process)
        {
            var requestObj = new
            {
                type = "generate_once",
                prompt,
                temperature = 0.9,
                repetition_penalty = 1.075,
                force_funny = true,
                num_tokens = numTokens
            };

            Writer.WriteLine(JsonConvert.SerializeObject(requestObj));
            var respText = Reader.ReadLine();
            Console.WriteLine(respText);
            var response = JsonDocument.Parse(respText);
            var result = response.RootElement.GetProperty("result").GetProperty("response_decoded").GetString();
            Console.WriteLine($"Raw output:");
            Console.WriteLine(result);
            //result = result.Substring(prompt.Length);
            return result;
        }
    }
}

public class ChatLine
{
    public DateTime Time { get; set; }
    public string Nick { get; set; }
    public string Source { get; set; }
    public string Message { get; set; }
}

public class GptUtil
{
    private readonly Diffuser Diffuser;
    
    public GptUtil(Diffuser diffuser)
    {
        Diffuser = diffuser ?? throw new ArgumentNullException(nameof(diffuser));
    }

    private Dictionary<string, GptInstance> GptProcesses = new();

    private Dictionary<string, List<ChatLine>> Lines = new();

    public Dictionary<string, ChatLine> CandidateLines = new();

    public void RecordLine(string args, string source, string nick)
    {
        if (args.StartsWith(".chat") || args.StartsWith(".diffuse"))
            return;

        if (string.IsNullOrWhiteSpace(GetModel(source)))
            return;
        
        if (!Lines.ContainsKey(source))
            Lines[source] = new List<ChatLine>();

        if (CandidateLines.ContainsKey(source))
        {
            var candidateLine = CandidateLines[source];
            var age = DateTime.UtcNow - candidateLine.Time;

            if (age.TotalSeconds < 20)
            {
                Lines[source].Add(candidateLine);
            }

            CandidateLines.Remove(source);
        }

        var line = new ChatLine() { Message = args, Source = source, Nick = nick, Time = DateTime.UtcNow};
        Lines[source].Add(line);
        
        while (Lines[source].Count > 10)
            Lines[source].RemoveAt(0);

        if (source == "ezbake/#ezbake" && Random.Shared.NextDouble() < (1d / 50d) || args.Contains("diffuser"))
        {
            Diffuser.ChatOneShot(args, source, nick);
        }
    }

    public string GetPromptResponseForSource(string source, ChatLine? tempAddLine = null, bool forceExtra = false)
    {
        var model = GetModel(source);
        if (string.IsNullOrWhiteSpace(model))
            throw new Exception("No model found for source");
        
        StartInstanceIfNotStarted(source);
        var instance = GptProcesses[model];

        var lines = new List<ChatLine>(Lines[source]);

        if (tempAddLine is not null)
        {
            lines.RemoveAt(0);
            lines.Add(tempAddLine);
        }
        
        var linesComposed = string.Join('\n', lines.Select(l => $"<{l.Nick}> {l.Message}")) + "\n";

        var extra = forceExtra || lines.Last().Message.StartsWith("!tldr") || lines.Last().Message.StartsWith(".wiki") ||  lines.Last().Message.StartsWith(".ud");
        var result = instance.GetOutputForPrompt(linesComposed, extra ? 150 : 64);
        return result;
    }

    private void StartInstanceIfNotStarted(string source, string args = null)
    {
        lock (GptProcesses)
        {
            args = args ?? "0.95 1.05";
            var model = GetModel(source);

            if (string.IsNullOrWhiteSpace(model))
                throw new Exception("No model found for source");

            if (GptProcesses.ContainsKey(model) && !GptProcesses[model].Process.HasExited)
                return;

            var binary = Config.GetString("gpt.binary");

            if (!File.Exists(binary))
                throw new Exception();

            bool inRealTime = false;

            Dictionary<string, string> annoyingNickMappings = new()
            {
                {"meleeman", "very_good_programmer"},
                {"meleeman|m", "very_good_programmer"},
                {"hatInTheCat", "very_good_programmer"},
                {"hatInTheCat_", "very_good_programmer"}
            };

            Dictionary<string, string> replacements = new()
            {
            };

            foreach (var kv in annoyingNickMappings)
                replacements[kv.Key] = kv.Value;

            var psi = new ProcessStartInfo(binary, $"{model} {args} sync");
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardInput = true;
            //psi.

            if (GptProcesses.ContainsKey(model) && !GptProcesses[model].Process.HasExited)
            {
                GptProcesses[model].Process.Kill(true);
            }

            try
            {
                var GptProcess = Process.Start(psi) ?? throw new Exception();
                var instance = new GptInstance(GptProcess);

                var line = GptProcess.StandardOutput.ReadLine();

                while (line != "ready")
                {
                    Console.WriteLine(line);
                    line = GptProcess.StandardOutput.ReadLine();
                }

                Console.WriteLine($"Model marked ready");

                GptProcesses[model] = instance;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }

    public void StartGpt(string source, string args)
    {
        void RealFunc()
        {
            
            var model = Config.GetString($"gpt.target.{source}");

            if (string.IsNullOrWhiteSpace(model))
            {
                Diffuser.SendMessage($"No model configured for {source}.", source);
                return;
            }
            
            try
            {
                bool inRealTime = false;
                
                if (args.Contains("sync"))
                {
                    inRealTime = true;
                    // args = args.Replace("sync", "");
                }

                Dictionary<string, string> annoyingNickMappings = new()
                {
                    {"meleeman", "very_good_programmer"},
                    {"meleeman|m", "very_good_programmer"},
                    {"hatInTheCat", "very_good_programmer"},
                    {"hatInTheCat_", "very_good_programmer"}
                };

                Dictionary<string, string> replacements = new()
                {
                };

                foreach (var kv in annoyingNickMappings)
                    replacements[kv.Key] = kv.Value;

                string ProcessLineWithNickColors(string line)
                {
                    try
                    {
                        line = line.Trim();

                        var firstBracket = line.IndexOf('<');
                        var lastBracket = line.IndexOf('>');

                        if (firstBracket == -1 || lastBracket == -1 || lastBracket <= firstBracket)
                        {
                            return line;
                        }
                        
                        if (line.Length > lastBracket)
                        {
                            if (line[lastBracket + 1] == '.' || line[lastBracket + 1] == '!')
                            {
                                line = line.Insert(lastBracket + 1, " ");
                            }
                        }

                        var lineContents = line.Substring(lastBracket + 1);
                        foreach (var kv in replacements)
                        {
                            lineContents = lineContents.Replace(kv.Key, kv.Value);
                        }
                        
                        
                        // var lineContentWords = lineContents.Split(' ');
                        //
                        // for (int i = 0; i < lineContentWords.Length; i++)
                        // {
                        //     var wordMap = lineContentWords[i].ToLowerInvariant();
                        //     if (replacements.ContainsKey(wordMap))
                        //     {
                        //         lineContentWords[i] = replacements[wordMap];
                        //     }
                        // }
                        //
                        // lineContents = string.Join(' ', lineContentWords);
                        line = line.Substring(0, lastBracket + 1) + lineContents;

                        var nick = line.Substring(firstBracket + 1, (lastBracket - firstBracket) - 1);

                        if (annoyingNickMappings.ContainsKey(nick))
                            nick = annoyingNickMappings[nick];
                        
                        var nickColor = (Math.Abs(nick.GetHashCode()) % 11) + 2;

                        var escapedNick = string.Join('​', nick.ToCharArray());
                        
                        if (nick.Length > 1)
                            replacements[nick.ToLowerInvariant()] = escapedNick;

                        line = line.Remove(firstBracket + 1, (lastBracket - firstBracket) - 1);
                        line = line.Insert(firstBracket + 1, $"\x03{nickColor.ToString().PadLeft(2, '0')}{escapedNick}\x03");

                        return line;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        return line;
                    }
                }

                //var outReader = GptProcess.StandardOutput;
                //outReader.BaseStream.
                
                StartInstanceIfNotStarted(source, args);
                var instance = GptProcesses[GetModel(source)];

                int lastTime = 0;
                int cumulativeTimeCounter = 0;
                int lastCumulativeTime = 0;
                
                if (inRealTime)
                {
                    List<string> bufferedLines = new();
                    string lastLinePartial = "";
                    var eogMark = "<|END-OF-GENERATION|>";
                    var lastLineTimeStr = "";
                    bool lastLineInstant = false;

                    void ConsumeLine()
                    {
                        if (bufferedLines.Count > 500)
                            return;

                        //var line = outReader.ReadLine() ?? throw new Exception("Read null line");
                        var lines = instance.GetNextOverlappingOutput().Split('\n');

                        for (int i = 0; i < lines.Length; i++)
                        {
                            var line = lines[i];
                            if (!string.IsNullOrWhiteSpace(lastLinePartial))
                            {
                                line = lastLinePartial + line;
                                lastLinePartial = "";

                                if (line.Contains('\n'))
                                {
                                    var newlinePos = line.IndexOf('\n');
                                    lastLinePartial = line.Substring(newlinePos + 1);
                                    line = line.Substring(0, newlinePos);
                                }
                            }

                            if (i == lines.Length - 1)
                            {
                                lastLinePartial += line;
                                //GptProcess.StandardInput.Write('a');
                                return;
                            }

                            int maxLine = 2048;

                            if (line.Length > maxLine)
                            {
                                lastLinePartial += line.Substring(maxLine);
                                line = line.Substring(0, maxLine);
                            }

                            bufferedLines.Add(line);
                            Console.WriteLine($"Added line: {line}");
                        }
                    }

                    while (!instance.Process.HasExited)
                    {
                        if ((lastLineInstant && bufferedLines.Count > 10) || bufferedLines.Count > 50)
                        {
                            var nextReadyLine = bufferedLines[0];
                            bufferedLines.RemoveAt(0);
                            
                            //Console.WriteLine($"nextReadyLine: {nextReadyLine}");

                            try
                            {                   
                                var lineTimeStr = nextReadyLine.Split(' ')[0].Trim('[', ']');
                                    var lineTime = lineTimeStr.Split(':');
                                var lineTimeSeconds = int.Parse(lineTime[0]) * 3600 + int.Parse(lineTime[1]) * 60 +
                                                      int.Parse(lineTime[2]);

                                nextReadyLine = nextReadyLine.Substring(lineTimeStr.Length + 2).Trim();
                                
                                if (lineTimeSeconds < lastTime)
                                {
                                    cumulativeTimeCounter += 24 * 60 * 60;
                                }

                                
                                lastTime = lineTimeSeconds;
                                lineTimeSeconds += cumulativeTimeCounter;

                                int waitTime = lastCumulativeTime == 0 ? 0 : (lineTimeSeconds - lastCumulativeTime);
                                waitTime = Math.Min(10, waitTime);
                                waitTime = Math.Max(0, waitTime);
                                Console.WriteLine($"Waiting {waitTime}s from {lastCumulativeTime} ({lastLineTimeStr}) to {lineTimeSeconds} ({lineTimeStr}) for line {nextReadyLine}...");
                                lastCumulativeTime = lineTimeSeconds;
                                lastLineTimeStr = lineTimeStr;

                                nextReadyLine = ProcessLineWithNickColors(nextReadyLine);

                                lastLineInstant = waitTime == 0;

                                if (waitTime == 0)
                                {
                                    Thread.Sleep(20);
                                }

                                // if (waitTime > 5)
                                // {
                                //     var sw = Stopwatch.StartNew();
                                //     ConsumeLine();
                                //     waitTime -= (int)(sw.ElapsedMilliseconds / 1000);
                                //
                                //     if (waitTime < 0)
                                //     {
                                //         waitTime = 0;
                                //         if (sw.ElapsedMilliseconds < 0)
                                //             Thread.Sleep(20);
                                //     }
                                //
                                // }
                                
                                for (int i = 0; i < waitTime; i++)
                                {
                                    Thread.Sleep(1000);
                                    
                                    if (i > 5)
                                        ConsumeLine();

                                    if (instance.Process.HasExited)
                                    {
                                        Diffuser.SendMessage("Process exited", source);
                                        return;
                                    }
                                }

                                Diffuser.SendMessage(nextReadyLine, source);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                            }
                            continue;
                        }
                        
                        ConsumeLine();
                    }
                }
                else
                {
                    var leftover = "";
                    while (!instance.Process.HasExited)
                    {
                        var lines = instance.GetNextOverlappingOutput().Split('\n');

                        foreach (var l in lines)
                        {
                            var line = leftover + l;
                            leftover = "";

                            int maxLine = 512;

                            if (line.Length > maxLine)
                            {
                                leftover += line.Substring(maxLine);
                                line = line.Substring(0, maxLine);
                            }

                            var lineParts = line.Split(' ');

                            if (lineParts.Length > 1 && lineParts[0].StartsWith('[') && lineParts[0].EndsWith(']'))
                            {
                                var latterLine = string.Join(' ', lineParts.Skip(1));
                                latterLine = ProcessLineWithNickColors(latterLine);
                                line = $"{lineParts[0]} {latterLine}";
                            }

                            //GptProcess.StandardInput.Write('a');

                            //Console.WriteLine(line);
                            Diffuser.SendMessage(line, source);
                            Thread.Sleep(300);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Diffuser.SendMessage(e.ToString(), source);
                //throw;
            }
        }
        
        new System.Threading.Thread((ThreadStart)RealFunc).Start();
    }

    public string? GetModel(string source)
    {
        var model = Config.GetString($"gpt.target.{source}");
        return model;
    }

    public void StopGpt(string source)
    {
        try
        {
            var model = GetModel(source);
            if (GptProcesses.ContainsKey(model))
                GptProcesses[model].Process.Kill(true);

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}