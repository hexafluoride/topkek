using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using HeimdallBase;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FarField
{

    public class DiffuseUtil
    {
        private FarField FarField;

        public DiffuseUtil(FarField farField)
        {
            FarField = farField ?? throw new ArgumentNullException(nameof(farField));
            HttpClient.Timeout = TimeSpan.FromMinutes(2);
            GanClient.Timeout = TimeSpan.FromSeconds(10);
            ImageClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public Queue<TextToImageRequest> PendingRequests = new();
        public Dictionary<(string, string), TextToImageRequest> LastRequests = new();
        public Dictionary<Guid, List<TextToImageResult>> Results = new(); 
        public const int TotalQueueMax = 10;
        public const int PerUserQueueMax = 2;
        private HttpClient HttpClient = new();
        private HttpClient GanClient = new();
        private HttpClient ImageClient = new();

        public string? EnqueueRequest(TextToImageRequest req)
        {
            lock (PendingRequests)
            {
                if (req.Nick != "kate" && File.Exists("diffuser-down.json"))
                {
                    try
                    {
                        var diffuserDowntime = JObject.Parse(File.ReadAllText("diffuser-down.json"));
                        var until = DateTime.Parse(diffuserDowntime["until"].Value<string>());
                        var message = diffuserDowntime.ContainsKey("message")
                            ? diffuserDowntime["message"].Value<string>() : null;

                        if (DateTime.UtcNow < until)
                        {
                            if (message is not null)
                            {
                                return
                                    $".diffuse is currently undergoing a planned downtime, until {until} ({YoutubeUtil.NiceString(until - DateTime.UtcNow)} from now). Reason: \"{message}\"";
                            }
                            else
                            {
                                return
                                    $".diffuse is currently undergoing a planned downtime, until {until} ({YoutubeUtil.NiceString(until - DateTime.UtcNow)} from now).";
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                
                if (PendingRequests.Count > TotalQueueMax)
                    return
                        $"The global request queue is full ({TotalQueueMax}/{TotalQueueMax} requests). Try again in a short while!";

                var userRequests = PendingRequests.Where(r => req.Nick == r.Nick);
                string notes = "";
                
                if (userRequests.Count() >= PerUserQueueMax)
                {
                    return $"You can only have {PerUserQueueMax} requests in the queue at a time.";
                }

                req.Id = Guid.NewGuid();
                req.Timestamp = DateTime.UtcNow;

                if (!req.Parameters.ContainsKey("seed"))
                {
                    var seed = Random.Next(1, 1000000);
                    req.Parameters["seed"] = seed.ToString();
                }

                int w = 512, h = 512, copies = 1, steps = 60;
                if (req.Parameters.ContainsKey("copies") &&
                    int.TryParse(req.Parameters["copies"], out int rawCopies))
                {
                    if (rawCopies >= 1 && rawCopies <= 4)
                    {
                        copies = rawCopies;
                        if (rawCopies == 4)
                        {
                            w = 384;
                            h = 384;
                        }
                    }
                }

                if (req.Parameters.ContainsKey("pick"))
                {
                    var picked = req.Parameters["pick"].Split(',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var validPicks = picked.Where(p => int.TryParse(p, out int q)).Select(p => int.Parse(p))
                        .Where(p => p > 0 && p <= copies).Distinct().ToArray();

                    if (validPicks.Any())
                    {
                        copies = validPicks.Length;
                        req.Parameters["pick"] = string.Join(',', validPicks.Select(p => p - 1));
                    }
                    else
                    {
                        req.Parameters.Remove("pick");
                    }
                }

                if (req.Parameters.ContainsKey("width") &&
                    int.TryParse(req.Parameters["width"], out int rawWidth))
                {
                    if (rawWidth >= 128 && rawWidth <= 1024)
                    {
                        w = rawWidth - (rawWidth % 128);
                    }
                }

                if (req.Parameters.ContainsKey("height") &&
                    int.TryParse(req.Parameters["height"], out int rawHeight))
                {
                    if (rawHeight >= 128 && rawHeight <= 1024)
                    {
                        h = rawHeight - (rawHeight % 128);
                    }
                }
                
                if (req.Parameters.ContainsKey("steps") &&
                    int.TryParse(req.Parameters["steps"], out int rawSteps))
                {
                    if (rawSteps >= 1)
                    {
                        steps = rawSteps;
                    }
                }

                var score = (1024d * 1024d) / (w * h);
                score /= (Math.Sqrt(copies));
                int ceilingSteps = 100;
                int allowedSteps = (int) (ceilingSteps * score);
                
                if (steps > allowedSteps)
                {
                    notes +=
                        $"You are allowed {allowedSteps} steps at most at {copies}x{w}x{h}, so your request has been altered. Lower copies/width/height to use more steps. ";
                    steps = allowedSteps;
                    req.Parameters["steps"] = steps.ToString();
                }

                PendingRequests.Enqueue(req);
                //ProcessRequest(req);

                if (PendingRequests.Count >= 2)
                    return $"You are #{PendingRequests.Count} in the queue. {notes}";
                else
                {
                    return string.IsNullOrWhiteSpace(notes) ? null : notes;
                }
            }
        }

        public void ProcessorThread()
        {
            while (true)
            {
                ProcessRequest();
            }
        }

        private JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings()
        {
        };

        private static Random Random = new Random();

        public List<int> CommitResults(List<TextToImageResult> results)
        {
            try
            {
                var diffusionDir = Config.GetString("diffusion.storage");
                var commitDir = $"{diffusionDir}/committed";
                Directory.CreateDirectory(commitDir);

                var committedCount = new DirectoryInfo(commitDir).GetFiles().Length;
                var committedObjects = new List<CommittedImage>();

                foreach (var result in results)
                {
                    if (result.CommitIndex >= 0)
                    {
                        committedObjects.Add(JsonConvert.DeserializeObject<CommittedImage>(File.ReadAllText($"{commitDir}/{result.CommitIndex}.json")));
                        continue;
                    }
                    
                    var commitObj = new CommittedImage()
                    {
                        Id = Guid.NewGuid(),
                        SequenceNumber = committedCount++,
                        SourceResult = result
                    };

                    File.WriteAllText($"{commitDir}/{commitObj.SequenceNumber}.json",
                        JsonConvert.SerializeObject(commitObj));
                    File.Copy($"{diffusionDir}/results/{result.Id}.png", $"{commitDir}/{commitObj.SequenceNumber}.png");
                    
                    // Overwrite result object pointing to committed image id
                    result.CommitIndex = commitObj.SequenceNumber;
                    File.WriteAllText($"{diffusionDir}/results/{result.Id}.json",
                        JsonConvert.SerializeObject(result));

                    committedObjects.Add(commitObj);
                }

                return committedObjects.Select(o => o.SequenceNumber).ToList();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return new List<int>();
            }
        }
        
        private void ProcessRequest()
        {
            TextToImageRequest request = default;
            while (!PendingRequests.TryPeek(out request))
            {
                Thread.Sleep(100);
            }

            try
            {
                var diffusionDir = Config.GetString("diffusion.storage");
                var widthSanitized = 512;
                var heightSanitized = 512;
                var copies = 1;
                var steps = 60;
                var cfg = 7.0;
                var seed = Random.Next(1, int.MaxValue / 2);
                bool doGan = false;
                string picks = "";
                int latentsBatchSize = -1;
                string initImage = null;
                string mask = null;
                var img2imgPromptStrength = 1d;


                if (request.Parameters.ContainsKey("copies") &&
                    int.TryParse(request.Parameters["copies"], out int rawCopies))
                {
                    if (rawCopies >= 1 && rawCopies <= 4)
                    {
                        copies = rawCopies;

                        if (copies == 4)
                        {
                            widthSanitized = 384;
                            heightSanitized = 384;
                        }
                    }
                }
                
                if (request.Parameters.ContainsKey("pick"))
                {
                    picks = request.Parameters["pick"];
                    latentsBatchSize = copies;
                    copies = picks.Split(',').Length;
                }

                if (request.Parameters.ContainsKey("width") &&
                    int.TryParse(request.Parameters["width"], out int rawWidth))
                {
                    if (rawWidth >= 128 && rawWidth <= 1024)
                    {
                        widthSanitized = rawWidth - (rawWidth % 128);
                    }
                }
                
                if (request.Parameters.ContainsKey("seed") &&
                    int.TryParse(request.Parameters["seed"], out int rawSeed))
                {
                    if (rawSeed > 0)
                        seed = rawSeed;
                }

                if (request.Parameters.ContainsKey("height") &&
                    int.TryParse(request.Parameters["height"], out int rawHeight))
                {
                    if (rawHeight >= 128 && rawHeight <= 1024)
                    {
                        heightSanitized = rawHeight - (rawHeight % 128);
                    }
                }

                bool outpaint = request.Parameters.ContainsKey("outpaint");

                if (request.Parameters.ContainsKey("img"))
                {
                    img2imgPromptStrength = 0.8;
                    Bitmap? bitmap = default;
                    
                    if (int.TryParse(request.Parameters["img"], out int rawImageIndex))
                    {
                        var sourceImagePath = $"{diffusionDir}/committed/{rawImageIndex}.png";
                        if (File.Exists(sourceImagePath))
                        {
                            // Determine source and target aspect ratios
                            bitmap = new Bitmap(sourceImagePath);
                        }
                    }
                    else if (Uri.TryCreate(request.Parameters["img"], UriKind.Absolute, out Uri maybeUri))
                    {
                        try
                        {
                            using (var resp = ImageClient.GetAsync(maybeUri).Result)
                            {
                                byte[] data = resp.Content.ReadAsByteArrayAsync().Result;
                                MemoryStream ms = new MemoryStream(data);
                                bitmap = new Bitmap(ms);

                                if (bitmap.Width < 10 || bitmap.Height < 10 || bitmap.Width > 5000 ||
                                    bitmap.Height > 5000)
                                {
                                    throw new Exception("Image size out of bounds");
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                            bitmap = null;
                        }
                    }

                    if (bitmap is not null)
                    {
                        var targetAspectRatio = (double) widthSanitized / (double) heightSanitized;
                        var sourceAspectRatio = (double) bitmap.Width / (double) bitmap.Height;

                        if (Math.Abs(targetAspectRatio - sourceAspectRatio) < 0.01)
                        {
                            using var paddedBitmap = new Bitmap(widthSanitized, heightSanitized);
                            using var g = Graphics.FromImage(paddedBitmap);
                            g.CompositingQuality = CompositingQuality.HighQuality;
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            
                            g.DrawImage(bitmap, new Rectangle(0, 0, widthSanitized, heightSanitized), new Rectangle(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);

                            var ms = new MemoryStream();
                            paddedBitmap.Save(ms, ImageFormat.Png);

                            using var maskBmp = new Bitmap(widthSanitized, heightSanitized);
                            using var g2 = Graphics.FromImage(maskBmp);
                            
                            g2.FillRectangle(Brushes.White, 0, 0, widthSanitized, heightSanitized);

                            var ms2 = new MemoryStream();
                            maskBmp.Save(ms2, ImageFormat.Png);
                            
                            initImage =
                                $"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}";
                            mask =
                                $"data:image/png;base64,{Convert.ToBase64String(ms2.ToArray())}";
                        }
                        else
                        {
                            if (sourceAspectRatio > targetAspectRatio)
                            {
                                // Introduce letterboxing
                                var newW = widthSanitized;
                                var newH = (int)(widthSanitized / sourceAspectRatio);
                                
                                //var newH = (int)(heightSanitized * targetAspectRatio);
                                using var paddedBitmap = new Bitmap(widthSanitized, heightSanitized);
                                using var g = Graphics.FromImage(paddedBitmap);
                                var letterHeight = (float)(heightSanitized - newH) / 2f;
                                
                                g.FillRectangle(Brushes.White, 0, 0, paddedBitmap.Width, paddedBitmap.Height);

                                byte[] buf = new byte[3];
                                for (int y = 0; y < paddedBitmap.Height; y++)
                                {
                                    for (int x = 0; x < paddedBitmap.Width; x++)
                                    {
                                        Random.NextBytes(buf);
                                        paddedBitmap.SetPixel(x, y, Color.FromArgb(buf[0], buf[1], buf[2]));
                                    }
                                }
                                
                                Console.WriteLine($"neww {newW} newh {newH} source {sourceAspectRatio} target {targetAspectRatio} letterheight {letterHeight}");
                                g.DrawImage(bitmap, new Rectangle(0, (int)letterHeight, newW, newH), new Rectangle(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
                                using var ms = new MemoryStream();
                                paddedBitmap.Save(ms, ImageFormat.Png);
                                initImage =
                                    $"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}";

                                using var maskBitmap = new Bitmap(widthSanitized, heightSanitized);
                                using var g2 = Graphics.FromImage(maskBitmap);
                                g2.FillRectangle(Brushes.White, 0, 0, widthSanitized, letterHeight);
                                g2.FillRectangle(Brushes.White, 0, newH + letterHeight, widthSanitized, letterHeight);
                                
                                g2.FillRectangle(outpaint ? Brushes.Black : Brushes.White, 0, letterHeight, widthSanitized, newH);
                                //
                                // var ramp = 50;
                                //
                                // for (int i = 0; i < ramp; i++)
                                // {
                                //     var shade = (int)(255 - ((i / (double) ramp) * 255d));
                                //     var pen = new Pen(Color.FromArgb(shade, shade, shade));
                                //     g2.DrawLine(pen, 0, letterHeight + i, widthSanitized, letterHeight + i);
                                //     g2.DrawLine(pen, 0, (newH + letterHeight) - i, widthSanitized, (newH + letterHeight) - i);
                                // }
                                //
                                
                                //g2.FillRectangle(Brushes.White, 0, (heightSanitized - letterHeight) / 2, widthSanitized, letterHeight);
                                
                                using var ms2 = new MemoryStream();
                                maskBitmap.Save(ms2, ImageFormat.Png);
                                
                                paddedBitmap.Save($"{diffusionDir}/requests/{request.Id}_padded.png");
                                maskBitmap.Save($"{diffusionDir}/requests/{request.Id}_mask.png");
                                
                                mask = $"data:image/png;base64,{Convert.ToBase64String(ms2.ToArray())}";
                            }
                            else
                            {
                                // Introduce pillarboxing
                                var newW = (int)(sourceAspectRatio * heightSanitized);
                                var newH = heightSanitized;
                                using var paddedBitmap = new Bitmap(widthSanitized, heightSanitized);
                                using var g = Graphics.FromImage(paddedBitmap);
                                g.FillRectangle(Brushes.Black, 0, 0, paddedBitmap.Width, paddedBitmap.Height);
                                var pillarWidth = (float)((widthSanitized - newW) / 2);
                                Console.WriteLine($"neww {newW} newh {newH} source {sourceAspectRatio} target {targetAspectRatio} pillarWidth {pillarWidth}");
                                g.DrawImage(bitmap, new Rectangle((int)pillarWidth, 0, (int)newW, newH), new Rectangle(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
                                
                                using var ms = new MemoryStream();
                                paddedBitmap.Save(ms, ImageFormat.Png);
                                initImage =
                                    $"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}";
                                using var maskBitmap = new Bitmap(widthSanitized, heightSanitized);
                                using var g2 = Graphics.FromImage(maskBitmap);
                                g2.FillRectangle(Brushes.White, 0, 0, pillarWidth, heightSanitized);
                                g2.FillRectangle(Brushes.White, (int)newW + pillarWidth, 0, pillarWidth, heightSanitized);
                                g2.FillRectangle(outpaint ? Brushes.Black : Brushes.White, pillarWidth, 0, (int)newW, heightSanitized);

                                using var ms2 = new MemoryStream();
                                maskBitmap.Save(ms2, ImageFormat.Png);
                                
                                paddedBitmap.Save($"{diffusionDir}/requests/{request.Id}_padded.png");
                                maskBitmap.Save($"{diffusionDir}/requests/{request.Id}_mask.png");
                                
                                mask = $"data:image/png;base64,{Convert.ToBase64String(ms2.ToArray())}";
                            }
                        }
                    }
                }

                if (request.Parameters.ContainsKey("overwrite") &&
                    double.TryParse(request.Parameters["overwrite"], out double overwriteRaw))
                {
                    if (overwriteRaw >= 0 && overwriteRaw <= 1)
                    {
                        img2imgPromptStrength = overwriteRaw;
                    }
                }
                
                if (request.Parameters.ContainsKey("steps") &&
                    int.TryParse(request.Parameters["steps"], out int rawSteps))
                {
                    if (rawSteps >= 1 && rawSteps <= 400)
                    {
                        steps = rawSteps;
                    }
                }

                if (request.Parameters.ContainsKey("cfg") &&
                    double.TryParse(request.Parameters["cfg"], out double rawCfg))
                {
                    if (rawCfg >= 1d && rawCfg <= 20)
                    {
                        cfg = rawCfg;
                    }
                }

                doGan = request.Parameters.ContainsKey("gan");

                var encodedObj = new
                {
                    input = new 
                    {
                        prompt = request.Prompt,
                        width = widthSanitized,
                        height = heightSanitized,
                        guidance_scale = cfg,
                        num_inference_steps = steps,
                        num_outputs = copies,
                        seed,
                        latents_batch_pick = picks,
                        latents_batch_size = latentsBatchSize,
                        init_image = initImage,
                        mask,
                        prompt_strength = img2imgPromptStrength
                    }
                };
                var encoded = JsonConvert.SerializeObject(encodedObj, JsonSerializerSettings);

                request.Parameters["actualRequest"] = encoded;
                Directory.CreateDirectory($"{diffusionDir}/requests");
                File.WriteAllText($"{diffusionDir}/requests/{request.Id}.json", JsonConvert.SerializeObject(request));

                var content = new StringContent(encoded, Encoding.UTF8, "application/json");

                var sw = Stopwatch.StartNew();
                var response = HttpClient.PostAsync(Config.GetString("diffusion.endpoint"), content).Result;
                
                if (!response.IsSuccessStatusCode)
                {
                    FarField.SendMessage($"{request.Nick}: Something went wrong while serving your request. Sorry! Status code: {response.StatusCode}",
                        request.Source);
                    return;
                }

                var responseParsed = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                if (!responseParsed.ContainsKey("status") || responseParsed["status"].Value<string>() != "succeeded")
                {
                    FarField.SendMessage(
                        $"{request.Nick}: Something went really wrong while serving your request. Sorry!",
                        request.Source);
                    return;
                }

                var tiles = responseParsed["output"].Select(t => t.Value<string>()).ToArray();
                var tilesGan = new string[tiles.Length];
                var tilesOld = new string[tiles.Length];
                bool ganFailed = false;

                Directory.CreateDirectory($"{diffusionDir}/results");
                Directory.CreateDirectory($"{diffusionDir}/results/tiled");
                
                if (doGan)
                {
                    try
                    {
                        var ganEndpoint = Config.GetString("diffusion.gan_endpoint");
                        for (int i = 0; i < tiles.Length; i++)
                        {
                            var ganReq = new
                            {
                                input = new
                                {
                                    img = tiles[i]
                                }
                            };
                            
                            var ganResp = GanClient.PostAsJsonAsync(ganEndpoint, ganReq).Result;
                            var resBody = ganResp.Content.ReadAsStringAsync().Result;
                            var parsed = JsonDocument.Parse(resBody);
                            var ganImageStr = parsed.RootElement.GetProperty("output").GetString() ?? throw new Exception("Failed to decode GAN result");

                            if (!ganImageStr.StartsWith("data:image/png;base64,"))
                            {
                                throw new Exception(
                                    $"Expected base64 encoded png, GAN tile started with {ganImageStr.Substring(0, 20)}...");
                            }

                            tilesGan[i] = ganImageStr;
                        }

                        tilesOld = tiles;
                        tiles = tilesGan;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        ganFailed = true;
                    }
                }
                
                var tilesLoaded = new List<Bitmap>();
                var resultObjects = new List<TextToImageResult>();

                int w = 0, h = 0;
                for (int i = 0; i < tiles.Length; i++)
                {
                    var tileStr = tiles[i];
                    if (tileStr is null)
                    {
                        throw new Exception("Tile is null");
                    }
                    
                    if (!tileStr.StartsWith("data:image/png;base64,"))
                    {
                        throw new Exception(
                            $"Expected base64 encoded png, tile started with {tileStr.Substring(0, 20)}...");
                    }

                    var tileBytes = Convert.FromBase64String(tileStr.Substring("data:image/png;base64,".Length));
                    using var ms = new MemoryStream(tileBytes);
                    var bitmap = new Bitmap(ms);
                    tilesLoaded.Add(bitmap);

                    w = bitmap.Width;
                    h = bitmap.Height;

                    var resultObj = new TextToImageResult()
                    {
                        Created = DateTime.UtcNow,
                        Id = Guid.NewGuid(),
                        RequestId = request.Id,
                        Index = i
                    };

                    if (!string.IsNullOrWhiteSpace(tilesOld[i]))
                    {
                        var oldTileBytes = Convert.FromBase64String(tilesOld[i].Substring("data:image/png;base64,".Length));
                        File.WriteAllBytes($"{diffusionDir}/results/{resultObj.Id}.pre-gan.png", oldTileBytes);
                        resultObj.Metadata["gan"] = "true";
                    }

                    File.WriteAllBytes($"{diffusionDir}/results/{resultObj.Id}.png", tileBytes);
                    File.WriteAllText($"{diffusionDir}/results/{resultObj.Id}.json",
                        JsonConvert.SerializeObject(resultObj));
                    
                    resultObjects.Add(resultObj);
                }

                Bitmap composite = default;

                if (w == h)
                {
                    if (tilesLoaded.Count < 4)
                    {
                        composite = new Bitmap(w * tilesLoaded.Count, h);
                        var g = Graphics.FromImage(composite);

                        for (int i = 0; i < tilesLoaded.Count; i++)
                        {
                            g.DrawImageUnscaled(tilesLoaded[i], i * w, 0);
                        }
                    }
                    else
                    {
                        composite = new Bitmap(w * 2, h * 2);
                        var g = Graphics.FromImage(composite);

                        for (int i = 0; i < tilesLoaded.Count; i++)
                        {
                            g.DrawImageUnscaled(tilesLoaded[i], (i & 1) * w, ((i & 2) >> 1) * h);
                        }
                    }
                }
                else if (w < h)
                {
                    composite = new Bitmap(w * tilesLoaded.Count, h);
                    var g = Graphics.FromImage(composite);

                    for (int i = 0; i < tilesLoaded.Count; i++)
                    {
                        g.DrawImageUnscaled(tilesLoaded[i], i * w, 0);
                    }
                }
                else if (w > h)
                {
                    composite = new Bitmap(w, h * tilesLoaded.Count);
                    var g = Graphics.FromImage(composite);

                    for (int i = 0; i < tilesLoaded.Count; i++)
                    {
                        g.DrawImageUnscaled(tilesLoaded[i], 0, i * h);
                    }
                }

                Directory.CreateDirectory($"{diffusionDir}/results/tiled");
                composite.Save($"{diffusionDir}/results/tiled/{request.Id}.png");

                var shortId = SHA256.HashData(request.Id.ToByteArray()).Take(4).ToArray();
                shortId[3] = 0;
                var shortIdNumeric = BitConverter.ToInt32(shortId);

                var diffusionOutputDir = Config.GetString("diffusion.output");
                while (File.Exists($"{diffusionOutputDir}/{shortIdNumeric}.png"))
                    shortIdNumeric++;

                var diffusionHost = Config.GetString("diffusion.host");

                File.Copy($"{diffusionDir}/results/tiled/{request.Id}.png",
                    $"{diffusionOutputDir}/{shortIdNumeric}.png");

                FarField.SendMessage(
                    $"{request.Nick}: your diffusion for prompt \"{request.Prompt.Substring(0, Math.Min(20, request.Prompt.Length))}{(request.Prompt.Length > 20 ? "..." : "")}\" with seed {seed}: https://{diffusionHost}/{shortIdNumeric} ({sw.Elapsed.TotalSeconds:0.00}s{(ganFailed ? ", GAN upscaling failed, sorry!" : "")})",
                    request.Source);
                LastRequests[(request.Source, request.Nick)] = request;
                Results[request.Id] = resultObjects;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                FarField.SendMessage($"{request.Nick}: Exception thrown: {e.Message}", request.Source);
            }
            finally
            {
                PendingRequests.Dequeue();
            }
        }
    }

    public class TextToImageRequest
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Source { get; set; }
        public string Nick { get; set; }
        public string Prompt { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }

    public class TextToImageResult
    {
        public Guid RequestId { get; set; }
        public Guid Id { get; set; }

        public DateTime Created { get; set; }
        public int Index { get; set; } = -1;
        public int CommitIndex { get; set; } = -1;
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class CommittedImage
    {
        public Guid Id { get; set; }
        public TextToImageResult SourceResult { get; set; }
        public int SequenceNumber { get; set; }
    }
}