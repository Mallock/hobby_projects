using TinyGptDemo.Tokenization;

namespace TinyGptDemo.Utils
{
    public static class CliExamples
    {
        public static string ForMode(TokenizationMode mode) =>
            mode == TokenizationMode.Sentence
                ? "Hello world.|4|0.8"
                : "hello world|16|0.9";
    }
}