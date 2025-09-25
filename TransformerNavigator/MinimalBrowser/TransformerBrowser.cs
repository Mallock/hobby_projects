using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using TransformerNavigator;

namespace MinimalBrowser
{
    public partial class TransformerBrowser : Form
    {
        private static readonly Regex LangQueryRegex = new Regex(@"(?<=\?|&)lang=[^&]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private enum ChatProvider { OpenAI, LlamaCpp }

        private readonly PortalSettings _settings;
        private readonly string _defaultLanguage;

        private readonly HashSet<string> _generatedFiles = new(StringComparer.OrdinalIgnoreCase);
        private string _lastTempFile = null;
        private bool _ignoreNextNavigation = false;
        private bool _homePageRendered = false;
        private string _lastRoute = "initial-load";
        private string _pendingRoute = null;

        public TransformerBrowser()
        {
            InitializeComponent();

            var settingsPath = Path.Combine(AppContext.BaseDirectory, "portalsettings.json");
            _settings = PortalSettings.Load(settingsPath);
            _defaultLanguage = string.IsNullOrWhiteSpace(_settings.DefaultLanguage) ? "fi" : _settings.DefaultLanguage;

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
            var model = string.IsNullOrWhiteSpace(_settings.OpenAI?.PrimaryModel) ? "gpt-4o-mini" : _settings.OpenAI.PrimaryModel;
            return CreateChatClient(model, systemPrompt);
        }

        private async void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (_ignoreNextNavigation)
            {
                _ignoreNextNavigation = false;
                return;
            }

            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
                return;

            // First render to home
            if (!_homePageRendered)
            {
                e.Cancel = true;
                _homePageRendered = true;
                await RenderRoute($"/?lang={NormalizeLanguageCode(_defaultLanguage)}");
                return;
            }

            // Internal file navigation from our template: transform to route handling
            if (uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                // If it's a file we created, let it load
                var localPath = Path.GetFullPath(uri.LocalPath);
                if (_generatedFiles.Contains(localPath))
                    return;

                // Otherwise treat file:///.../?lang=xx as a portal route
                e.Cancel = true;
                string route = ExtractPathAndQuery(uri);
                route = StripDrivePrefix(route);
                await RenderRoute(EnsureLangInContext(route, ExtractLang(route)));
                return;
            }

            // External web: open in default browser
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true });
                }
                catch { }
                return;
            }

            // Portal absolute routes: we render ourselves
            if (uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase))
                return;

            // Fallback: treat as a route
            e.Cancel = true;
            string nav = ExtractPathAndQuery(uri);
            nav = StripDrivePrefix(nav);
            await RenderRoute(EnsureLangInContext(nav, ExtractLang(nav)));
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

            _pendingRoute = route;
            try
            {
                var payload = await GetLlmNavigationAsync(route, _lastRoute, languageCode);

                var builder = new MuiHtmlTemplateBuilder()
                    .SetDocumentTitle(payload.Title ?? DeriveTitleFromRoute(route, languageCode))
                    .SetWelcome(payload.Title ?? DeriveTitleFromRoute(route, languageCode),
                                string.IsNullOrWhiteSpace(payload.Welcome)
                                    ? (languageCode == "fi"
                                        ? "Tutki alla olevia pitkiä kuvailevia reittejä ja syvennä ymmärrystäsi."
                                        : "Explore the descriptive routes below to deepen your understanding.")
                                    : payload.Welcome)
                    .AddMenu(languageCode == "fi" ? "Etusivu" : "Home", "home")
                    .AddMenu(languageCode == "fi" ? "Tutki" : "Explore", "explore")
                    .AddMenu(languageCode == "fi" ? "Tietoja" : "About", "info");

                foreach (var tag in payload.Tags ?? new List<LlmTag>())
                {
                    var label = string.IsNullOrWhiteSpace(tag?.Label) ? "" : tag.Label;
                    var icon = string.IsNullOrWhiteSpace(tag?.Icon) ? "label" : tag.Icon;
                    if (!string.IsNullOrWhiteSpace(label))
                        builder.AddTag(label, icon);
                }

                var links = (payload.Links ?? new List<LlmLink>()).ToList();
                if (links.Count == 0)
                {
                    links.Add(new LlmLink
                    {
                        Title = languageCode == "fi" ? "Esimerkkireitti portaalissa" : "Example route in the portal",
                        Summary = languageCode == "fi"
                            ? "Tämä on varareitti, joka näytetään, jos LLM ei tuottanut linkkejä."
                            : "This is a fallback route shown when the LLM returned no links.",
                        Route = "/explore-example"
                    });
                }

                foreach (var link in links)
                {
                    var title = link.Title ?? "";
                    var summary = link.Summary ?? "";
                    var href = string.IsNullOrWhiteSpace(link.Route) ? "" : EnsureLangInContext(StripDrivePrefix(link.Route), languageCode);
                    builder.AddCard(title, summary, href);
                }

                string html = builder.Build();
                if (!string.IsNullOrEmpty(_lastTempFile) && File.Exists(_lastTempFile))
                {
                    try { File.Delete(_lastTempFile); } catch { }
                }

                string tempPath = Path.GetTempPath();
                string filePath = Path.Combine(tempPath, $"portal_{Guid.NewGuid():N}.html");
                await File.WriteAllTextAsync(filePath, html);

                _lastTempFile = filePath;
                _generatedFiles.Add(filePath);

                _ignoreNextNavigation = true;
                webView21.CoreWebView2.Navigate("file:///" + filePath.Replace("\\", "/"));
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

        private async Task RenderErrorFallback(string route, Exception ex)
        {
            string lang = ExtractLang(route);
            var builder = new MuiHtmlTemplateBuilder()
                .SetDocumentTitle(lang == "fi" ? "Virhe portaalissa" : "Portal error")
                .SetWelcome(lang == "fi" ? "Sivun muodostus epäonnistui" : "Page generation failed",
                            ex.Message)
                .AddMenu(lang == "fi" ? "Etusivu" : "Home", "home")
                .AddMenu(lang == "fi" ? "Yritä uudelleen" : "Try again", "refresh");

            builder.AddCard(
                lang == "fi" ? "Palaa etusivulle" : "Return to home",
                lang == "fi" ? "Aloita alusta ja jatka tutkimista." : "Start over and continue exploring.",
                EnsureLangInContext("/", lang)
            );

            string html = builder.Build();
            string tempPath = Path.GetTempPath();
            string filePath = Path.Combine(tempPath, $"portal_err_{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(filePath, html);

            _lastTempFile = filePath;
            _generatedFiles.Add(filePath);

            _ignoreNextNavigation = true;
            webView21.CoreWebView2.Navigate("file:///" + filePath.Replace("\\", "/"));
        }

        // LLM wiring

        private async Task<LlmPayload> GetLlmNavigationAsync(string route, string originRoute, string languageCode)
        {
            var client = CreatePrimaryChatClient(languageCode);

            string userPrompt = BuildUserPrompt(route, originRoute, languageCode);
            client.AddUserMessage(userPrompt);

            string finalInstruction = BuildFinalInstruction(languageCode);
            client.SetFinalInstructionMessage(finalInstruction);

            string raw = await client.GetChatCompletionAsync();
            string json = StripCodeFence(raw);

            try
            {
                using var doc = JsonDocument.Parse(json);
                return ParseLlmPayload(doc, languageCode);
            }
            catch
            {
                // Try to salvage the first {...} block if any
                int s = json.IndexOf('{');
                int e = json.LastIndexOf('}');
                if (s >= 0 && e > s)
                {
                    string sub = json.Substring(s, e - s + 1);
                    using var doc = JsonDocument.Parse(sub);
                    return ParseLlmPayload(doc, languageCode);
                }
                return new LlmPayload(); // fallback handled by caller
            }
        }

        private static LlmPayload ParseLlmPayload(JsonDocument doc, string languageCode)
        {
            var payload = new LlmPayload
            {
                Title = TryGetString(doc.RootElement, "title"),
                Welcome = TryGetString(doc.RootElement, "welcome"),
                Tags = new List<LlmTag>(),
                Links = new List<LlmLink>()
            };

            if (doc.RootElement.TryGetProperty("tags", out var tagsEl))
            {
                if (tagsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tagsEl.EnumerateArray())
                    {
                        if (t.ValueKind == JsonValueKind.String)
                        {
                            payload.Tags.Add(new LlmTag { Label = t.GetString() ?? "", Icon = "label" });
                        }
                        else if (t.ValueKind == JsonValueKind.Object)
                        {
                            payload.Tags.Add(new LlmTag
                            {
                                Label = TryGetString(t, "label") ?? "",
                                Icon = TryGetString(t, "icon") ?? "label"
                            });
                        }
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("links", out var linksEl) && linksEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var l in linksEl.EnumerateArray())
                {
                    if (l.ValueKind != JsonValueKind.Object) continue;
                    var link = new LlmLink
                    {
                        Title = TryGetString(l, "title") ?? "",
                        Summary = TryGetString(l, "summary") ?? "",
                        Route = TryGetString(l, "route") ?? ""
                    };
                    if (!string.IsNullOrWhiteSpace(link.Title) && !string.IsNullOrWhiteSpace(link.Route))
                        payload.Links.Add(link);
                }
            }

            return payload;
        }

        private static string TryGetString(JsonElement el, string name)
            => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : null;

        private string BuildSystemPrompt(string languageCode)
        {
            string langName = languageCode == "fi" ? "Finnish" : "English";

            var tokens = new Dictionary<string, string>
            {
                ["LanguageCode"] = languageCode,
                ["LanguageName"] = langName,
                ["MinLinks"] = "8",
                ["MaxLinks"] = "12",
                ["MinTags"] = "3",
                ["MaxTags"] = "7",
                ["ResponseQualityDirective"] = _settings.Prompts?.ResponseQualityDirective ?? ""
            };

            string fromConfig = TemplateRenderer.Render(_settings.OpenAI?.SystemPrompt ?? "", tokens);

            if (string.IsNullOrWhiteSpace(fromConfig))
            {
                // Fallback default if config is empty
                fromConfig =
                    "You are a navigation planner for a knowledge portal. " +
                    "Only return minified JSON, no code fences or commentary. " +
                    "Top-level schema: {\"title\":string,\"welcome\":string,\"tags\":[{\"label\":string,\"icon\":string}],\"links\":[{\"title\":string,\"summary\":string,\"route\":string}]}. " +
                    "Tag.icon must be a Material Symbols name. " +
                    "Link.route must be an absolute, hyphenated portal path (e.g., \"/energy-policy-outlook\"), not an external URL. " +
                    "Language: " + langName + ". Keep all output strictly in this language. " +
                    ((_settings.Prompts?.ResponseQualityDirective ?? "").Trim());
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
                ["MinLinks"] = "8",
                ["MaxLinks"] = "12",
                ["MinTags"] = "3",
                ["MaxTags"] = "7",
                ["ResponseQualityDirective"] = _settings.Prompts?.ResponseQualityDirective ?? ""
            };

            string fromConfig = TemplateRenderer.Render(_settings.Prompts?.NavigationTemplate ?? "", tokens);

            if (string.IsNullOrWhiteSpace(fromConfig))
            {
                // Fallback default if config is empty
                fromConfig =
                $@"Current route: {route}
                Origin route: {originRoute}
                Language: {langName} ({languageCode})

                Task:

                Propose 8–12 long, descriptive navigation links relevant to this route.

                Each link must have:

                title: concise, descriptive headline.
                summary: 1–2 sentence executive summary.
                route: absolute, hyphenated path starting with '/' (no external URLs).
                Provide a page-level title and a 1-paragraph welcome.

                Provide 3–7 topical tags: {{ ""label"": string, ""icon"": Material-Symbols-name }}.

                Keep everything strictly in {langName}.
                {(_settings.Prompts?.ResponseQualityDirective ?? "").Trim()}";
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
                // Fallback default if config is empty
                fromConfig =
                    "Return only minified JSON for schema " +
                    "{\"title\":\"string\",\"welcome\":\"string\",\"tags\":[{\"label\":\"string\",\"icon\":\"string\"}],\"links\":[{\"title\":\"string\",\"summary\":\"string\",\"route\":\"string\"}]}." +
                    " No code fences, markdown, or commentary.";
            }

            return fromConfig;
        }
        // Utilities

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
            // Preserve absolute-style path: "/foo" + "?a=b"
            var path = uri.AbsolutePath;
            var query = uri.Query ?? "";
            return string.IsNullOrEmpty(query) ? path : path + query;
        }

        private static string StripCodeFence(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            string trimmed = content.Trim();
            if (!trimmed.StartsWith("```")) return trimmed;
            int firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak == -1) return trimmed;
            string withoutFence = trimmed[(firstLineBreak + 1)..];
            int closing = withoutFence.LastIndexOf("```", StringComparison.Ordinal);
            if (closing >= 0) withoutFence = withoutFence[..closing];
            return withoutFence.Trim();
        }

        private string NormalizeLanguageCode(string lang)
        {
            if (string.Equals(lang, "fi", StringComparison.OrdinalIgnoreCase)) return "fi";
            if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase)) return "en";
            return "fi";
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
            string p = route.Split('?')[0];
            if (p == "/" || string.IsNullOrWhiteSpace(p))
                return languageCode == "fi" ? "Portaalin etusivu" : "Portal home";

            var seg = p.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "topic";
            string friendly = seg.Replace('-', ' ').Replace('_', ' ');
            var culture = languageCode == "fi" ? new CultureInfo("fi-FI") : new CultureInfo("en-US");
            return culture.TextInfo.ToTitleCase(friendly);
        }

        // Data contracts for LLM payload
        private sealed class LlmPayload
        {
            public string Title { get; set; }
            public string Welcome { get; set; }
            public List<LlmTag> Tags { get; set; } = new();
            public List<LlmLink> Links { get; set; } = new();
        }
        private sealed class LlmTag
        {
            public string Label { get; set; }
            public string Icon { get; set; }
        }
        private sealed class LlmLink
        {
            public string Title { get; set; }
            public string Summary { get; set; }
            public string Route { get; set; }
        }
    }
}