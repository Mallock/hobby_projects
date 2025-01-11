// See https://aka.ms/new-console-template for more information
using WebPageCrawler;
using static System.Net.WebRequestMethods;

Console.WriteLine("Crawl!");

var urlColletor = new Crawler();

var foundUrls = new List<string>();
var urlsFrom = "https://dmoz-odp.org/Recreation/Humor/";
foundUrls = urlColletor.CollectUrls(urlsFrom, "https://dmoz-odp.org");

foreach (var foundUrl in foundUrls)
{
    if (!foundUrl.StartsWith("/") && !foundUrl.StartsWith("https://dmoz-odp.org") && foundUrl.Contains(".com"))
    {
        
            Console.WriteLine(foundUrl);
       
    }
}

Console.WriteLine("Completed press enter key to end");
Console.ReadLine();