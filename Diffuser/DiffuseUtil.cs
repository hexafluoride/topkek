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
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Diffuser
{
    public class DiffuseUtil
    {
        private Diffuser Diffuser;

        public DiffuseUtil(Diffuser diffuser)
        {
            Diffuser = diffuser ?? throw new ArgumentNullException(nameof(diffuser));
            HttpClient.Timeout = TimeSpan.FromMinutes(2);
            GanClient.Timeout = TimeSpan.FromSeconds(10);
            ImageClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public Queue<TextToImageRequest> PendingRequests = new();
        public Dictionary<(string, string), TextToImageRequest> LastRequests = new();
        public Dictionary<Guid, List<TextToImageResult>> Results = new(); 
        public const int TotalQueueMax = 15;
        public const int PerUserQueueMax = 3;
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
                                    $".diffuse is currently undergoing a planned downtime, until {until} ({NiceString(until - DateTime.UtcNow)} from now). Reason: \"{message}\"";
                            }
                            else
                            {
                                return
                                    $".diffuse is currently undergoing a planned downtime, until {until} ({NiceString(until - DateTime.UtcNow)} from now).";
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

                int w = 1024, h = 1024, copies = 4, steps = 60;
                if (req.Parameters.ContainsKey("copies") &&
                    int.TryParse(req.Parameters["copies"], out int rawCopies))
                {
                    if (rawCopies >= 1 && rawCopies <= 8)
                    {
                        copies = rawCopies;
                        // if (rawCopies == 4)
                        // {
                        //     w = 448;
                        //     h = 448;
                        //     steps = 45;
                        // }
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
                    if (rawWidth >= 128 && rawWidth <= 4096)
                    {
                        w = rawWidth - (rawWidth % 8);
                    }
                }

                if (req.Parameters.ContainsKey("height") &&
                    int.TryParse(req.Parameters["height"], out int rawHeight))
                {
                    if (rawHeight >= 128 && rawHeight <= 4096)
                    {
                        h = rawHeight - (rawHeight % 8);
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
                int ceilingSteps = 400;
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
                    return $"You are #{PendingRequests.Count} in the queue with seed {req.Parameters["seed"]}. {notes}";
                else
                {
                    return string.IsNullOrWhiteSpace(notes) ? null : notes;
                }
            }
        }

        public async Task ProcessorThread()
        {
            Dictionary<string, Guid> endpoints = Config.GetArray<string>("diffusion.endpoints").ToDictionary(e => e, e => Guid.Empty);

            var endpointKeys = endpoints.Keys.ToList();
            async Task<string> GetNextFreeEndpointAsync()
            {
                // go through endpoints round robin until one is free
                int i = 0, j = 0;
                while (true)
                {
                    string endpoint = endpointKeys[i++];
                    if (i >= endpointKeys.Count)
                    {
                        i = 0;
                        j++;
                    }

                    Guid currentRequest = endpoints[endpoint];
                    if (currentRequest == Guid.Empty || Results.ContainsKey(currentRequest))
                    {
                        return endpoint;
                    }

                    if (j > 0)
                    {
                        await Task.Delay(15);
                        if (i == 0 && j % 100 == 0)
                        {
                            Console.WriteLine($"endpoints {string.Join(",", endpointKeys)} busy on {string.Join(',', endpointKeys.Select(k => endpoints[k]))}");
                        }
                    }
                }
            }
            while (true)
            {
                TextToImageRequest request;
                // string nextEndpoint = await GetNextFreeEndpointAsync();
                await GetNextFreeEndpointAsync();
                while (!PendingRequests.TryDequeue(out request))
                    await Task.Delay(10);

                int copies = 4;
                if (request.Parameters.ContainsKey("copies") &&
                    int.TryParse(request.Parameters["copies"], out int rawCopies))
                {
                    if (rawCopies >= 1 && rawCopies <= 8)
                    {
                        copies = rawCopies;
                    }
                }
                string[] nextEndpoints = endpointKeys.Where(e => endpoints[e] == Guid.Empty || Results.ContainsKey(endpoints[e])).ToArray();
                var schedule = CreateSchedule(copies, nextEndpoints);
                
                foreach ((var nextEndpoint, int count) in schedule)
                {
                    if (count != 0)
                    {
                        endpoints[nextEndpoint] = request.Id;
                        Console.WriteLine($"Scheduling {request.Id} to {nextEndpoint}");
                    }
                }
                
                ProcessRequest(request, schedule);
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

        private Dictionary<string, int> CreateSchedule(int count, string[] endpoints)
        {
            Dictionary<string, int> schedule = new();
            for (int i = 0; i < count; i++)
            {
                string nextEndpoint = endpoints[i % endpoints.Length];
                if (!schedule.ContainsKey(nextEndpoint))
                    schedule[nextEndpoint] = 0;
                schedule[nextEndpoint]++;
            }
            
            Console.WriteLine($"Scheduled {count} tiles to {endpoints.Length} endpoints: {JsonSerializer.Serialize(schedule)}");

            return schedule;
        }

        private async Task ProcessRequest(TextToImageRequest request, Dictionary<string, int> endpoints)
        {
            try
            {
                var patchMatchBinary = Config.GetString("diffusion.patchmatch");
                bool canPatchMatch = File.Exists(patchMatchBinary);

                var diffusionDir = Config.GetString("diffusion.storage");
                var widthSanitized = 1024;
                var heightSanitized = 1024;
                var copies = 4;
                var steps = 8;
                var cfg = 1d;
                var seed = Random.Next(1, int.MaxValue / 2);
                bool doGan = false;
                string picks = "";
                int latentsBatchSize = -1;
                string initImage = null;
                string mask = null;
                var img2imgPromptStrength = 1d;
                string negativePrompt = "";
                string refiner = "expert_ensemble_refiner";
                string lora = "";

                if (request.Parameters.ContainsKey("lora") && request.Nick == "kate")
                {
                    lora = request.Parameters["lora"];
                }

                if (request.Parameters.ContainsKey("negative"))
                {
                    negativePrompt = request.Parameters["negative"];
                }

                if (request.Parameters.ContainsKey("refiner"))
                {
                    refiner = request.Parameters["refiner"];
                }
                
                if (request.Parameters.ContainsKey("copies") &&
                    int.TryParse(request.Parameters["copies"], out int rawCopies))
                {
                    if (rawCopies >= 1 && rawCopies <= 8)
                    {
                        copies = rawCopies;

                        if (copies >= 4 && !request.Parameters.ContainsKey("img"))
                        {
                            widthSanitized = 1024;
                            heightSanitized = 1024;
                            steps = 6;
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
                    if (rawWidth >= 128 && rawWidth <= 4096)
                    {
                        widthSanitized = rawWidth - (rawWidth % 8);
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
                    if (rawHeight >= 128 && rawHeight <= 4096)
                    {
                        heightSanitized = rawHeight - (rawHeight % 8);
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
                            using (var resp = await ImageClient.GetAsync(maybeUri))
                            {
                                byte[] data = await resp.Content.ReadAsByteArrayAsync();
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
                            // mask =
                            //     $"data:image/png;base64,{Convert.ToBase64String(ms2.ToArray())}";
                        }
                        else
                        {
                            using var paddedBitmap = new Bitmap(widthSanitized, heightSanitized);
                            using var maskBitmap = new Bitmap(widthSanitized, heightSanitized);
                            using var g = Graphics.FromImage(paddedBitmap);
                            using var g2 = Graphics.FromImage(maskBitmap);
                            if (sourceAspectRatio > targetAspectRatio)
                            {
                                // Introduce letterboxing
                                var newW = widthSanitized;
                                var newH = (int)(widthSanitized / sourceAspectRatio);
                                
                                //var newH = (int)(heightSanitized * targetAspectRatio);
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

                                g2.FillRectangle(Brushes.White, 0, 0, widthSanitized, letterHeight);
                                g2.FillRectangle(Brushes.White, 0, newH + letterHeight, widthSanitized, letterHeight);
                                
                                //g2.FillRectangle(outpaint ? Brushes.Black : Brushes.White, 0, letterHeight, widthSanitized, newH);
                                g2.FillRectangle(Brushes.Black, 0, letterHeight, widthSanitized, newH);
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
                            }
                            else
                            {
                                // Introduce pillarboxing
                                var newW = (int)(sourceAspectRatio * heightSanitized);
                                var newH = heightSanitized;
                                g.FillRectangle(Brushes.Black, 0, 0, paddedBitmap.Width, paddedBitmap.Height);
                                var pillarWidth = (float)((widthSanitized - newW) / 2);
                                Console.WriteLine($"neww {newW} newh {newH} source {sourceAspectRatio} target {targetAspectRatio} pillarWidth {pillarWidth}");
                                g.DrawImage(bitmap, new Rectangle((int)pillarWidth, 0, (int)newW, newH), new Rectangle(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
                                
                                using var ms = new MemoryStream();
                                paddedBitmap.Save(ms, ImageFormat.Png);
                                initImage =
                                    $"data:image/png;base64,{Convert.ToBase64String(ms.ToArray())}";
                                g2.FillRectangle(Brushes.White, 0, 0, pillarWidth, heightSanitized);
                                g2.FillRectangle(Brushes.White, (int)newW + pillarWidth, 0, pillarWidth, heightSanitized);
                                // g2.FillRectangle(outpaint ? Brushes.Black : Brushes.White, pillarWidth, 0, (int)newW, heightSanitized);
                                g2.FillRectangle(Brushes.Black, pillarWidth, 0, (int)newW, heightSanitized);
                                
                            }
                            
                            using var ms2 = new MemoryStream();
                            maskBitmap.Save(ms2, ImageFormat.Png);

                            var paddedPath = $"{diffusionDir}/requests/{request.Id}_padded.png";
                            var maskPath = $"{diffusionDir}/requests/{request.Id}_mask.png";
                            paddedBitmap.Save(paddedPath);
                            maskBitmap.Save(maskPath);

                            if (canPatchMatch && !(request.Parameters.ContainsKey("patchmatch") && request.Parameters["patchmatch"] == "false"))
                            {
                                try
                                {
                                    var patchedPath = $"{diffusionDir}/requests/{request.Id}_patched.png";
                                    var patchMatchPsi = new ProcessStartInfo(patchMatchBinary,
                                        $"\"{paddedPath}\" \"{maskPath}\" \"{patchedPath}\"");
                                    var patchMatchProcess = Process.Start(patchMatchPsi);
                                    if (!patchMatchProcess.WaitForExit(15000))
                                    {
                                        patchMatchProcess.Kill();
                                        throw new Exception("PatchMatch timed out");
                                    }

                                    initImage =
                                        $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(patchedPath))}";
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                }
                            }
                                
                            mask = $"data:image/png;base64,{Convert.ToBase64String(ms2.ToArray())}";
                            
                            if (!outpaint)
                                mask = null;
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
                    if (rawCfg >= 0.001d && rawCfg <= 20)
                    {
                        cfg = rawCfg;
                    }
                }

                string loraFilename = "";
                double loraStrength = 0.8;

                if (request.Parameters.ContainsKey("lora-filename"))
                {
                    loraFilename = request.Parameters["lora-filename"];
                }

                if (request.Parameters.ContainsKey("lora-scale") &&
                    double.TryParse(request.Parameters["lora-scale"], out double _loraStrength) && _loraStrength > 0 &&
                    _loraStrength <= 1)
                {
                    loraStrength = _loraStrength;
                }

                var sw = Stopwatch.StartNew();
                doGan = request.Parameters.ContainsKey("gan");
                int totalCopies = copies;
                // double copiesPerEndpoint = totalCopies / (double)endpoints.Length;
                var tiles = new string[totalCopies];
                var requests = new List<Task<HttpResponseMessage>>();
                Dictionary<string, string> actualRequests = new();
                int requestedTiles = 0;

                string[] endpointsList = endpoints.Keys.ToArray();
                for (int i = 0; i < endpoints.Count; i++)
                {
                    var endpoint = endpointsList[i];
                    int endpointCount = endpoints[endpoint];
                    if (endpointCount == 0)
                    {
                        continue;
                    }
                    requestedTiles += endpointCount;
                    var encodedObj = new
                    {
                        input = new
                        {
                            prompt = request.Prompt,
                            negative_prompt = negativePrompt,
                            width = widthSanitized,
                            height = heightSanitized,
                            guidance_scale = cfg,
                            num_inference_steps = steps,
                            num_outputs = endpointCount,
                            seed = seed + (i * 10000000),
                            disable_safety_checker = true,
                            apply_watermark = false,
                            refine = refiner,
                            image = initImage,
                            mask,
                            lora_tag = lora,
                            lora_scale = loraStrength,
                            lora_filename = loraFilename,
                            // latents_batch_pick = picks,
                            // latents_batch_size = latentsBatchSize,
                            // init_image = initImage,
                            // mask,
                            prompt_strength = img2imgPromptStrength
                        }
                    };
                    var encoded = JsonConvert.SerializeObject(encodedObj, JsonSerializerSettings);

                    // request.Parameters["actualRequest"] = encoded;
                    actualRequests[endpoint] = encoded;

                    var content = new StringContent(encoded, Encoding.UTF8, "application/json");
                    requests.Add(HttpClient.PostAsync(endpoint, content));
                }

                request.Parameters["actualRequest"] = JsonSerializer.Serialize(actualRequests);
                Directory.CreateDirectory($"{diffusionDir}/requests");
                await File.WriteAllTextAsync($"{diffusionDir}/requests/{request.Id}.json",
                    JsonConvert.SerializeObject(request));

                bool succeeded = true;
                string failMessage = "";
                int savedTiles = 0;
                for (int i = 0; i < requests.Count; i++)
                {
                    var endpoint = endpointsList[i];
                    var response = await requests[i];
                    string responseText = await response.Content.ReadAsStringAsync();
                    var responseParsed = JObject.Parse(responseText);
                    if (!responseParsed.ContainsKey("status") ||
                        responseParsed["status"].Value<string>() != "succeeded")
                    {
                        succeeded = false;
                        Console.WriteLine($"Endpoint {endpoint} failed: {responseText}");
                        if (!response.IsSuccessStatusCode)
                        {
                            failMessage = $"Status code: {response.StatusCode}";
                        }

                        continue;
                    }

                    var subTiles = responseParsed["output"].Select(t => t.Value<string>()).ToList();
                    for (int j = 0; j < subTiles.Count; j++)
                    {
                        tiles[savedTiles + j] = subTiles[j];
                    }
                    savedTiles += subTiles.Count;
                }

                if (savedTiles != tiles.Length)
                {
                    succeeded = false;
                    failMessage = $"Expected {tiles.Length} tiles, got {savedTiles}";
                }

                if (!succeeded)
                {
                    Diffuser.SendMessage($"{request.Nick}: Something went wrong while serving your request. Sorry! {failMessage}",
                        request.Source);
                    Results[request.Id] = new List<TextToImageResult>();
                    return;
                }
                
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
                            
                            var ganResp = await GanClient.PostAsJsonAsync(ganEndpoint, ganReq);
                            var parsed = await JsonDocument.ParseAsync(await ganResp.Content.ReadAsStreamAsync());
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
                        await File.WriteAllBytesAsync($"{diffusionDir}/results/{resultObj.Id}.pre-gan.png", oldTileBytes);
                        resultObj.Metadata["gan"] = "true";
                    }

                    await File.WriteAllBytesAsync($"{diffusionDir}/results/{resultObj.Id}.png", tileBytes);
                    await File.WriteAllTextAsync($"{diffusionDir}/results/{resultObj.Id}.json",
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

                if (File.Exists("/usr/bin/cwebp"))
                {
                    try
                    {
                        Process process = Process.Start("/usr/bin/cwebp",
                            $"-q 100 \"{diffusionOutputDir}/{shortIdNumeric}.png\" -o \"{diffusionOutputDir}/{shortIdNumeric}.webp\"");
                        Console.WriteLine($"started cwebp with pid {process.Id}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"failed to webp compress: {e}");
                    }
                }
                else
                {
                    Console.WriteLine("did not find cwebp");
                }

                Diffuser.SendMessage(
                    $"{request.Nick}: your diffusion for prompt \"{request.Prompt.Substring(0, Math.Min(20, request.Prompt.Length))}{(request.Prompt.Length > 20 ? "..." : "")}\" with seed {seed}: https://{diffusionHost}/{shortIdNumeric} ({sw.Elapsed.TotalSeconds:0.00}s{(ganFailed ? ", GAN upscaling failed, sorry!" : "")})",
                    request.Source);
                LastRequests[(request.Source, request.Nick)] = request;
                Results[request.Id] = resultObjects;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Diffuser.SendMessage($"{request.Nick}: Exception thrown: {e.Message}", request.Source);
            }
        }
        
        public static string ConditionalPlural(double val, string noun)
        {
            int c = (int)val;

            if (c == 1)
                return c.ToString() + " " + noun;

            return c.ToString() + " " + noun + "s";
        }

        public static string NiceString(TimeSpan span)
        {
            if (span.TotalDays > 1)
                return ConditionalPlural(span.TotalDays, "day");

            if (span.TotalHours > 1)
                return ConditionalPlural(span.TotalHours, "hour");

            if (span.TotalMinutes > 1)
                return ConditionalPlural(span.TotalMinutes, "minute");

            if (span.TotalSeconds > 1)
                return ConditionalPlural(span.TotalSeconds, "second");

            return span.ToString();
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

        public TextToImageRequest Clone()
        {
            return new TextToImageRequest()
            {
                Id = Id,
                Timestamp = Timestamp,
                Source = Source,
                Nick = Nick,
                Prompt = Prompt,
                Parameters = new Dictionary<string, string>(Parameters)
            };
        }
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