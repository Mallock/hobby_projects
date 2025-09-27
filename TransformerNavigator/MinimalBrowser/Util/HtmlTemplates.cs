namespace MinimalBrowser.Util
{
    public sealed class ChatPageOptions
    {
        public string Title { get; set; } = "Chat Assistant";
        public bool ShowTopbar { get; set; } = true;

        // Layout
        public string ChatWidth { get; set; } = "90%";
        public string ChatMargin { get; set; } = "3em";   // shorthand fallback

        // New: side-specific margins (override ChatMargin if provided)
        public string ChatMarginTop { get; set; } = null;
        public string ChatMarginRight { get; set; } = null;
        public string ChatMarginBottom { get; set; } = null;
        public string ChatMarginLeft { get; set; } = null;

        // Features
        public bool EnablePrintButton { get; set; } = true;
        public bool EnableHighlightJs { get; set; } = true;
        public bool EnableCodeCopyButtons { get; set; } = true;

        // CDN resources (pinned versions)
        public string HighlightThemeCssHref { get; set; } =
            "https://cdn.jsdelivr.net/npm/highlight.js@11.9.0/styles/github-dark.min.css";
        public string HighlightJsHref { get; set; } =
            "https://cdn.jsdelivr.net/npm/highlight.js@11.9.0/lib/common.min.js";
        public string MarkedJsHref { get; set; } =
            "https://cdn.jsdelivr.net/npm/marked@12.0.1/marked.min.js";
        public string DomPurifyJsHref { get; set; } =
            "https://cdn.jsdelivr.net/npm/dompurify@3.0.8/dist/purify.min.js";

        // Extension points
        public string ExtraCss { get; set; } = null;
        public string ExtraHeadHtml { get; set; } = null;
        public string ExtraBodyEndHtml { get; set; } = null;
    }

    public static class HtmlTemplates
    {
        public static string BuildChatPageHtml(ChatPageOptions options = null)
        {
            options ??= new ChatPageOptions();

            var css = CssBlock(options);
            var headLibs = HeadLibraries(options);
            var bodyTopbar = options.ShowTopbar ? TopbarHtml(options) : "";
            var js = JsBlock(options);

            return $@"<!doctype html>
<html> <head> <meta charset='utf-8'> <title>{EscapeHtml(options.Title)}</title> {headLibs} <style> {css} </style> {options.ExtraHeadHtml ?? ""} </head> <body> {bodyTopbar} <div id='chat'></div> <script> {js} </script> {options.ExtraBodyEndHtml ?? ""} </body> </html>";
        }
        private static string HeadLibraries(ChatPageOptions o)
        {
            // Keep CSS theme optional in case you want to bundle styles offline later
            var hlCss = o.EnableHighlightJs && !string.IsNullOrWhiteSpace(o.HighlightThemeCssHref)
                ? $"<link rel='stylesheet' href='{o.HighlightThemeCssHref}'>"
                : "";

            var marked = $"<script src='{o.MarkedJsHref}'></script>";
            var dompurify = $"<script src='{o.DomPurifyJsHref}'></script>";
            var highlight = o.EnableHighlightJs
                ? $"<script src='{o.HighlightJsHref}'></script>"
                : "";

            return $"{hlCss}\n{marked}\n{dompurify}\n{highlight}";
        }

        private static string CssBlock(ChatPageOptions o)
        {
            // Resolve side-specific margins with fallback to ChatMargin
            string mt = o.ChatMarginTop ?? o.ChatMargin ?? "3em";
            string mr = o.ChatMarginRight ?? o.ChatMargin ?? "3em";
            string mb = o.ChatMarginBottom ?? o.ChatMargin ?? "3em";
            string ml = o.ChatMarginLeft ?? o.ChatMargin ?? "3em";

            return $@"
/* Theme tokens */
:root {{
--bg: #12172e;
--fg: #e3eafc;
--muted: #8ea2c8;

--card-1: #192245;
--card-2: #232950;

--user-1: #26337d;
--user-2: #334592;

--accent: #223469;
--accent-hover: #2b447a;
--separator: #29304b;
--shadow: #0002;

--code-bg: #181f3a;
--code-border: #2b3c60;

--scrollbar-bg: #191f37;
--scrollbar-thumb: #223469;

--font: 'Segoe UI', system-ui, -apple-system, SegoeUI, Roboto, Arial, sans-serif;
}}

html, body {{
margin: 0;
padding: 0;
background: var(--bg);
color: var(--fg);
font-family: var(--font);
}}

#topbar {{
position: sticky;
top: 0; left: 0; right: 0;
background: rgba(16, 20, 38, 0.97);
z-index: 10;
display: flex;
align-items: center;
justify-content: space-between;
padding: 0.6em 2em;
border-bottom: 1px solid var(--separator);
box-shadow: 0 2px 8px var(--shadow);
}}
#topbar h1 {{
font-size: 1.3em;
letter-spacing: 0.06em;
margin: 0;
color: #9ec8fa;
font-weight: 500;
}}
#topbar button {{
background: var(--accent);
color: var(--fg);
border: 0;
border-radius: 6px;
padding: 0.45em 1.1em;
font-size: 1em;
cursor: pointer;
transition: background .18s;
}}
#topbar button:hover {{ background: var(--accent-hover); }}

