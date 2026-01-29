using System.Drawing.Imaging;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

public class ImageUploader
{
    // Simpler HttpClient to match device quirks
    private static readonly HttpClient client = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    static ImageUploader()
    {
        // Ensure legacy stacks also skip Expect: 100-Continue
        ServicePointManager.Expect100Continue = false;
    }

    // Simple capability model
    public class DeviceCapabilities
    {
        public string Model { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool SupportsJpg { get; set; } = true;
        public bool SupportsGif { get; set; } = true;
        public Size RequiredGifSizeImage { get; set; } = new Size(240, 240);
        public Size RequiredGifSizeWeather { get; set; } = new Size(80, 80);
        public string UploadEndpoint { get; set; } = "/doUpload?dir=/image";
        public string WeatherGifDir { get; set; } = "/gif";
        public string ImageDir { get; set; } = "/image";
        public string RebootEndpoint { get; set; } = "/set?reboot=1";
        public bool Wifi24GOnly { get; set; } = true;
        public int? FreeSpaceGifKB { get; set; }
        public int? FreeSpaceImageKB { get; set; }
    }

    public async Task<DeviceCapabilities> ProbeDeviceAsync(string ipAddress, CancellationToken ct = default)
    {
        var caps = new DeviceCapabilities();
        if (string.IsNullOrWhiteSpace(ipAddress)) return caps;

        string Base(string path) => $"http://{ipAddress}{path}";

        try
        {
            var html = await client.GetStringAsync(Base("/"));
            // Example: Model: HelloCubic-Lite,Version: V7.0.16
            var m = Regex.Match(html, @"Model:\s*([^,<]+).*?Version:\s*([^\s<]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (m.Success)
            {
                caps.Model = m.Groups[1].Value.Trim();
                caps.Version = m.Groups[2].Value.Trim();
            }
        }
        catch { /* ignore */ }

        try
        {
            var weather = await client.GetStringAsync(Base("/weather.html"));
            // Example: Free Space:815KB, Current Path: /gif
            var fm = Regex.Match(weather, @"Free\s*Space:(\d+)KB.*?/gif", RegexOptions.IgnoreCase);
            if (fm.Success)
            {
                caps.FreeSpaceGifKB = int.TryParse(fm.Groups[1].Value, out var v) ? v : null;
            }
        }
        catch { /* ignore */ }

        try
        {
            var image = await client.GetStringAsync(Base("/image.html"));
            // Example: Free Space:1087KB, Current path:/image
            var fm = Regex.Match(image, @"Free\s*Space:(\d+)KB.*?/image", RegexOptions.IgnoreCase);
            if (fm.Success)
            {
                caps.FreeSpaceImageKB = int.TryParse(fm.Groups[1].Value, out var v) ? v : null;
            }
        }
        catch { /* ignore */ }

        return caps;
    }

    public async Task UploadImageAsync(PictureBox picBox, TextBox txtIPAddress, int retryCount = 0)
    {
        if (picBox.Image == null)
        {
            MessageBox.Show("No image to upload.");
            return;
        }

        if (string.IsNullOrEmpty(txtIPAddress.Text))
        {
            MessageBox.Show("IP address is empty.");
            return;
        }

        if (retryCount > 1)
        {
            //MessageBox.Show("Maximum retry attempts reached.");
            return;
        }

        string ipAddress = txtIPAddress.Text.Trim();
        string url = $"http://{ipAddress}/doUpload?dir=/image";

        // Set required headers
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.ExpectContinue = false; // reduce handshake delay
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        // Add other necessary headers  

        var boundary = "----WebKitFormBoundary" + DateTime.Now.Ticks.ToString("x");
        var imageData = GetMultipartFormData(boundary, picBox);

        var content = new ByteArrayContent(imageData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/form-data");
        content.Headers.ContentType.Parameters.Add(new System.Net.Http.Headers.NameValueHeaderValue("boundary", boundary));

        try
        {
            var response = await client.PostAsync(url, content);
            //var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                //MessageBox.Show("Image uploaded successfully!");
            }
            else
            {
                //MessageBox.Show($"Failed to upload image. Status code: {response.StatusCode}\nResponse: {responseContent}");
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Handle timeout exception  
            //MessageBox.Show("Upload timed out. Rebooting the device and retrying...");

            // Reboot the device  
            await RebootDeviceAsync(ipAddress);

            // Wait for 10 seconds  
            await Task.Delay(10000);

            // Retry the upload  
            await UploadImageAsync(picBox, txtIPAddress, retryCount + 1);
        }
        catch (Exception ex)
        {
            //MessageBox.Show($"Error uploading image: {ex.Message}");
        }
    }
    public async Task UploadGifAsync(TextBox txtIPAddress, string filePath, int retryCount = 0)
    {
        if (string.IsNullOrEmpty(txtIPAddress.Text))
        {
            MessageBox.Show("IP address is empty.");
            return;
        }

        if (retryCount > 1)
        {
            // Maximum retries reached  
            return;
        }

        string ipAddress = txtIPAddress.Text.Trim();
        string url = $"http://{ipAddress}/doUpload?dir=/image";

        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.ExpectContinue = false;
        client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

        var boundary = "----WebKitFormBoundary" + DateTime.Now.Ticks.ToString("x");

        if (!File.Exists(filePath))
        {
            // Handle missing file  
            return;
        }

        byte[] fileData = File.ReadAllBytes(filePath);
        string contentType = GetContentType(filePath);
        string fileName = Path.GetFileName(filePath);

        var multipartData = GetMultipartFormDataGif(boundary, fileData, contentType, fileName);

        var content = new ByteArrayContent(multipartData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/form-data");
        content.Headers.ContentType.Parameters.Add(new System.Net.Http.Headers.NameValueHeaderValue("boundary", boundary));

        try
        {
            var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Error");
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            Console.WriteLine("Error");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error");
        }
    }
    private string GetContentType(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLower();
        switch (extension)
        {
            case ".jpg":
            case ".jpeg":
                return "image/jpeg";
            case ".gif":
                return "image/gif";
            case ".png":
                return "image/png";
            default:
                return "application/octet-stream";
        }
    }
    private async Task RebootDeviceAsync(string ipAddress)
    {
        string rebootUrl = $"http://{ipAddress}/set?reboot=1";
        try
        {
            var rebootResponse = await client.GetAsync(rebootUrl);
            if (rebootResponse.IsSuccessStatusCode)
            {
                //MessageBox.Show("Device reboot command sent successfully.");
            }
            else
            {
                //MessageBox.Show($"Failed to send reboot command. Status code: {rebootResponse.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            //MessageBox.Show($"Error sending reboot command: {ex.Message}");
        }
    }
    private byte[] GetMultipartFormDataGif(string boundary, byte[] fileData, string contentType, string fileName)
    {
        using (var memStream = new MemoryStream())
        {
            // Use ASCII/no BOM to avoid corrupting multipart boundaries
            var writer = new StreamWriter(memStream, Encoding.ASCII, 1024, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };

            // Write boundary and form-data  
            writer.Write("--" + boundary + "\r\n");
            writer.Write("Content-Disposition: form-data; name=\"dir\"\r\n\r\n");
            writer.Write("/image\r\n");
            writer.Write("--" + boundary + "\r\n");
            writer.Write("Content-Disposition: form-data; name=\"file\"; filename=\"screen.gif\"\r\n");
            // Use the actual content type (image/gif expected by the device)
            writer.Write($"Content-Type: {contentType}\r\n\r\n");

            // Write file data  
            memStream.Write(fileData, 0, fileData.Length);

            // Write closing boundary  
            writer.Write("\r\n--" + boundary + "--\r\n");

            return memStream.ToArray();
        }
    }
    private byte[] GetMultipartFormData(string boundary, PictureBox picBox)
    {
        using (var memStream = new System.IO.MemoryStream())
        using (var writer = new System.IO.StreamWriter(memStream, Encoding.ASCII, 1024, leaveOpen: true))
        {
            writer.NewLine = "\r\n";
            writer.AutoFlush = true;
            // Write boundary  
            writer.Write("--" + boundary + "\r\n");
            // Write dir field  
            writer.Write("Content-Disposition: form-data; name=\"dir\"\r\n\r\n");
            writer.Write("/image\r\n");

            // Write boundary  
            writer.Write("--" + boundary + "\r\n");
            // Write file field headers  
            writer.Write("Content-Disposition: form-data; name=\"file\"; filename=\"screen.jpg\"\r\n");
            writer.Write("Content-Type: image/jpeg\r\n\r\n");
            // writer flushes automatically via AutoFlush

            // Save the current position of the stream  
            long imageStartPosition = memStream.Position;

            // Write image bytes  
            picBox.Image.Save(memStream, ImageFormat.Jpeg);
            writer.Write("\r\n");
            writer.Write("--" + boundary + "--\r\n");

            return memStream.ToArray();
        }
    }
}