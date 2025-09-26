using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TransformerNavigator;

namespace MinimalBrowser
{
    public partial class TransformerBrowser : Form
    {
        private static readonly Regex LangQueryRegex = new Regex(@"(?<=\?|&)lang=[^&]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private enum ChatProvider { OpenAI, LlamaCpp }
        private enum Section { None, Title, Welcome, Menu, Tags, Links, Article }

        private readonly PortalSettings _settings;
        private readonly string _defaultLanguage;

        private readonly HashSet<string> _generatedFiles = new(StringComparer.OrdinalIgnoreCase);
        private string _lastTempFile = null;
        private bool _ignoreNextNavigation = false;
        private bool _homePageRendered = false;
        private string _lastRoute = "initial-load";
        private string _pendingRoute = null;

        // Streaming control
        private CancellationTokenSource _routeCts;

        // Non-blocking streaming pipeline
        private ConcurrentQueue<string> _deltaQueue = new ConcurrentQueue<string>();
        private System.Windows.Forms.Timer _flushTimer;
        private readonly StringBuilder _parserBuffer = new StringBuilder();

        // Section routing state (UI thread)
        private Section _currentSection = Section.Welcome;
        private readonly StringBuilder _streamTitleBuffer = new StringBuilder();
        private int _streamMenuIndex = 1;

        public TransformerBrowser()
        {
            InitializeComponent();

            var settingsPath = Path.Combine(AppContext.BaseDirectory, "portalsettings.json");
            _settings = PortalSettings.Load(settingsPath);
            _defaultLanguage = string.IsNullOrWhiteSpace(_settings.DefaultLanguage) ? "en" : _settings.DefaultLanguage;

            this.Load += async (s, e) =>
            {
                await webView21.EnsureCoreWebView2Async();
                webView21.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                webView21.CoreWebView2.Navigate("about:blank");
            };
        }

        private ChatProvider GetProvider()
        {
            var fromEnv = Environment.GetEnvironmentVariable("LLM_PROVIDER");
            if (string.Equals(fromEnv, "llama", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fromEnv, "llamacpp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fromEnv, "llama.cpp", StringComparison.OrdinalIgnoreCase))
                return ChatProvider.LlamaCpp;
            return ChatProvider.OpenAI;
        }

        private IChatClient CreateChatClient(string model, string systemMessage)
        {
            var provider = GetProvider();
            if (provider == ChatProvider.LlamaCpp)
            {
                var baseUrl = Environment.GetEnvironmentVariable("LLAMA_BASE_URL") ?? "http://0.0.0.0:1337";
                var apiKey = Environment.GetEnvironmentVariable("LLAMA_API_KEY") ?? "secret-key-123";
                var temperature = 0.7;
                return new LlamaCppChatClient(model, baseUrl, apiKey, systemMessage, temperature);
            }
            else
            {
                var openAi = new OpenAIChatClient(model: model, systemMessage: systemMessage, temperature: 0.7);
                return new OpenAIChatClientAdapter(openAi);
            }
        }

        private IChatClient CreatePrimaryChatClient(string languageCode)
        {
            string systemPrompt = BuildSystemPrompt(languageCode);
            var model = string.IsNullOrWhiteSpace(_settings.OpenAI?.PrimaryModel) ? "gpt-4.1" : _settings.OpenAI.PrimaryModel;
            return CreateChatClient(model, systemPrompt);
        }

        private void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            CancelAnyInFlightRouteStream();

            if (_ignoreNextNavigation)
            {
                _ignoreNextNavigation = false;
                return;
            }

            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
                return;

            if (!_homePageRendered)
            {
                e.Cancel = true;
                _homePageRendered = true;
                _ = RenderRoute($"/?lang={NormalizeLanguageCode(_defaultLanguage)}");
                return;
            }

            if (uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                var localPath = Path.GetFullPath(uri.LocalPath);
                if (_generatedFiles.Contains(localPath))
                    return;

                e.Cancel = true;
                string route = ExtractPathAndQuery(uri);
                route = StripDrivePrefix(route);
                _ = RenderRoute(EnsureLangInContext(route, ExtractLang(route)));
                return;
            }

            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                try { Process.Start(new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true }); } catch { }
                return;
            }

            if (uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase))
                return;

            e.Cancel = true;
            string nav = ExtractPathAndQuery(uri);
            nav = StripDrivePrefix(nav);
            _ = RenderRoute(EnsureLangInContext(nav, ExtractLang(nav)));
        }