#chat {{
width: {o.ChatWidth};
max-width: none;
margin: {mt} {mr} {mb} {ml};  /* top right bottom left */
padding: 0;
}}

.message {{
display: flex;
flex-direction: column;
align-items: flex-start;
width: 95%;
max-width: 95%;
margin: 1.2em 0;
padding: 1.2em 4vw;
background: linear-gradient(110deg, var(--card-1) 90%, var(--card-2) 100%);
box-shadow: 0 2px 8px #1115;
font-size: 1em;
line-height: 1.0;
white-space: pre-wrap;
word-break: break-word;
overflow-wrap: break-word;
overflow: hidden;
border-radius: 0;
}}
.message.user {{
background: linear-gradient(110deg, var(--user-1) 80%, var(--user-2) 100%);
color: #d3e5ff;
}}
.message.assistant {{ }}
.message .meta {{
font-size: 0.80em;
color: var(--muted);
margin-bottom: 0.5em;
user-select: none;
}}

code {{
font-family: 'Cascadia Code', Consolas, monospace;
background: var(--code-bg);
border: 1px solid var(--code-border);
border-radius: 6px;
padding: 0 0.25em;
font-size: 1em;
}}
pre {{
background: var(--code-bg);
border: 1px solid var(--code-border);
border-radius: 8px;
padding: 1.15em 1.5em;
overflow: auto;
margin-bottom: 0.4em;
position: relative;
}}
pre code {{
background: transparent;
border: 0;
padding: 0;
font-size: 1.08em;
}}

