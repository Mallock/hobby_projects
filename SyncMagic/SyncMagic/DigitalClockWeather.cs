using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.ServiceModel.Syndication;
using System.Xml;

public class DigitalClockWeather
{
    // Add private fields for caching weather data    
    private WeatherData _cachedWeatherData = null;
    private DateTime _lastWeatherFetchTime = DateTime.MinValue;

    // Add private fields for caching news data    
    private List<string> _newsHeadlines = new List<string>();
    private DateTime _lastNewsFetchTime = DateTime.MinValue;
    private int _currentNewsIndex = 0;
    private float _newsScrollingOffset = 0f;
    private DateTime _lastNewsScrollUpdateTime = DateTime.Now;

    public Bitmap GetClock()
    {
        int width = 240;
        int height = 240;

        Bitmap bmp = new Bitmap(width, height);
        try
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                // Set high-quality rendering settings    
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                // Fill background    
                g.Clear(Color.Transparent);

                // Draw digital clock    
                DrawClock(g, width, height);

                // Draw weather info    
                DrawWeather(g, width, height);

                // Draw news feed    
                DrawNews(g, width, height);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetClock: {ex.Message}");
            // Optionally, draw an error message on the bitmap    
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                using (Font errorFont = SystemFonts.DefaultFont)
                using (Brush errorBrush = new SolidBrush(Color.Red))
                {
                    string errorMessage = "An error occurred.";
                    SizeF errorSize = g.MeasureString(errorMessage, errorFont);
                    float x = (width - errorSize.Width) / 2;
                    float y = (height - errorSize.Height) / 2;
                    g.DrawString(errorMessage, errorFont, errorBrush, x, y);
                }
            }
        }

        return bmp;
    }

    private void DrawClock(Graphics g, int width, int height)
    {
        try
        {
            // Get current time    
            DateTime now = DateTime.Now;
            // Ensure time uses ':' as separators    
            string timeString = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            // Get current date    
            string dateString = now.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);

            // Ensure that the time string is valid    
            if (string.IsNullOrEmpty(timeString))
                throw new ArgumentException("Time string is null or empty.");

            // Determine maximum font size for time    
            float maxTimeFontSize = 100f;
            float minTimeFontSize = 10f;
            Font timeFont = null;
            SizeF timeSize = SizeF.Empty;

            while (maxTimeFontSize - minTimeFontSize > 0.5f)
            {
                float currentFontSize = (maxTimeFontSize + minTimeFontSize) / 2f;
                using (Font testFont = new Font(FontFamily.GenericSansSerif, currentFontSize, FontStyle.Bold))
                {
                    SizeF testSize = g.MeasureString(timeString, testFont);
                    if (testSize.Width > width - 20 || testSize.Height > height / 3)
                    {
                        maxTimeFontSize = currentFontSize;
                    }
                    else
                    {
                        minTimeFontSize = currentFontSize;
                        timeFont = (Font)testFont.Clone();
                        timeSize = testSize;
                    }
                }
            }

            // Determine maximum font size for date    
            float maxDateFontSize = 50f;
            float minDateFontSize = 10f;
            Font dateFont = null;
            SizeF dateSize = SizeF.Empty;

            while (maxDateFontSize - minDateFontSize > 0.5f)
            {
                float currentFontSize = (maxDateFontSize + minDateFontSize) / 2f;
                using (Font testFont = new Font(FontFamily.GenericSansSerif, currentFontSize, FontStyle.Regular))
                {
                    SizeF testSize = g.MeasureString(dateString, testFont);
                    if (testSize.Width > width - 20 || testSize.Height > height / 6)
                    {
                        maxDateFontSize = currentFontSize;
                    }
                    else
                    {
                        minDateFontSize = currentFontSize;
                        dateFont = (Font)testFont.Clone();
                        dateSize = testSize;
                    }
                }
            }

            // Calculate total height    
            float totalTextHeight = timeSize.Height + dateSize.Height + 5; // 5 pixels gap between time and date  

            // Calculate starting y position    
            float y = 10; // Start 10 pixels from the top  

            // Set brush for time and date    
            using (Brush timeBrush = new SolidBrush(Color.White))
            using (Brush dateBrush = new SolidBrush(Color.LightBlue))
            {
                // Draw time    
                float timeX = (width - timeSize.Width) / 2;
                g.DrawString(timeString, timeFont, timeBrush, timeX, y);

                // Draw date    
                float dateX = (width - dateSize.Width) / 2;
                float dateY = y + timeSize.Height + 5;
                g.DrawString(dateString, dateFont, dateBrush, dateX, dateY);
            }

            // Clean up fonts    
            timeFont.Dispose();
            dateFont.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in DrawClock: {ex.Message}");
            // Optionally, draw an error message    
            using (Font errorFont = SystemFonts.DefaultFont)
            using (Brush errorBrush = new SolidBrush(Color.Red))
            {
                string errorMessage = "Clock display error.";
                SizeF errorSize = g.MeasureString(errorMessage, errorFont);
                float x = (width - errorSize.Width) / 2;
                float y = 10;
                g.DrawString(errorMessage, errorFont, errorBrush, x, y);
            }
        }
    }

    private void DrawWeather(Graphics g, int width, int height)
    {
        try
        {
            // Retrieve weather data (now uses caching)    
            var weatherData = GetWeatherData();

            string weatherString;

            if (weatherData == null)
            {
                // If data retrieval failed, display an error message    
                weatherString = "Weather data not available.";
            }
            else
            {
                // Prepare weather string    
                weatherString = $"{weatherData.Location}{Environment.NewLine}Temperature: {weatherData.Temperature} °C";
            }

            // Ensure that the weather string is valid    
            if (string.IsNullOrEmpty(weatherString))
                throw new ArgumentException("Weather string is null or empty.");

            // Determine maximum font size for weather    
            float maxFontSize = 60f;
            float minFontSize = 8f;
            Font weatherFont = null;
            SizeF weatherSize = SizeF.Empty;

            while (maxFontSize - minFontSize > 0.5f)
            {
                float currentFontSize = (maxFontSize + minFontSize) / 2f;
                using (Font testFont = new Font(FontFamily.GenericSansSerif, currentFontSize, FontStyle.Regular))
                {
                    SizeF testSize = g.MeasureString(weatherString, testFont);
                    if (testSize.Width > width - 20 || testSize.Height > (height / 6))
                    {
                        maxFontSize = currentFontSize;
                    }
                    else
                    {
                        minFontSize = currentFontSize;
                        weatherFont = (Font)testFont.Clone();
                        weatherSize = testSize;
                    }
                }
            }

            // Set brush    
            using (Brush weatherBrush = new SolidBrush(Color.Gold))
            {
                // Position text at the bottom center    
                float x = (width - weatherSize.Width) / 2;
                float y = height - weatherSize.Height - 10;

                // Draw weather information    
                g.DrawString(weatherString, weatherFont, weatherBrush, x, y);
            }

            // Clean up font    
            weatherFont.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in DrawWeather: {ex.Message}");
            // Optionally, draw an error message    
            try
            {
                using (Font errorFont = new Font(FontFamily.GenericSansSerif, 12))
                using (Brush errorBrush = new SolidBrush(Color.Red))
                {
                    string errorMessage = "Weather display error.";
                    SizeF errorSize = g.MeasureString(errorMessage, errorFont);
                    float x = (width - errorSize.Width) / 2;
                    float y = height - errorSize.Height - 10;
                    g.DrawString(errorMessage, errorFont, errorBrush, x, y);
                }
            }
            catch (Exception innerEx)
            {
                Debug.WriteLine($"Error while displaying error message: {innerEx.Message}");
                // As a last resort, we won't attempt to draw anything more.    
            }
        }
    }

    private void DrawNews(Graphics g, int width, int height)
    {
        try
        {
            // Retrieve news data (with caching)    
            GetNewsData(width);

            if (_newsHeadlines == null || _newsHeadlines.Count == 0)
            {
                throw new Exception("No news headlines available.");
            }

            // Update scrolling offset    
            TimeSpan timeSinceLastScroll = DateTime.Now - _lastNewsScrollUpdateTime;
            _lastNewsScrollUpdateTime = DateTime.Now;

            // Pixels to scroll per second    
            float scrollSpeed = 50f; // Adjust scroll speed as needed    
            _newsScrollingOffset -= (float)(scrollSpeed * timeSinceLastScroll.TotalSeconds);

            // Get the current news headline    
            string headline = _newsHeadlines[_currentNewsIndex];

            // Determine maximum font size for news    
            float maxFontSize = 50f;
            float minFontSize = 8f;
            Font newsFont = null;
            SizeF textSize = SizeF.Empty;

            while (maxFontSize - minFontSize > 0.5f)
            {
                float currentFontSize = (maxFontSize + minFontSize) / 2f;
                using (Font testFont = new Font(FontFamily.GenericSansSerif, currentFontSize))
                {
                    SizeF testSize = g.MeasureString(headline, testFont);
                    if (testSize.Height > (height / 6))
                    {
                        maxFontSize = currentFontSize;
                    }
                    else
                    {
                        minFontSize = currentFontSize;
                        newsFont = (Font)testFont.Clone();
                        textSize = testSize;
                    }
                }
            }

            // If the entire text has scrolled off the screen, move to next headline    
            if (_newsScrollingOffset < -textSize.Width)
            {
                _currentNewsIndex = (_currentNewsIndex + 1) % _newsHeadlines.Count;
                headline = _newsHeadlines[_currentNewsIndex];
                _newsScrollingOffset = width;
                // Recalculate textSize for new headline    
                textSize = g.MeasureString(headline, newsFont);
            }

            // Set brush    
            using (Brush newsBrush = new SolidBrush(Color.Yellow))
            {
                // Draw the headline with scrolling effect    
                float y = height / 2 + (height / 4 - textSize.Height) / 2; // Position in the middle lower quarter    

                g.DrawString(headline, newsFont, newsBrush, _newsScrollingOffset, y);
            }

            // Clean up font    
            newsFont.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in DrawNews: {ex.Message}");
            // Optionally, draw an error message    
            using (Font errorFont = new Font(FontFamily.GenericSansSerif, 12))
            using (Brush errorBrush = new SolidBrush(Color.Red))
            {
                string errorMessage = "News display error.";
                SizeF errorSize = g.MeasureString(errorMessage, errorFont);
                float x = (width - errorSize.Width) / 2;
                float y = (height - errorSize.Height) / 2;
                g.DrawString(errorMessage, errorFont, errorBrush, x, y);
            }
        }
    }

    // Updated GetNewsData method signature to accept 'width' parameter  
    private void GetNewsData(int width)
    {
        try
        {
            // Check if the cached data is still valid (less than 1 hour old)    
            if (_newsHeadlines != null && _newsHeadlines.Count > 0 && (DateTime.Now - _lastNewsFetchTime).TotalMinutes < 60)
            {
                Debug.WriteLine("Using cached news data.");
                return;
            }

            // Choose the RSS feed URL (e.g., Main Headlines)    
            string url = "https://feeds.yle.fi/uutiset/v1/majorHeadlines/YLE_UUTISET.rss";

            Debug.WriteLine($"Fetching news data from URL: {url}");

            // Fetch and parse the RSS feed    
            using (XmlReader reader = XmlReader.Create(url))
            {
                SyndicationFeed feed = SyndicationFeed.Load(reader);
                _newsHeadlines.Clear();

                foreach (var item in feed.Items)
                {
                    _newsHeadlines.Add(item.Title.Text);
                }
            }

            if (_newsHeadlines.Count > 0)
            {
                _lastNewsFetchTime = DateTime.Now;
                _currentNewsIndex = 0;
                _newsScrollingOffset = width;
                _lastNewsScrollUpdateTime = DateTime.Now;
                Debug.WriteLine("Updated news data cache.");
            }
            else
            {
                throw new Exception("No news items found in the feed.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetNewsData: {ex.Message}");
            // Handle errors (e.g., network issues, parsing errors)    
            // Optionally set _newsHeadlines to an empty list to avoid repeated fetch attempts    
            _newsHeadlines = new List<string>();
        }
    }

    private WeatherData GetWeatherData()
    {
        try
        {
            // Check if the cached data is still valid (less than 1 hour old)    
            if (_cachedWeatherData != null && (DateTime.Now - _lastWeatherFetchTime).TotalMinutes < 60)
            {
                Debug.WriteLine("Using cached weather data.");
                return _cachedWeatherData;
            }

            // Build API request URL with updated stored query ID and parameters    
            string baseUrl = "https://opendata.fmi.fi/wfs";
            string requestParams = $"service=WFS" +
                                   $"&version=2.0.0" +
                                   $"&request=getFeature" +
                                   $"&storedquery_id=fmi::observations::weather::timevaluepair" +
                                   $"&place=Kangasala" +
                                   $"&parameters=t2m";

            string url = $"{baseUrl}?{requestParams}";

            Debug.WriteLine($"Fetching data from URL: {url}");

            using (WebClient client = new WebClient())
            {
                // Fetch XML data    
                string xmlContent = client.DownloadString(url);

                // Parse XML data    
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xmlContent);

                // Create namespace manager with updated namespaces    
                XmlNamespaceManager nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
                nsmgr.AddNamespace("wfs", "http://www.opengis.net/wfs/2.0");
                nsmgr.AddNamespace("gml", "http://www.opengis.net/gml/3.2");
                nsmgr.AddNamespace("om", "http://www.opengis.net/om/2.0");
                nsmgr.AddNamespace("omso", "http://inspire.ec.europa.eu/schemas/omso/3.0");
                nsmgr.AddNamespace("wml2", "http://www.opengis.net/waterml/2.0");
                nsmgr.AddNamespace("sams", "http://www.opengis.net/samplingSpatial/2.0");

                // Select all PointTimeSeriesObservation nodes    
                XmlNodeList observationNodes = xmlDoc.SelectNodes("//wfs:member/omso:PointTimeSeriesObservation", nsmgr);

                if (observationNodes == null || observationNodes.Count == 0)
                {
                    Debug.WriteLine("No weather data elements found.");
                    return null;
                }

                WeatherData latestData = null;
                DateTime latestTime = DateTime.MinValue;

                // Iterate over observations to find the latest temperature    
                foreach (XmlNode observationNode in observationNodes)
                {
                    XmlNode resultNode = observationNode.SelectSingleNode("om:result", nsmgr);
                    if (resultNode != null)
                    {
                        XmlNodeList measurementTVPNodes = resultNode.SelectNodes(".//wml2:MeasurementTVP", nsmgr);
                        foreach (XmlNode measurementTVPNode in measurementTVPNodes)
                        {
                            string timeStr = measurementTVPNode.SelectSingleNode("wml2:time", nsmgr)?.InnerText.Trim();
                            string valueStr = measurementTVPNode.SelectSingleNode("wml2:value", nsmgr)?.InnerText.Trim();

                            if (DateTime.TryParse(timeStr, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime observationTime))
                            {
                                if (observationTime > latestTime)
                                {
                                    if (double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double temperature))
                                    {
                                        latestData = new WeatherData
                                        {
                                            Temperature = temperature.ToString("0.0", CultureInfo.InvariantCulture),
                                            Location = "Kangasala",
                                            ObservationTime = observationTime
                                        };
                                        latestTime = observationTime;
                                    }
                                }
                            }
                        }
                    }
                }

                if (latestData != null)
                {
                    // Update the cache    
                    _cachedWeatherData = latestData;
                    _lastWeatherFetchTime = DateTime.Now;
                    Debug.WriteLine("Updated weather data cache.");
                    return latestData;
                }
                else
                {
                    Debug.WriteLine("No valid weather observations found.");
                    return null;
                }
            }
        }
        catch (WebException webEx)
        {
            Debug.WriteLine($"WebException in GetWeatherData: {webEx.Message}");
            using (var response = webEx.Response as HttpWebResponse)
            {
                if (response != null)
                {
                    Debug.WriteLine($"HTTP Status Code: {(int)response.StatusCode} {response.StatusCode}");
                    using (var stream = response.GetResponseStream())
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            string responseText = reader.ReadToEnd();
                            Debug.WriteLine($"Response: {responseText}");
                        }
                    }
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetWeatherData: {ex.Message}");
            return null;
        }
    }
    public void DrawSmallClock(Graphics g, Rectangle area)
    {
        try
        {
            // Get current time  
            DateTime now = DateTime.Now;
            string timeString = now.ToString("HH:mm", CultureInfo.InvariantCulture);

            // Get weather data  
            var weatherData = GetWeatherData();
            string weatherString = weatherData != null ? $"{weatherData.Temperature}°C" : "N/A";


            // Calculate available space  
            int padding = 5;
            int availableWidth = area.Width - 2 * padding;
            int availableHeight = area.Height - 2 * padding;

            // Determine font size for time  
            float maxTimeFontSize = availableHeight * 0.6f; // Use 60% of space for time  
            float timeFontSize = FindOptimalFontSize(g, timeString, availableWidth, availableHeight * 0.6f, maxTimeFontSize);

            // Determine font size for weather  
            float maxWeatherFontSize = availableHeight * 0.4f; // Use remaining space for weather  
            float weatherFontSize = FindOptimalFontSize(g, weatherString, availableWidth, availableHeight * 0.4f, maxWeatherFontSize);

            // Create fonts  
            using (Font timeFont = new Font(FontFamily.GenericSansSerif, timeFontSize, FontStyle.Bold))
            using (Font weatherFont = new Font(FontFamily.GenericSansSerif, weatherFontSize))
            {
                // Measure text sizes  
                SizeF timeSize = g.MeasureString(timeString, timeFont);
                SizeF weatherSize = g.MeasureString(weatherString, weatherFont);

                // Draw time  
                float timeX = area.X + padding + (availableWidth - timeSize.Width) / 2;
                float timeY = area.Y + padding;
                using (Brush timeBrush = new SolidBrush(Color.White))
                {
                    g.DrawString(timeString, timeFont, timeBrush, timeX, timeY);
                }

                // Draw weather  
                float weatherX = area.X + padding + (availableWidth - weatherSize.Width) / 2;
                float weatherY = timeY + timeSize.Height;
                using (Brush weatherBrush = new SolidBrush(Color.LightBlue))
                {
                    g.DrawString(weatherString, weatherFont, weatherBrush, weatherX, weatherY);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in DrawSmallClock: {ex.Message}");
            // Optionally draw an error message or handle the exception  
        }
    }

    // Draw a black bottom bar with time on the left and temperature on the right
    public void DrawBottomStatusBar(Graphics g, Rectangle area)
    {
        try
        {
            // Background bar
            using (Brush barBrush = new SolidBrush(Color.Black))
            {
                g.FillRectangle(barBrush, area);
            }

            // Prepare strings
            DateTime now = DateTime.Now;
            string timeString = now.ToString("HH:mm", CultureInfo.InvariantCulture);

            var weatherData = GetWeatherData();
            string tempString = weatherData != null ? $"{weatherData.Temperature}°C" : "N/A";

            // Layout
            int padding = 4; // slightly smaller padding to allow bigger fonts
            int innerX = area.X + padding;
            int innerY = area.Y + padding;
            int innerW = area.Width - padding * 2;
            int innerH = area.Height - padding * 2;

            // Split into left/right halves
            int leftW = innerW / 2 - padding / 2;
            int rightW = innerW - leftW;

            // Determine font sizes to fit height and half widths
            float maxFontByHeight = innerH + 2; // allow a touch larger fonts while still fitting
            float timeFontSize = FindOptimalFontSize(g, timeString, leftW, innerH, maxFontByHeight);
            float tempFontSize = FindOptimalFontSize(g, tempString, rightW, innerH, maxFontByHeight);
            float fontSize = Math.Min(timeFontSize, tempFontSize);

            // Try to grow font size a bit if both strings still fit
            float candidate = fontSize;
            for (int i = 0; i < 3; i++)
            {
                float test = candidate * 1.08f; // 8% growth step
                using (Font testTime = new Font(FontFamily.GenericSansSerif, test, FontStyle.Bold))
                using (Font testTemp = new Font(FontFamily.GenericSansSerif, test, FontStyle.Regular))
                {
                    SizeF t1 = g.MeasureString(timeString, testTime);
                    SizeF t2 = g.MeasureString(tempString, testTemp);
                    bool fitsLeft = t1.Width <= leftW && t1.Height <= innerH;
                    bool fitsRight = t2.Width <= rightW && t2.Height <= innerH;
                    if (fitsLeft && fitsRight)
                    {
                        candidate = test;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            fontSize = candidate;

            using (Font timeFont = new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Bold))
            using (Font tempFont = new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Regular))
            using (Brush timeBrush = new SolidBrush(Color.White))
            using (Brush tempBrush = new SolidBrush(Color.LightBlue))
            {
                // Measure to vertically center
                SizeF timeSize = g.MeasureString(timeString, timeFont);
                SizeF tempSize = g.MeasureString(tempString, tempFont);

                float timeX = innerX;
                float timeY = innerY + (innerH - timeSize.Height) / 2f;
                g.DrawString(timeString, timeFont, timeBrush, timeX, timeY);

                float tempX = area.X + area.Width - padding - tempSize.Width;
                float tempY = innerY + (innerH - tempSize.Height) / 2f;
                g.DrawString(tempString, tempFont, tempBrush, tempX, tempY);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in DrawBottomStatusBar: {ex.Message}");
            // Fail silently to avoid breaking rendering
        }
    }

    private float FindOptimalFontSize(Graphics g, string text, float maxWidth, float maxHeight, float maxFontSize)
    {
        float minFontSize = 5f;
        float optimalFontSize = minFontSize;

        while (maxFontSize - minFontSize > 0.5f)
        {
            float currentFontSize = (maxFontSize + minFontSize) / 2f;
            using (Font testFont = new Font(FontFamily.GenericSansSerif, currentFontSize))
            {
                SizeF testSize = g.MeasureString(text, testFont);
                if (testSize.Width > maxWidth || testSize.Height > maxHeight)
                {
                    maxFontSize = currentFontSize;
                }
                else
                {
                    minFontSize = currentFontSize;
                    optimalFontSize = currentFontSize;
                }
            }
        }
        return optimalFontSize;
    }

    private class WeatherData
    {
        public string Temperature { get; set; }
        public string Location { get; set; }
        public DateTime ObservationTime { get; set; }
    }
}