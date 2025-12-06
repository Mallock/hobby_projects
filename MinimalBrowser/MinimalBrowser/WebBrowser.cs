using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Globalization;
using System.Linq;

namespace MinimalBrowser
{
    public partial class WebBrowser : Form
    {
        private ListBox suggestionsListBox;
        private bool isUserTyping = false;

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
                isUserTyping = true;
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    NavigateToAddressBar();
                }
            };

            webView21.NavigationCompleted += (s, e) =>
            {
                isUserTyping = false; // Prevent suggestions on programmatic change
                addressBar.Text = webView21.Source?.ToString() ?? "";
            };

            // Initialize suggestionsListBox
            suggestionsListBox = new ListBox
            {
                Visible = false,
                Height = 100,
                Width = addressBar.Width,
                Left = addressBar.Left,
                Top = addressBar.Bottom + 2
            };
            suggestionsListBox.Click += SuggestionsListBox_Click;
            this.Controls.Add(suggestionsListBox);
            suggestionsListBox.BringToFront();

            addressBar.LocationChanged += (s, e) => UpdateSuggestionsListBoxPosition();
            addressBar.SizeChanged += (s, e) => UpdateSuggestionsListBoxPosition();

            addressBar.Click += (s, e) => addressBar.SelectAll();

            // Wire up TextChanged event for addressBar
            addressBar.TextChanged += addressBar_TextChanged;

            // Keyboard navigation for addressBar and suggestionsListBox
            addressBar.PreviewKeyDown += AddressBar_PreviewKeyDown;
            suggestionsListBox.PreviewKeyDown += SuggestionsListBox_PreviewKeyDown;
            suggestionsListBox.KeyDown += SuggestionsListBox_KeyDown;
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

            // Show navigation controls
            backButton.Visible = true;
            forwardButton.Visible = true;
            goButton.Visible = true;
            addressBar.Visible = true;

            // Show the top panel
            topPanel.Visible = true;

            webView21.Dock = DockStyle.Fill;
        }

        private void WebBrowser_Deactivate(object sender, EventArgs e)
        {
            this.FormBorderStyle = FormBorderStyle.None;

            // Hide navigation controls
            backButton.Visible = false;
            forwardButton.Visible = false;
            goButton.Visible = false;
            addressBar.Visible = false;

            // Hide the top panel
            topPanel.Visible = false;

            webView21.Dock = DockStyle.Fill;
        }

        private async void addressBar_TextChanged(object sender, EventArgs e)
        {
            if (!isUserTyping)
            {
                suggestionsListBox.Visible = false;
                return;
            }

            var query = addressBar.Text.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                var suggestions = await GetSearchSuggestionsAsync(query);
                suggestionsListBox.Items.Clear();
                suggestionsListBox.Items.AddRange(suggestions);
                suggestionsListBox.Visible = suggestions.Any();
            }
            else
            {
                suggestionsListBox.Visible = false;
            }
        }

        // Handle suggestion selection
        private void SuggestionsListBox_Click(object sender, EventArgs e)
        {
            if (suggestionsListBox.SelectedItem != null)
            {
                string suggestion = suggestionsListBox.SelectedItem.ToString();
                addressBar.Text = suggestion;
                suggestionsListBox.Visible = false;

                // Navigate to Google search results for the suggestion
                string googleSearchUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(suggestion)}";
                try
                {
                    webView21.Source = new Uri(googleSearchUrl);
                }
                catch
                {
                    // Optionally show error
                }
            }
        }

        private async Task<string[]> GetSearchSuggestionsAsync(string query)
        {
            try
            {
                var url = $"http://www.google.com/complete/search?output=toolbar&q={Uri.EscapeDataString(query)}&hl={CultureInfo.CurrentCulture.TwoLetterISOLanguageName}";
                using var client = new HttpClient();
                var xml = await client.GetStringAsync(url);
                var doc = XDocument.Parse(xml);
                return doc.Descendants("suggestion")
                          .Select(x => x.Attribute("data")?.Value)
                          .Where(x => !string.IsNullOrEmpty(x))
                          .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private void UpdateSuggestionsListBoxPosition()
        {
            suggestionsListBox.Left = addressBar.Left;
            suggestionsListBox.Top = addressBar.Bottom + 2;
            suggestionsListBox.Width = addressBar.Width;
        }

        private void AddressBar_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (suggestionsListBox.Visible && e.KeyCode == Keys.Down && suggestionsListBox.Items.Count > 0)
            {
                suggestionsListBox.Focus();
                suggestionsListBox.SelectedIndex = 0;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                suggestionsListBox.Visible = false;
            }
        }

        private void SuggestionsListBox_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Up && suggestionsListBox.SelectedIndex == 0)
            {
                addressBar.Focus();
            }
        }

        private void SuggestionsListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && suggestionsListBox.SelectedItem != null)
            {
                SuggestionsListBox_Click(sender, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                suggestionsListBox.Visible = false;
                addressBar.Focus();
            }
        }

        // Optionally, reset the flag when the address bar loses focus
        private void addressBar_Leave(object sender, EventArgs e)
        {
            isUserTyping = false;
        }
    }
}