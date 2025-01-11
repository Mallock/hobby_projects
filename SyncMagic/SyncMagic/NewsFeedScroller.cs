using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;

public class NewsItem
{
    public string Title { get; set; }
    public string Summary { get; set; }
    public string Source { get; set; }   // News source    
    public Bitmap Image { get; set; }    // Image associated with the news item    
    public int ImageHeight { get; set; } // Height of the scaled image    
    public string ImageUrl { get; set; } // URL of the image to download when needed    
    public bool IsVisible { get; set; }  // Indicates if the item is currently visible    
}

public class NewsFeedScroller : IDisposable
{
    private List<string> _rssFeedUrls = new List<string>()
    {
        "https://feeds.yle.fi/uutiset/v1/majorHeadlines/YLE_UUTISET.rss",  
        // ... (other RSS feed URLs)  
        "https://old.reddit.com/.rss?feed=04f02faad8a2c21c846c054a78182734a7e61017&user=Mallock78"
    };

    private List<NewsItem> _newsItems = new List<NewsItem>();
    private DateTime _lastNewsFetchTime = DateTime.MinValue;
    private Random _random = new Random();

    // Scrolling parameters    
    private int _scrollPosition; // Current scroll position    
    private int _textHeight;     // Total height of the text content    
    private Bitmap _bitmap;
    private Font _fontTitle;
    private Font _fontSummary;
    private Font _fontSource;    // Font for the source text    
    private Brush _brush;
    private Pen _penSeparator;   // Pen for drawing separators  
    private int _width;
    private int _height;
    private int _scrollSpeed;    // Speed of scrolling in pixels per update    

    // Constants for layout    
    private const int paddingTop = 0;
    private const int paddingBottom = 0;
    private const int spacingBetweenItems = 10;
    private const int separatorHeight = 2;

