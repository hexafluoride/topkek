using System.Diagnostics;
using HeimdallBase;
using Newtonsoft.Json;

namespace Diffuser;

public class EspeakUtil
{
    public Diffuser Diffuser;
    
    public EspeakUtil(Diffuser diffuser)
    {
        Diffuser = diffuser;
    }

    public string? DoChunkedTiktokTts(string prompt, string voice, string outFile)
    {
        var ttsScript = Config.GetString("tts.tiktok-script");
        var muxScript = Config.GetString("tts.mux-script");
        if (!File.Exists(ttsScript))
            return null;

        var ttsOutputPath = Config.GetString("tts.output");

        var chunks = new List<string>();
        var words = prompt.Split(' ').ToList();
        var currentChunk = "";
        var chunkFiles = new List<string>();

        void ConsumeChunk()
        {
            var chunkOutfile = $"/tmp/{Random.Shared.Next(1000000)}.mp3";
            var psi = new ProcessStartInfo(ttsScript, chunkOutfile);
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;

            var proc = Process.Start(psi) ?? throw new Exception("Could not start TTS script");

            proc.StandardInput.WriteLine(JsonConvert.SerializeObject(new { text = currentChunk, voice }));
            proc.StandardInput.Flush();
            proc.StandardInput.Close();

            if (!proc.WaitForExit(15000))
            {
                throw new Exception($"Timed out");
            }

            chunks.Add(chunkOutfile);
            currentChunk = "";
        }

        while (words.Any())
        {
            var word = words[0];
            var nextChunk = currentChunk + " " + word;
            if (nextChunk.Length > 250)
            {
                ConsumeChunk();
            }
            else
            {
                currentChunk = nextChunk;
                words.RemoveAt(0);
            }
        }

        if (currentChunk.Length > 1)
        {
            ConsumeChunk();
        }
        
        var muxFile = Path.GetTempFileName();
        File.WriteAllLines(muxFile, chunks.Select(c => $"file '{c}'"));
        
        var psiFinal = new ProcessStartInfo(muxScript, $"{muxFile} {outFile}");

        var procFinal = Process.Start(psiFinal) ?? throw new Exception("Could not start TTS script");

        if (!procFinal.WaitForExit(15000))
        {
            return "Timed out";
        }

        if (!File.Exists(outFile))
        {
            return "Failed to produce file";
        }

        return outFile;
    }

    public string? DoTts(string prompt, string type = "normal")
    {
        var tiktok = type != "normal";
        string tiktokVoice = type switch
        {
            "tiktok" => "en_us_001",
            "sing" => "en_female_f08_salut_damour",
            _ => type
        };

        if (tiktok)
        {
            return "TikTok TTS is currently disabled as I have been banned from the API. Sorry!";
        }
        
        var ttsScript = tiktok ? Config.GetString("tts.tiktok-script") : Config.GetString("tts.script");
        if (!File.Exists(ttsScript))
            return null;

        var ttsOutputPath = Config.GetString("tts.output");
        var ttsHost = Config.GetString("tts.host");

        var outFile = $"{ttsOutputPath}/{Random.Shared.Next(1000000)}.mp3";

        if (tiktok && prompt.Length > 200)
        {
            DoChunkedTiktokTts(prompt, tiktokVoice, outFile);
        }
        else
        {
            var psi = new ProcessStartInfo(ttsScript, outFile);
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;

            var proc = Process.Start(psi) ?? throw new Exception("Could not start TTS script");

            if (tiktok)
            {
                proc.StandardInput.WriteLine(JsonConvert.SerializeObject(new {text = prompt, voice = tiktokVoice}));
            }
            else
            {
                proc.StandardInput.WriteLine(prompt);
            }

            proc.StandardInput.Flush();
            proc.StandardInput.Close();

            if (!proc.WaitForExit(15000))
            {
                return "Timed out";
            }
        }

        if (!File.Exists(outFile))
        {
            return "Failed to produce file";
        }

        return $"https://{ttsHost}/{Path.GetFileName(outFile)}";
    }
}