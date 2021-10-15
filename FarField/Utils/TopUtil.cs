using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace FarField
{
    public partial class FarField
    {
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
                            SendMessage($"{n}, Here's your video: {Upload(filename)}", source);
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
        public string Upload(string path)
        {
            using (var content = new MultipartFormDataContent())
            using (var fileStream = File.OpenRead(path))
            {
                var fileContent = new StreamContent(fileStream);

                fileContent.Headers.Add("Content-Type", "application/octet-stream");
                fileContent.Headers.Add("Content-Disposition",
                    "form-data; name=\"file\"; filename=\"top.mp4\"");
                content.Add(fileContent, "file", "top.mp4");

                var response = uploadClient.PostAsync("https://uguu.se/api.php?d=upload-tool", content).Result;
                var respStr = response.Content.ReadAsStringAsync().Result;
                return respStr;
            }
        }
    }
}