    public NewsFeedScroller()
    {
        _width = 240;   // Width of the bitmap    
        _height = 240;  // Height of the bitmap    
        _bitmap = new Bitmap(_width, _height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        // Fonts for the news text    
        _fontTitle = new Font("Verdana", 18, FontStyle.Bold);          // Font for title    
        _fontSummary = new Font("Verdana", 18, FontStyle.Regular);     // Font for summary    
        _fontSource = new Font("Segoe UI", 14, FontStyle.Italic);       // Font for source (italic)    

        _brush = new SolidBrush(Color.White);    // Brush color for the text    
        _penSeparator = new Pen(Color.Cyan);    // Pen color for separators  
        _scrollSpeed = 18;         // Adjusted scroll speed for smoother scrolling    

        // Fetch news data initially    
        GetNewsData();

        // Calculate total text height based on the news items (using estimated image heights)    
        _textHeight = CalculateTextHeight();
        _scrollPosition = 0;       // Initialize scroll position    
    }

    private void GetNewsData()
    {
        try
        {
            // Check if the cached data is still valid (less than 1 hour old)      
            if (_newsItems != null && _newsItems.Count > 0 && (DateTime.Now - _lastNewsFetchTime).TotalMinutes < 60)
            {
                // Using cached news data      
                return;
            }

            // Dispose images in old news items      
            foreach (var item in _newsItems)
            {
                item.Image?.Dispose();
            }

            _newsItems.Clear();
            HashSet<string> headlinesSet = new HashSet<string>(); // To avoid duplicates      

            foreach (var url in _rssFeedUrls)
            {
                try
                {
                    // Fetch and parse the RSS feed      
                    using (XmlReader reader = XmlReader.Create(url))
                    {
                        SyndicationFeed feed = SyndicationFeed.Load(reader);

                        string feedSource = feed.Title.Text.Trim(); // Get the feed's title as the source      

                        foreach (var item in feed.Items)
                        {
                            string headline = item.Title.Text.Trim();
                            string summary = item.Summary != null ? item.Summary.Text.Trim() : "";

                            // Get image URL if available      
                            string imageUrl = GetImageUrlFromItem(item);

                            if (headlinesSet.Add(headline))
                            {
                                _newsItems.Add(new NewsItem
                                {
                                    Title = headline,
                                    Summary = summary,
                                    Source = feedSource,
                                    ImageUrl = imageUrl
                                });
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Error fetching from this URL, continue with next feed      
                }
            }

            if (_newsItems.Count > 0)
            {
                // Rearrange the news items to avoid consecutive items from the same source    

                // Group items by source into tempItemsBySource    
                var tempItemsBySource = new Dictionary<string, List<NewsItem>>();
                foreach (var item in _newsItems)
                {
                    if (!tempItemsBySource.ContainsKey(item.Source))
                    {
                        tempItemsBySource[item.Source] = new List<NewsItem>();
                    }
                    tempItemsBySource[item.Source].Add(item);
                }

                // Shuffle items within each source and convert to Queues    
                var itemsBySource = new Dictionary<string, Queue<NewsItem>>();
                foreach (var kvp in tempItemsBySource)
                {
                    var source = kvp.Key;
                    var itemList = kvp.Value.OrderBy(x => _random.Next()).ToList();
                    itemsBySource[source] = new Queue<NewsItem>(itemList);
                }

                var rearrangedNewsItems = new List<NewsItem>();
                string lastSource = null;

                while (rearrangedNewsItems.Count < _newsItems.Count)
                {
                    // Get list of available sources with remaining items    
                    var availableSources = itemsBySource.Keys.ToList();

                    // Exclude lastSource if possible    
                    var possibleSources = availableSources.Where(s => s != lastSource).ToList();

                    if (possibleSources.Count == 0)
                    {
                        // No choice but to pick from the same source    
                        possibleSources = availableSources;
                    }

                    // Randomly select a source from possibleSources    
                    var randomIndex = _random.Next(possibleSources.Count);
                    var currentSource = possibleSources[randomIndex];

                    // Dequeue an item from selected source    
                    var item = itemsBySource[currentSource].Dequeue();
                    rearrangedNewsItems.Add(item);
                    lastSource = currentSource;

                    // Remove the source from itemsBySource if no items left    
                    if (itemsBySource[currentSource].Count == 0)
                    {
                        itemsBySource.Remove(currentSource);
                    }
                }

                // Replace _newsItems with rearranged list    
                _newsItems = rearrangedNewsItems;

                _lastNewsFetchTime = DateTime.Now;
                // Update text height based on new headlines (using estimated image heights)      
                _textHeight = CalculateTextHeight();
                _scrollPosition = 0;
            }
            else
            {
                throw new Exception("No news items found in the feeds.");
            }
        }
        catch (Exception)
        {
            // Handle errors (e.g., network issues, parsing errors)      
            _newsItems = new List<NewsItem>();
        }
    }

    private string GetImageUrlFromItem(SyndicationItem item)
    {
        string url = null;
        // Try to get the image URL from Media thumbnails    
        foreach (var extension in item.ElementExtensions)
        {
            try
            {
                var xmlElement = extension.GetObject<XmlElement>();
                if ((xmlElement.LocalName == "thumbnail" || xmlElement.LocalName == "content") && xmlElement.NamespaceURI.Contains("media"))
                {
                    url = xmlElement.GetAttribute("url");
                    if (!string.IsNullOrEmpty(url))
                    {
                        return url;
                    }
                }
            }
            catch
            {
                // Ignore and continue    
            }
        }

        // Try to get the image URL from Enclosures    
        if (item.Links != null)
        {
            foreach (var link in item.Links)
            {
                if (link.RelationshipType == "enclosure" && link.MediaType != null && link.MediaType.StartsWith("image"))
                {
                    return link.Uri.ToString();
                }
            }
        }

        // Try to get the image URL from Links    
        if (item.Links != null)
        {
            foreach (var link in item.Links)
            {
                if (link.MediaType != null && link.MediaType.StartsWith("image"))
                {
                    return link.Uri.ToString();
                }
            }
        }

        return null;
    }

    private int CalculateTextHeight()
    {
        int totalHeight = paddingTop + paddingBottom;
        using (Graphics g = Graphics.FromImage(_bitmap))
        {
            // Set up string format for wrapping    
            StringFormat stringFormat = new StringFormat();
            stringFormat.Alignment = StringAlignment.Near;
            stringFormat.LineAlignment = StringAlignment.Near;
            stringFormat.Trimming = StringTrimming.Word;

            foreach (var item in _newsItems)
            {
                int itemHeight = 0;

                if (!string.IsNullOrEmpty(item.ImageUrl))
                {
                    // Estimate default image height (e.g., 100 pixels)    
                    item.ImageHeight = 100;
                    itemHeight += item.ImageHeight;
                    itemHeight += spacingBetweenItems; // Some spacing after image    
                }
                else
                {
                    item.ImageHeight = 0;
                }

                // Measure the height of the title (allow wrapping)    
                SizeF titleSize = g.MeasureString(item.Title, _fontTitle, new SizeF(_width, float.MaxValue), stringFormat);

                // Measure the height of the summary (allow wrapping)    
                SizeF summarySize = g.MeasureString(item.Summary, _fontSummary, new SizeF(_width, float.MaxValue), stringFormat);

                // Measure the height of the source (allow wrapping)    
                SizeF sourceSize = g.MeasureString(item.Source, _fontSource, new SizeF(_width, float.MaxValue), stringFormat);

                itemHeight += (int)(titleSize.Height + summarySize.Height + sourceSize.Height);
                itemHeight += separatorHeight;
                itemHeight += spacingBetweenItems;

                totalHeight += itemHeight;
            }
        }
        return totalHeight;
    }

    public Bitmap GetNewsFeed()
    {
        // Before drawing, check if we need to refresh the news data    
        if ((DateTime.Now - _lastNewsFetchTime).TotalMinutes >= 60)
        {
            // Time to refresh the news headlines    
            GetNewsData();
        }

        using (Graphics g = Graphics.FromImage(_bitmap))
        {
            // Clear the bitmap with transparent background    
            g.Clear(Color.Transparent);

            // Set text rendering to anti-alias for smoother text    
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // Set up string format for wrapping    
            StringFormat stringFormat = new StringFormat();
            stringFormat.Alignment = StringAlignment.Near;
            stringFormat.LineAlignment = StringAlignment.Near;
            stringFormat.Trimming = StringTrimming.Word;

            // Calculate the starting Y position for the text based on scroll position    
            int yOffset = _height - _scrollPosition + paddingTop;

            foreach (var item in _newsItems)
            {
                int itemYOffset = yOffset;
                int itemHeight = 0;

                // Estimate item height using estimated or actual image height    
                int estimatedImageHeight = 100;

                if (item.Image != null)
                {
                    itemHeight += item.ImageHeight;
                    itemHeight += spacingBetweenItems; // Some spacing after image    
                }
                else if (!string.IsNullOrEmpty(item.ImageUrl))
                {
                    itemHeight += estimatedImageHeight;
                    itemHeight += spacingBetweenItems; // Some spacing after image    
                }

                // Measure the height of the title (allow wrapping)    
                SizeF titleSize = g.MeasureString(item.Title, _fontTitle, new SizeF(_width, float.MaxValue), stringFormat);

                // Measure the height of the summary (allow wrapping)    
                SizeF summarySize = g.MeasureString(item.Summary, _fontSummary, new SizeF(_width, float.MaxValue), stringFormat);

                // Measure the height of the source (allow wrapping)    
                SizeF sourceSize = g.MeasureString(item.Source, _fontSource, new SizeF(_width, float.MaxValue), stringFormat);

                itemHeight += (int)(titleSize.Height + summarySize.Height + sourceSize.Height);
                itemHeight += separatorHeight;
                itemHeight += spacingBetweenItems;

                int itemTop = itemYOffset;
                int itemBottom = itemYOffset + itemHeight;

                bool isCurrentlyVisible = itemBottom > 0 && itemTop < _height;

                if (isCurrentlyVisible && !item.IsVisible)
                {
                    item.IsVisible = true;

                    if (item.Image == null && !string.IsNullOrEmpty(item.ImageUrl))
                    {
                        item.Image = DownloadImage(item.ImageUrl);
                        if (item.Image != null)
                        {
                            // Scale image to fit width    
                            float aspectRatio = (float)item.Image.Height / item.Image.Width;
                            item.ImageHeight = (int)(_width * aspectRatio);

                            // Adjust itemHeight with actual image height    
                            int heightDifference = item.ImageHeight - estimatedImageHeight;
                            itemHeight += heightDifference;
                        }
                        else
                        {
                            // If image failed to load, set height to zero    
                            item.ImageHeight = 0;
                        }
                    }
                }
                else if (!isCurrentlyVisible && item.IsVisible)
                {
                    item.IsVisible = false;

                    if (item.Image != null)
                    {
                        item.Image.Dispose();
                        item.Image = null;
                        item.ImageHeight = 0;
                    }
                }

                // Drawing the item if it is visible    
                if (itemBottom > 0 && itemTop < _height)
                {
                    int currentYOffset = itemYOffset;

                    // Draw image if available    
                    if (item.Image != null)
                    {
                        // Scale image to fit width    
                        int scaledWidth = _width;
                        int scaledHeight = item.ImageHeight;

                        Rectangle destRect = new Rectangle(0, currentYOffset, scaledWidth, scaledHeight);

                        // Only draw if within visible bounds    
                        if (destRect.Bottom > 0 && destRect.Top < _height)
                        {
                            g.DrawImage(item.Image, destRect);
                        }

                        currentYOffset += scaledHeight;
                        currentYOffset += spacingBetweenItems; // Some spacing after image    
                    }

                    // Draw title    
                    if (currentYOffset + titleSize.Height > 0 && currentYOffset < _height)
                    {
                        g.DrawString(item.Title, _fontTitle, _brush, new RectangleF(0, currentYOffset, _width, titleSize.Height), stringFormat);
                    }
                    currentYOffset += (int)titleSize.Height;

                    // Draw summary    
                    if (currentYOffset + summarySize.Height > 0 && currentYOffset < _height)
                    {
                        g.DrawString(item.Summary, _fontSummary, _brush, new RectangleF(0, currentYOffset, _width, summarySize.Height), stringFormat);
                    }
                    currentYOffset += (int)summarySize.Height;

                    // Draw source    
                    if (currentYOffset + sourceSize.Height > 0 && currentYOffset < _height)
                    {
                        g.DrawString(item.Source, _fontSource, _brush, new RectangleF(0, currentYOffset, _width, sourceSize.Height), stringFormat);
                    }
                    currentYOffset += (int)sourceSize.Height;

                    // Draw separator line    
                    if (currentYOffset >= 0 && currentYOffset <= _height)
                    {
                        g.DrawLine(_penSeparator, 0, currentYOffset, _width, currentYOffset);
                    }

                    currentYOffset += separatorHeight;
                    currentYOffset += spacingBetweenItems;
                }

                // Update yOffset for next item    
                yOffset += itemHeight;
            }

            // Update the scroll position for the next frame    
            _scrollPosition += _scrollSpeed;

            // Reset scroll position if we've scrolled all the text off the screen    
            if (_scrollPosition > _textHeight + _height)
            {
                _scrollPosition = 0;
            }
        }

        // Return a copy of the bitmap to prevent external modifications    
        return (Bitmap)_bitmap.Clone();
    }

    private Bitmap DownloadImage(string url)
    {
        try
        {
            using (var webClient = new WebClient())
            {
                byte[] imageBytes = webClient.DownloadData(url);
                using (var ms = new System.IO.MemoryStream(imageBytes))
                {
                    return (Bitmap)Image.FromStream(ms);
                }
            }
        }
        catch
        {
            // Ignore any exceptions during image download    
            return null;
        }
    }

    public void Dispose()
    {
        // Dispose of fonts, brushes, pens, and bitmap to release resources    
        _fontTitle.Dispose();
        _fontSummary.Dispose();
        _fontSource.Dispose();
        _brush.Dispose();
        _penSeparator.Dispose();
        _bitmap.Dispose();

        // Dispose images in news items    
        foreach (var item in _newsItems)
        {
            item.Image?.Dispose();
        }
    }
}