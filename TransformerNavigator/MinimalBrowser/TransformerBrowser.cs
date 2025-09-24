using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using TransformerNavigator;
using Timer = System.Windows.Forms.Timer;

namespace MinimalBrowser
{
    public partial class TransformerBrowser : Form
    {
        private const string BootstrapUrl = "https://portal-bootstrap/";
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private readonly PortalSettings _settings;
        private readonly string _responseQualityDirective;
        private readonly string _defaultLanguage;

        private Panel _progressBar;
        private int _snippetCharIndex = 0;
        private string _currentSnippet = "";
        private int _progressBarMaxWidth = 320;

        private static readonly Regex LangQueryRegex =
            new Regex(@"(?<=\?|&)lang=[^&]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DrivePathRegex =
            new Regex(@"^/+[A-Za-z]:/", RegexOptions.Compiled);

        private static readonly HashSet<string> TopicStopWords =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "search","topic","topics","guide","guides","guidance","collection","collections",
                "knowledge","hub","hubs","chapter","section","portal","portaalin","luku","c","index",
                "overview","insight","insights","framework","frameworks","playbook","playbooks",
                "narrative","narratives","desk","deskset","catalog","catalogue","compendium",
                "dossier","dossiers","brief","briefs","summary","summaries"
            };

        private static readonly string[] FragmentKeys =
        {
            "heroHtml","articleHtml","factsHtml","speculationHtml","insightsHtml",
            "dataTableHtml","timelineHtml","keyFindingsHtml","actionsHtml","sourcesHtml"
        };

        private static readonly Dictionary<string, (string TitleFi, string BodyFi, string TitleEn, string BodyEn)> PlaceholderDefaults =
            new()
            {
                ["heroHtml"] = ("Hero-osio puuttuu", "Portaalin esittely valmistellaan parhaillaan – yritä hetken kuluttua uudelleen.",
                                "Hero section missing", "The hero introduction is still compiling – please refresh shortly."),
                ["articleHtml"] = ("Keskeinen artikkeli", "Analyysi ei ehtinyt valmistua. Odota hetki ja kokeile uudestaan.",
                                   "Core article", "The primary analysis did not materialise in time. Please reload soon."),
                ["factsHtml"] = ("Faktakatsaus", "Tarkat tilastot päivittyvät paraikaa.", "Fact sheet", "Verified statistics are being refreshed."),
                ["speculationHtml"] = ("Skenaarioarvio", "Strateginen spekulointi päivittyy hetken kuluttua.", "Scenario outlook", "Scenario commentary will become available shortly."),
                ["insightsHtml"] = ("Portaalin oivallukset", "Portaalin omat havainnot koostetaan parhaillaan.", "Portal insights", "Internal insights are still being compiled."),
                ["dataTableHtml"] = ("Vertailutaulukko", "Vertailutiedot ladataan.", "Comparison table", "Comparative metrics are on their way."),
                ["timelineHtml"] = ("Aikajana", "Keskeiset aikamerkinnät muodostetaan.", "Timeline", "Chronological anchors are still being assembled."),
                ["keyFindingsHtml"] = ("Keskeiset havainnot", "Keskeiset päätelmät täydentyvät pian.", "Key findings", "Key conclusions will appear shortly."),
                ["actionsHtml"] = ("Toimenpiteet", "Toimintasuositukset päivittyvät.", "Recommended actions", "Actionable steps are still loading."),
                ["sourcesHtml"] = ("Lähdekansiot", "Lähdesivuja ei voitu ladata juuri nyt.", "Source dossiers", "Source dossiers could not be prepared in time.")
            };

        private sealed record LocalizedLink(string FiLabel, string EnLabel, string PathTemplate);

        private static readonly LocalizedLink[] FooterLinkTemplates =
        {
            new("Analyytikon muistio", "Analyst brief", "/analyst-brief-{slug}"),
            new("Skenaario-työpaja", "Scenario lab", "/scenario-lab-{slug}"),
            new("Data-arkiston polku", "Data vault pathway", "/data-vault/{slug}"),
            new("Sidosryhmäkooste", "Stakeholder digest", "/stakeholder-digest-{slug}"),
            new("Menetelmäpankki", "Method bank", "/method-bank-{slug}")
        };

        private sealed record ContentRequest(
            string Prompt,
            string NavContext,
            string OriginContext,
            string LanguageCode,
            string LanguageName,
            string AltLanguageCode,
            string AltLanguageName,
            string PrimaryTopic,
            string TopicSlug,
            List<string> TopicTokens,
            string LoadingMessage,
            bool IsSearch
        );

        private readonly HashSet<string> _generatedFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<double> _recentDurations = new();
        private const int MaxDurationSamples = 12;

        private string _lastTempFile = null;
        private bool _ignoreNextNavigation = false;
        private bool _homePageRendered = false;
        private string _lastNavContext = "initial-load";

        private Panel _loadingOverlay;
        private Label _loadingLabel;
        private readonly Timer _loadingTimer;
        private DateTime _loadingStart = DateTime.MinValue;
        private string _currentLoadingMessage = string.Empty;

        private readonly List<string> _loadingSnippets = new();

