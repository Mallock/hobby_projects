using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace TransformerNavigator
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Text.Encodings.Web;

    public class MuiHtmlTemplateBuilder
    {
        private class MenuItem { public string Label { get; set; } = ""; public string Icon { get; set; } = ""; }
        private class TagItem { public string Label { get; set; } = ""; public string Icon { get; set; } = "label"; }
        private class CardItem { public string Title { get; set; } = ""; public string Body { get; set; } = ""; public string Href { get; set; } = ""; }

        private string _documentTitle = "MUI Dark Theme Portal";
        private string _welcomeTitle = "Welcome";
        private string _welcomeText = "Explore curated routes below.";

        private readonly List<MenuItem> _menus = new List<MenuItem>();
        private readonly List<TagItem> _tags = new List<TagItem>();
        private readonly List<CardItem> _cards = new List<CardItem>();

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        public MuiHtmlTemplateBuilder SetDocumentTitle(string title)
        {
            _documentTitle = string.IsNullOrWhiteSpace(title) ? _documentTitle : title;
            return this;
        }

        public MuiHtmlTemplateBuilder SetWelcome(string title, string text)
        {
            _welcomeTitle = string.IsNullOrWhiteSpace(title) ? _welcomeTitle : title;
            _welcomeText = string.IsNullOrWhiteSpace(text) ? _welcomeText : text;
            return this;
        }

        public MuiHtmlTemplateBuilder AddMenu(string label, string iconName)
        {
            _menus.Add(new MenuItem { Label = label ?? "", Icon = iconName ?? "home" });
            return this;
        }

        public MuiHtmlTemplateBuilder AddTag(string label, string iconName = "label")
        {
            _tags.Add(new TagItem { Label = label ?? "", Icon = iconName ?? "label" });
            return this;
        }

        public MuiHtmlTemplateBuilder AddCard(string title, string body, string href = "")
        {
            _cards.Add(new CardItem { Title = title ?? "", Body = body ?? "", Href = href ?? "" });
            return this;
        }

        public string Build()
        {
            if (_menus.Count == 0)
            {
                AddMenu("Home", "home").AddMenu("Explore", "explore").AddMenu("About", "info");
            }
            if (_tags.Count == 0)
            {
                AddTag("Insights").AddTag("Briefs").AddTag("Scenarios");
            }
            if (_cards.Count == 0)
            {
                for (int i = 1; i <= 6; i++)
                    AddCard($"Exploration route {i}", "A descriptive path into an adjacent topic area.", "/explore-route-" + i);
            }

            var dataJson = new
            {
                menus = _menus,
                tags = _tags,
                cards = _cards,
                welcome = new { title = _welcomeTitle, text = _welcomeText }
            };

            string json = JsonSerializer.Serialize(dataJson, JsonOpts);

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("  <head>");
            sb.AppendLine("    <meta charset=\"UTF-8\" />");
            sb.AppendLine($"    <title>{HtmlEscape(_documentTitle)}</title>");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            sb.AppendLine("    <script crossorigin src=\"https://unpkg.com/react@18/umd/react.development.js\"></script>");
            sb.AppendLine("    <script crossorigin src=\"https://unpkg.com/react-dom@18/umd/react-dom.development.js\"></script>");
            sb.AppendLine("    <script src=\"https://unpkg.com/@emotion/react@11.11.1/dist/emotion-react.umd.min.js\"></script>");
            sb.AppendLine("    <script src=\"https://unpkg.com/@emotion/styled@11.11.0/dist/emotion-styled.umd.min.js\"></script>");
            sb.AppendLine("    <script src=\"https://unpkg.com/@mui/material@5.15.14/umd/material-ui.development.js\"></script>");
            sb.AppendLine("    <script src=\"https://unpkg.com/@babel/standalone/babel.min.js\"></script>");
            sb.AppendLine("    <link href=\"https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:wght,FILL,GRAD@400,0,0\" rel=\"stylesheet\" />");
            sb.AppendLine("    <style>");
            sb.AppendLine("      body { margin: 0; font-family: Roboto, sans-serif; }");
            sb.AppendLine("      .content { padding: 2rem; }");
            sb.AppendLine("      .material-symbols-outlined {");
            sb.AppendLine("        font-variation-settings: 'FILL' 0, 'wght' 400, 'GRAD' 0, 'opsz' 24;");
            sb.AppendLine("        vertical-align: middle;");
            sb.AppendLine("      }");
            sb.AppendLine("    </style>");
            sb.AppendLine("  </head>");
            sb.AppendLine("  <body>");
            sb.AppendLine("    <header id=\"app-header\"></header>");
            sb.AppendLine("    <main id=\"root\"></main>");
            sb.AppendLine("    <script type=\"text/babel\" data-presets=\"env,react\">");
            sb.AppendLine("      const { AppBar, Toolbar, Typography, CssBaseline, Container, ThemeProvider, createTheme, Button, Box, Grid, Card, CardContent, CardActions, Chip, Stack } = MaterialUI;");
            sb.AppendLine("      const DATA = " + json + ";");
            sb.AppendLine("      function Icon({ name, size = 20 }) {");
            sb.AppendLine("        return (<span className=\"material-symbols-outlined\" style={{ fontSize: size, lineHeight: 0 }} aria-hidden=\"true\">{name}</span>);");
            sb.AppendLine("      }");
            sb.AppendLine("      const darkTheme = createTheme({ palette: { mode: 'dark', primary: { main: '#90caf9' }, secondary: { main: '#f48fb1' }}});");
            sb.AppendLine("      function LinkCard({ title, body, href }) {");
            sb.AppendLine("        const isExternal = href && href.startsWith('http');");
            sb.AppendLine("        const props = isExternal ? { target: '_blank', rel: 'noopener' } : {};");
            sb.AppendLine("        return (");
            sb.AppendLine("          <Card sx={{ borderRadius: 3, boxShadow: 3 }}>");
            sb.AppendLine("            <CardContent>");
            sb.AppendLine("              <Typography variant=\"h6\" gutterBottom>{title}</Typography>");
            sb.AppendLine("              <Typography variant=\"body2\">{body}</Typography>");
            sb.AppendLine("            </CardContent>");
            sb.AppendLine("            <CardActions>");
            sb.AppendLine("              {href ? (<Button size=\"small\" color=\"primary\" component=\"a\" href={href} {...props}>Follow route</Button>) : null}");
            sb.AppendLine("            </CardActions>");
            sb.AppendLine("          </Card>");
            sb.AppendLine("        );");
            sb.AppendLine("      }");
            sb.AppendLine("      function App() {");
            sb.AppendLine("        return (");
            sb.AppendLine("          <ThemeProvider theme={darkTheme}>");
            sb.AppendLine("            <CssBaseline />");
            sb.AppendLine("            <AppBar position=\"static\" color=\"primary\" component=\"nav\">");
            sb.AppendLine("              <Toolbar sx={{ gap: 2, flexWrap: 'wrap' }}>");
            sb.AppendLine("                <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', flexGrow: 1 }}>");
            sb.AppendLine("                  {DATA.menus.map((m, idx) => (<Button key={idx} color=\"inherit\" startIcon={<Icon name={m.icon} />}>{m.label}</Button>))}");
            sb.AppendLine("                </Box>");
            sb.AppendLine("                <Stack direction=\"row\" spacing={1} sx={{ flexWrap: 'wrap' }}>");
            sb.AppendLine("                  {DATA.tags.map((t, idx) => (<Chip key={idx} variant=\"outlined\" color=\"secondary\" label={t.label} icon={<Icon name={t.icon} />} />))}");
            sb.AppendLine("                </Stack>");
            sb.AppendLine("              </Toolbar>");
            sb.AppendLine("            </AppBar>");
            sb.AppendLine("            <Container className=\"content\" component=\"section\">");
            sb.AppendLine("              <Typography variant=\"h4\" gutterBottom component=\"h1\">{DATA.welcome.title}</Typography>");
            sb.AppendLine("              <Typography paragraph>{DATA.welcome.text}</Typography>");
            sb.AppendLine("              <Grid container spacing={3}>");
            sb.AppendLine("                {DATA.cards.map((c, i) => (");
            sb.AppendLine("                  <Grid item xs={12} sm={6} md={4} key={i}><LinkCard title={c.title} body={c.body} href={c.href} /></Grid>");
            sb.AppendLine("                ))}");
            sb.AppendLine("              </Grid>");
            sb.AppendLine("            </Container>");
            sb.AppendLine("          </ThemeProvider>");
            sb.AppendLine("        );");
            sb.AppendLine("      }");
            sb.AppendLine("      const root = ReactDOM.createRoot(document.getElementById('root'));");
            sb.AppendLine("      root.render(<App />);");
            sb.AppendLine("    </script>");
            sb.AppendLine("  </body>");
            sb.AppendLine("</html>");
            return sb.ToString();
        }

        private static string HtmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}