        private async Task RenderRoute(string routeWithLang)
        {
            string languageCode = ExtractLang(routeWithLang);
            string route = StripDrivePrefix(routeWithLang);

            if (string.Equals(route, _lastRoute, StringComparison.OrdinalIgnoreCase))
                return;
            if (!string.IsNullOrEmpty(_pendingRoute) &&
                string.Equals(route, _pendingRoute, StringComparison.OrdinalIgnoreCase))
                return;

            CancelAnyInFlightRouteStream();
            _pendingRoute = route;

            try
            {
                string fallbackQuip = languageCode == "fi"
                    ? "Kootaan sisältöä — hetki vielä."
                    : "Assembling content — just a moment.";
                string quip = await WithTimeout(GetLoadingSnippetAsync(route, _lastRoute, languageCode), 1200, fallbackQuip);

                await ShowStreamingShellAsync(route, languageCode, quip);
                await WaitForShellReadyAsync();

                // Indicate busy and clear live console
                FireAndForget(PortalSetBusyAsync(true));
                FireAndForget(PortalClearRawAsync());

                // Start non-blocking streaming
                _ = StreamNaturalLanguagePageAsync(route, _lastRoute, languageCode);
            }
            catch (Exception ex)
            {
                await RenderErrorFallback(routeWithLang, ex);
            }
            finally
            {
                _lastRoute = route;
                _pendingRoute = null;
            }
        }

        private void CancelAnyInFlightRouteStream()
        {
            try { _routeCts?.Cancel(); } catch { }
            try { _routeCts?.Dispose(); } catch { }
            _routeCts = null;

            if (_flushTimer != null)
            {
                try
                {
                    _flushTimer.Stop();
                    _flushTimer.Tick -= FlushTimer_Tick;
                    _flushTimer.Dispose();
                }
                catch { }
                _flushTimer = null;
            }

            _deltaQueue = new ConcurrentQueue<string>();
            _parserBuffer.Clear();
            _streamTitleBuffer.Clear();
            _streamMenuIndex = 1;
            _currentSection = Section.Welcome;
        }

        // Off-UI streaming + UI timer flush
        private async Task StreamNaturalLanguagePageAsync(string route, string originRoute, string languageCode)
        {
            var client = CreatePrimaryChatClient(languageCode);

            string userPrompt = BuildUserPrompt(route, originRoute, languageCode);
            client.AddUserMessage(userPrompt);

            string finalInstruction = BuildFinalInstruction(languageCode);
            client.SetFinalInstructionMessage(finalInstruction);

            _routeCts = new CancellationTokenSource();

            _parserBuffer.Clear();
            _deltaQueue = new ConcurrentQueue<string>();
            _streamTitleBuffer.Clear();
            _streamMenuIndex = 1;
            _currentSection = Section.Welcome;

            _flushTimer = new System.Windows.Forms.Timer { Interval = 33 };
            _flushTimer.Tick += FlushTimer_Tick;
            _flushTimer.Start();

            Exception streamError = null;

            var streamTask = Task.Run(async () =>
            {
                try
                {
                    await GetCompletionStreamingOrFallbackAsync(
                        client,
                        delta =>
                        {
                            if (!string.IsNullOrEmpty(delta))
                                _deltaQueue.Enqueue(delta);
                        },
                        _routeCts.Token
                    ).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    streamError = ex;
                }
            });

            await Task.Yield();
            await streamTask;

            if (_flushTimer != null)
            {
                try
                {
                    FlushTimer_Tick(this, EventArgs.Empty);
                    _flushTimer.Stop();
                    _flushTimer.Tick -= FlushTimer_Tick;
                    _flushTimer.Dispose();
                }
                catch { }
                _flushTimer = null;
            }

            if (_streamTitleBuffer.Length > 0)
                FireAndForget(PortalSetTitleAsync(_streamTitleBuffer.ToString().Trim()));

            FireAndForget(PortalSetBusyAsync(false));

            if (streamError != null)
                await RenderErrorFallback(route, streamError);
        }

        // UI thread: drain queue and route to placeholders; also show raw chunks every tick
        private void FlushTimer_Tick(object sender, EventArgs e)
        {
            if (webView21?.CoreWebView2 == null) return;

            var tickSb = new StringBuilder();

            while (_deltaQueue.TryDequeue(out var d))
            {
                _parserBuffer.Append(d);
                tickSb.Append(d);
            }

            // Show the exact streamed chunk immediately (raw console)
            if (tickSb.Length > 0)
                FireAndForget(PortalAppendRawAsync(tickSb.ToString()));

            // Process complete lines for placeholders
            while (true)
            {
                int nl = IndexOfNewline(_parserBuffer);
                if (nl < 0) break;
                string line = _parserBuffer.ToString(0, nl);
                _parserBuffer.Remove(0, nl + 1);
                RouteLineUI(line);
            }
        }

