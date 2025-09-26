using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MinimalBrowser
{
    public sealed class ChatMessage
    {
        public string role { get; set; }     // "system" | "user" | "assistant"
        public string content { get; set; }
    }

    public sealed class LlamaSseClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly Uri _endpoint;
        private readonly string _model;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public LlamaSseClient(
            string model,
            string baseUrl = null,
            string apiKey = null,
            TimeSpan? timeout = null)
        {
            _model = string.IsNullOrWhiteSpace(model)
                ? (Environment.GetEnvironmentVariable("LLAMA_MODEL") ?? "gpt")
                : model;

            var baseU = baseUrl ?? Environment.GetEnvironmentVariable("LLAMA_BASE_URL") ?? "http://0.0.0.0:1337";
            _endpoint = BuildEndpoint(baseU);

            _http = new HttpClient
            {
                Timeout = timeout ?? TimeSpan.FromMinutes(30)
            };

            var key = apiKey ?? Environment.GetEnvironmentVariable("LLAMA_API_KEY") ?? "secret-key-123";
            if (!string.IsNullOrWhiteSpace(key))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

            // We’ll request SSE but will gracefully handle non-SSE replies too.
            _http.DefaultRequestHeaders.Accept.Clear();
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private static Uri BuildEndpoint(string baseU)
        {
            // Heuristics: accept a full endpoint or a base server URL
            // - If it already contains /chat/completions, use as-is.
            // - Else if it ends with /v1, append /chat/completions.
            // - Else append /v1/chat/completions.
            if (Uri.TryCreate(baseU, UriKind.Absolute, out var full) &&
                full.AbsolutePath.TrimEnd('/').EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return full;
            }

            var trimmed = baseU.TrimEnd('/');
            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return new Uri(trimmed + "/chat/completions");

            return new Uri(trimmed + "/v1/chat/completions");
        }

        public async Task<string> StreamChatAsync(
            List<ChatMessage> history,
            Action<string> onDelta,
            double temperature = 0.7,
            int? maxTokens = null,
            int? nPredict = null,
            CancellationToken ct = default)
        {
            var req = new
            {
                model = _model,
                messages = history,
                temperature = temperature,
                max_tokens = maxTokens,
                n_predict = nPredict,
                stream = true
            };

            var json = JsonSerializer.Serialize(req, JsonOptions);
            using var httpReq = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            httpReq.Headers.Accept.Clear();
            httpReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            httpReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await _http
                .SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new Exception($"LLM HTTP {(int)resp.StatusCode} {resp.StatusCode}: {Truncate(err, 2000)}");
            }

            var contentType = resp.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();

            if (!string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                // Fallback: non-streaming JSON
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var full = TryExtractFullText(body) ?? "";
                if (full.Length > 0)
                {
                    try { onDelta?.Invoke(full); } catch { }
                }
                return full;
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var sb = new StringBuilder();
            var dataBuffer = new StringBuilder();
            bool sawAnyDelta = false;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                string line = await reader.ReadLineAsync().WaitAsync(ct).ConfigureAwait(false);
                if (line == null)
                {
                    // End-of-stream: flush any buffered event once
                    FlushEventBuffer(dataBuffer, sb, onDelta, ref sawAnyDelta);
                    break;
                }

                if (line.Length == 0)
                {
                    // blank line => end of one event
                    FlushEventBuffer(dataBuffer, sb, onDelta, ref sawAnyDelta);
                    continue;
                }

                if (line.StartsWith(":", StringComparison.Ordinal))
                {
                    // comment/keepalive; ignore
                    continue;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var s = line.Length > 5 ? line.Substring(5) : string.Empty;
                    if (s.StartsWith(" ", StringComparison.Ordinal)) s = s.Substring(1);
                    if (s == "[DONE]")
                    {
                        FlushEventBuffer(dataBuffer, sb, onDelta, ref sawAnyDelta);
                        break;
                    }
                    dataBuffer.AppendLine(s);
                }
                // ignore other SSE fields (event:, id:, retry:)
            }

            return sb.ToString();
        }

        private static void FlushEventBuffer(StringBuilder buf, StringBuilder outAll, Action<string> onDelta, ref bool sawAny)
        {
            if (buf.Length == 0) return;

            var payload = buf.ToString().Trim();
            buf.Clear();

            if (payload.Length == 0 || payload == "[DONE]") return;

            var delta = ExtractDelta(payload);
            if (!string.IsNullOrEmpty(delta))
            {
                sawAny = true;
                outAll.Append(delta);
                try { onDelta?.Invoke(delta); } catch { }
            }
        }

        private static string ExtractDelta(string jsonChunk)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonChunk);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("error", out var err))
                    return null; // let caller handle errors elsewhere

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var c0 = choices[0];

                    // Standard OpenAI streaming delta
                    if (c0.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                    {
                        if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                            return content.GetString();
                    }

                    // llama.cpp sometimes uses message.content even in stream
                    if (c0.TryGetProperty("message", out var msg) &&
                        msg.ValueKind == JsonValueKind.Object &&
                        msg.TryGetProperty("content", out var content2) &&
                        content2.ValueKind == JsonValueKind.String)
                    {
                        return content2.GetString();
                    }

                    // Some servers stream as choices[].text
                    if (c0.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                        return textEl.GetString();
                }
            }
            catch
            {
                // If not valid JSON, ignore
            }
            return null;
        }

        private static string TryExtractFullText(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var c0 = choices[0];

                    if (c0.TryGetProperty("message", out var msg) &&
                        msg.ValueKind == JsonValueKind.Object &&
                        msg.TryGetProperty("content", out var content) &&
                        content.ValueKind == JsonValueKind.String)
                        return content.GetString();

                    if (c0.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                        return textEl.GetString();

                    if (c0.TryGetProperty("delta", out var delta) &&
                        delta.ValueKind == JsonValueKind.Object &&
                        delta.TryGetProperty("content", out var dcont) &&
                        dcont.ValueKind == JsonValueKind.String)
                        return dcont.GetString();
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));

        public void Dispose() => _http?.Dispose();
    }
}