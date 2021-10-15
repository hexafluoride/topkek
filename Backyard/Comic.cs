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
            if (args.StartsWith(".comicimg"))
                return;
            
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

            var pickedLines = LastLines[source].Where(l => !l.Item2.StartsWith(".comic")).ToList();
            pickedLines = pickedLines.Skip(Math.Max(0, pickedLines.Count - (numLines + skipBackwards))).ToList();
            pickedLines = pickedLines.Take(Math.Min(pickedLines.Count, numLines)).ToList();

            lock (uploadClient)
            {
                var rendered = RenderComic(source.Split('/')[0], source.Split('/')[1], pickedLines, n);

                if (!File.Exists(rendered))
                {
                    SendMessage("Something went wrong while creating your comic.", source);
                    return;
                }

                var link = UploadFile(source, rendered);
                SendMessage($"Here's your comic: {link}", source);
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
                }).ToArray();

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
                        urlFragment = urlFragment.Substring(0, urlFragment.IndexOf('\''));
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

        string RenderComic(string source, string place, List<(string, string)> lines, string author)
        {
            var comicJson = new
            {
                author,
                channel = place,
                messages = lines.Select(p => new { author = p.Item1, contents = p.Item2 }).ToArray(),
                characterSet = source
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
}