.code-copy-btn {{
position: absolute;
top: 10px;
right: 16px;
background: #26337d;
color: #cfe7ff;
border: 0;
border-radius: 5px;
font-size: 0.95em;
padding: 3px 13px;
cursor: pointer;
opacity: 0.7;
transition: background 0.13s, opacity 0.15s;
z-index: 2;
display: none;
}}
pre:hover .code-copy-btn {{ display: inline-block; opacity: 1; }}
.code-copy-btn:active {{ background: #173060; }}

@media (max-width:700px) {{
#chat {{ width: 100vw; padding: 0; margin: 0; }}
.message {{ font-size: 1em; padding: 0.7em 2vw; }}
}}

::-webkit-scrollbar {{ width: 8px; background: var(--scrollbar-bg); }}
::-webkit-scrollbar-thumb {{ background: var(--scrollbar-thumb); border-radius: 5px; }}

{(string.IsNullOrWhiteSpace(o.ExtraCss) ? "" : o.ExtraCss)}";
        }

        private static string TopbarHtml(ChatPageOptions o)
        {
            var printBtn = o.EnablePrintButton
                ? "<button onclick='printToPDF()' title='Print or Save as PDF'>🖨️ Print PDF</button>"
                : "";
            return $@"<div id='topbar'>
<h1>{EscapeHtml(o.Title)}</h1> {printBtn} </div>";
        }
        private static string JsBlock(ChatPageOptions o)
        {
            // Embed booleans so the JS can conditionally execute features
            var enableCopy = o.EnableCodeCopyButtons ? "true" : "false";
            var enableHl = o.EnableHighlightJs ? "true" : "false";

            return $@"
(function() {{
'use strict';

const ENABLE_COPY = {enableCopy};
const ENABLE_HL = {enableHl};

const ChatUI = {{
currentAssistant: null,

nowTime() {{
  const d = new Date();
  return d.toLocaleTimeString([], {{hour:'2-digit', minute:'2-digit'}});
}},

appendMessage(role, md) {{
  const msg = document.createElement('div');
  msg.className = 'message ' + role;
  msg.innerHTML = `<div class='meta'>${{role==='user'?'You':'Assistant'}} · <span>${{ChatUI.nowTime()}}</span></div>`;
  const content = document.createElement('div');
  ChatUI.renderMarkdown(content, md);
  msg.appendChild(content);
  document.getElementById('chat').appendChild(msg);
  if (role === 'assistant') ChatUI.currentAssistant = content;
  window.scrollTo(0, document.body.scrollHeight);
}},

updateAssistantMessage(md) {{
  if (!ChatUI.currentAssistant) {{
    ChatUI.appendMessage('assistant', md);
    return;
  }}
  ChatUI.renderMarkdown(ChatUI.currentAssistant, md);
}},

finishAssistantMessage() {{
  ChatUI.currentAssistant = null;
}},

renderMarkdown(el, md) {{
  try {{
    if (window.marked && typeof marked.setOptions === 'function') {{
      marked.setOptions({{ gfm:true, breaks:true, headerIds:false, mangle:false }});
      let html = marked.parse(md ?? '');
      const temp = document.createElement('div');
      const safeHtml = window.DOMPurify
        ? DOMPurify.sanitize(html, {{ USE_PROFILES: {{ html:true }} }})
        : html;
      temp.innerHTML = safeHtml;

      if (ENABLE_COPY) {{
        temp.querySelectorAll('pre').forEach(pre => {{
          const btn = document.createElement('button');
          btn.className = 'code-copy-btn';
          btn.innerText = 'Copy';
          btn.title = 'Copy code to clipboard';
          btn.onclick = function() {{
            const code = pre.querySelector('code');
            if (code && navigator.clipboard) {{
              navigator.clipboard.writeText(code.innerText).then(() => {{
                btn.innerText = 'Copied!';
                setTimeout(() => {{ btn.innerText = 'Copy'; }}, 1700);
              }});
            }}
          }};
          pre.insertBefore(btn, pre.firstChild);
        }});
      }}

      el.innerHTML = temp.innerHTML;

      if (ENABLE_HL && window.hljs && typeof hljs.highlightElement === 'function') {{
        el.querySelectorAll('pre code').forEach(e => {{
          try {{ hljs.highlightElement(e); }} catch (_e) {{}}
        }});
      }}
    }} else {{
      el.textContent = md ?? '';
    }}
  }} catch (_err) {{
    el.textContent = md ?? '';
  }}
}},
}};

// Expose the functions expected by the C# renderer (stable API)
window.appendMessage = ChatUI.appendMessage;
window.updateAssistantMessage = ChatUI.updateAssistantMessage;
window.finishAssistantMessage = ChatUI.finishAssistantMessage;

window.printToPDF = function() {{
try {{
document.querySelectorAll('#topbar button').forEach(btn => btn.style.visibility = 'hidden');
window.print();
}} finally {{
setTimeout(() => {{
document.querySelectorAll('#topbar button').forEach(btn => btn.style.visibility = 'visible');
}}, 1000);
}}
}};
}})();";
        }

        private static string EscapeHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }
    }
}