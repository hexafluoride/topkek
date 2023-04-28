using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using HeimdallBase;
using Newtonsoft.Json;

namespace Backyard
{
    public partial class Backyard
    {
        private Dictionary<string, List<(string, string)>> LastLines = new();
        private HttpClient uploadClient = new();
        
        public void GenerateComic(string args, string source, string n)
        {
            if (args.StartsWith(".comicimg") || args.StartsWith(".comicvoice"))
                return;

            bool movie = args.StartsWith(".ttscomic");

            if (movie)
            {
                SendMessage($"{n}: TTS comics are currently disabled as I have been banned from the TikTok TTS API. Sorry!", source);
                return;
            }

            if (movie)
                args = args.Substring(".ttscomic".Length).Trim();
            else
                args = args.Substring(".comic".Length).Trim();

            if (CanAct("comic", source, n))
                MarkAct("comic", source, n);
            else
            {
                SendNotice("You are rate limited.", source, n);
                return;
            }

            int skipBackwards = 0;
            int numLines = Random.Next(4, 10);

            int minLength = 1;
            int maxLength = 30;
            int minSkip = 0;
            int maxSkip = 20;

            if (args.Contains(' '))
            {
                var parts = args.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                if (int.TryParse(parts[0], out int lineCount) && lineCount >= minLength && lineCount <= maxLength)
                    numLines = lineCount;
                
                if (int.TryParse(parts[1], out int skipCount) && skipCount >= minSkip && skipCount <= maxSkip)
                    skipBackwards = skipCount;
            }
            else
            {
                if (int.TryParse(args, out int lineCount) && lineCount >= minLength && lineCount <= maxLength)
                    numLines = lineCount;
            }

            var pickedLines = LastLines[source].Where(l => !l.Item2.StartsWith(".comic") && !l.Item2.StartsWith(".ttscomic")).ToList();
            pickedLines = pickedLines.Skip(Math.Max(0, pickedLines.Count - (numLines + skipBackwards))).ToList();
            pickedLines = pickedLines.Take(Math.Min(pickedLines.Count, numLines)).ToList();

            lock (uploadClient)
            {
                string rendered = "";

                if (movie)
                {
                    rendered = RenderComicMovie(source.Split('/')[0], source.Split('/')[1], pickedLines, n, source);   
                }
                else
                {
                    rendered = RenderComic(source.Split('/')[0], source.Split('/')[1], pickedLines, n);
                }

                if (!File.Exists(rendered))
                {
                    SendMessage("Something went wrong while creating your comic.", source);
                    return;
                }

                var link = UploadFile(source, rendered);
                SendMessage($"Here's your comic{(movie ? " movie" : "")}: {link}", source);
            }
        }

        void GenerateCharacterSet(string source)
        {
            var comicHandlerPath = Config.GetString("comic.basedir");
            var comicImagePath = comicHandlerPath + "/" + Config.GetString("comic.imagedir");
            var trimmedSource = source.Substring(0, source.IndexOf('/'));
            var sourceDir = comicImagePath + "/" + trimmedSource;

            var files = Directory.GetFiles(sourceDir, "*.png")
                .Where(p => !p.EndsWith(".orig.png") && !p.EndsWith(".crop.png")).Select(p =>
                {
                    try
                    {

                        var bmp = new Bitmap(p);
                        return new
                        {
                            name = Path.GetFileNameWithoutExtension(p),
                            filename = $"{trimmedSource}/{Path.GetFileName(p)}",
                            width = bmp.Width,
                            height = bmp.Height,
                            emotions = new
                            {
                                idle = new int[] {0}
                            }
                        };
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"exception {e} while opening {p}");
                        return null;
                    }
                }).Where(c => c is not null).ToArray();

            var characterSet = new
            {
                name = trimmedSource,
                author = "topkek-next",
                history = "",
                locations = new string[0],
                characters = files
            };
            
            File.WriteAllText(sourceDir + ".characterset", JsonConvert.SerializeObject(characterSet));
        }

