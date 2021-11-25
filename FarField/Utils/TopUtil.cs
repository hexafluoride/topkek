using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace FarField
{
    public partial class FarField
    {
        public bool ObtainingHypnogram = false;

        public void Hypno(string args, string source, string n)
        {
            if (ObtainingHypnogram)
            {
                SendMessage("Please wait your turn in line!", source);
                return;
            }

            if (!CanAct("top.n", source, n))
            {
                SendMessage("You are rate limited.", source);
                return;
            }

            var prompt = args.Substring(".hypno".Length).Trim();

            if (string.IsNullOrWhiteSpace(prompt))
            {
                SendMessage("Provide me with a prompt.", source);
                return;
            }

            ObtainingHypnogram = true;
            
            SendMessage("Please wait for a sec...", source);
            
            var psi = new ProcessStartInfo("./hypnogram.sh", "");
            psi.ArgumentList.Add(prompt);
            //psi.ArgumentList.Add(keyword);
            
            psi.RedirectStandardOutput = true;
            //psi.RedirectStandardError = true;

            var client = new HttpClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://hypnogram.xyz/");

            new Thread(new ThreadStart(delegate
            {
                try
                {
                    var proc = Process.Start(psi);
                    var output = proc.StandardOutput.ReadToEnd();

                    var parsed = JObject.Parse(output);

                    if (parsed.ContainsKey("error_message"))
                    {
                        SendMessage($"{n}, hypnogram.xyz returned error: \"{parsed.Value<string>("error_message")}\"", source);
                        return;
                    }

                    if (!parsed.ContainsKey("image_id"))
                    {
                        SendMessage($"{n}: expected \"image_id\" or \"error_message\" field in the response ({output.Length}) but found neither", source);
                        return;
                    }

                    var try_until = DateTime.Now.AddSeconds(120);
                    var image_data = new byte[0];
                    
                    while (DateTime.Now < try_until)
                    {
                        try
                        {
                            var fetch = client
                                .GetAsync(
                                    $"https://s3.amazonaws.com/hypnogram-images/{parsed.Value<string>("image_id")}.jpg")
                                .Result;

                            if (!fetch.IsSuccessStatusCode)
                                continue;

                            image_data = fetch.Content.ReadAsByteArrayAsync().Result;
                        }
                        catch
                        {
                        }
                        finally
                        {
                            Thread.Sleep(5000);
                        }
                    }

                    if (image_data.Length == 0)
                    {
                        SendMessage($"{n}: expected image data but received none. Possibly timed out. image_id: {parsed.Value<string>("image_id")}", source);
                        return;
                    }
                    
                    //var image_data = Convert.FromBase64String(parsed.Value<string>("image"));
                    var tmp = Path.GetTempFileName();
                    File.WriteAllBytes(tmp, image_data);

                    var filename = new string(prompt.Where(c => char.IsLetter(c)).ToArray()).ToLower();

                    if (string.IsNullOrWhiteSpace(filename))
                        filename = "hypnogram";

                    if (filename.Length > 20)
                        filename = filename.Substring(0, 20);

                    filename += ".jpg";
                    var link = Upload(tmp, filename);

                    SendMessage($"{n}, Your hypnogram for \"{prompt}\": {link}", source);
                }
                catch (Exception ex)
                {
                    SendMessage($"Oops: {ex.Message}", source);
                }
                finally
                {
                    ObtainingHypnogram = false;
                }
            })).Start();
        }
    
        public bool InJob = false;

        public void GetTopN(string args, string source, string n)
        {
            if (InJob)
            {
                SendMessage("Please wait your turn in line!", source);
                return;
            }

            if (!CanAct("top.n", source, n))
            {
                SendMessage("You are rate limited.", source);
                return;
            }

            args = args.Substring(".top".Length).Trim();
            var parts = args.Split(' ');

            if (!int.TryParse(parts[0], out int number) || number < 1 || number > 2000)
            {
                SendMessage("You gave me wrong parameters.", source);
                return;
            }
            
            InJob = true;

            //var number = int.Parse(args.Split(' ')[0]);
            var keyword = args.Substring(number.ToString().Length).Trim();

            var psi = new ProcessStartInfo("/home/kate/cheese/automate-all.sh", "");
            psi.ArgumentList.Add(number.ToString());
            psi.ArgumentList.Add(keyword);
            
            psi.RedirectStandardOutput = true;
            //psi.RedirectStandardError = true;

            SendMessage("I am starting your job... this WILL take a good few minutes", source);

                /*new Thread((ThreadStart) delegate
            {
                
            }).Start();*/
            new Thread(new ThreadStart(delegate
            {
                string last_line = "";
                try
                {
                    var proc = Process.Start(psi);

                    while (!proc.HasExited)
                    {
                        var nextline = proc.StandardOutput.ReadLine();

                        if (nextline.StartsWith("[+]"))
                        {
                            if (number >= 500)
                                SendMessage(nextline, source);

                            last_line = nextline;
                        }
                        else if (nextline.StartsWith("[-]"))
                        {
                            var filename = nextline.Substring(4).Trim();
                            SendMessage($"{n}, Here's your video: {Upload(filename, "top.mp4")}", source);
                            InJob = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    SendMessage($"Something horrible has happened: {ex.Message}", source);
                    
                    if (!string.IsNullOrWhiteSpace(last_line))
                        SendMessage($"Last line: {last_line}", source);
                }
                finally
                {
                    if (InJob)
                    {
                        SendMessage("I don't think that quite worked. Sorry about that!", source);
                    }
                    
                    InJob = false;
                }
            })).Start();
        }

        private HttpClient uploadClient = new();
        public string Upload(string path, string filename)
        {
            using (var content = new MultipartFormDataContent())
            using (var fileStream = File.OpenRead(path))
            {
                var fileContent = new StreamContent(fileStream);

                fileContent.Headers.Add("Content-Type", "application/octet-stream");
                fileContent.Headers.Add("Content-Disposition",
                    $"form-data; name=\"file\"; filename=\"{filename}\"");
                content.Add(fileContent, "file", filename);

                var response = uploadClient.PostAsync("https://uguu.se/api.php?d=upload-tool", content).Result;
                var respStr = response.Content.ReadAsStringAsync().Result;
                return respStr;
            }
        }
    }
}