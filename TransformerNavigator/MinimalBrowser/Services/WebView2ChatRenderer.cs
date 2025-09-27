using Microsoft.Web.WebView2.WinForms;
using System;
using System.Threading.Tasks;

namespace MinimalBrowser.Services
{
    public sealed class WebView2ChatRenderer : MinimalBrowser.Controllers.IChatRenderer
    {
        private readonly WebView2 _web;

        public WebView2ChatRenderer(WebView2 web) => _web = web;

        public Task LoadAsync()
        {
            var html = Util.HtmlTemplates.BuildChatPageHtml();
            return RunOnUiAsync(() =>
            {
                // Optional: detach existing NavigationCompleted handlers if you add them later
                _web.CoreWebView2?.NavigateToString(html);
            });
        }

        public Task AppendUserMessageAsync(string text)
        {
            string safe = Util.TextEscaping.EscapeJs(text);
            return ExecScriptOnUiAsync($"appendMessage('user', `{safe}`);");
        }

        public Task UpdateAssistantMessageAsync(string fullAssistantText)
        {
            string safe = Util.TextEscaping.EscapeJs(fullAssistantText);
            return ExecScriptOnUiAsync($"updateAssistantMessage(`{safe}`);");
        }

        public Task FinishAssistantMessageAsync()
        {
            return ExecScriptOnUiAsync("finishAssistantMessage();");
        }

        // Helpers

        private Task ExecScriptOnUiAsync(string js)
        {
            return RunOnUiAsync(() => _web.CoreWebView2.ExecuteScriptAsync(js));
        }

        private Task RunOnUiAsync(Func<Task> asyncUiAction)
        {
            var tcs = new TaskCompletionSource<object>();
            if (_web.InvokeRequired)
            {
                _web.BeginInvoke(async () =>
                {
                    try { await asyncUiAction().ConfigureAwait(false); tcs.SetResult(null); }
                    catch (Exception ex) { tcs.SetException(ex); }
                });
            }
            else
            {
                try
                {
                    var task = asyncUiAction();
                    task.ContinueWith(t =>
                    {
                        if (t.IsFaulted) tcs.SetException(t.Exception.InnerException ?? t.Exception);
                        else if (t.IsCanceled) tcs.SetCanceled();
                        else tcs.SetResult(null);
                    });
                }
                catch (Exception ex) { tcs.SetException(ex); }
            }
            return tcs.Task;
        }

        private Task RunOnUiAsync(Action uiAction)
        {
            var tcs = new TaskCompletionSource<object>();
            if (_web.InvokeRequired)
            {
                _web.BeginInvoke(new Action(() =>
                {
                    try { uiAction(); tcs.SetResult(null); }
                    catch (Exception ex) { tcs.SetException(ex); }
                }));
            }
            else
            {
                try { uiAction(); tcs.SetResult(null); }
                catch (Exception ex) { tcs.SetException(ex); }
            }
            return tcs.Task;
        }
    }
}