using System;
using System.Drawing;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MinimalBrowser
{
    internal sealed class MessageBubble : Panel
    {
        private readonly WebView2 _web;
        private readonly bool _isUser;
        private string _pendingMarkdown = "";
        private bool _initialized;
        private bool _pageLoaded;

        public MessageBubble(bool isUser)
        {
            _isUser = isUser;

            DoubleBuffered = true;
            BorderStyle = BorderStyle.FixedSingle;
            Padding = new Padding(8);
            Margin = new Padding(6);
            BackColor = isUser ? Color.FromArgb(0x1e, 0x2b, 0x4a) : Color.FromArgb(0x0b, 0x12, 0x20);

            _web = new WebView2
            {
                AllowExternalDrop = false,
                DefaultBackgroundColor = Color.Transparent,
                Dock = DockStyle.Fill
            };
            Controls.Add(_web);
        }

        protected override async void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_initialized || !IsHandleCreated) return;
            _initialized = true;

            try
            {
                await _web.EnsureCoreWebView2Async();
                InitializePage();
            }
            catch
            {
                FallbackToRtb();
            }
        }

        public async Task SetMarkdownAsync(string markdown)
        {
            _pendingMarkdown = markdown ?? "";
            if (_web.CoreWebView2 == null || !_pageLoaded) return;
            try
            {
                await ApplyContentAsync();
            }
            catch
            {
                // ignore
            }
        }

        private void InitializePage()
        {
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = true;

            core.WebMessageReceived += (_, e) =>
            {
                string msg = e.TryGetWebMessageAsString();
                if (!string.IsNullOrEmpty(msg) && msg.StartsWith("h:", StringComparison.Ordinal))
                {
                    if (int.TryParse(msg.Substring(2), out int h))
                    {
                        Height = h + Padding.Vertical + 4;
                    }
                }
            };

            core.NavigationCompleted += async (_, __) =>
            {
                _pageLoaded = true;
                try { await ApplyContentAsync(); } catch { }
            };

            _web.NavigateToString(BuildPageHtml());
        }

        private async Task ApplyContentAsync()
        {
            string bodyHtml = MarkdownToHtml(_pendingMarkdown);
            string js = "window.setContent(`" + EscapeJs(bodyHtml) + "`);";
            await _web.CoreWebView2.ExecuteScriptAsync(js);
        }

        private void FallbackToRtb()
        {
            Controls.Clear();
            var rtb = new RichTextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = BackColor,
                ForeColor = Color.FromArgb(0xcf, 0xe0, 0xff),
                DetectUrls = true,
                Dock = DockStyle.Fill,
                ScrollBars = RichTextBoxScrollBars.None,
                Font = new Font("Segoe UI", 10f)
            };
            Controls.Add(rtb);
            rtb.Text = _pendingMarkdown ?? "";
            Height = CalcRtbHeight(rtb, rtb.Text);
        }

        private static string EscapeJs(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("`", "\\`").Replace("${", "\\${");
        }

        private string BuildPageHtml()
        {
            const string css = @"
:root {
--fg: #cfe0ff;
--fg-soft: #a9c0e6;
--link: #8ab4ff;
--code-bg: #0a0f1a;
--code-br: #223242;
}
html, body { margin: 0; padding: 0; background: transparent; }
body { color: var(--fg); font-family: 'Segoe UI', SegoeUI, 'Segoe UI Emoji','Segoe UI Symbol', system-ui, sans-serif; font-size: 14px; line-height: 1.5; }
.container { padding: 2px; }
p { margin: 0.45rem 0; }
h1, h2, h3, h4 { margin: 0.8rem 0 0.4rem; color: #e6eeff; font-weight: 600; }
ul, ol { margin: 0.4rem 0 0.4rem 1.3rem; }
a { color: var(--link); text-decoration: none; }
a:hover { text-decoration: underline; }
code { font-family: 'Cascadia Code', Consolas, Menlo, monospace; background: var(--code-bg); border: 1px solid var(--code-br); border-radius: 6px; padding: 0 4px; }
pre { background: var(--code-bg); border: 1px solid var(--code-br); border-radius: 8px; padding: 10px; overflow: auto; }
pre code { background: transparent; border: 0; padding: 0; }
blockquote { margin: 0.5rem 0; padding: 0.2rem 0.8rem; border-left: 3px solid #3a4d6a; color: var(--fg-soft); }
table { border-collapse: collapse; margin: 0.4rem 0; }
th, td { border: 1px solid #2a3853; padding: 6px 8px; }
";

            const string js = @"
function updateHeight() {
const h = document.documentElement.scrollHeight || document.body.scrollHeight || 0;
if (window.chrome && window.chrome.webview) {
window.chrome.webview.postMessage('h:' + h);
}
}
window.setContent = function(html) {
document.getElementById('content').innerHTML = html;
requestAnimationFrame(updateHeight);
};
new ResizeObserver(updateHeight).observe(document.body);
setTimeout(updateHeight, 0);
";

            return "<!doctype html><html><head><meta charset='utf-8'><style>" + css + "</style></head>" +
                   "<body><div id='content' class='container'></div><script>" + js + "</script></body></html>";
        }

        private static int CalcRtbHeight(RichTextBox rtb, string text)
        {
            rtb.Text = text ?? "";
            int len = Math.Max(0, rtb.TextLength - 1);
            int y = rtb.GetPositionFromCharIndex(len).Y;
            return Math.Max(40, y + rtb.Font.Height + 12);
        }

        // Minimal Markdown -> HTML (headings, lists, blockquotes, fenced code, paragraphs)
        private static string MarkdownToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return "";

            var sb = new StringBuilder();
            string md = markdown.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = md.Split('\n');

            bool inCode = false;
            bool inUl = false;
            bool inOl = false;
            bool inPara = false;
            bool inQuote = false;

            void ClosePara()
            {
                if (inPara)
                {
                    sb.Append("</p>");
                    inPara = false;
                }
            }

            void CloseLists()
            {
                if (inUl) { sb.Append("</ul>"); inUl = false; }
                if (inOl) { sb.Append("</ol>"); inOl = false; }
            }

            void CloseQuote()
            {
                if (inQuote) { sb.Append("</blockquote>"); inQuote = false; }
            }

            for (int idx = 0; idx < lines.Length; idx++)
            {
                string raw = lines[idx];
                string line = raw;

                if (!inCode && line.StartsWith("```"))
                {
                    ClosePara(); CloseLists(); CloseQuote();
                    sb.Append("<pre><code>");
                    inCode = true;
                    continue;
                }
                if (inCode)
                {
                    if (line.StartsWith("```"))
                    {
                        sb.Append("</code></pre>");
                        inCode = false;
                    }
                    else
                    {
                        sb.Append(WebUtility.HtmlEncode(line)).Append('\n');
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    ClosePara(); CloseLists(); CloseQuote();
                    continue;
                }

                int i = 0;
                int quotes = 0;
                while (i < line.Length && line[i] == '>')
                {
                    quotes++; i++;
                    if (i < line.Length && line[i] == ' ') i++;
                }
                if (quotes > 0)
                {
                    ClosePara(); CloseLists();
                    if (!inQuote) { sb.Append("<blockquote>"); inQuote = true; }
                    string content = line.Substring(i).TrimEnd();
                    sb.Append("<p>").Append(WebUtility.HtmlEncode(content)).Append("</p>");
                    continue;
                }
                else
                {
                    CloseQuote();
                }

                int h = 0; i = 0;
                while (i < line.Length && line[i] == '#' && h < 6) { h++; i++; }
                if (h > 0 && (i < line.Length && char.IsWhiteSpace(line[i])))
                {
                    ClosePara(); CloseLists(); CloseQuote();
                    string content = line.Substring(i).Trim();
                    sb.Append("<h").Append(h).Append(">")
                      .Append(WebUtility.HtmlEncode(content))
                      .Append("</h").Append(h).Append(">");
                    continue;
                }

                var m = Regex.Match(line, @"^\s*(\d+)\.\s+(.*)");
                if (m.Success)
                {
                    ClosePara(); CloseQuote();
                    if (inUl) { sb.Append("</ul>"); inUl = false; }
                    if (!inOl) { sb.Append("<ol>"); inOl = true; }
                    sb.Append("<li>").Append(WebUtility.HtmlEncode(m.Groups[2].Value.TrimEnd())).Append("</li>");
                    continue;
                }

                m = Regex.Match(line, @"^\s*[-+*]\s+(.*)");
                if (m.Success)
                {
                    ClosePara(); CloseQuote();
                    if (inOl) { sb.Append("</ol>"); inOl = false; }
                    if (!inUl) { sb.Append("<ul>"); inUl = true; }
                    sb.Append("<li>").Append(WebUtility.HtmlEncode(m.Groups[1].Value.TrimEnd())).Append("</li>");
                    continue;
                }

                if (!inPara)
                {
                    CloseLists(); CloseQuote();
                    sb.Append("<p>");
                    inPara = true;
                }
                else
                {
                    sb.Append("<br>");
                }
                sb.Append(WebUtility.HtmlEncode(line.TrimEnd()));
            }

            if (inCode) sb.Append("</code></pre>");
            ClosePara(); CloseLists(); CloseQuote();

            return sb.ToString();
        }
    }
}