        public TransformerBrowser()
        {
            InitializeComponent();

            var settingsPath = Path.Combine(AppContext.BaseDirectory, "portalsettings.json");
            _settings = PortalSettings.Load(settingsPath);
            _responseQualityDirective = _settings.Prompts.ResponseQualityDirective ?? string.Empty;
            _defaultLanguage = string.IsNullOrWhiteSpace(_settings.DefaultLanguage) ? "fi" : _settings.DefaultLanguage;

            this.Activated += WebBrowser_Activated;
            this.Deactivate += WebBrowser_Deactivate;

            _loadingOverlay = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(160, 30, 32, 40),
                Visible = false
            };
            Controls.Add(_loadingOverlay);

            _loadingLabel = new Label
            {
                AutoSize = false,
                Size = new Size(420, 180),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(245, 245, 255),
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold | FontStyle.Italic),
                BackColor = Color.Transparent
            };
            _loadingOverlay.Controls.Add(_loadingLabel);

            _progressBar = new Panel
            {
                Width = 0,
                Height = 8,
                BackColor = Color.FromArgb(180, 90, 180, 255),
                Location = new Point(0, 0)
            };
            _loadingOverlay.Controls.Add(_progressBar);

            _loadingOverlay.Resize += (s, e) =>
            {
                _loadingLabel.Location = new Point((_loadingOverlay.Width - _loadingLabel.Width) / 2, (_loadingOverlay.Height - _loadingLabel.Height) / 2);
                _progressBar.Location = new Point((_loadingOverlay.Width - _progressBarMaxWidth) / 2, (_loadingOverlay.Height - 8) / 2 + 60);
                _progressBar.Width = 0;
            };
            _loadingOverlay.BringToFront();
            CenterLoadingLabel();

            _loadingTimer = new Timer { Interval = 200 };
            _loadingTimer.Tick += (s, e) => UpdateLoadingText();

