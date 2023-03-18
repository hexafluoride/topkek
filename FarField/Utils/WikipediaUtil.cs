using System;
using System.Linq;
using System.Net;
using System.Web;
using HtmlAgilityPack;

namespace FarField
{
    public partial class FarField
    {
        private HtmlWeb HtmlWeb = new HtmlWeb();
        
        public string FetchWikipediaSummaryFromPageId(string pageId)
        {
            try
            {
                HtmlWeb.PreRequest = (req) =>
                {
                    return req.AllowAutoRedirect = true;
                };
                
                var body = HtmlWeb.Load($"https://en.wikipedia.org/w/index.php?search={WebUtility.UrlEncode(pageId)}&title=Special:Search&go=Go");
                var articleBody = body.GetElementbyId("mw-content-text").FirstChild;
                var firstParagraph = articleBody.SelectNodes("p").FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.InnerText));
                var text = HttpUtility.HtmlDecode(firstParagraph.InnerText);
        
                var isDisambiguation = body.GetElementbyId("disambigbox") != null;

                if (isDisambiguation)
                {
                    var options = articleBody.Descendants().Where(n => n.OriginalName == "li");

                    options = options.Where(opt =>
                    {
                        var parent = opt.ParentNode;
                        while (parent is not null)
                        {
                            if (parent.Id == "toc")
                                return false;
                            parent = parent.ParentNode;
                        }

                        return true;
                    });
                    
                    if (text.TrimEnd().EndsWith(":"))
                    {
                        text = text.TrimEnd().TrimEnd(':');
                    }
            
                    text +=
                        $" {string.Join("; ", options.Where(o => !string.IsNullOrWhiteSpace(o.InnerText)).Select(o => HttpUtility.HtmlDecode(o.InnerText)))}";
                }

                if (text.Length > 3072)
                    text = text.Substring(0, 3069) + "...";
                return text;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return null;
            }
        }
    }
}