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
            int? maxTokens = 8000)
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

        public async Task<string> GetChatCompletionAsync(CancellationToken ct = default)
        {
            var messagesToSend = new List<Message>(_conversationHistory);

            if (!string.IsNullOrWhiteSpace(_finalInstructionMessage))
                messagesToSend.Add(new Message { role = "user", content = _finalInstructionMessage });

            var request = new ChatCompletionRequest
            {
                model = _model,
                messages = messagesToSend,
                temperature = _temperature,
                max_tokens = _maxTokens,
                stream = false
            };

            var json = JsonSerializer.Serialize(request, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(_endpoint, content, ct);
            response.EnsureSuccessStatusCode();

            var respJson = await response.Content.ReadAsStringAsync(ct);
            var payload = JsonSerializer.Deserialize<ChatCompletionResponse>(respJson, JsonOptions);

            var reply = payload?.choices?.FirstOrDefault()?.message?.content ?? string.Empty;

            if (!string.IsNullOrEmpty(reply))
                AddAssistantMessage(reply);

            return reply;
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
            public int? max_tokens { get; set; }         // NEW
            public bool? stream { get; set; }
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
}