            this.Load += async (s, e) =>
            {
                await webView21.EnsureCoreWebView2Async();
                webView21.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                webView21.CoreWebView2.Navigate(BootstrapUrl);
            };
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TransformerBrowser/1.1 (Windows; StaticTemplate)");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            return client;
        }

        private void WebBrowser_Activated(object sender, EventArgs e)
        {
            this.FormBorderStyle = FormBorderStyle.Sizable;
        }

        private void WebBrowser_Deactivate(object sender, EventArgs e)
        {
            this.FormBorderStyle = FormBorderStyle.None;
        }
        private string _pendingNavContext = null;
        private async void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (_ignoreNextNavigation)
            {
                _ignoreNextNavigation = false;
                return;
            }

            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
                return;

            if (uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase))
                return;

            if (!_homePageRendered)
            {
                e.Cancel = true;
                _homePageRendered = true;

                string langCode = NormalizeLanguageCode(_defaultLanguage);
                var homeRequest = BuildHomeRequest(langCode);

                _pendingNavContext = StripDrivePrefix(homeRequest.NavContext);
                try
                {
                    await RenderPrompt(homeRequest);
                }
                finally
                {
                    _pendingNavContext = null;
                }
                return;
            }

            if (uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                var localPath = Path.GetFullPath(uri.LocalPath);
                if (_generatedFiles.Contains(localPath))
                    return;
            }

            e.Cancel = true;

            string navContextRaw = uri.PathAndQuery;
            string navContextClean = CleanNavigationContext(navContextRaw);
            var queryParams = ParseQueryParams(navContextClean);

            string languageCode = NormalizeLanguageCode(
                queryParams.TryGetValue("lang", out var langValue) ? langValue : null);

            string navContextWithLang = EnsureLangInContext(navContextClean, languageCode);
            navContextWithLang = StripDrivePrefix(navContextWithLang);

            string originContext = _lastNavContext ?? "initial-load";
            originContext = StripDrivePrefix(originContext);

            if (string.Equals(navContextWithLang, originContext, StringComparison.OrdinalIgnoreCase))
                return;

            if (!string.IsNullOrEmpty(_pendingNavContext) &&
                string.Equals(navContextWithLang, _pendingNavContext, StringComparison.OrdinalIgnoreCase))
                return;

            string searchQuery = BuildSearchQuery(navContextWithLang, queryParams);
            string searchSnapshot = await FetchSearchSnapshotAsync(searchQuery, languageCode);

            ContentRequest request;
            if (navContextWithLang.StartsWith("/search", StringComparison.OrdinalIgnoreCase))
            {
                queryParams.TryGetValue("q", out string qValue);
                request = BuildSearchRequest(
                    navContextWithLang, originContext, languageCode, qValue ?? string.Empty,
                    searchSnapshot, searchQuery);
            }
            else
            {
                request = BuildNavigationRequest(
                    navContextWithLang, originContext, languageCode,
                    searchSnapshot, searchQuery);
            }

            _pendingNavContext = StripDrivePrefix(request.NavContext);
            try
            {
                await RenderPrompt(request);
            }
            finally
            {
                _pendingNavContext = null;
            }
        }

        private void CenterLoadingLabel()
        {
            if (_loadingLabel == null || _loadingOverlay == null)
                return;

            _loadingLabel.Location = new Point(
                (_loadingOverlay.Width - _loadingLabel.Width) / 2,
                (_loadingOverlay.Height - _loadingLabel.Height) / 2);
        }

        private void ShowLoadingOverlay(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => ShowLoadingOverlay(message));
                return;
            }

            _currentLoadingMessage = message;
            _loadingTimer.Stop();
            _loadingStart = DateTime.UtcNow;
            _loadingOverlay.Visible = true;
            _loadingOverlay.BringToFront();
            UpdateLoadingText();
            _loadingTimer.Start();
        }

        private void UpdateLoadingText()
        {
            if (_loadingOverlay == null || !_loadingOverlay.Visible)
                return;

            double elapsed = _loadingStart == DateTime.MinValue
                ? 0
                : (DateTime.UtcNow - _loadingStart).TotalSeconds;

            double pct = (elapsed % 3.0) / 3.0;
            _progressBar.Width = (int)(_progressBarMaxWidth * pct);
            _progressBar.Refresh();

            int snippetIndex = -1;
            string snippetText = "";
            if (_loadingSnippets.Count > 0)
            {
                snippetIndex = (int)(elapsed / 10);
                if (snippetIndex >= _loadingSnippets.Count)
                    snippetIndex = _loadingSnippets.Count - 1;
                snippetText = _loadingSnippets[snippetIndex];
            }

            string snippetLine = "";
            if (!string.IsNullOrWhiteSpace(snippetText))
            {
                if (_currentSnippet != snippetText)
                {
                    _currentSnippet = snippetText;
                    _snippetCharIndex = 0;
                }
                if (_snippetCharIndex < snippetText.Length)
                    _snippetCharIndex += 2;
                snippetLine = "\n" + snippetText.Substring(0, Math.Min(_snippetCharIndex, snippetText.Length));
            }

            string avgText = _recentDurations.Count > 0
                ? $"Avg: {_recentDurations.Average():F1}s"
                : "Avg: gathering data";

            _loadingLabel.Text = $"{_currentLoadingMessage}{snippetLine}\nElapsed: {elapsed:F1}s | {avgText}";
            CenterLoadingLabel();
        }

        private void HideLoadingOverlay()
        {
            if (InvokeRequired)
            {
                BeginInvoke(HideLoadingOverlay);
                return;
            }

            _loadingTimer.Stop();

            if (_loadingStart != DateTime.MinValue)
            {
                double seconds = (DateTime.UtcNow - _loadingStart).TotalSeconds;
                _recentDurations.Enqueue(seconds);
                while (_recentDurations.Count > MaxDurationSamples)
                    _recentDurations.Dequeue();
                _loadingStart = DateTime.MinValue;
            }

            _loadingSnippets.Clear();
            _loadingOverlay.Visible = false;
        }

        private ContentRequest BuildHomeRequest(string languageCode)
        {
            string languageName = GetLanguageDisplayName(languageCode);
            string altLanguageCode = languageCode == "fi" ? "en" : "fi";
            string altLanguageName = GetLanguageDisplayName(altLanguageCode);

            string navContext = EnsureLangInContext("/", languageCode);
            string originContext = "bootstrap";
            var topicTokens = ExtractTopicTokens("portal-koti", "intelligence");
            string topicSlug = string.Join("-", topicTokens);
            if (string.IsNullOrWhiteSpace(topicSlug))
                topicSlug = "portal-home";
            string primaryTopic = languageCode == "fi" ? "Portaalin etusivu" : "Portal landing";

            var tokens = new Dictionary<string, string>
            {
                ["NavContext"] = navContext,
                ["OriginContext"] = originContext,
                ["LanguageName"] = languageName,
                ["LanguageCode"] = languageCode,
                ["AltLanguageName"] = altLanguageName,
                ["AltLanguageCode"] = altLanguageCode,
                ["TopicSlug"] = topicSlug,
                ["TopicTokens"] = string.Join(", ", topicTokens),
                ["PrimaryTopic"] = primaryTopic,
                ["ResponseQualityDirective"] = _responseQualityDirective
            };

            string prompt = TemplateRenderer.Render(_settings.Prompts.HomePage, tokens);
            string loadingMessage = GetLoadingMessage(false, languageCode);

            return new ContentRequest(
                Prompt: prompt,
                NavContext: navContext,
                OriginContext: originContext,
                LanguageCode: languageCode,
                LanguageName: languageName,
                AltLanguageCode: altLanguageCode,
                AltLanguageName: altLanguageName,
                PrimaryTopic: primaryTopic,
                TopicSlug: topicSlug,
                TopicTokens: topicTokens,
                LoadingMessage: loadingMessage,
                IsSearch: false
            );
        }

        private ContentRequest BuildNavigationRequest(string navContext, string originContext, string languageCode, string searchSnapshot, string searchQuery)
        {
            string languageName = GetLanguageDisplayName(languageCode);
            string altLanguageCode = languageCode == "fi" ? "en" : "fi";
            string altLanguageName = GetLanguageDisplayName(altLanguageCode);

            string path = navContext.Split('?')[0];
            if (!path.StartsWith("/"))
                path = "/" + path.TrimStart('/');

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                segments = new[] { "home" };

            var humanSegments = segments
                .Select((seg, idx) => $"Level {idx + 1}: '{seg}' (interpreted as \"{HumanizeSegment(seg)}\")");
            string segmentBulletList = string.Join(Environment.NewLine, humanSegments);

            var query = ParseQueryParams(navContext);
            string queryDescription = query.Count == 0
                ? "No query parameters were provided."
                : string.Join(Environment.NewLine,
                    query.Select(kvp => $"- {kvp.Key} = \"{kvp.Value}\" (use this to refine the topic)."));

            string primarySegment = segments.Last();
            var topicTokens = ExtractTopicTokens(primarySegment, primarySegment);
            string topicSlug = string.Join("-", topicTokens);
            if (string.IsNullOrWhiteSpace(topicSlug))
                topicSlug = SanitizeSlug(primarySegment);

            string primaryTopic = BuildTopicTitle(topicTokens, languageCode);
            string liveContext = FormatSearchSnapshot(searchQuery, searchSnapshot);

            var tokens = new Dictionary<string, string>
            {
                ["OriginContext"] = originContext,
                ["NavContext"] = navContext,
                ["SegmentBulletList"] = segmentBulletList,
                ["QueryDescription"] = queryDescription,
                ["LiveContext"] = liveContext,
                ["PrimaryTopic"] = primaryTopic,
                ["TopicSlug"] = topicSlug,
                ["TopicTokens"] = string.Join(", ", topicTokens),
                ["LanguageName"] = languageName,
                ["LanguageCode"] = languageCode,
                ["AltLanguageName"] = altLanguageName,
                ["AltLanguageCode"] = altLanguageCode,
                ["ResponseQualityDirective"] = _responseQualityDirective
            };

            string prompt = TemplateRenderer.Render(_settings.Prompts.NavigationTemplate, tokens);
            string loadingMessage = GetLoadingMessage(false, languageCode);

            return new ContentRequest(
                Prompt: prompt,
                NavContext: navContext,
                OriginContext: originContext,
                LanguageCode: languageCode,
                LanguageName: languageName,
                AltLanguageCode: altLanguageCode,
                AltLanguageName: altLanguageName,
                PrimaryTopic: primaryTopic,
                TopicSlug: topicSlug,
                TopicTokens: topicTokens,
                LoadingMessage: loadingMessage,
                IsSearch: false
            );
        }

        private ContentRequest BuildSearchRequest(string navContext, string originContext, string languageCode, string rawQuery, string searchSnapshot, string searchQuery)
        {
            string languageName = GetLanguageDisplayName(languageCode);
            string altLanguageCode = languageCode == "fi" ? "en" : "fi";
            string altLanguageName = GetLanguageDisplayName(altLanguageCode);

            string interpreted = string.IsNullOrWhiteSpace(rawQuery) ? "Global Research Digest" : rawQuery.Trim();
            var topicTokens = ExtractTopicTokens(interpreted, "insight");
            string topicSlug = string.Join("-", topicTokens);
            if (string.IsNullOrWhiteSpace(topicSlug))
                topicSlug = SanitizeSlug(interpreted);

            string primaryTopic = BuildTopicTitle(topicTokens, languageCode);
            string liveContext = FormatSearchSnapshot(searchQuery, searchSnapshot);

            var tokens = new Dictionary<string, string>
            {
                ["OriginContext"] = originContext,
                ["NavContext"] = navContext,
                ["InterpretedQuery"] = interpreted,
                ["LiveContext"] = liveContext,
                ["PrimaryTopic"] = primaryTopic,
                ["TopicSlug"] = topicSlug,
                ["TopicTokens"] = string.Join(", ", topicTokens),
                ["LanguageName"] = languageName,
                ["LanguageCode"] = languageCode,
                ["AltLanguageName"] = altLanguageName,
                ["AltLanguageCode"] = altLanguageCode,
                ["ResponseQualityDirective"] = _responseQualityDirective
            };

            string prompt = TemplateRenderer.Render(_settings.Prompts.SearchTemplate, tokens);
            string loadingMessage = GetLoadingMessage(true, languageCode);

            return new ContentRequest(
                Prompt: prompt,
                NavContext: navContext,
                OriginContext: originContext,
                LanguageCode: languageCode,
                LanguageName: languageName,
                AltLanguageCode: altLanguageCode,
                AltLanguageName: altLanguageName,
                PrimaryTopic: primaryTopic,
                TopicSlug: topicSlug,
                TopicTokens: topicTokens,
                LoadingMessage: loadingMessage,
                IsSearch: true
            );
        }
        private string StripDrivePrefix(string route)
        {
            if (string.IsNullOrWhiteSpace(route))
                return "/";

            if (route.Equals("initial-load", StringComparison.OrdinalIgnoreCase))
                return route;

            string trimmed = route.Trim();

            int queryIndex = trimmed.IndexOf('?');
            string path = queryIndex >= 0 ? trimmed[..queryIndex] : trimmed;
            string query = queryIndex >= 0 ? trimmed[queryIndex..] : string.Empty;

            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                if (path.Length >= 4 && char.IsLetter(path[1]) && path[2] == ':' && path[3] == '/')
                {
                    path = path[3..];            // “/C:/foo” → “/foo”
                }
            }

            if (!path.StartsWith("/", StringComparison.Ordinal))
                path = "/" + path.TrimStart('/');

            while (path.Contains("//", StringComparison.Ordinal))
                path = path.Replace("//", "/");

            return path + query;
        }
        private async Task RenderPrompt(ContentRequest request)
        {
            string originRoute = EnsureLangInContext(request.OriginContext ?? "initial-load", request.LanguageCode);

            var enrichmentTokens = new Dictionary<string, string>
            {
                ["OriginRoute"] = originRoute,
                ["Context"] = request.NavContext,
                ["LanguageName"] = request.LanguageName,
                ["LanguageCode"] = request.LanguageCode,
                ["AltLanguageName"] = request.AltLanguageName,
                ["AltLanguageCode"] = request.AltLanguageCode,
                ["ResponseQualityDirective"] = _responseQualityDirective
            };

            string enrichment = TemplateRenderer.Render(_settings.Prompts.MainEnrichmentTemplate, enrichmentTokens);
            string basePrompt = request.Prompt;
            string enrichedPrompt = string.IsNullOrWhiteSpace(enrichment)
                ? basePrompt
                : $"{basePrompt}\n\n{enrichment}";

            await PrepareLoadingSnippetsAsync(request.NavContext, request.OriginContext, request.LanguageCode, request.LanguageName);
            ShowLoadingOverlay(request.LoadingMessage);

            string html;

            try
            {
                var fragments = await GenerateFragmentsAsync(enrichedPrompt, request);
                html = BuildPageFromTemplate(request, fragments);
            }
            catch (Exception ex)
            {
                html = BuildErrorHtml(ex, enrichedPrompt, request.NavContext, request.LanguageCode);
            }

            try
            {
                if (!string.IsNullOrEmpty(_lastTempFile) && File.Exists(_lastTempFile))
                {
                    try { File.Delete(_lastTempFile); } catch { }
                }

                string tempPath = Path.GetTempPath();
                string fileName = $"transformer_{Guid.NewGuid():N}.html";
                string filePath = Path.Combine(tempPath, fileName);

                await File.WriteAllTextAsync(filePath, html);

                _lastTempFile = filePath;
                _generatedFiles.Add(filePath);

                string fileUri = "file:///" + filePath.Replace("\\", "/");
                _ignoreNextNavigation = true;
                webView21.CoreWebView2.Navigate(fileUri);
            }
            finally
            {
                _lastNavContext = StripDrivePrefix(request.NavContext);
                HideLoadingOverlay();
            }
        }

        private async Task PrepareLoadingSnippetsAsync(string destinationRoute, string originRoute, string languageCode, string languageName)
        {
            _loadingSnippets.Clear();

            try
            {
                var snippetTokens = new Dictionary<string, string>
                {
                    ["DestinationRoute"] = destinationRoute,
                    ["OriginRoute"] = originRoute,
                    ["LanguageName"] = languageName,
                    ["LanguageCode"] = languageCode
                };

                string snippetPrompt = TemplateRenderer.Render(
                    _settings.Prompts.LoadingSnippetTemplate,
                    snippetTokens);

                string teaserSystemPrompt = TemplateRenderer.Render(
                    _settings.OpenAI.TeaserSystemPrompt,
                    new Dictionary<string, string>
                    {
                        ["LanguageName"] = languageName,
                        ["LanguageCode"] = languageCode
                    });

                var teaserClient = new OpenAIChatClient(
                    model: _settings.OpenAI.TeaserModel,
                    systemMessage: teaserSystemPrompt
                );

                string teaserFinalInstruction = TemplateRenderer.Render(
                    _settings.OpenAI.TeaserFinalInstructionTemplate,
                    new Dictionary<string, string>
                    {
                        ["LanguageName"] = languageName,
                        ["LanguageCode"] = languageCode
                    });

                teaserClient.AddUserMessage(snippetPrompt);
                teaserClient.SetFinalInstructionMessage(teaserFinalInstruction);

                string raw = await teaserClient.GetChatCompletionAsync();

                var lines = raw
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim().TrimStart('-', '•', '·'))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Take(6)
                    .ToList();

                if (lines.Count == 0)
                    lines = GetFallbackLoadingSnippets(destinationRoute, languageCode);

                _loadingSnippets.AddRange(lines);
            }
            catch
            {
                _loadingSnippets.Clear();
                _loadingSnippets.AddRange(GetFallbackLoadingSnippets(destinationRoute, languageCode));
            }
        }

        private List<string> GetFallbackLoadingSnippets(string destinationRoute, string languageCode)
        {
            string title = BuildTopicTitle(ExtractTopicTokens(destinationRoute, "analysis"), languageCode);
            if (languageCode.Equals("fi", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string>
                {
                    $"{title} -luku avaa hetken kuluttua tärkeimmät aikajaksot ja päätöspisteet.",
                    "Koordinointitiimi kerää parhaillaan todennetut tilastot ja lähdeviitteet.",
                    "Strateginen skenaarioharjoitus ripustetaan seuraavaksi valmiiksi.",
                    "Toimintasuositukset viimeistellään numeroihin sidotuilla perusteluilla.",
                    "Vertailutaulukon mittarit kalibroidaan analyysin tueksi.",
                    "Lähdekansiot linkitetään pian, jotta syventäminen jatkuu saumattomasti."
                };
            }

            return new List<string>
            {
                $"The {title} dossier is warming up, mapping pivotal timelines and decisions.",
                "An analyst cell is curating statistics and source tags to anchor the findings.",
                "Logistics and diplomatic ledgers are being cross-checked for the upcoming summary.",
                "Recommended response tracks are queuing with explicit metrics and evidence anchors.",
                "Scenario comparisons are lining up inside the forthcoming spotlight table.",
                "Source vault links will surface shortly so the deep dive continues seamlessly."
            };
        }

        private async Task<Dictionary<string, string>> GenerateFragmentsAsync(string prompt, ContentRequest request)
        {
            var client = CreatePrimaryChatClient();
            client.AddUserMessage(prompt);

            var finalInstructionTokens = new Dictionary<string, string>
            {
                ["LanguageName"] = request.LanguageName,
                ["LanguageCode"] = request.LanguageCode,
                ["AltLanguageName"] = request.AltLanguageName,
                ["AltLanguageCode"] = request.AltLanguageCode,
                ["ResponseQualityDirective"] = _responseQualityDirective
            };

            string finalInstruction = TemplateRenderer.Render(
                _settings.OpenAI.FinalInstructionTemplate,
                finalInstructionTokens);

            client.SetFinalInstructionMessage(finalInstruction);

            string raw = await client.GetChatCompletionAsync();
            raw = StripCodeFence(raw);

            var fragments = ParseFragmentJson(raw);
            return EnsureFragments(fragments, request);
        }

        private Dictionary<string, string> ParseFragmentJson(string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Model reply was not a JSON object.");

                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    dict[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        _ => property.Value.GetRawText()
                    };
                }
                return dict;
            }
            catch (JsonException jex)
            {
                throw new InvalidOperationException("Failed to parse model JSON output.", jex);
            }
        }

        private Dictionary<string, string> EnsureFragments(Dictionary<string, string> fragments, ContentRequest request)
        {
            var ensured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string key in FragmentKeys)
            {
                if (fragments.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    ensured[key] = value;
                }
                else
                {
                    ensured[key] = GetPlaceholderHtml(key, request.LanguageCode);
                }
            }

            return ensured;
        }

        private string BuildPageFromTemplate(ContentRequest request, Dictionary<string, string> fragments)
        {
            var tokens = new Dictionary<string, string>
            {
                ["LanguageCode"] = request.LanguageCode,
                ["PageTitle"] = WebUtility.HtmlEncode(request.PrimaryTopic),
                ["PortalTitle"] = WebUtility.HtmlEncode(request.PrimaryTopic),
                ["PortalSubtitle"] = WebUtility.HtmlEncode(GetPortalSubtitle(request)),
                ["LanguageSelector"] = BuildLanguageSelector(request),
                ["SearchUtility"] = BuildSearchUtility(request.LanguageCode),
                ["HeroSection"] = fragments["heroHtml"],
                ["ArticleSection"] = fragments["articleHtml"],
                ["FactsSection"] = fragments["factsHtml"],
                ["SpeculationSection"] = fragments["speculationHtml"],
                ["InsightsSection"] = fragments["insightsHtml"],
                ["DataTableSection"] = fragments["dataTableHtml"],
                ["TimelineSection"] = fragments["timelineHtml"],
                ["KeyFindingsSection"] = fragments["keyFindingsHtml"],
                ["ActionsSection"] = fragments["actionsHtml"],
                ["SourcesSection"] = fragments["sourcesHtml"],
                ["SideNavigation"] = BuildSideNavigation(request),
                ["FooterNavigation"] = BuildFooterNavigation(request),
                ["GeneratedStamp"] = WebUtility.HtmlEncode(GetGeneratedStamp(request.LanguageCode))
            };

            return TemplateRenderer.Render(_settings.Templates.PageLayout, tokens);
        }

        private string BuildLanguageSelector(ContentRequest request)
        {
            string current = GetLanguageNativeName(request.LanguageCode);
            string alternate = GetLanguageNativeName(request.AltLanguageCode);
            string altLink = EnsureLangInContext(request.NavContext, request.AltLanguageCode);

            string label = request.LanguageCode == "fi" ? "Kielivalinta:" : "Language:";
            string descriptor = request.LanguageCode == "fi" ? "aktiivinen" : "active";
            string changeText = request.LanguageCode == "fi" ? "Vaihda kieleen" : "Switch to";

            return $"<div class='language-selector' role='navigation' aria-label='{WebUtility.HtmlEncode(label)}'>{WebUtility.HtmlEncode(label)} <span>{WebUtility.HtmlEncode(current)}</span> · <a href='{altLink}'>{WebUtility.HtmlEncode(changeText)} {WebUtility.HtmlEncode(alternate)}</a></div>";
        }

        private string BuildSearchUtility(string languageCode)
        {
            string utilityLabel = languageCode == "fi" ? "Portaalin hakutyökalu" : "Portal search utility";
            string searchAria = languageCode == "fi" ? "Hae tietoa portaalista" : "Search the portal";
            string searchLabel = languageCode == "fi" ? "Hakusana:" : "Search term:";
            string placeholderText = languageCode == "fi"
                ? "Kirjoita haun avainsanat ja paina Hae"
                : "Enter search keywords and press Search";
            string buttonText = languageCode == "fi" ? "Hae" : "Search";

            var tokens = new Dictionary<string, string>
            {
                ["UtilityLabel"] = WebUtility.HtmlEncode(utilityLabel),
                ["SearchAria"] = WebUtility.HtmlEncode(searchAria),
                ["SearchLabel"] = WebUtility.HtmlEncode(searchLabel),
                ["PlaceholderText"] = WebUtility.HtmlEncode(placeholderText),
                ["ButtonText"] = WebUtility.HtmlEncode(buttonText),
                ["LangCode"] = WebUtility.HtmlEncode(languageCode)
            };

            return TemplateRenderer.Render(_settings.Templates.StaticSearchUtility, tokens);
        }

        private string BuildSideNavigation(ContentRequest request)
        {
            var labels = GetSideNavLabels(request.LanguageCode);
            var random = new Random(unchecked(request.NavContext.GetHashCode()));
            var chosen = new HashSet<int>();
            var tokens = request.TopicTokens.Count > 0 ? request.TopicTokens : new List<string> { "insight", "brief" };

            var items = new List<string>();
            while (items.Count < Math.Min(6, labels.Length) && chosen.Count < labels.Length)
            {
                int index = random.Next(labels.Length);
                if (!chosen.Add(index))
                    continue;

                string template = labels[index];
                string labelText = template.Replace("{Topic}", request.PrimaryTopic);
                string slugSuffix = SanitizeSlug(template.Replace("{Topic}", string.Join("-", tokens)));
                if (string.IsNullOrWhiteSpace(slugSuffix))
                    slugSuffix = request.TopicSlug;

                string path = EnsureLangInContext($"/{request.TopicSlug}-{slugSuffix}", request.LanguageCode);
                items.Add($"<li><a href='{path}'>{WebUtility.HtmlEncode(labelText)}</a></li>");
            }

            string heading = request.LanguageCode == "fi" ? "Reittivalikko" : "Route menu";
            return $"<h2>{WebUtility.HtmlEncode(heading)}</h2><ul>{string.Join(string.Empty, items)}</ul>";
        }

        private string BuildFooterNavigation(ContentRequest request)
        {
            var links = FooterLinkTemplates.Select(template =>
            {
                string label = request.LanguageCode == "fi" ? template.FiLabel : template.EnLabel;
                string slug = template.PathTemplate.Replace("{slug}", request.TopicSlug);
                string href = EnsureLangInContext(slug, request.LanguageCode);
                return $"<li><a href='{href}'>{WebUtility.HtmlEncode(label)}</a></li>";
            });

            string heading = request.LanguageCode == "fi" ? "<h2>Jatka tutkimusta</h2>" : "<h2>Continue exploring</h2>";
            return $"{heading}<ul>{string.Join(string.Empty, links)}</ul>";
        }

        private string GetPortalSubtitle(ContentRequest request)
        {
            return request.LanguageCode == "fi"
                ? $"Analyyttinen näkymä: {request.PrimaryTopic}"
                : $"Analytical viewport: {request.PrimaryTopic}";
        }

        private string GetGeneratedStamp(string languageCode)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
            return languageCode == "fi"
                ? $"Muodostettu {timestamp}"
                : $"Generated {timestamp}";
        }

        private string[] GetSideNavLabels(string languageCode)
        {
            if (languageCode == "fi")
            {
                return new[]
                {
                    "Syventävä analyysi: {Topic}",
                    "Trendien indikaattorit",
                    "Riskimatriisi ja varautuminen",
                    "Sidosryhmien ääni",
                    "Kenttäoperaatioiden tilannekuva",
                    "Data-arkiston polut",
                    "Strateginen kello"
                };
            }

            return new[]
            {
                "Deep dive: {Topic}",
                "Trend indicators",
                "Risk matrix and readiness",
                "Stakeholder pulse",
                "Field operations status",
                "Data vault pathways",
                "Strategic timekeeper"
            };
        }

        private OpenAIChatClient CreatePrimaryChatClient()
            => new OpenAIChatClient(_settings.OpenAI.PrimaryModel, _settings.OpenAI.SystemPrompt);

        private static string StripCodeFence(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            string trimmed = content.Trim();

            if (!trimmed.StartsWith("```"))
                return trimmed;

            int firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak == -1)
                return trimmed;

            string withoutFence = trimmed[(firstLineBreak + 1)..];

            int closingFenceIndex = withoutFence.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFenceIndex >= 0)
                withoutFence = withoutFence[..closingFenceIndex];

            return withoutFence.Trim();
        }

        private string BuildErrorHtml(Exception ex, string prompt, string context, string languageCode)
        {
            string safeMessage = WebUtility.HtmlEncode(ex.Message);
            string safePrompt = WebUtility.HtmlEncode(prompt ?? string.Empty);
            string safeContext = WebUtility.HtmlEncode(context ?? string.Empty);
            string safeLang = WebUtility.HtmlEncode(NormalizeLanguageCode(languageCode));

            return $@"<!DOCTYPE html>
<html lang='{safeLang}'>
<head>
    <meta charset='utf-8' />
    <title>Generation Error</title>
    <style>
        body {{
            font-family: 'Segoe UI', Arial, sans-serif;
            background: #121212;
            color: #eee;
            margin: 0;
            padding: 0;
            display: flex;
            align-items: center;
            justify-content: center;
            height: 100vh;
        }}
        .card {{
            background: #1f1f1f;
            border-radius: 16px;
            padding: 32px;
            max-width: 640px;
            width: 90%;
            box-shadow: 0 24px 48px rgba(0, 0, 0, 0.45);
        }}
        h1 {{
            margin-top: 0;
            font-size: 1.8rem;
        }}
        .prompt {{
            background: rgba(255, 255, 255, 0.05);
            padding: 12px 16px;
            border-radius: 12px;
            margin-top: 20px;
            font-size: 0.95rem;
            word-break: break-word;
        }}
        a {{
            color: #62b0ff;
        }}
    </style>
</head>
<body>
    <div class='card'>
        <h1>Unable to generate the requested page</h1>
        <p>An error occurred while processing the request for <strong>{safeContext}</strong>.</p>
        <p><strong>Details:</strong> {safeMessage}</p>
        <div class='prompt'>
            <strong>Prompt sent:</strong> {safePrompt}
        </div>
        <p style='margin-top: 24px;'>
            Try <a href='/?lang={safeLang}'>returning to the home page</a> or <a href='/explore-critical-insights-and-guides?lang={safeLang}'>visiting the exploration hub</a>.
        </p>
    </div>
</body>
</html>";
        }

        private string NormalizeLanguageCode(string lang)
        {
            if (string.Equals(lang, "fi", StringComparison.OrdinalIgnoreCase))
                return "fi";
            if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
                return "en";
            return _defaultLanguage;   // fall back to the configured default, e.g. "fi"
        }

        private string GetLanguageDisplayName(string code)
            => string.Equals(code, "fi", StringComparison.OrdinalIgnoreCase) ? "Finnish" : "English";

        private string GetLanguageNativeName(string code)
            => string.Equals(code, "fi", StringComparison.OrdinalIgnoreCase) ? "Suomi" : "English";

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

        private string CleanNavigationContext(string navContext)
            => StripDrivePrefix(string.IsNullOrWhiteSpace(navContext) ? "/" : navContext.Trim());

        private string BuildSearchQuery(string navContext, Dictionary<string, string> queryParams)
        {
            if (queryParams != null && queryParams.TryGetValue("q", out string queryValue) && !string.IsNullOrWhiteSpace(queryValue))
                return queryValue.Trim();

            string path = navContext.Split('?')[0];
            if (!path.StartsWith("/"))
                path = "/" + path.TrimStart('/');

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string primarySegment = segments.Length == 0 ? "intelligence insights" : segments.Last();
            var tokens = ExtractTopicTokens(primarySegment, primarySegment);

            if (tokens.Count == 0)
                tokens.Add("intelligence insights");

            return string.Join(" ", tokens.Select(t => t.Replace('-', ' ')));
        }

        private async Task<string> FetchSearchSnapshotAsync(string query, string languageCode)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            try
            {
                string locale = languageCode == "fi" ? "fi-FI" : "en-US";
                string url = $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}&setlang={locale}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.AcceptLanguage.ParseAdd(locale);

                using var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                string html = await response.Content.ReadAsStringAsync();

                if (html.Length > 20000)
                    html = html.Substring(0, 20000);

                return html;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string HumanizeSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
                return "Home";

            string cleaned = segment.Replace('-', ' ').Replace('_', ' ').Trim();
            var textInfo = CultureInfo.InvariantCulture.TextInfo;
            return textInfo.ToTitleCase(cleaned);
        }

        private string SanitizeSlug(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
                return "home";

            string slug = Regex.Replace(segment.ToLowerInvariant(), @"[^a-z0-9\-]", "-");
            slug = Regex.Replace(slug, "-{2,}", "-").Trim('-');
            return slug.Length == 0 ? "topic" : slug;
        }

        private Dictionary<string, string> ParseQueryParams(string pathAndQuery)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int qIndex = pathAndQuery.IndexOf('?');
            if (qIndex < 0)
                return result;

            string query = pathAndQuery[(qIndex + 1)..];
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                string key = Uri.UnescapeDataString(kv[0]);
                string value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
                result[key] = value;
            }
            return result;
        }

        private List<string> ExtractTopicTokens(string source, string fallback)
        {
            List<string> tokens = SplitTokens(source)
                .Where(token => !TopicStopWords.Contains(token))
                .ToList();

            if (!tokens.Any() && !string.IsNullOrWhiteSpace(fallback))
            {
                tokens = SplitTokens(fallback)
                    .Where(token => !TopicStopWords.Contains(token))
                    .ToList();
            }

            if (!tokens.Any())
                tokens.AddRange(new[] { "insight", "brief" });

            return tokens.Take(6).ToList();
        }

        private IEnumerable<string> SplitTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            var rawTokens = text
                .Replace("+", " ")
                .Split(new[] { ' ', '-', '_', '/', '.', '%', '?', '&', '=', ':' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var raw in rawTokens)
            {
                string sanitized = SanitizeSlug(raw);
                if (!string.IsNullOrWhiteSpace(sanitized))
                    yield return sanitized;
            }
        }

        private string BuildTopicTitle(IEnumerable<string> tokens, string languageCode)
        {
            var tokenList = tokens?.ToList() ?? new List<string>();
            if (!tokenList.Any())
                return languageCode == "fi" ? "Tietokooste" : "Insight brief";

            string joined = string.Join(" ", tokenList.Select(t => t.Replace('-', ' ')));
            var culture = languageCode == "fi" ? new CultureInfo("fi-FI") : new CultureInfo("en-US");
            return culture.TextInfo.ToTitleCase(joined);
        }

        private string FormatSearchSnapshot(string query, string snapshot)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "No external search query supplied.";

            return $"Search query reference: \"{query}\" (external snapshot omitted for local file mode).";
        }

        private string GetLoadingMessage(bool isSearch, string languageCode)
        {
            if (languageCode == "fi")
            {
                return isSearch ? "Jäsennellään hakutuloksia..." : "Kootaan uutta analyysijaksoa...";
            }

            return isSearch ? "Curating investigative findings..." : "Generating a new intelligence chapter...";
        }

        private string GetPlaceholderHtml(string key, string languageCode)
        {
            if (!PlaceholderDefaults.TryGetValue(key, out var defaults))
                return "<div class='placeholder-block'><p>Content temporarily unavailable.</p></div>";

            if (languageCode == "fi")
            {
                return $"<div class='placeholder-block'><h3>{WebUtility.HtmlEncode(defaults.TitleFi)}</h3><p>{WebUtility.HtmlEncode(defaults.BodyFi)}</p></div>";
            }

            return $"<div class='placeholder-block'><h3>{WebUtility.HtmlEncode(defaults.TitleEn)}</h3><p>{WebUtility.HtmlEncode(defaults.BodyEn)}</p></div>";
        }
    }
}