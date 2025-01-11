using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Xml;

public class WeatherDisplay : IDisposable
{
    private List<string> cities;
    private int currentCityIndex;
    private DateTime lastRotationTime;
    private object bitmapLock = new object();
    private Bitmap currentBitmap;

    private Dictionary<string, WeatherData> cityWeatherData;
    private TimeSpan dataFetchInterval = TimeSpan.FromMinutes(30); // Updated to 30 minutes    
    private TimeSpan rotationInterval = TimeSpan.FromSeconds(5);

    public WeatherDisplay(List<string> configuredCities)
    {
        if (configuredCities == null || configuredCities.Count == 0)
            throw new ArgumentException("At least one city must be configured.");

        cities = configuredCities;
        currentCityIndex = 0;
        lastRotationTime = DateTime.Now;

        cityWeatherData = new Dictionary<string, WeatherData>();

        foreach (var city in cities)
        {
            cityWeatherData[city] = new WeatherData
            {
                Temperature = "N/A",
                WindSpeed = "N/A",
                Humidity = "N/A",
                LastFetchedTime = DateTime.MinValue
            };
        }

        // Initialize current bitmap      
        UpdateWeatherBitmap();
    }

    private void UpdateWeatherBitmap()
    {
        string city = cities[currentCityIndex];

        // Check if we need to fetch new data        
        if ((DateTime.Now - cityWeatherData[city].LastFetchedTime) > dataFetchInterval)
        {
            // Fetch new data        
            var newWeatherData = GetWeatherDataForCity(city);

            // Only update if the fetch was successful      
            if (newWeatherData != null)
            {
                cityWeatherData[city] = newWeatherData;
            }
        }

        // Get the weather data to display        
        var weatherData = cityWeatherData[city];

        Bitmap bmp = new Bitmap(240, 240);

        using (Graphics g = Graphics.FromImage(bmp))
        {
            // Fill background          
            g.Clear(Color.Black); // Changed background to black for better contrast    

            // Set font sizes    
            float tempFontSize = 36; // Updated font size for temperature    
            float dataFontSize = 28; // Updated font size for other data    
            float cityFontSize = 22; // Updated font size for city name    

            using (Font tempFont = new Font("Arial", tempFontSize, FontStyle.Bold))
            using (Font dataFont = new Font("Arial", dataFontSize))
            using (Font cityFont = new Font("Arial", cityFontSize, FontStyle.Italic))
            {
                // Draw humidity    
                string humidityText = $"{weatherData.Humidity}%";
                SizeF humiditySize = g.MeasureString(humidityText, dataFont);
                float humidityX = (bmp.Width - humiditySize.Width) / 2; // Centered horizontally  
                g.DrawString(humidityText, dataFont, Brushes.Yellow, new PointF(humidityX, 20));

                // Draw wind speed    
                string windText = $"{weatherData.WindSpeed} m/s";
                SizeF windSize = g.MeasureString(windText, dataFont);
                float windX = (bmp.Width - windSize.Width) / 2; // Centered horizontally  
                g.DrawString(windText, dataFont, Brushes.LightBlue, new PointF(windX, 80));

                // Draw temperature    
                string tempText = $"{weatherData.Temperature}°C";
                SizeF tempSize = g.MeasureString(tempText, tempFont);
                float tempX = (bmp.Width - tempSize.Width) / 2; // Centered horizontally  
                g.DrawString(tempText, tempFont, Brushes.LightGreen, new PointF(tempX, 130));

                // Draw city name at the bottom    
                SizeF citySize = g.MeasureString(city, cityFont);
                float cityX = (bmp.Width - citySize.Width) / 2; // Centered horizontally    
                float cityY = bmp.Height - citySize.Height - 10; // From bottom with padding    
                g.DrawString(city, cityFont, Brushes.White, new PointF(cityX, cityY));
            }
        }

        // Update the current bitmap        
        lock (bitmapLock)
        {
            if (currentBitmap != null)
            {
                currentBitmap.Dispose();
            }
            currentBitmap = bmp;
        }
    }

    private WeatherData GetWeatherDataForCity(string city)
    {
        try
        {
            string url = $"https://opendata.fmi.fi/wfs?service=WFS&" +
                         $"version=2.0.0&request=getFeature&storedquery_id=" +
                         $"fmi::observations::weather::timevaluepair&" +
                         $"place={WebUtility.UrlEncode(city)}&" +
                         $"parameters=t2m,ws_10min,rh";

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(url);

            XmlNamespaceManager nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
            nsmgr.AddNamespace("wfs", "http://www.opengis.net/wfs/2.0");
            nsmgr.AddNamespace("wml2", "http://www.opengis.net/waterml/2.0");
            nsmgr.AddNamespace("om", "http://www.opengis.net/om/2.0");
            nsmgr.AddNamespace("gml", "http://www.opengis.net/gml/3.2");

            WeatherData weatherData = new WeatherData
            {
                Temperature = "N/A",
                WindSpeed = "N/A",
                Humidity = "N/A",
                LastFetchedTime = DateTime.Now
            };

            XmlNodeList timeseriesNodes = xmlDoc.SelectNodes("//wml2:MeasurementTimeseries", nsmgr);

            foreach (XmlNode timeseriesNode in timeseriesNodes)
            {
                string tsID = timeseriesNode.Attributes["gml:id"].Value;

                // Determine parameter based on gml:id  
                string parameter = "";

                if (tsID.Contains("t2m"))
                    parameter = "Temperature";
                else if (tsID.Contains("ws_10min"))
                    parameter = "WindSpeed";
                else if (tsID.Contains("rh"))
                    parameter = "Humidity";
                else
                    continue; // Skip unknown parameters  

                // Get the last MeasurementTVP  
                XmlNodeList measurementNodes = timeseriesNode.SelectNodes("wml2:point/wml2:MeasurementTVP", nsmgr);
                if (measurementNodes != null && measurementNodes.Count > 0)
                {
                    XmlNode lastMeasurementNode = measurementNodes[measurementNodes.Count - 1];
                    string value = lastMeasurementNode["wml2:value"].InnerText;

                    // Update weatherData accordingly  
                    if (parameter == "Temperature")
                        weatherData.Temperature = value;
                    else if (parameter == "WindSpeed")
                        weatherData.WindSpeed = value;
                    else if (parameter == "Humidity")
                        weatherData.Humidity = value;
                }
            }

            return weatherData;
        }
        catch (Exception ex)
        {
            // Log the exception (you can replace this with proper logging)    
            Console.WriteLine($"Error fetching weather data for city {city}: {ex.Message}");

            // Return null to indicate failure so we don't update LastFetchedTime    
            return null;
        }
    }

    public Bitmap GetWeatherBitmap()
    {
        lock (bitmapLock)
        {
            // Check if we need to rotate to the next city      
            if ((DateTime.Now - lastRotationTime) > rotationInterval)
            {
                currentCityIndex = (currentCityIndex + 1) % cities.Count;
                lastRotationTime = DateTime.Now;
            }

            // Update the bitmap (this will check if data needs to be fetched)      
            UpdateWeatherBitmap();

            return (Bitmap)currentBitmap.Clone();
        }
    }

    public void Dispose()
    {
        if (currentBitmap != null)
        {
            currentBitmap.Dispose();
        }
    }
}

public class WeatherData
{
    public string Temperature { get; set; }
    public string WindSpeed { get; set; }
    public string Humidity { get; set; }
    public DateTime LastFetchedTime { get; set; }
}