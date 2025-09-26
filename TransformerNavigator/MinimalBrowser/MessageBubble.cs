using Microsoft.Web.WebView2.WinForms;

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
        try { await ApplyContentAsync(); } catch { }
    }

    private void InitializePage()
    {
        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;

        core.WebMessageReceived += (_, e) =>
        {
            string msg = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(msg)) return;

            if (msg.StartsWith("h:", StringComparison.Ordinal))
            {
                if (int.TryParse(msg.Substring(2), out int h))
                {
                    Height = h + Padding.Vertical + 4;
                }
            }
            else if (msg.StartsWith("w:", StringComparison.Ordinal))
            {
                if (int.TryParse(msg.Substring(2), out int dy))
                {
                    ScrollParentBy(dy);
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
        string js = "window.setMarkdown(`" + EscapeJs(_pendingMarkdown) + "`);";
        await _web.CoreWebView2.ExecuteScriptAsync(js);
    }

    private void ScrollParentBy(int deltaY)
    {
        var sc = FindScrollableContainer();
        if (sc == null) return;

        var vs = sc.VerticalScroll;
        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines <= 0) lines = 3;
        int step = lines * 16;

        int newVal = vs.Value + Math.Sign(deltaY) * step;
        if (newVal < vs.Minimum) newVal = vs.Minimum;
        int max = Math.Max(vs.Minimum, vs.Maximum - vs.LargeChange + 1);
        if (newVal > max) newVal = max;

        try
        {
            vs.Value = newVal;
            sc.Invalidate();
        }
        catch { }
    }

    private ScrollableControl FindScrollableContainer()
    {
        Control c = Parent;
        while (c != null)
        {
            if (c is ScrollableControl sc && sc.AutoScroll)
                return sc;
            c = c.Parent;
        }
        return null;
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
hr { border: 0; border-top: 1px solid #2a3853; margin: 0.75rem 0; }
code { font-family: 'Cascadia Code', Consolas, Menlo, monospace; background: var(--code-bg); border: 1px solid var(--code-br); border-radius: 6px; padding: 0 4px; }
pre { background: var(--code-bg); border: 1px solid var(--code-br); border-radius: 8px; padding: 10px; overflow: auto; }
pre code { background: transparent; border: 0; padding: 0; }
blockquote { margin: 0.5rem 0; padding: 0.2rem 0.8rem; border-left: 3px solid #3a4d6a; color: var(--fg-soft); }
table { border-collapse: collapse; margin: 0.6rem 0; display: block; overflow-x: auto; max-width: 100%; }
thead tr { background: #142032; }
th, td { border: 1px solid #2a3853; padding: 6px 8px; text-align: left; }
tbody tr:nth-child(even) { background: #0d1626; }
img { max-width: 100%; }
";

        const string js = @"
function updateHeight() {
const h = document.documentElement.scrollHeight || document.body.scrollHeight || 0;
if (window.chrome && window.chrome.webview) {
window.chrome.webview.postMessage('h:' + h);
}
}

document.addEventListener('wheel', function(e) {
try {
if (window.chrome && window.chrome.webview) {
window.chrome.webview.postMessage('w:' + (e.deltaY || 0));
}
} catch {}
}, { passive: true });

window.setMarkdown = function(md) {
try {
if (window.marked && window.DOMPurify) {
window.marked.setOptions({ gfm: true, breaks: true, headerIds: false, mangle: false });
const html = window.marked.parse(md ?? '');
const clean = window.DOMPurify.sanitize(html, { USE_PROFILES: { html: true } });
document.getElementById('content').innerHTML = clean;
if (window.hljs) {
document.querySelectorAll('pre code').forEach(el => { try { window.hljs.highlightElement(el); } catch(e){} });
}
} else {
document.getElementById('content').textContent = md ?? '';
}
} catch {
document.getElementById('content').textContent = md ?? '';
}
requestAnimationFrame(updateHeight);
};

new ResizeObserver(updateHeight).observe(document.body);
setTimeout(updateHeight, 0);
";

        return "<!doctype html><html><head><meta charset='utf-8'>" +
               "<meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; img-src data: https:;\">" +
               "<style>" + css + "</style>" +
               "<link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/highlight.js@11/styles/github-dark.min.css'>" +
               "</head><body>" +
               "<div id='content' class='container'></div>" +
               "<script src='https://cdn.jsdelivr.net/npm/marked/marked.min.js'></script>" +
               "<script src='https://cdn.jsdelivr.net/npm/dompurify@3/dist/purify.min.js'></script>" +
               "<script src='https://cdn.jsdelivr.net/npm/highlight.js@11/lib/common.min.js'></script>" +
               "<script>" + js + "</script>" +
               "</body></html>";
    }

    private static int CalcRtbHeight(RichTextBox rtb, string text)
    {
        rtb.Text = text ?? "";
        int len = Math.Max(0, rtb.TextLength - 1);
        int y = rtb.GetPositionFromCharIndex(len).Y;
        return Math.Max(40, y + rtb.Font.Height + 12);
    }
}
