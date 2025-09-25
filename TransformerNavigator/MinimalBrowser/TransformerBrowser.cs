using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
        private string _lastLlmRaw = null;
        private string _lastLlmJsonCandidate = null;
        private string _lastLlmParseError = null;
        private string _lastLlmDebugDumpFile = null;
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
        private IChatClient CreateRepairChatClient(string languageCode)
        {
            string langName = languageCode == "fi" ? "Finnish" : "English";
            // Korjaaja: ei luo uutta sisältöä, vaan korjaa syntaksin ja sulkee rakenteet
            string system = "You are a strict JSON repair engine. Fix invalid or truncated JSON to a single valid JSON object only. Do not invent new content beyond closing open strings and tags. Maintain language and fields.";
            var model = string.IsNullOrWhiteSpace(_settings.OpenAI?.PrimaryModel) ? "gpt-4o-mini" : _settings.OpenAI.PrimaryModel;
            return CreateChatClient(model, system);
        }
        private async Task<JsonDocument> TryRepairJsonWithLlmAsync(string brokenJson, string languageCode)
        {
            try
            {
                var client = CreateRepairChatClient(languageCode);

                // Tiukka ohjeistus: sama skeema, minimöity, ei selitteitä
                string schema = "{\"title\":\"\",\"welcome\":\"\",\"tags\":[{\"label\":\"\",\"icon\":\"\"}],\"links\":[{\"title\":\"\",\"summary\":\"\",\"route\":\"\"}],\"articleHtml\":\"\"}";
                string final = $"Return only minified JSON for schema {schema}. No code fences, no comments, no extra fields.";
                client.AddUserMessage(
                    "Repair the following truncated or invalid JSON to a single syntactically valid JSON object. " +
                    "Keep the content; only complete open strings (especially articleHtml) and close tags and braces. " +
                    "Ensure articleHtml uses only <h2>,<h3>,<p>,<ul>,<ol>,<li>,<a> and is properly closed. " +
                    "Output strictly in the original language. JSON below between <json> tags:\n<json>\n" +
                    brokenJson + "\n</json>");
                client.SetFinalInstructionMessage(final);

                string repaired = await client.GetChatCompletionAsync();
                string candidate = StripCodeFence(repaired);
                return JsonDocument.Parse(candidate);
            }
            catch
            {
                return null;
            }
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
                // 1) Kick off a quick teaser; time-box to avoid blocking the spinner
                string fallbackQuip = languageCode == "fi"
                    ? "Kootaan reittejä ja artikkelia – hetki vielä."
                    : "Assembling routes and the article — just a moment.";
                string quip = await WithTimeout(GetLoadingSnippetAsync(route, _lastRoute, languageCode), 1200, fallbackQuip);

                // 2) Show the loading shell immediately
                await ShowLoadingShellAsync(route, languageCode, quip);

                // 3) Fetch the real payload
                var payload = await GetLlmNavigationAsync(route, _lastRoute, languageCode);

                // 4) Build final page (existing logic)
                var builder = new MuiHtmlTemplateBuilder()
                    .SetDocumentTitle(payload.Title ?? DeriveTitleFromRoute(route, languageCode))
                    .SetWelcome(payload.Title ?? DeriveTitleFromRoute(route, languageCode),
                                string.IsNullOrWhiteSpace(payload.Welcome)
                                    ? (languageCode == "fi"
                                        ? "Tutki alla olevia pitkiä kuvailevia reittejä ja syvennä ymmärrystäsi."
                                        : "Explore the descriptive routes below to deepen your understanding.")
                                    : payload.Welcome)
                    .SetArticleHtml(payload.ArticleHtml)
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
                            ex.Message);

            // DIAG: näytä LLM‑debug‑sisältö artikkelina
            builder.SetArticleHtml(BuildLlmDebugHtml(lang, ex));

            builder
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

            // Nollaa debug‑muisti
            _lastLlmRaw = null;
            _lastLlmJsonCandidate = null;
            _lastLlmParseError = null;
            _lastLlmDebugDumpFile = null;

            // 1) Hae vastaus
            string raw = await client.GetChatCompletionAsync();
            string json = StripCodeFence(raw);

            _lastLlmRaw = raw;
            _lastLlmJsonCandidate = json;

            // 2) Perusparse
            try
            {
                using var doc = JsonDocument.Parse(json);
                return ParseLlmPayload(doc, languageCode);
            }
            catch (Exception ex)
            {
                _lastLlmParseError = ex.Message;
                System.Diagnostics.Debug.WriteLine("[Portal] Primary JSON parse failed: " + ex.Message);

                // 3) Pelastus 1: laajin alimerkki (ensimmäisestä { viimeiseen })
                try
                {
                    int s = json.IndexOf('{');
                    int e = json.LastIndexOf('}');
                    if (s >= 0 && e > s)
                    {
                        string sub = json.Substring(s, e - s + 1);
                        using var doc2 = JsonDocument.Parse(sub);
                        System.Diagnostics.Debug.WriteLine("[Portal] Parsed using broad substring salvage.");
                        return ParseLlmPayload(doc2, languageCode);
                    }
                }
                catch (Exception subEx)
                {
                    _lastLlmParseError += " | Broad substring salvage failed: " + subEx.Message;
                }

                // 4) Pelastus 2: tasapainota ensimmäinen JSON‑olio
                try
                {
                    string firstBalanced = TryExtractFirstJsonObject(json);
                    if (!string.IsNullOrEmpty(firstBalanced))
                    {
                        using var doc3 = JsonDocument.Parse(firstBalanced);
                        System.Diagnostics.Debug.WriteLine("[Portal] Parsed using balanced‑braces salvage.");
                        return ParseLlmPayload(doc3, languageCode);
                    }
                }
                catch (Exception balEx)
                {
                    _lastLlmParseError += " | Balanced‑braces salvage failed: " + balEx.Message;
                }

                // 5) Pelastus 3: LLM‑repair (UUSI) — pyydä mallia korjaamaan katkennut/virheellinen JSON
                try
                {
                    var repairedDoc = await TryRepairJsonWithLlmAsync(json, languageCode);
                    if (repairedDoc != null)
                    {
                        System.Diagnostics.Debug.WriteLine("[Portal] Parsed after LLM repair.");
                        return ParseLlmPayload(repairedDoc, languageCode);
                    }
                }
                catch (Exception repEx)
                {
                    _lastLlmParseError += " | LLM repair failed: " + repEx.Message;
                }

                // 6) Kaikki epäonnistui: dumpataan ja virhesivu
                await DumpLlmDebugAsync(route, languageCode, raw, json, _lastLlmParseError);
                throw new InvalidOperationException(
                    $"LLM JSON parse failed. See debug dump: {_lastLlmDebugDumpFile ?? "(no file)"} | Error: {_lastLlmParseError}");
            }
        }

        private static LlmPayload ParseLlmPayload(JsonDocument doc, string languageCode)
        {
            var payload = new LlmPayload
            {
                Title = TryGetString(doc.RootElement, "title"),
                Welcome = TryGetString(doc.RootElement, "welcome"),
                Tags = new List<LlmTag>(),
                Links = new List<LlmLink>(),
                ArticleHtml = TryGetString(doc.RootElement, "articleHtml") // NEW
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
        private async Task<T> WithTimeout<T>(Task<T> task, int millisecondsTimeout, T fallback)
        {
            var completed = await Task.WhenAny(task, Task.Delay(millisecondsTimeout));
            if (completed == task)
            {
                try { return await task; } catch { return fallback; }
            }
            return fallback;
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
                fromConfig = "You craft punchy progress teasers for this portal, strictly in " + langName + ".";
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
                fromConfig = "Deliver exactly six sentences, each ≤ 22 words, separated by newline only.";
            return fromConfig;
        }

        private IChatClient CreateTeaserChatClient(string languageCode)
        {
            string model = string.IsNullOrWhiteSpace(_settings.OpenAI?.TeaserModel)
                ? (string.IsNullOrWhiteSpace(_settings.OpenAI?.PrimaryModel) ? "gpt-4o-mini" : _settings.OpenAI.PrimaryModel)
                : _settings.OpenAI.TeaserModel;
            string system = BuildTeaserSystemPrompt(languageCode);
            return CreateChatClient(model, system);
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
                    $"Destination route: '{destinationRoute}'\nOrigin route: '{originRoute}'\nLanguage: {langName} ({languageCode})\n\nCompose six teaser sentences, each max 22 words, describing high-stakes solution modules, looming decisions, and playful data takeaways. Tone: urgent yet entertaining. Separate with newline only.",
                    tokens);

                client.AddUserMessage(userPrompt);
                client.SetFinalInstructionMessage(BuildTeaserFinalInstruction(languageCode));

                string raw = await client.GetChatCompletionAsync();
                string text = StripCodeFence(raw);
                var lines = (text ?? "").Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (lines.Count > 0) return lines[0];
            }
            catch { }

            return languageCode == "fi"
                ? "Kootaan reittejä ja artikkelia – hetki vielä."
                : "Assembling routes and the article — just a moment.";
        }

        private static string HtmlEscapeLocal(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private async Task ShowLoadingShellAsync(string route, string languageCode, string quip)
        {
            string readableTitle = DeriveTitleFromRoute(route, languageCode);
            string title = languageCode == "fi" ? "Ladataan sivua" : "Loading page";
            string subtitle = languageCode == "fi"
                ? "Luodaan navigaatiokortteja ja artikkelia..."
                : "Generating navigation cards and the article...";

            string html = $@"<!DOCTYPE html>
                <html lang=""{languageCode}"">
                <head>
                <meta charset=""UTF-8"" />
                <title>{HtmlEscapeLocal(readableTitle)} — {HtmlEscapeLocal(title)}</title>
                <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
                <script crossorigin src=""https://unpkg.com/react@18/umd/react.development.js""></script>
                <script crossorigin src=""https://unpkg.com/react-dom@18/umd/react-dom.development.js""></script>
                <script src=""https://unpkg.com/@emotion/react@11.11.1/dist/emotion-react.umd.min.js""></script>
                <script src=""https://unpkg.com/@emotion/styled@11.11.0/dist/emotion-styled.umd.min.js""></script>
                <script src=""https://unpkg.com/@mui/material@5.15.14/umd/material-ui.development.js""></script>
                <link href=""https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:wght,FILL,GRAD@400,0,0"" rel=""stylesheet"" />
                <style>
                  body {{ margin: 0; font-family: Roboto, sans-serif; }}
                  .content {{ padding: 2rem; }}
                  .material-symbols-outlined {{
                    font-variation-settings: 'FILL' 0, 'wght' 400, 'GRAD' 0, 'opsz' 24;
                    vertical-align: middle;
                  }}
                </style>
                </head>
                <body>
                  <main id=""root""></main>
                  <script>
                    const {{ AppBar, Toolbar, Typography, CssBaseline, Container, ThemeProvider, createTheme, Box, LinearProgress, CircularProgress, Grid, Card, CardContent, Skeleton }} = MaterialUI;
                    const theme = createTheme({{ palette: {{ mode: 'dark', primary: {{ main: '#90caf9' }} }} }});
                    function App() {{
                      return (
                        React.createElement(ThemeProvider, {{ theme }},
                          React.createElement(CssBaseline, null),
                          React.createElement(AppBar, {{ position: 'static', color: 'primary' }},
                            React.createElement(Toolbar, {{ sx: {{ gap: 2 }} }},
                              React.createElement(Typography, {{ variant: 'h6', sx: {{ flexGrow: 1 }} }}, '{HtmlEscapeLocal(readableTitle)}'),
                              React.createElement(Box, {{ sx: {{ width: 240 }} }},
                                React.createElement(LinearProgress, {{ color: 'inherit' }})
                              )
                            )
                          ),
                          React.createElement(Container, {{ className: 'content' }},
                            React.createElement(Box, {{ sx: {{ display:'flex', alignItems:'center', gap:2, mb:2 }} }},
                              React.createElement(CircularProgress, {{ size: 22 }}),
                              React.createElement(Typography, {{ variant: 'body1' }}, '{HtmlEscapeLocal(subtitle)}')
                            ),
                            React.createElement(Typography, {{ variant: 'body2', sx: {{ fontStyle:'italic', color:'#9fbce8', mb: 3 }} }}, '{HtmlEscapeLocal(quip)}'),
                            React.createElement(Grid, {{ container: true, spacing: 2 }},
                              Array.from({{ length: 6 }}).map((_, i) =>
                                React.createElement(Grid, {{ item: true, xs:12, sm:6, md:4, key: i }},
                                  React.createElement(Card, {{ sx: {{ borderRadius:3, boxShadow:3 }} }},
                                    React.createElement(CardContent, null,
                                      React.createElement(Skeleton, {{ variant:'text', width:'70%' }}),
                                      React.createElement(Skeleton, {{ variant:'text', width:'90%' }}),
                                      React.createElement(Skeleton, {{ variant:'rectangular', height: 60, sx: {{ mt:1 }} }})
                                    )
                                  )
                                )
                              )
                            )
                          )
                        )
                      );
                    }}
                    const root = ReactDOM.createRoot(document.getElementById('root'));
                    root.render(React.createElement(App, null));
                  </script>
                </body>
                </html>";

            string tempPath = Path.GetTempPath();
            string filePath = Path.Combine(tempPath, $"portal_loading_{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(filePath, html);

            _lastTempFile = filePath;
            _generatedFiles.Add(filePath);

            _ignoreNextNavigation = true;
            webView21.CoreWebView2.Navigate("file:///" + filePath.Replace("\\", "/"));
        }
        private static string SafeTruncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            return s.Length <= max ? s : s.Substring(0, max) + " …[truncated]";
        }

        private string BuildLlmDebugHtml(string languageCode, Exception ex)
        {
            string t1 = languageCode == "fi" ? "Vianmääritys: LLM‑vastauksen JSON‑parsi epäonnistui" : "Diagnostics: LLM response JSON parsing failed";
            string t2 = languageCode == "fi" ? "Virhe" : "Error";
            string t3 = languageCode == "fi" ? "Parserin lisätiedot" : "Parser details";
            string t4 = languageCode == "fi" ? "JSON‑ehdokas (alusta, katkaistu)" : "JSON candidate (start, truncated)";
            string t5 = languageCode == "fi" ? "Raakavastaus (alusta, katkaistu)" : "Raw response (start, truncated)";
            string t6 = languageCode == "fi" ? "Debug‑tiedosto tallennettu" : "Debug file saved";

            string err = System.Net.WebUtility.HtmlEncode(ex?.Message ?? "");
            string perr = System.Net.WebUtility.HtmlEncode(_lastLlmParseError ?? "");
            string cand = System.Net.WebUtility.HtmlEncode(SafeTruncate(_lastLlmJsonCandidate, 4000));
            string raw = System.Net.WebUtility.HtmlEncode(SafeTruncate(_lastLlmRaw, 4000));
            string fileInfo = string.IsNullOrEmpty(_lastLlmDebugDumpFile) ? "" :
                        $"<p>{t6}: <code>{System.Net.WebUtility.HtmlEncode(_lastLlmDebugDumpFile)}</code></p>";

                    return $@"
              <h2>{t1}</h2>
              <p>{t2}: <code>{err}</code></p>
              {(string.IsNullOrEmpty(perr) ? "" : $"<p>{t3}: <code>{perr}</code></p>")}
              <h3>{t4}</h3>
              <pre style=""white-space:pre-wrap"">{cand}</pre>
              <h3>{t5}</h3>
              <pre style=""white-space:pre-wrap"">{raw}</pre>
              {fileInfo}
            ";
        }

        private async Task DumpLlmDebugAsync(string route, string lang, string raw, string jsonCandidate, string parseError)
        {
            try
            {
                string tempPath = Path.GetTempPath();
                string filePath = Path.Combine(tempPath, $"llm_debug_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.txt");
                var sb = new StringBuilder();
                sb.AppendLine($"Route: {route}");
                sb.AppendLine($"Lang:  {lang}");
                sb.AppendLine("---- Parse error ----");
                sb.AppendLine(parseError ?? "(none)");
                sb.AppendLine();
                sb.AppendLine("---- JSON candidate (after StripCodeFence) ----");
                sb.AppendLine(jsonCandidate ?? "(null)");
                sb.AppendLine();
                sb.AppendLine("---- RAW ----");
                sb.AppendLine(raw ?? "(null)");
                await File.WriteAllTextAsync(filePath, sb.ToString());
                _lastLlmDebugDumpFile = filePath;
                System.Diagnostics.Debug.WriteLine($"[Portal] LLM debug dumped to: {filePath}");
            }
            catch
            {
                _lastLlmDebugDumpFile = null;
            }
        }

        // Yrittää poimia ensimmäisen tasapainoisen JSON-olion tekstistä, ohittaen merkkijonot ja escapet
        private static string TryExtractFirstJsonObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int start = text.IndexOf('{');
            if (start < 0) return null;

            bool inStr = false;
            bool esc = false;
            int depth = 0;

            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr)
                {
                    if (esc) { esc = false; }
                    else if (c == '\\') { esc = true; }
                    else if (c == '"') { inStr = false; }
                }
                else
                {
                    if (c == '"') { inStr = true; }
                    else if (c == '{') { depth++; }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                            return text.Substring(start, i - start + 1);
                    }
                }
            }
            return null;
        }
        // Data contracts for LLM payload
        private sealed class LlmPayload
        {
            public string Title { get; set; }
            public string Welcome { get; set; }
            public List<LlmTag> Tags { get; set; } = new();
            public List<LlmLink> Links { get; set; } = new();
            public string ArticleHtml { get; set; } // NEW
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