        // UI thread: interpret headers and dispatch
        private void RouteLineUI(string rawLine)
        {
            if (rawLine == null) return;
            string t = rawLine.Trim();

            if (t.Length == 0)
            {
                if (_currentSection == Section.Article)
                    FireAndForget(PortalAppendArticleAsync("\n\n"));
                else if (_currentSection == Section.Welcome)
                    FireAndForget(PortalAppendWelcomeAsync("\n"));
                return;
            }

            if (IsHeader(t, out var newSection, out var rest))
            {
                _currentSection = newSection;
                if (!string.IsNullOrWhiteSpace(rest))
                    DispatchToSectionUI(_currentSection, rest);
                return;
            }

            DispatchToSectionUI(_currentSection, t);
        }

        private void DispatchToSectionUI(Section current, string text)
        {
            switch (current)
            {
                case Section.Title:
                    if (_streamTitleBuffer.Length > 0) _streamTitleBuffer.Append(' ');
                    _streamTitleBuffer.Append(text);
                    FireAndForget(PortalSetTitleAsync(_streamTitleBuffer.ToString()));
                    break;

                case Section.Welcome:
                    FireAndForget(PortalAppendWelcomeAsync(text + " "));
                    break;

                case Section.Menu:
                    if (_streamMenuIndex <= 5)
                    {
                        string s = StripBullet(text);
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            int idx = _streamMenuIndex++;
                            FireAndForget(PortalSetMenuAsync(idx, s));
                        }
                    }
                    break;

                case Section.Tags:
                    foreach (var tag in SplitTags(text))
                    {
                        if (!string.IsNullOrWhiteSpace(tag))
                            FireAndForget(PortalAddTagAsync(tag.Trim()));
                    }
                    break;

                case Section.Links:
                    {
                        string linkText = StripBullet(text);
                        if (!string.IsNullOrWhiteSpace(linkText))
                            FireAndForget(PortalAddLinkAsync(linkText));
                        break;
                    }

                case Section.Article:
                default:
                    FireAndForget(PortalAppendArticleAsync(text + "\n"));
                    break;
            }
        }

        private static void FireAndForget(Task t)
        {
            if (t == null) return;
            t.ContinueWith(_ => { }, TaskScheduler.Default);
        }

        private async Task<string> GetCompletionStreamingOrFallbackAsync(IChatClient client, Action<string> onDelta, CancellationToken ct)
        {
            if (client is TransformerNavigator.LlamaCppChatClient llama)
            {
                return await llama.GetChatCompletionStreamingAsync(onDelta, ct).ConfigureAwait(false);
            }
            var text = await client.GetChatCompletionAsync(ct).ConfigureAwait(false);
            try { onDelta?.Invoke(text); } catch { }
            return text;
        }

        private static bool IsHeader(string trimmedLine, out Section section, out string remainder)
        {
            section = Section.None;
            remainder = "";

            string lower = trimmedLine.ToLowerInvariant();

            if (lower.StartsWith("title:"))
            {
                section = Section.Title;
                remainder = trimmedLine.Substring("title:".Length).TrimStart();
                return true;
            }
            if (lower.StartsWith("welcome:"))
            {
                section = Section.Welcome;
                remainder = trimmedLine.Substring("welcome:".Length).TrimStart();
                return true;
            }
            if (lower.StartsWith("menu:"))
            {
                section = Section.Menu;
                remainder = trimmedLine.Substring("menu:".Length).TrimStart();
                return true;
            }
            if (lower.StartsWith("tags:"))
            {
                section = Section.Tags;
                remainder = trimmedLine.Substring("tags:".Length).TrimStart();
                return true;
            }
            if (lower.StartsWith("links:"))
            {
                section = Section.Links;
                remainder = trimmedLine.Substring("links:".Length).TrimStart();
                return true;
            }
            if (lower.StartsWith("article:"))
            {
                section = Section.Article;
                remainder = trimmedLine.Substring("article:".Length).TrimStart();
                return true;
            }

            if (lower.StartsWith("### title") || lower.StartsWith("## title")) { section = Section.Title; return true; }
            if (lower.StartsWith("### welcome") || lower.StartsWith("## welcome")) { section = Section.Welcome; return true; }
            if (lower.StartsWith("### menu") || lower.StartsWith("## menu")) { section = Section.Menu; return true; }
            if (lower.StartsWith("### tags") || lower.StartsWith("## tags")) { section = Section.Tags; return true; }
            if (lower.StartsWith("### links") || lower.StartsWith("## links")) { section = Section.Links; return true; }
            if (lower.StartsWith("### article") || lower.StartsWith("## article")) { section = Section.Article; return true; }

            return false;
        }

        private async Task ShowStreamingShellAsync(string route, string languageCode, string quip)
        {
            string readableTitle = DeriveTitleFromRoute(route, languageCode);
            string title = languageCode == "fi" ? "Ladataan sivua" : "Loading page";
            string subtitle = languageCode == "fi"
                ? "Luodaan sisältöä ja navigointiehdotuksia..."
                : "Generating content and navigation suggestions...";

            string html = $@"<!DOCTYPE html>
<html lang=""{languageCode}"">
<head>
<meta charset=""UTF-8"" />
<title>{HtmlEscapeLocal(readableTitle)} — {HtmlEscapeLocal(title)}</title>
<meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
<script crossorigin src=""https://unpkg.com/react@18/umd/react.development.js""></script>
<script crossorigin src=""https://unpkg.com/react-dom@18/umd/react-dom.development.js""></script>
<script src=""https://unpkg.com/@mui/material@5.15.14/umd/material-ui.development.js""></script>
<link href=""https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:wght,FILL,GRAD@400,0,0"" rel=""stylesheet"" />
<link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/npm/highlight.js@11.9.0/styles/github.min.css"">
<script src=""https://cdn.jsdelivr.net/npm/markdown-it@13.0.1/dist/markdown-it.min.js""></script>
<script src=""https://cdn.jsdelivr.net/npm/dompurify@3.0.3/dist/purify.min.js""></script>
<style>
  body {{ margin: 0; font-family: Roboto, sans-serif; background:#0f1220; color:#e6ecff; }}
  .material-symbols-outlined {{ font-variation-settings:'FILL' 0, 'wght' 400, 'GRAD' 0, 'opsz' 24; }}
  #shell {{ padding: 24px; max-width: 1200px; margin: 0 auto; }}
  #titleRow {{ display:flex; align-items:center; gap: 12px; }}
  #pageTitle {{ font-size: 22px; font-weight: 600; }}
  #menuBar {{ margin-left:auto; display:flex; gap:6px; flex-wrap: wrap; }}
  .menuItem {{ background:#18203a; border:1px solid #263050; color:#c9ddff; border-radius:8px; padding:6px 10px; display:none; }}
  #busy {{ height:4px; background: linear-gradient(90deg,#5aa0ff,#a18bff); animation: load 2s linear infinite; }}
  @keyframes load {{ 0% {{background-position:0 0}} 100% {{background-position: 1200px 0}} }}
  #welcome {{ color:#bcd1ff; margin:10px 0 12px 0; }}
  #tags {{ display:flex; gap:8px; flex-wrap:wrap; margin:10px 0 12px 0; }}
  .tag {{ background:#1a2446; color:#bcd1ff; border:1px solid #2a3761; border-radius:16px; padding:4px 10px; font-size:12px; }}
  #grid {{ display:grid; grid-template-columns: 1fr 2fr; gap:18px; align-items:flex-start; }}
  #linksTitle {{ font-weight:600; margin-bottom:6px; }}
  #linksList {{ list-style: disc; padding-left: 18px; margin:0; }}
  #article {{ background:#0b0f1a; border:1px solid #1f2946; border-radius:8px; padding:14px; min-height:200px; }}
  #quip {{ font-style: italic; color:#8db3ff; margin-bottom: 12px; }}
  #liveWrap {{ margin-top: 14px; background:#0b0f1a; border:1px solid #1f2946; border-radius:8px; padding:10px; }}
  #liveTitle {{ font-size: 12px; color:#9fbce8; margin-bottom:6px; }}
  #live {{ white-space: pre-wrap; font-family: Consolas, 'Courier New', monospace; color:#bcd1ff; max-height:180px; overflow:auto; margin:0; }}
</style>
</head>
<body>
  <div id=""shell"">
    <div id=""titleRow"">
      <div class=""material-symbols-outlined"">hub</div>
      <div id=""pageTitle"">{HtmlEscapeLocal(readableTitle)}</div>
      <div id=""menuBar"">
        <button id=""menu1"" class=""menuItem""></button>
        <button id=""menu2"" class=""menuItem""></button>
        <button id=""menu3"" class=""menuItem""></button>
        <button id=""menu4"" class=""menuItem""></button>
        <button id=""menu5"" class=""menuItem""></button>
      </div>
    </div>
    <div id=""busy""></div>
    <div id=""welcome"">{HtmlEscapeLocal(subtitle)}</div>
    <div id=""quip"">{HtmlEscapeLocal(quip)}</div>
    <div id=""tags""></div>
    <div id=""grid"">
      <div>
        <div id=""linksTitle"">Suggested links</div>
        <ul id=""linksList""></ul>
      </div>
      <div id=""article""></div>
    </div>
    <div id=""liveWrap"">
      <div id=""liveTitle"">Live stream</div>
      <pre id=""live""></pre>
    </div>
  </div>

  <script>
    const md = window.markdownit({{html:false, linkify:true, breaks:true, typographer:true}});
    let articleBuf = '';
    let articleScheduled = false;

    function renderArticle() {{
      articleScheduled = false;
      const dirty = md.render(articleBuf);
      const clean = DOMPurify.sanitize(dirty);
      document.getElementById('article').innerHTML = clean;
    }}

    window.portalSetBusy = function(isBusy) {{
      const b = document.getElementById('busy');
      if (!b) return;
      b.style.display = isBusy ? 'block' : 'none';
    }};

    window.portalSetTitle = function (s) {{
      if (s && s.trim().length>0) {{
        document.title = s.trim();
        const el = document.getElementById('pageTitle');
        if (el) el.textContent = s.trim();
      }}
    }};

    window.portalAppendWelcome = function (s) {{
      const el = document.getElementById('welcome');
      if (!el || !s) return;
      el.textContent = (el.textContent || '') + s;
    }};

    window.portalAddTag = function (s) {{
      const wrap = document.getElementById('tags');
      if (!wrap || !s) return;
      const span = document.createElement('span');
      span.className = 'tag';
      span.textContent = s.trim();
      wrap.appendChild(span);
    }};

    window.portalAddLink = function (s) {{
      const ul = document.getElementById('linksList');
      if (!ul || !s) return;
      const li = document.createElement('li');
      li.textContent = s.trim();
      ul.appendChild(li);
    }};

    window.portalAppendArticle = function (s) {{
      articleBuf += s || '';
      if (!articleScheduled) {{ articleScheduled = true; setTimeout(renderArticle, 40); }}
    }};

    window.portalSetMenu = function (index, s) {{
      const btn = document.getElementById('menu' + index);
      if (!btn) return;
      btn.textContent = (s || '').trim();
      if (btn.textContent.length > 0) btn.style.display = 'inline-block';
    }};

    // Raw live stream (every chunk)
    window.portalClearRaw = function() {{
      const el = document.getElementById('live');
      if (el) el.textContent = '';
    }};
    window.portalAppendRaw = function(s) {{
      const el = document.getElementById('live');
      if (!el || !s) return;
      el.textContent += s;
      el.scrollTop = el.scrollHeight;
    }};

    window.__portalShellReady = true;
  </script>
</body>
</html>";

            string tempPath = Path.GetTempPath();
            string filePath = Path.Combine(tempPath, $"portal_stream_{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(filePath, html);

            _lastTempFile = filePath;
            _generatedFiles.Add(filePath);

            _ignoreNextNavigation = true;
            webView21.CoreWebView2.Navigate("file:///" + filePath.Replace("\\", "/"));
        }

        private async Task WaitForShellReadyAsync(int timeoutMs = 4000)
        {
            if (webView21?.CoreWebView2 == null) return;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    var result = await webView21.CoreWebView2.ExecuteScriptAsync("Boolean(window.__portalShellReady)");
                    if (string.Equals(result?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                        return;
                }
                catch { }
                await Task.Delay(50);
            }
        }

        private static int IndexOfNewline(StringBuilder sb)
        {
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] == '\n') return i;
            }
            return -1;
        }

        private static string StripBullet(string s)
        {
            string t = s.Trim();
            if (t.StartsWith("- ")) return t.Substring(2).TrimStart();
            if (t.StartsWith("• ")) return t.Substring(2).TrimStart();
            if (t.Length >= 3 && char.IsDigit(t[0]) && (t[1] == '.' || t[1] == ')') && t[2] == ' ')
                return t.Substring(3).TrimStart();
            return t;
        }

        private static IEnumerable<string> SplitTags(string line)
        {
            var t = line.Trim();
            if (t.StartsWith("- ") || t.StartsWith("• "))
                return new[] { StripBullet(t) };
            return t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        // WebView2 interop
        private Task ExecAsync(string js)
        {
            if (webView21?.CoreWebView2 == null) return Task.CompletedTask;
            return webView21.CoreWebView2.ExecuteScriptAsync(js);
        }

        private Task PortalSetBusyAsync(bool isBusy)
        {
            var arg = isBusy ? "true" : "false";
            return ExecAsync($"window.portalSetBusy({arg});");
        }

        private Task PortalSetTitleAsync(string text)
        {
            var json = JsonSerializer.Serialize(text ?? "");
            return ExecAsync($"window.portalSetTitle({json});");
        }

        private Task PortalAppendWelcomeAsync(string text)
        {
            var json = JsonSerializer.Serialize(text ?? "");
            return ExecAsync($"window.portalAppendWelcome({json});");
        }

        private Task PortalAddTagAsync(string text)
        {
            var json = JsonSerializer.Serialize(text ?? "");
            return ExecAsync($"window.portalAddTag({json});");
        }

        private Task PortalAddLinkAsync(string text)
        {
            var json = JsonSerializer.Serialize(text ?? "");
            return ExecAsync($"window.portalAddLink({json});");
        }

        private Task PortalAppendArticleAsync(string text)
        {
            var json = JsonSerializer.Serialize(text ?? "");
            return ExecAsync($"window.portalAppendArticle({json});");
        }

        private Task PortalSetMenuAsync(int index, string text)
        {
            var jText = JsonSerializer.Serialize(text ?? "");
            return ExecAsync($"window.portalSetMenu({index}, {jText});");
        }

        private Task PortalClearRawAsync()
        {
            return ExecAsync("window.portalClearRaw && window.portalClearRaw();");
        }

        private Task PortalAppendRawAsync(string text)
        {
            var json = JsonSerializer.Serialize(text ?? "");
            return ExecAsync($"window.portalAppendRaw({json});");
        }

        // Prompts and utilities
        private string BuildSystemPrompt(string languageCode)
        {
            string langName = languageCode == "fi" ? "Finnish" : "English";
            var tokens = new Dictionary<string, string>
            {
                ["LanguageCode"] = languageCode,
                ["LanguageName"] = langName,
                ["ResponseQualityDirective"] = _settings.Prompts?.ResponseQualityDirective ?? ""
            };
            string fromConfig = TemplateRenderer.Render(_settings.OpenAI?.SystemPrompt ?? "", tokens);

            if (string.IsNullOrWhiteSpace(fromConfig))
            {
                fromConfig =
                    "You are a Navigation + Article Planner. Output natural language only, no JSON, no code fences. " +
                    "Use these exact English section headers: Title:, Welcome:, Menu:, Tags:, Links:, Article:. " +
                    "Keep headers in English; write all content strictly in " + langName + ". " +
                    "Tags: 3–7 labels; Links: 8–12 ideas with real datasets/entities; Menu: 3–5 short items; " +
                    "Article: 500–900 words, Markdown allowed (no images).";
            }
            return fromConfig;
        }

        private string BuildUserPrompt(string route, string originRoute, string languageCode)
        {
            string langName = languageCode == "fi" ? "Finnish" : "English";
            var tokens = new Dictionary<string, string>
            {
                ["Route"] = route,
                ["OriginRoute"] = originRoute ?? "",
                ["LanguageCode"] = languageCode,
                ["LanguageName"] = langName,
                ["ResponseQualityDirective"] = _settings.Prompts?.ResponseQualityDirective ?? ""
            };
            string fromConfig = TemplateRenderer.Render(_settings.Prompts?.NavigationTemplate ?? "", tokens);
            if (string.IsNullOrWhiteSpace(fromConfig))
            {
                fromConfig =
                    $"Current route: {route}\nOrigin route: {originRoute}\nLanguage: {langName} ({languageCode})\n\n" +
                    "Produce natural-language sections with these exact English headers:\nTitle:\nWelcome:\nMenu:\nTags:\nLinks:\nArticle:\n" +
                    "No JSON, no code fences. Keep headers in English; write body in " + langName + ".";
            }
            return fromConfig;
        }

        private string BuildFinalInstruction(string languageCode)
        {
            string langName = languageCode == "fi" ? "Finnish" : "English";
            var tokens = new Dictionary<string, string>
            {
                ["LanguageCode"] = languageCode,
                ["LanguageName"] = langName
            };
            string fromConfig = TemplateRenderer.Render(_settings.OpenAI?.FinalInstructionTemplate ?? "", tokens);
            if (string.IsNullOrWhiteSpace(fromConfig))
            {
                fromConfig =
                    "Stream only natural language. Use headers: Title:, Welcome:, Menu:, Tags:, Links:, Article:. " +
                    "Keep headers in English; write body in " + langName + ". No JSON, no code fences.";
            }
            return fromConfig;
        }

        private async Task<string> GetLoadingSnippetAsync(string destinationRoute, string originRoute, string languageCode)
        {
            try
            {
                var client = CreateTeaserChatClient(languageCode);
                string langName = languageCode == "fi" ? "Finnish" : "English";
                var tokens = new Dictionary<string, string>
                {
                    ["DestinationRoute"] = destinationRoute,
                    ["OriginRoute"] = originRoute ?? "",
                    ["LanguageName"] = langName,
                    ["LanguageCode"] = languageCode
                };

                string userPrompt = TemplateRenderer.Render(
                    _settings.Prompts?.LoadingSnippetTemplate ??
                    $"Destination route: '{destinationRoute}'\nOrigin route: '{originRoute}'\nLanguage: {langName} ({languageCode})\n\nCompose six teaser sentences, each ≤ 22 words. Separate with newline.",
                    tokens);

                client.AddUserMessage(userPrompt);
                client.SetFinalInstructionMessage(BuildTeaserFinalInstruction(languageCode));

                string raw = await client.GetChatCompletionAsync();
                var lines = (raw ?? "").Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (lines.Count > 0) return lines[0];
            }
            catch { }

            return languageCode == "fi"
                ? "Kootaan sisältöä — hetki vielä."
                : "Assembling content — just a moment.";
        }

        private IChatClient CreateTeaserChatClient(string languageCode)
        {
            string model = string.IsNullOrWhiteSpace(_settings.OpenAI?.TeaserModel)
                ? (string.IsNullOrWhiteSpace(_settings.OpenAI?.PrimaryModel) ? "gpt-4.1-mini" : _settings.OpenAI.PrimaryModel)
                : _settings.OpenAI.TeaserModel;

            string system = BuildTeaserSystemPrompt(languageCode);
            return CreateChatClient(model, system);
        }

        private string BuildTeaserSystemPrompt(string languageCode)
        {
            string langName = languageCode == "fi" ? "Finnish" : "English";
            var tokens = new Dictionary<string, string>
            {
                ["LanguageCode"] = languageCode,
                ["LanguageName"] = langName
            };
            string fromConfig = TemplateRenderer.Render(_settings.OpenAI?.TeaserSystemPrompt ?? "", tokens);
            if (string.IsNullOrWhiteSpace(fromConfig))
                fromConfig = "You craft punchy teasers, strictly in " + langName + ".";
            return fromConfig;
        }

        private string BuildTeaserFinalInstruction(string languageCode)
        {
            string langName = languageCode == "fi" ? "Finnish" : "English";
            var tokens = new Dictionary<string, string>
            {
                ["LanguageCode"] = languageCode,
                ["LanguageName"] = langName
            };
            string fromConfig = TemplateRenderer.Render(_settings.OpenAI?.TeaserFinalInstructionTemplate ?? "", tokens);
            if (string.IsNullOrWhiteSpace(fromConfig))
                fromConfig = "Produce exactly six sentences, each ≤ 22 words, separated by newline only.";
            return fromConfig;
        }

        private async Task RenderErrorFallback(string route, Exception ex)
        {
            string lang = ExtractLang(route);
            string title = lang == "fi" ? "Portaalin virhe" : "Portal error";
            string sub = lang == "fi" ? "Sivun muodostus epäonnistui" : "Page generation failed";
            string msg = System.Net.WebUtility.HtmlEncode(ex?.Message ?? "(unknown)");

            string html = $@"<!doctype html>
<html lang=""{lang}""><head>
<meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>{title}</title>
<style>
body{{font-family:Segoe UI,Arial,sans-serif;background:#10131f;color:#e6ecff;margin:0}}
main{{max-width:900px;margin:40px auto;padding:0 18px}}
h1{{font-size:22px;margin:0 0 8px 0}}
p,pre{{background:#0b0f1a;border:1px solid #1f2946;border-radius:8px;padding:14px}}
a{{color:#5aa0ff}}
</style></head>
<body>
<main>
  <h1>{title}</h1>
  <p>{sub}</p>
  <pre>{msg}</pre>
  <p><a href=""{EnsureLangInContext("/", lang)}"">Return to start</a></p>
</main>
</body></html>";

            string tempPath = Path.GetTempPath();
            string filePath = Path.Combine(tempPath, $"portal_err_{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(filePath, html);

            _lastTempFile = filePath;
            _generatedFiles.Add(filePath);

            _ignoreNextNavigation = true;
            webView21.CoreWebView2.Navigate("file:///" + filePath.Replace("\\", "/"));
        }

        private static string HtmlEscapeLocal(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return System.Net.WebUtility.HtmlEncode(s);
        }

        // Routing helpers
        private string ExtractLang(string route)
        {
            if (string.IsNullOrWhiteSpace(route)) return NormalizeLanguageCode(_defaultLanguage);
            int q = route.IndexOf('?');
            if (q < 0) return NormalizeLanguageCode(_defaultLanguage);

            var query = route[(q + 1)..];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(kv[0]);
                if (key.Equals("lang", StringComparison.OrdinalIgnoreCase))
                {
                    string value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                    return NormalizeLanguageCode(value);
                }
            }
            return NormalizeLanguageCode(_defaultLanguage);
        }

        private static string ExtractPathAndQuery(Uri uri)
        {
            var path = uri.AbsolutePath;
            var query = uri.Query ?? "";
            return string.IsNullOrEmpty(query) ? path : path + query;
        }

        private string NormalizeLanguageCode(string lang)
        {
            if (string.Equals(lang, "fi", StringComparison.OrdinalIgnoreCase)) return "fi";
            if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase)) return "en";
            return "en";
        }

        private string EnsureLangInContext(string navContext, string languageCode)
        {
            string normalized = string.IsNullOrWhiteSpace(navContext) ? "/" : navContext.Trim();

            int hashIndex = normalized.IndexOf('#');
            string fragment = hashIndex >= 0 ? normalized[hashIndex..] : string.Empty;
            if (hashIndex >= 0)
                normalized = normalized[..hashIndex];

            if (LangQueryRegex.IsMatch(normalized))
                normalized = LangQueryRegex.Replace(normalized, $"lang={languageCode}");
            else
                normalized += normalized.Contains('?') ? $"&lang={languageCode}" : $"?lang={languageCode}";

            normalized = StripDrivePrefix(normalized);
            return normalized + fragment;
        }

        private static string ExtractPath(string route)
        {
            int q = route.IndexOf('?');
            return q >= 0 ? route[..q] : route;
        }

        private string StripDrivePrefix(string route)
        {
            if (string.IsNullOrWhiteSpace(route)) return "/";

            string trimmed = route.Trim();
            int queryIndex = trimmed.IndexOf('?');
            string path = queryIndex >= 0 ? trimmed[..queryIndex] : trimmed;
            string query = queryIndex >= 0 ? trimmed[queryIndex..] : string.Empty;

            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                if (path.Length >= 4 && char.IsLetter(path[1]) && path[2] == ':' && path[3] == '/')
                {
                    path = path[3..];
                }
            }

            if (!path.StartsWith("/", StringComparison.Ordinal))
                path = "/" + path.TrimStart('/');

            while (path.Contains("//", StringComparison.Ordinal))
                path = path.Replace("//", "/");

            return path + query;
        }

        private string DeriveTitleFromRoute(string route, string languageCode)
        {
            string p = ExtractPath(route);
            if (p == "/" || string.IsNullOrWhiteSpace(p))
                return languageCode == "fi" ? "Portaalin etusivu" : "Portal home";

            var seg = p.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "topic";
            string friendly = seg.Replace('-', ' ').Replace('_', ' ');
            var culture = languageCode == "fi" ? new CultureInfo("fi-FI") : new CultureInfo("en-US");
            return culture.TextInfo.ToTitleCase(friendly);
        }

        private async Task<T> WithTimeout<T>(Task<T> task, int millisecondsTimeout, T fallback)
        {
            var completed = await Task.WhenAny(task, Task.Delay(millisecondsTimeout));
            if (completed == task)
            {
                try { return await task; } catch { return fallback; }
            }
            return fallback;
        }
    }
}