using System.Drawing;
using System.Drawing.Drawing2D;

namespace MinimalBrowser
{
    public partial class WebBrowser : Form
    {
        public WebBrowser()
        {
            InitializeComponent();

            this.FormBorderStyle = FormBorderStyle.None;

            backButton.Click += (s, e) =>
            {
                if (webView21.CanGoBack)
                    webView21.GoBack();
            };

            forwardButton.Click += (s, e) =>
            {
                if (webView21.CanGoForward)
                    webView21.GoForward();
            };

            goButton.Click += (s, e) => NavigateToAddressBar();
            addressBar.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    NavigateToAddressBar();
                }
            };

            webView21.NavigationCompleted += (s, e) =>
            {
                addressBar.Text = webView21.Source?.ToString() ?? "";
            };
        }

        private void NavigateToAddressBar()
        {
            var url = addressBar.Text.Trim();
            if (!string.IsNullOrEmpty(url))
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    url = "https://" + url;
                try
                {
                    webView21.Source = new Uri(url);
                }
                catch
                {
                    // Optionally show error
                }
            }
        }

        private void WebBrowser_Activated(object sender, EventArgs e)
        {
            this.FormBorderStyle = FormBorderStyle.Sizable;
        }

        private void WebBrowser_Deactivate(object sender, EventArgs e)
        {

            this.FormBorderStyle = FormBorderStyle.None;

        }
    }
}