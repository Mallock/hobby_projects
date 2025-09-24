using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;

public class OpenAIChatClient
{
    private const int MaxConversationMessages = 3;

    private readonly List<ChatMessage> _conversationHistory = new();
    private string _finalInstructionMessage = null;
    private readonly string _model;
    private readonly ChatClient _chat;
    private double? _temperature;

    public OpenAIChatClient(string model = "gpt-3.5-turbo", string systemMessage = null, double? temperature = 0.9)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");

        _model = model;
        _temperature = temperature;
        _chat = new ChatClient(_model, apiKey);

        if (!string.IsNullOrWhiteSpace(systemMessage))
            _conversationHistory.Add(SystemChatMessage.CreateSystemMessage(systemMessage));
    }

    public void SetTemperature(double? temperature)
    {
        if (temperature.HasValue && (temperature < 0 || temperature > 2))
            throw new ArgumentOutOfRangeException(nameof(temperature), "Temperature must be between 0 and 2.");
        _temperature = temperature;
    }

    public void AddUserMessage(string message)
    {
        _conversationHistory.Add(UserChatMessage.CreateUserMessage(message));
        TrimHistory();
    }

    public void AddAssistantMessage(string message)
    {
        _conversationHistory.Add(AssistantChatMessage.CreateAssistantMessage(message));
        TrimHistory();
    }

    public void SetFinalInstructionMessage(string instruction)
        => _finalInstructionMessage = instruction;

    public async Task<string> GetChatCompletionAsync()
    {
        var messagesToSend = new List<ChatMessage>(_conversationHistory);

        if (!string.IsNullOrWhiteSpace(_finalInstructionMessage))
            messagesToSend.Add(UserChatMessage.CreateUserMessage(_finalInstructionMessage));

        var options = new ChatCompletionOptions
        {
            Temperature = (float)_temperature
        };

        var response = await _chat.CompleteChatAsync(messagesToSend, options);
        var reply = response.Value.Content;

        AddAssistantMessage(reply.First().Text);

        return reply.First().Text;
    }

    public void ClearHistory()
    {
        var systemMessages = _conversationHistory.Where(m => m is SystemChatMessage).ToList();
        _conversationHistory.Clear();
        _conversationHistory.AddRange(systemMessages);
    }

    private void TrimHistory()
    {
        int nonSystemCount = _conversationHistory.Count(m => m is not SystemChatMessage);
        int toRemove = nonSystemCount - MaxConversationMessages;

        if (toRemove <= 0)
            return;

        for (int i = 0; i < _conversationHistory.Count && toRemove > 0;)
        {
            if (_conversationHistory[i] is SystemChatMessage)
            {
                i++;
                continue;
            }

            _conversationHistory.RemoveAt(i);
            toRemove--;
        }
    }
}