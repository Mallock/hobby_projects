using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TransformerNavigator
{
    // Direct llama.cpp client (OpenAI-compatible /v1/chat/completions)
    public sealed class LlamaCppChatClient : IChatClient
    {
        private const int MaxConversationMessages = 3;

        private readonly List<Message> _conversationHistory = new();
        private readonly HttpClient _http;
        private readonly Uri _endpoint;

        private string _finalInstructionMessage = null;
        private string _model;
        private double? _temperature;
        private readonly int? _maxTokens;
        public LlamaCppChatClient(
            string model,
            string baseUrl = "http://0.0.0.0:1337",
            string apiKey = "secret-key-123",
            string systemMessage = null,
            double? temperature = 0.9,
            int? maxTokens = 8192)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model));

            _model = model;
            _temperature = temperature;
            _maxTokens = maxTokens;

            var root = new Uri(baseUrl.TrimEnd('/') + "/");
            _endpoint = new Uri(root, "v1/chat/completions");

            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(8600);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(systemMessage))
                _conversationHistory.Add(new Message { role = "system", content = systemMessage });
        }

        public void SetTemperature(double? temperature)
        {
            if (temperature.HasValue && (temperature < 0 || temperature > 2))
                throw new ArgumentOutOfRangeException(nameof(temperature), "Temperature must be between 0 and 2.");
            _temperature = temperature;
        }

        public void AddUserMessage(string message)
        {
            _conversationHistory.Add(new Message { role = "user", content = message });
            TrimHistory();
        }

        public void AddAssistantMessage(string message)
        {
            _conversationHistory.Add(new Message { role = "assistant", content = message });
            TrimHistory();
        }

        public void SetFinalInstructionMessage(string instruction)
            => _finalInstructionMessage = instruction;

        private static string Truncate(string s, int max) =>
    string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));

        private static string ExtractServerErrorMessage(string jsonBody)
        {
            if (string.IsNullOrWhiteSpace(jsonBody)) return null;
            try
            {
                using var doc = JsonDocument.Parse(jsonBody);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;

                // OpenAI-style: { "error": { "message": "...", ... } }
                if (root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var msg))
                        return msg.GetString();
                    if (error.ValueKind == JsonValueKind.String)
                        return error.GetString();
                }

                // Generic: { "message": "..." }
                if (root.TryGetProperty("message", out var plain))
                    return plain.GetString();
            }
            catch { }
            return null;
        }

        private static bool IsTransient(System.Net.HttpStatusCode code) =>
            code == System.Net.HttpStatusCode.RequestTimeout   // 408
            || (int)code == 429                                // Too Many Requests
            || code == System.Net.HttpStatusCode.InternalServerError   // 500
            || code == System.Net.HttpStatusCode.BadGateway            // 502
            || code == System.Net.HttpStatusCode.ServiceUnavailable    // 503
            || code == System.Net.HttpStatusCode.GatewayTimeout;       // 504

        private static TimeSpan GetRetryDelay(System.Net.Http.HttpResponseMessage response, int attempt, TimeSpan baseDelay)
        {
            // Honor Retry-After on 429 if present
            if (response != null && (int)response.StatusCode == 429)
            {
                if (response.Headers.TryGetValues("Retry-After", out var vals))
                {
                    if (int.TryParse(vals.FirstOrDefault(), out var seconds) && seconds >= 0)
                        return TimeSpan.FromSeconds(seconds);
                }
            }
            // Exponential backoff with jitter, cap at ~4s
            var ms = baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            var jitter = Random.Shared.Next(50, 250);
            var total = Math.Min(4000, ms + jitter);
            return TimeSpan.FromMilliseconds(total);
        }


        public async Task<string> GetChatCompletionAsync(CancellationToken ct = default)
        {
            var messagesToSend = new List<Message>(_conversationHistory);
            if (!string.IsNullOrWhiteSpace(_finalInstructionMessage))
                messagesToSend.Add(new Message { role = "user", content = _finalInstructionMessage });

            const int maxAttempts = 3;
            TimeSpan baseDelay = TimeSpan.FromMilliseconds(400);
            Exception lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    // Säädöt yrityksen mukaan: alempi lämpö ja pidempi ennuste, JSON-moodi
                    double temp = _temperature ?? 0.7;
                    if (attempt >= 2) temp = Math.Min(temp, 0.35);
                    if (attempt >= 3) temp = Math.Min(temp, 0.2);

                    int? nPredict = _maxTokens ?? 2048;
                    if (attempt >= 2 && nPredict < 4096) nPredict = 4096;
                    if (attempt >= 3 && nPredict < 6144) nPredict = 6144;

                    var request = new ChatCompletionRequest
                    {
                        model = _model,
                        messages = messagesToSend,
                        temperature = temp,
                        max_tokens = _maxTokens,           // osa palvelimista käyttää tätä
                        n_predict = nPredict,              // llama.cpp käyttää tätä
                        top_p = 0.95,
                        seed = 42,
                        presence_penalty = 0.0,
                        frequency_penalty = 0.0,
                        response_format = new { type = "json_object" }, // JSON-moodi, jos tuettu
                        stream = false
                    };

                    var json = JsonSerializer.Serialize(request, JsonOptions);
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var response = await _http.PostAsync(_endpoint, content, ct);
                    var respJson = await response.Content.ReadAsStringAsync(ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        var code = (int)response.StatusCode;
                        var message = ExtractServerErrorMessage(respJson) ?? response.ReasonPhrase ?? "Unknown error";
                        var ex = new ChatClientException($"LLM HTTP {code} {response.StatusCode}: {message}")
                        {
                            StatusCode = code,
                            ResponseBody = Truncate(respJson, 4000)
                        };
                        if (IsTransient(response.StatusCode) && attempt < maxAttempts)
                        {
                            await Task.Delay(GetRetryDelay(response, attempt, baseDelay), ct);
                            continue;
                        }
                        throw ex;
                    }

                    ChatCompletionResponse payload;
                    try
                    {
                        payload = JsonSerializer.Deserialize<ChatCompletionResponse>(respJson, JsonOptions);
                    }
                    catch (Exception jex)
                    {
                        throw new ChatClientException("Failed to parse LLM response JSON.", jex)
                        {
                            ResponseBody = Truncate(respJson, 4000)
                        };
                    }

                    var reply = payload?.choices?.FirstOrDefault()?.message?.content;
                    if (string.IsNullOrWhiteSpace(reply))
                    {
                        var ex = new ChatClientException("LLM returned an empty response or no choices.")
                        {
                            ResponseBody = Truncate(respJson, 4000)
                        };
                        if (attempt < maxAttempts)
                        {
                            await Task.Delay(GetRetryDelay(null, attempt, baseDelay), ct);
                            continue;
                        }
                        throw ex;
                    }

                    AddAssistantMessage(reply);
                    return reply;
                }
                catch (OperationCanceledException oce) when (ct.IsCancellationRequested)
                {
                    throw new ChatClientException("Chat completion was canceled by caller.", oce);
                }
                catch (TaskCanceledException tce)
                {
                    lastException = tce;
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(GetRetryDelay(null, attempt, baseDelay), ct);
                        continue;
                    }
                    throw new ChatClientException("Chat completion request timed out.", tce);
                }
                catch (HttpRequestException hre)
                {
                    lastException = hre;
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(GetRetryDelay(null, attempt, baseDelay), ct);
                        continue;
                    }
                    throw new ChatClientException("Network/HTTP error while calling LLM endpoint.", hre);
                }
                catch (ChatClientException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(GetRetryDelay(null, attempt, baseDelay), ct);
                        continue;
                    }
                    throw new ChatClientException("Unexpected error during chat completion.", ex);
                }
            }

            throw new ChatClientException("Chat completion failed after multiple attempts.", lastException);
        }

        private void TrimHistory()
        {
            int nonSystemCount = _conversationHistory.Count(m => m.role != "system");
            int toRemove = nonSystemCount - MaxConversationMessages;

            if (toRemove <= 0) return;

            for (int i = 0; i < _conversationHistory.Count && toRemove > 0;)
            {
                if (_conversationHistory[i].role == "system")
                {
                    i++;
                    continue;
                }
                _conversationHistory.RemoveAt(i);
                toRemove--;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private sealed class ChatCompletionRequest
        {
            public string model { get; set; }
            public List<Message> messages { get; set; }
            public double? temperature { get; set; }
            public int? max_tokens { get; set; }
            public bool? stream { get; set; }

            // Uudet, llama.cpp OpenAI-compat kentät (ohitetaan jos palvelin ei tue)
            public object response_format { get; set; }          // e.g., new { type = "json_object" }
            public int? n_predict { get; set; }                  // llama.cpp ennuste-pituus
            public double? top_p { get; set; }
            public int? seed { get; set; }
            public double? presence_penalty { get; set; }
            public double? frequency_penalty { get; set; }
            public string[] stop { get; set; }
        }

        private sealed class ChatCompletionResponse
        {
            public string id { get; set; }
            public string @object { get; set; }
            public long created { get; set; }
            public List<Choice> choices { get; set; }
            public Usage usage { get; set; }
        }

        private sealed class Choice
        {
            public int index { get; set; }
            public Message message { get; set; }
            public string finish_reason { get; set; }
        }

        private sealed class Usage
        {
            public int prompt_tokens { get; set; }
            public int completion_tokens { get; set; }
            public int total_tokens { get; set; }
        }

        private sealed class Message
        {
            public string role { get; set; }   // "system" | "user" | "assistant"
            public string content { get; set; }
        }
    }
    public sealed class ChatClientException : Exception
    {
        public int? StatusCode { get; init; }
        public string ResponseBody { get; init; }

        public ChatClientException(string message, Exception inner = null) : base(message, inner) { }

        public override string ToString()
        {
            var sb = new StringBuilder(base.ToString());
            if (StatusCode.HasValue) sb.Append($" [StatusCode={StatusCode.Value}]");
            if (!string.IsNullOrEmpty(ResponseBody)) sb.AppendLine().AppendLine("[Response]").AppendLine(ResponseBody);
            return sb.ToString();
        }
    }
}
