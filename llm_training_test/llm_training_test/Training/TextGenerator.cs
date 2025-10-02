using System;
using System.Collections.Generic;
using TinyGptDemo.Model;
using TinyGptDemo.Tokenization;
using TinyGptDemo.Utils;

namespace TinyGptDemo.Training
{
    public sealed class TextGenerator
    {
        private readonly TinyGpt model;
        private readonly TextDataset dataset;
        private readonly ITokenizer tokenizer;
        private readonly ModelConfig modelConfig;
        private readonly TokenizationMode mode;
        private readonly Random rng;

        public TextGenerator(TinyGpt model, TextDataset dataset, ITokenizer tokenizer, ModelConfig modelConfig, TokenizationMode mode, Random rng)
        {
            this.model = model;
            this.dataset = dataset;
            this.tokenizer = tokenizer;
            this.modelConfig = modelConfig;
            this.mode = mode;
            this.rng = rng;
        }

        public void Repl()
        {
            while (true)
            {
                Console.Write("\nPrompt|length|temperature: ");
                string? line = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (string.Equals(line.Trim(), "q", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var request = ParseRequest(line);
                if (request == null)
                {
                    Console.WriteLine("Could not parse request. Use format prompt|length|temperature.");
                    continue;
                }

                var promptTokens = tokenizer.Tokenize(request.Value.Prompt);
                var promptIds = dataset.ToIds(promptTokens);

                if (promptIds.Count == 0)
                {
                    Console.WriteLine("Prompt contains no known tokens.");
                    continue;
                }

                var generatedIds = Generate(promptIds, request.Value.Length, request.Value.Temperature);
                var generatedTokens = ToTokens(generatedIds);

                Console.WriteLine(string.Join(" ", generatedTokens));
            }
        }

        private IReadOnlyList<int> Generate(List<int> promptIds, int length, float temperature)
        {
            var context = new List<int>(promptIds);

            for (int step = 0; step < length; step++)
            {
                var input = BuildModelInput(context);
                var cache = model.Forward(input);

                int vocabSize = dataset.VocabSize;
                var logits = new float[vocabSize];
                int T = modelConfig.ContextLength;

                for (int v = 0; v < vocabSize; v++)
                {
                    logits[v] = cache.Logits[0, T - 1, v];
                }

                var probs = ProbabilityUtils.Softmax(logits, temperature);
                int nextId = ProbabilityUtils.Sample(probs, rng);

                context.Add(nextId);

                if (nextId == dataset.EosId)
                {
                    break;
                }
            }

            return context;
        }

        private int[,] BuildModelInput(List<int> context)
        {
            int ctxLen = modelConfig.ContextLength;
            var buffer = new int[1, ctxLen];

            int start = Math.Max(0, context.Count - ctxLen);
            int sliceLength = context.Count - start;
            int pad = ctxLen - sliceLength;

            for (int i = 0; i < pad; i++)
            {
                buffer[0, i] = dataset.EosId;
            }

            for (int i = 0; i < sliceLength && i < ctxLen; i++)
            {
                buffer[0, pad + i] = context[start + i];
            }

            return buffer;
        }

        private IReadOnlyList<string> ToTokens(IReadOnlyList<int> ids)
        {
            var tokens = new List<string>(ids.Count);
            foreach (int id in ids)
            {
                tokens.Add(dataset.Vocabulary[id]);
            }
            return tokens;
        }

        private (string Prompt, int Length, float Temperature)? ParseRequest(string line)
        {
            string prompt = line;
            int length = mode == TokenizationMode.Sentence ? 4 : 16;
            float temperature = 1f;

            string[] parts = line.Split('|');
            if (parts.Length >= 1)
            {
                prompt = parts[0];
            }

            if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedLen))
            {
                length = Math.Max(1, parsedLen);
            }

            if (parts.Length >= 3 && float.TryParse(parts[2], out float parsedTemp))
            {
                temperature = MathF.Max(0.05f, parsedTemp);
            }

            return (prompt, length, temperature);
        }
    }
}