        public void SetComicVoice(string args, string source, string n)
        {
            var trimmedSource = source.Substring(0, source.IndexOf('/'));
            args = args.Substring(".comicvoice".Length).Trim();
            var unset = string.IsNullOrWhiteSpace(args) || args.ToLowerInvariant() == "unset";

            if (unset)
            {
                SetUserDataForSourceAndNick(trimmedSource, n, "comic.voice", "");
                SendMessage($"{n}: Your comic voice is now randomly set.", source);
            }
            else
            {
                var wantedVoice = args.Split(' ')[0];
                var allowedVoices = new List<string>()
                {
                    "en_us_001",
                    "en_us_002",
                    "en_us_006",
                    "en_us_007",
                    "en_us_009",
                    "en_us_010",
                    "en_uk_001",
                    "en_uk_003",
                    "en_au_001",
                    "en_au_002",
                    "jp_001",
                    "jp_003",
                    "jp_005",
                    "jp_006",
                    "es_mx_002",
                    "br_001",
                    "br_003",
                    "br_004",
                    "br_005",
                    "fr_001",
                    "fr_002",
                    "de_001",
                    "de_002",
                    "es_002",
                    "id_001",
                    "kr_002",
                    "kr_003",
                    "kr_004",
                    "en_male_narration",
                    "en_male_funny",
                    "en_male_cody",
                    "en_female_emotional",
                    "en_us_ghostface",
                    "en_us_chewbacca",
                    "en_us_c3po",
                    "en_us_stitch",
                    "en_us_stormtrooper",
                    "en_us_rocket",
                    "en_female_madam_leota",
                    "en_male_ghosthost",
                    "en_male_pirate",
                    // "en_female_f08_salut_damour",
                    // "en_male_m03_lobby",
                    // "en_male_m03_sunshine_soon",
                    // "en_female_f08_warmy_breeze",
                    // "en_female_ht_f08_glorious",
                    // "en_male_sing_funny_it_goes_up",
                    // "en_male_m2_xhxs_m03_silly",
                    // "en_female_ht_f08_wonderful_world"
                };

                if (!allowedVoices.Contains(wantedVoice))
                {
                    SendMessage($"{n}: That voice was not recognized or is disallowed. Please see https://github.com/oscie57/tiktok-voice/wiki/Voice-Codes", source);
                    return;
                }
                
                SetUserDataForSourceAndNick(trimmedSource, n, "comic.voice", wantedVoice);
                SendMessage($"{n}: Your voice is now set to {wantedVoice}.", source);
            }
        }
        
