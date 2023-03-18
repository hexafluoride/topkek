using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HeimdallBase;
using Newtonsoft.Json;

namespace Backyard
{

    public class TtsUtil
    {
        private Stream FfmpegStdin { get; set; }
        private Process? FfmpegProcess { get; set; }
        private string OutputPath { get; set; }
        private int SampleRate = 44100;
        private int BytesPerSample = 4;
        private Random Random = new();

        public TtsUtil(string outputPath)
        {
            OutputPath = outputPath;
        }

        public void Start()
        {
            var psi = new ProcessStartInfo("/usr/bin/ffmpeg",
                $"-f f32le -ar {SampleRate} -ac 1 -y -i - -b:a 192k \"{OutputPath}\"");

            Console.WriteLine($"Starting ffmpeg with arguments {psi.Arguments}");
            psi.RedirectStandardInput = true;

            FfmpegProcess = Process.Start(psi) ?? throw new Exception($"Could not start ffmpeg");
            Console.WriteLine($"ffmpeg started");
            FfmpegStdin = FfmpegProcess.StandardInput.BaseStream;
        }

        public double GetDuration(byte[] buffer)
        {
            var duration = buffer.Length / (double) (BytesPerSample * SampleRate);
            Console.WriteLine($"Buffer of length {buffer.Length} has duration {duration}s");
            return duration;
        }

        public void PipeClip(byte[] buffer)
        {
            for (int i = 0; i < buffer.Length; i += 1024)
            {
                var len = Math.Min(1024, buffer.Length - i);
                FfmpegStdin.Write(buffer, i, len);
            }
        }

        public void AddSilence(double duration)
        {
            var samples = duration * SampleRate;

            var empty =
                BitConverter.GetBytes(0f);
            for (int i = 0; i < samples; i++)
            {
                FfmpegStdin.Write(empty, 0, empty.Length);
            }
        }

        public void Close()
        {
            FfmpegStdin.Close();
            if (!FfmpegProcess.WaitForExit(15000))
                throw new Exception("Did not finalize successfully");
        }

        public void DoChunkedTiktokTts(string prompt, string voice, string outFile)
        {
            var ttsScript = Config.GetString("tts.tiktok-script");
            var muxScript = Config.GetString("tts.mux-script");
            if (!File.Exists(ttsScript))
                return;

            var chunks = new List<string>();
            var words = prompt.Split(' ').ToList();
            var currentChunk = "";
            var chunkFiles = new List<string>();

            void ConsumeChunk()
            {
                var chunkOutfile = $"/tmp/{Random.Next(1000000)}.mp3";
                var psi = new ProcessStartInfo(ttsScript, chunkOutfile);
                psi.UseShellExecute = false;
                psi.RedirectStandardInput = true;

                var proc = Process.Start(psi) ?? throw new Exception("Could not start TTS script");

                proc.StandardInput.WriteLine(JsonConvert.SerializeObject(new {text = currentChunk, voice}));
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
                // return "Timed out";
            }

            // if (!File.Exists(outFile))
            // {
            //     return "Failed to produce file";
            // }
            //
            // return outFile;
        }

        public byte[]? DoTts(string prompt, string type = "normal")
        {
            var tiktok = type != "normal";
            string tiktokVoice = type switch
            {
                "tiktok" => "en_us_001",
                "sing" => "en_female_f08_salut_damour",
                _ => type
            };

            var ttsScript = tiktok ? Config.GetString("tts.tiktok-script") : Config.GetString("tts.script");
            if (!File.Exists(ttsScript))
                return null;

            var ttsOutputPath = Config.GetString("tts.output");
            var ttsHost = Config.GetString("tts.host");

            //var outFile = $"{ttsOutputPath}/{Random.Shared.Next(1000000)}.mp3";
            var outFile = Path.GetTempFileName();

            if (tiktok)
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
                    return null;
                }
            }

            if (!File.Exists(outFile))
            {
                return null;
            }

            //return $"https://{ttsHost}/{Path.GetFileName(outFile)}";
            var bytes = File.ReadAllBytes(outFile);
            File.Delete(outFile);
            return bytes;
        }
    }
}