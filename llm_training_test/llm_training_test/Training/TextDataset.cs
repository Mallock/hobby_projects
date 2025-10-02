using System;
using System.Collections.Generic;
using System.Linq;
using TinyGptDemo.Tokenization;

namespace TinyGptDemo.Training
{
    public sealed class TextDataset
    {
        private readonly List<string> tokens;
        private readonly int[] tokenIds;

        private TextDataset(List<string> tokens, int[] tokenIds, Vocabulary vocabulary, string eosToken)
        {
            this.tokens = tokens;
            this.tokenIds = tokenIds;
            Vocabulary = vocabulary;
            EosToken = eosToken;
            EosId = vocabulary.GetId(eosToken);
        }

        public Vocabulary Vocabulary { get; }
        public string EosToken { get; }
        public int EosId { get; }
        public int TokenCount => tokenIds.Length;
        public int VocabSize => Vocabulary.Size;

        public static TextDataset Create(string text, ITokenizer tokenizer, string eosToken)
        {
            var initialTokens = tokenizer.Tokenize(text ?? string.Empty);
            var allTokens = new List<string>(initialTokens);
            allTokens.Add(eosToken);

            var vocabulary = Vocabulary.FromTokens(allTokens);
            var ids = allTokens.Select(vocabulary.GetId).ToArray();

            return new TextDataset(allTokens, ids, vocabulary, eosToken);
        }

        public BatchSampler CreateBatchSampler(int contextLength) =>
            new BatchSampler(this, contextLength);

        public List<int> ToIds(IEnumerable<string> rawTokens)
        {
            var ids = new List<int>();
            foreach (var token in rawTokens)
            {
                if (Vocabulary.TryGetId(token, out int id))
                {
                    ids.Add(id);
                }
            }
            return ids;
        }

        internal int[] TokenIds => tokenIds;
    }

    public sealed class BatchSampler
    {
        private readonly TextDataset dataset;
        private readonly int contextLength;
        private readonly int[] startPositions;

        public BatchSampler(TextDataset dataset, int contextLength)
        {
            this.dataset = dataset;
            this.contextLength = contextLength;
            startPositions = BuildStartPositions(dataset.TokenIds.Length, contextLength);
        }

        public int StepsPerEpoch(int batchSize)
        {
            if (batchSize <= 0) batchSize = 1;
            int total = Math.Max(1, startPositions.Length);
            return Math.Max(1, total / batchSize);
        }

        public Batch CreateBatch(Random rng, int batchSize)
        {
            int actualBatch = Math.Min(batchSize, startPositions.Length);
            actualBatch = Math.Max(actualBatch, 1);

            var inputs = new int[actualBatch, contextLength];
            var targets = new int[actualBatch, contextLength];

            for (int b = 0; b < actualBatch; b++)
            {
                int start = PickRandomStart(rng);
                for (int t = 0; t < contextLength; t++)
                {
                    int xi = Math.Min(start + t, dataset.TokenIds.Length - 2);
                    int yi = Math.Min(start + t + 1, dataset.TokenIds.Length - 1);

                    inputs[b, t] = dataset.TokenIds[xi];
                    targets[b, t] = dataset.TokenIds[yi];
                }
            }

            return new Batch(inputs, targets);
        }

        private int PickRandomStart(Random rng) =>
            startPositions[rng.Next(startPositions.Length)];

        private static int[] BuildStartPositions(int sequenceLength, int contextLength)
        {
            int max = Math.Max(1, sequenceLength - contextLength - 1);
            var starts = new int[max];
            for (int i = 0; i < max; i++)
            {
                starts[i] = i;
            }
            return starts;
        }
    }

    public readonly record struct Batch(int[,] Inputs, int[,] Targets);
}