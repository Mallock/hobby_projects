using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Collections.Generic;
using System.Net;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using System.Data;

namespace WebPageCrawler
{
    internal class Crawler
    {



        public List<string> CollectUrls(string url, string baseUrl)
        {
            Console.WriteLine("Crawling url: " + url);
            ServicePointManager
        .ServerCertificateValidationCallback +=
        (sender, cert, chain, sslPolicyErrors) => true;
            var web = new HtmlWeb();
            var doc = web.Load(url);
            var nodes = doc.DocumentNode.SelectNodes("//a[@href]");
            var urls = new List<string>();
            var count = 0;
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    
                    var u = node.Attributes["href"].Value;
                    Console.Clear();
                    Console.Write("\r{0}", u);
                    if (u.StartsWith("/") && u != "/")
                    {
                        if(urls.Contains(baseUrl + node.Attributes["href"].Value) == false)
                        {
                            urls.Add(baseUrl + node.Attributes["href"].Value);
                        }
                    }
                    else
                    {
                        if (urls.Contains(node.Attributes["href"].Value) == false)
                        {
                            urls.Add(node.Attributes["href"].Value);
                        }
                    }
                    count++;
                }
            }
            urls.AddRange(CollectSubUrls(urls, baseUrl));
            return urls;
        }

        private List<string> CollectSubUrls(List<string> urls, string baseUrl)
        {
            List<string> foundurls = new List<string>();

            foreach (var url in urls)
            {
                
                var web = new HtmlWeb();
                if (url.StartsWith(baseUrl))
                {
                    //Console.WriteLine("Crawling urls form: " + url);
                    var doc = web.Load(url);
                    var nodes = doc.DocumentNode.SelectNodes("//a[@href]");
                    Console.Clear();
                    Console.Write("\r{0}", url);
                    if (nodes != null)
                    {
                        var count = 0;
                        foreach (var node in nodes)
                        {
                            
                            var u = node.Attributes["href"].Value;
                            if (u.StartsWith("/") && u != "/" && !u.StartsWith("#"))
                            {
                                var nextUrl = url + node.Attributes["href"].Value;
                                if (!nextUrl.Contains(u))
                                {
                                    foundurls.AddRange(CollectUrls(nextUrl, baseUrl));
                                }
                            }
                            else
                            {
                                if (!u.StartsWith(baseUrl) && u != "/" && !u.StartsWith("#"))
                                {
                                    //Console.WriteLine();
                                    //Console.WriteLine("=========================");
                                    //Console.WriteLine("Found url: " + u);
                                    //Console.WriteLine("=========================");
                                }
                                if (!foundurls.Contains(node.Attributes["href"].Value) && !node.Attributes["href"].Value.Contains("?q=") && !node.Attributes["href"].Value.Contains("search?"))
                                {
                                    foundurls.Add(node.Attributes["href"].Value);
                                }
                            }
                            count++;
                        }
                    }
                }
            }

            return foundurls;
        }

       

    }

}