        public void SetComicImage(string args, string source, string n)
        {
            args = args.Substring(".comicimg".Length).Trim();

            if (CanAct("comicimg", source, n))
                MarkAct("comicimg", source, n);
            else
            {
                SendNotice("You are rate limited.", source, n);
                return;
            }

            bool set = true;
            
            if (args.StartsWith('@'))
            {
                var otherNick = args.Substring(1);
                args = GetUserDataForSourceAndNick<string>(source, otherNick, "comic.imageUrl");
            }

            if (string.IsNullOrWhiteSpace(args) || args.ToLowerInvariant() == "unset")
                set = false;

            var comicHandlerPath = Config.GetString("comic.basedir");
            var comicImagePath = comicHandlerPath + "/" + Config.GetString("comic.imagedir");

            if (!Directory.Exists(comicImagePath))
                Directory.CreateDirectory(comicImagePath);

            var trimmedSource = source.Substring(0, source.IndexOf('/'));
            var sourceDir = comicImagePath + "/" + trimmedSource;

            if (!Directory.Exists(sourceDir))
                Directory.CreateDirectory(sourceDir);
            
            var filename = $"{sourceDir}/{n}.png";
            var originalFilename = $"{sourceDir}/{n}.orig.png";
            var croppedFilename = $"{sourceDir}/{n}.crop.png";
            var tempOut = Path.GetTempFileName();

            try
            {
                if (!set)
                {
                    if (File.Exists(filename))
                        File.Delete(filename);
                    
                    SetUserDataForSourceAndNick(trimmedSource, n, "comic.imageUrl", "");
                }
                else
                {
                    if (!Uri.TryCreate(args, UriKind.Absolute, out Uri uri))
                    {
                        SendMessage("Invalid URL.", source);
                        return;
                    }

                    var path = FetchFileLengthLimited(uri.ToString(), tempOut);
                    if (!File.Exists(path))
                        throw new Exception();

                    var image = new Bitmap(path);
                    var maxDimension = 250;

                    if (image.Height > maxDimension || image.Width > maxDimension)
                    {
                        image.Save(originalFilename, ImageFormat.Png);
                        if (File.Exists("/usr/bin/convert"))
                        {
                            try
                            {
                                var exitStatus = Process.Start(new ProcessStartInfo("/usr/bin/convert",
                                        $"'{originalFilename}' -trim +repage '{croppedFilename}'"))
                                    ?.WaitForExit(1000);

                                if (exitStatus != true || File.Exists(croppedFilename))
                                    throw new Exception();

                                image = new Bitmap(croppedFilename);
                                Console.WriteLine($"Successfully cropped image with ImageMagick.");
                            }
                            catch
                            {
                            }
                        }

                        int scaledWidth, scaledHeight;

                        if (image.Height > image.Width)
                        {
                            var scaleFactor = image.Height / (double) maxDimension;
                            scaledWidth = (int) (image.Width / scaleFactor);
                            scaledHeight = maxDimension;
                        }
                        else
                        {
                            var scaleFactor = image.Width / (double) maxDimension;
                            scaledHeight = (int) (image.Height / scaleFactor);
                            scaledWidth = maxDimension;
                        }

                        var scaled = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb);
                        var graphics = Graphics.FromImage(scaled);
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.CompositingQuality = CompositingQuality.HighQuality;
                        graphics.DrawImage(image, 0, 0, scaledWidth, scaledHeight);

                        image = scaled;
                    }

                    image.Save(filename, ImageFormat.Png);
                    SetUserDataForSourceAndNick(trimmedSource, n, "comic.imageUrl", uri.ToString());
                }

                GenerateCharacterSet(source);
                
                if (set)
                    SendMessage("Saved your comic image.", source);
                else
                    SendMessage("Unset your comic image.", source);
            }
            catch (Exception ex)
            {
		Console.WriteLine(ex);
                SendMessage("Failed to fetch comic image.", source);
            }
        }

        string FetchFileLengthLimited(string url, string path, int maxSize = 5000000)
        {
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            request.UserAgent = Config.GetString("title.useragent");

            using (WebResponse response = request.GetResponse())
            {
                if (response.ContentLength > maxSize)
                    return null;
                
                byte[] buffer = new byte[1024];
                using (MemoryStream bufStream = new MemoryStream())
                using (Stream stream = response.GetResponseStream())
                {
                    for (int i = 0; i < maxSize;)
                    {
                        int read = stream.Read(buffer, 0, buffer.Length);
                        //Array.Resize(ref buffer, read);
                        //bu
                        i += read;
                        bufStream.Write(buffer, 0, read);

                        if (read == 0)
                            break;
                    }

                    if (bufStream.Length >= maxSize - 1)
                        return null;
                    
                    File.WriteAllBytes(path, bufStream.ToArray());
                    return path;
                }
            }
        }

        string UploadFile(string source, string file)
        {
            using (var content = new MultipartFormDataContent())
            {
                using (var fileStream = new FileStream(file, FileMode.Open))
                {
                    var uploadTarget = GetUserDataForSourceAndNick<string>(source.Substring(0, source.IndexOf('/')), "",
                        "comic.uploadTarget");

                    if (string.IsNullOrWhiteSpace(uploadTarget) || uploadTarget == "wetfish")
                    {
                        var fileContent = new StreamContent(fileStream);

                        fileContent.Headers.Add("Content-Type", "application/octet-stream");
                        fileContent.Headers.Add("Content-Disposition",
                            "form-data; name=\"Image\"; filename=\"comic.png\"");
                        content.Add(fileContent, "Image", Path.GetFileName(file));

                        var response = uploadClient.PostAsync("https://wiki.wetfish.net/upload.php", content).Result;
                        var respStr = response.Content.ReadAsStringAsync().Result;
                        var urlFragment = respStr.Substring(respStr.IndexOf("upload/"));
                        urlFragment = urlFragment.Substring(0, urlFragment.IndexOf('\"'));
                        return $"https://wiki.wetfish.net/{urlFragment}";
                    }
                    else if (uploadTarget == "uguu")
                    {
                        var fileContent = new StreamContent(fileStream);

                        fileContent.Headers.Add("Content-Type", "application/octet-stream");
                        fileContent.Headers.Add("Content-Disposition",
                            "form-data; name=\"file\"; filename=\"comic.png\"");
                        content.Add(fileContent, "file", Path.GetFileName(file));

                        var response = uploadClient.PostAsync("https://uguu.se/api.php?d=upload-tool", content).Result;
                        var respStr = response.Content.ReadAsStringAsync().Result;
                        return respStr;
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }

        string RenderComicMovie(string source, string place, List<(string, string)> lines, string author, string rawSource)
        {
            var comicJson = new
            {
                author,
                channel = place,
                messages = lines.Select(p => new { author = p.Item1, contents = p.Item2 }).ToArray(),
                characterSet = source,
                narrowIntro = true
            };

            var comicHandlerPath = Config.GetString("comic.basedir");
            var comicBinaryPath = comicHandlerPath + "/" + Config.GetString("comic.binary_name");

            var scriptPath = $"{comicHandlerPath}/script.json";
            var outputPath = $"{comicHandlerPath}/comic.png";

            File.Delete(outputPath);
            File.WriteAllText(scriptPath, JsonConvert.SerializeObject(comicJson));
            Console.WriteLine($"before process start");
            var process = Process.Start(new ProcessStartInfo(comicBinaryPath)
            {
                WorkingDirectory = comicHandlerPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            Console.WriteLine($"process started");

            if (!process.WaitForExit(5000))
                return null;

            if (!File.Exists(outputPath))
                return null;
            
            Console.WriteLine($"before read stderr");
            var allStdErr = process.StandardOutput.ReadToEnd().Split('\n');
            Console.WriteLine($"after read stderr");
            var frames = new List<int>();

            foreach (var line in allStdErr)
            {
                if (line.StartsWith("will draw") && line.EndsWith("messages"))
                {
                    frames.Add(int.Parse(line.Split(' ')[2]));
                }
            }
            Console.WriteLine($"{frames.Count} frames");
            var archivePath = $"{comicHandlerPath}/archive/{place}/{author}";
            if (!Directory.Exists(archivePath))
                Directory.CreateDirectory(archivePath);

            var date = DateTime.UtcNow.ToString("s");
            File.Copy(scriptPath, $"{archivePath}/{date}.json");
            File.Copy(outputPath, $"{archivePath}/{date}.png");

            var outMovie = $"{archivePath}/{date}.mp4";
            var outAudio = $"{archivePath}/{date}.mp3";
            var tts = new TtsUtil(outAudio);
            tts.Start();
            var renderer = new VideoGenerator(outMovie, 288, 288);
            renderer.Start();
            Console.WriteLine($"renderer started");

            {
                // Draw intros
                var characters = new List<string>();
                var voices = new Dictionary<string, string>();
                var availableVoices = new List<string>()
                {
                    "en_us_001",
                    "en_us_006",
                    "en_us_010",
                    "en_uk_001",
                    "en_au_001",
                    "en_male_narration",
                    "en_male_funny",
                    "en_female_emotional",
                    "en_us_ghostface"
                };

                var singingVoices = new List<string>()
                {
                    "en_female_f08_salut_damour",
                    "en_male_m03_lobby",
                    "en_male_m03_sunshine_soon",
                    "en_female_f08_warmy_breeze",
                    "en_female_ht_f08_glorious",
                    "en_male_sing_funny_it_goes_up",
                    "en_male_m2_xhxs_m03_silly",
                    "en_female_ht_f08_wonderful_world"
                };

                foreach (var line in lines)
                {
                    if (!characters.Contains(line.Item1) && characters.Count < 5)
                        characters.Add(line.Item1);

                    if (!voices.ContainsKey(line.Item1))
                    {
                        var assignedVoice = GetUserDataForSourceAndNick<string>(rawSource, line.Item1, "comic.voice");
                        
                        if (string.IsNullOrWhiteSpace(assignedVoice) || singingVoices.Contains(assignedVoice))
                            assignedVoice = availableVoices[Random.Next(availableVoices.Count)];
                        availableVoices.Remove(assignedVoice);
                        voices[line.Item1] = assignedVoice;
                    }
                }

                int introIndex = 1;
                var lastIntroFrame = renderer.PrepareCrop($"{comicHandlerPath}/intro_0.png");
                
                renderer.OutputCrossFade(renderer.Blank, lastIntroFrame, 0.3);
                tts.AddSilence(0.3);
                
                // Narrate "A __ comic by ___"
                var comicIntroNarration = tts.DoTts($"A {place} comic by {author}, starring", "tiktok") ?? throw new Exception($"Failed to do TTS");
                renderer.OutputFor(lastIntroFrame, tts.GetDuration(comicIntroNarration));
                tts.PipeClip(comicIntroNarration);
                
                foreach (var character in characters)
                {
                    var introFrame = renderer.PrepareCrop($"{comicHandlerPath}/intro_{introIndex++}.png");
                    renderer.OutputCrossFade(lastIntroFrame, introFrame, 0.3);
                    tts.AddSilence(0.3);
                    
                    // Narrate the name
                    var nameNarration = tts.DoTts(character, voices[character]);
                    renderer.OutputFor(introFrame, tts.GetDuration(nameNarration));
                    tts.PipeClip(nameNarration);

                    lastIntroFrame = introFrame;
                }
                
                renderer.OutputCrossFade(lastIntroFrame, renderer.Blank, 0.5);
                tts.AddSilence(0.5);
                
                int processedLines = 0;
                foreach (var lineCount in frames)
                {
                    var linesInFrame = lines.Skip(processedLines).Take(lineCount).ToArray();
                    FastBitmap? lastFrame = null;

                    for (int i = processedLines; i < processedLines + lineCount; i++)
                    {
                        var line = lines[i];
                        Console.WriteLine($"line {i}");
                        var pre = renderer.PrepareFrame($"{comicHandlerPath}/{i}_pre.png");
                        var post = renderer.PrepareFrame($"{comicHandlerPath}/{i}_post.png");

                        Console.WriteLine($"line {i} prepared");
                        
                        // Fade in from black
                        if (i == processedLines)
                        {
                            renderer.OutputCrossFade(renderer.Blank, pre, 0.3);
                            tts.AddSilence(0.3);
                        }
                        else if (lastFrame is not null)
                        {
                            renderer.OutputCrossFade(lastFrame, pre, 0.3);
                            tts.AddSilence(0.3);
                        }
                        
                        // Output pre for minimal time
                        renderer.OutputFor(pre, 0.3);
                        tts.AddSilence(0.3);
                        
                        // Cross from pre to post for narration time
                        var lineNarration = tts.DoTts(line.Item2, voices[line.Item1]) ?? throw new Exception($"Failed to do TTS");
                        var lineDuration = tts.GetDuration(lineNarration);
                        var fadeDuration = Math.Min(2.0, lineDuration);
                        renderer.OutputCrossFade(pre, post, fadeDuration);
                        if (fadeDuration < lineDuration)
                            renderer.OutputFor(post, lineDuration - fadeDuration);
                        
                        tts.PipeClip(lineNarration);
                        
                        lastFrame = post;
                        
                        // Output post for minimal time
                        //renderer.OutputFor(post, 0.3);
                        
                        // Fade to black
                        if (i == (processedLines + lineCount) - 1)
                        {
                            renderer.OutputCrossFade(post, renderer.Blank, 0.3);
                            tts.AddSilence(0.3);
                        }
                    }
                    
                    processedLines += lineCount;
                }
                
                renderer.OutputFor(renderer.Blank, 0.5);
                tts.AddSilence(0.5);

                renderer.Close();
                tts.Close();
                Console.WriteLine($"done");
            }

            var outFinal = $"{archivePath}/{date}-final.mp4";
            var finalMuxFfmpeg = new ProcessStartInfo("/usr/bin/ffmpeg", $"-i {outMovie} -i {outAudio} -vf \"scale=iw*2:ih*2\" -y {outFinal}");
            var finalProc = Process.Start(finalMuxFfmpeg) ?? throw new Exception($"Failed mux");
            
            finalProc.WaitForExit(15000);

            return outFinal;
        }

        string RenderComic(string source, string place, List<(string, string)> lines, string author)
        {
            var comicJson = new
            {
                author,
                channel = place,
                messages = lines.Select(p => new { author = p.Item1, contents = p.Item2 }).ToArray(),
                characterSet = source,
                narrowIntro = false
            };

            var comicHandlerPath = Config.GetString("comic.basedir");
            var comicBinaryPath = comicHandlerPath + "/" + Config.GetString("comic.binary_name");

            var scriptPath = $"{comicHandlerPath}/script.json";
            var outputPath = $"{comicHandlerPath}/comic.png";

            File.Delete(outputPath);
            File.WriteAllText(scriptPath, JsonConvert.SerializeObject(comicJson));
            var process = Process.Start(new ProcessStartInfo(comicBinaryPath)
            {
                WorkingDirectory = comicHandlerPath
            });

            if (!process.WaitForExit(5000))
                return null;

            if (!File.Exists(outputPath))
                return null;

            var archivePath = $"{comicHandlerPath}/archive/{place}/{author}";
            if (!Directory.Exists(archivePath))
                Directory.CreateDirectory(archivePath);

            var date = DateTime.UtcNow.ToString("s");
            File.Copy(scriptPath, $"{archivePath}/{date}.json");
            File.Copy(outputPath, $"{archivePath}/{date}.png");

            return outputPath;
        }
    }

    public class VideoGenerator
    {
        private int OutputWidth { get; set; }
        private int OutputHeight { get; set; }
        private string OutputPath { get; set; }
        private float FramesPerSecond { get; set; } = 30;

        private Stream StdoutStream { get; set; }

        private int FrameCounter = 0;
        
        public FastBitmap Blank { get; set; }
        public Process? FfmpegProcess { get; set; }
        
        public VideoGenerator(string outPath, int width, int height)
        {
            OutputWidth = width;
            OutputHeight = height;
            OutputPath = outPath;

            Blank = new FastBitmap(width, height, PixelFormat.Format24bppRgb);
            Blank.Lock();
            for (int i = 0; i < Blank.Data.Length; i++)
                Blank.Data[i] = 0;
        }

        public void Start()
        {
            var psi = new ProcessStartInfo("/usr/bin/ffmpeg",
                $"-f rawvideo -pixel_format bgr24 -video_size {OutputWidth}x{OutputHeight} -r {FramesPerSecond} -y -i - -threads 16 -b:v 1000k -preset veryfast -pix_fmt yuv420p \"{OutputPath}\"");

            Console.WriteLine($"Starting ffmpeg with arguments {psi.Arguments}");
            psi.RedirectStandardInput = true;
            
            FfmpegProcess = Process.Start(psi) ?? throw new Exception($"Could not start ffmpeg");
            Console.WriteLine($"ffmpeg started");
            StdoutStream = FfmpegProcess.StandardInput.BaseStream;
        }
        
        public void OutputCrossFade(FastBitmap a, FastBitmap b, double timeFade)
        {
            Console.WriteLine($"{a.Width}x{b.Width}");
            var scratch = new FastBitmap(a.Width, a.Height, PixelFormat.Format24bppRgb);
            scratch.Lock();
            
            // var aBytes = a.Data.ToArray();

            var fadeTimeFrame = Math.Floor(timeFade * FramesPerSecond);
            var fadeAmount = 1d / fadeTimeFrame;

            for (int i = 0; i < fadeTimeFrame; i++)
            {
                var mult = fadeAmount * i;
                for (int q = 0; q < scratch.Data.Length; q++)
                {
                    scratch.Data[q] = (byte)((a.Data[q] * (1 - mult)) + (b.Data[q] * mult));
                }
                
                OutputFrame(scratch);
            }
            
            scratch.Unlock();
            scratch.Dispose();
            //OutputFrame(b);
        }

        public void OutputFor(FastBitmap fastBmp, double duration)
        {
            var frames = Math.Floor(duration * FramesPerSecond);
            
            for (int i = 0; i < frames; i++)
                OutputFrame(fastBmp);
        }
        public FastBitmap PrepareCrop(string path)
        {
            var raw = new FastBitmap(path);
            if (raw.Height == OutputHeight && raw.Width == OutputWidth)
            {
                raw.Lock();
                return raw;
            }

            var canvas = new Bitmap(OutputWidth, OutputHeight, PixelFormat.Format24bppRgb);
            var canvasGraphics = Graphics.FromImage(canvas);
            
            canvasGraphics.FillRectangle(Brushes.White, 0, 0, OutputWidth, OutputHeight);
            canvasGraphics.DrawImageUnscaled(raw.InternalBitmap, 0, 0);
            
            var canvasFast = new FastBitmap(canvas);
            canvasFast.Lock();
            raw.Unlock();
            raw.Dispose();
            return canvasFast;
        }

        public FastBitmap PrepareFrame(string path)
        {
            var raw = new FastBitmap(path);
            if (raw.Height == OutputHeight && raw.Width == OutputWidth)
            {
                raw.Lock();
                return raw;
            }

            if (raw.Height > OutputHeight || raw.Width > OutputWidth)
                throw new Exception($"Image too large for canvas");

            var canvas = new Bitmap(OutputWidth, OutputHeight, PixelFormat.Format24bppRgb);
            var canvasGraphics = Graphics.FromImage(canvas);

            var gapX = OutputWidth - raw.Width;
            var gapY = OutputHeight - raw.Height;

            var innerX = gapX / 2;
            var innerY = gapY / 2;

            var frameX = Math.Max(0, innerX - 2);
            var frameY = Math.Max(0, innerY - 2);
            var frameW = Math.Min(OutputWidth, raw.Width + 4);
            var frameH = Math.Min(OutputHeight, raw.Height + 4);
            
            canvasGraphics.FillRectangle(Brushes.Black, frameX, frameY, frameW, frameH);
            canvasGraphics.DrawImageUnscaled(raw.InternalBitmap, innerX, innerY);

            raw.Unlock();
            raw.Dispose();
            var canvasFast = new FastBitmap(canvas);
            canvasFast.Lock();
            return canvasFast;
        }

        public void Close()
        {
            StdoutStream.Close();
            if (!FfmpegProcess.WaitForExit(15000))
                throw new Exception("Did not finalize successfully");
            
            Blank.Unlock();
            Blank.Dispose();
        }
        
        void OutputFrame(FastBitmap bitmap)
        {
            // if (FrameCounter % 1000 == 0)
            //     Console.Error.WriteLine($"{FrameCounter}");
            StdoutStream.Write(bitmap.Data);
            FrameCounter++;
        }
    }
}
