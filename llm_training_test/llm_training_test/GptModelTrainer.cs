using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TinyGptDemo.Model;
using TinyGptDemo.Tokenization;
using TinyGptDemo.Training;
using TinyGptDemo.Utils;

namespace llm_training_test
{
    public class GptModelTrainer
    {
        private const int Seed = 12345;

        public void Main()
        {
            var rng = new Random(Seed);

            var mode = PromptForMode();
            bool sentenceMode = mode == TokenizationMode.Sentence;

            string trainingText = PromptForTrainingText(sentenceMode);

            ITokenizer tokenizer = TokenizerFactory.Create(mode);
            string eosToken = sentenceMode ? SpecialTokens.SentenceEos : SpecialTokens.WordEos;

            var dataset = TextDataset.Create(trainingText, tokenizer, eosToken);

            if (dataset.TokenCount < 4)
            {
                Console.WriteLine("Need at least 4 tokens for training.");
                return;
            }

            var baseModelConfig = ModelConfig.Default();
            Console.WriteLine("\n--- Model configuration ---");
            int contextLen = CliPrompts.ReadInt(
                $"Context length (default {baseModelConfig.ContextLength}): ",
                baseModelConfig.ContextLength,
                "Context length: number of tokens the model attends to (larger = more memory of the prompt, slower & more RAM).");

            int embeddingDim = CliPrompts.ReadInt(
                $"Embedding dim (default {baseModelConfig.EmbeddingDim}): ",
                baseModelConfig.EmbeddingDim,
                "Embedding dim: width of token representations (larger = more expressiveness, but more parameters).");

            int layers = CliPrompts.ReadInt(
                $"Transformer layers (default {baseModelConfig.Layers}): ",
                baseModelConfig.Layers,
                "Transformer layers: depth of stacked attention/MLP blocks (more layers = better capacity, higher cost).");

            int hidden = CliPrompts.ReadInt(
                $"MLP hidden dim (default {baseModelConfig.HiddenDim}): ",
                baseModelConfig.HiddenDim,
                "MLP hidden dim: inner width of the feed-forward network (controls capacity & compute per layer).");



            var modelConfig = baseModelConfig with
            {
                ContextLength = contextLen,
                EmbeddingDim = embeddingDim,
                Layers = layers,
                HiddenDim = hidden
            };

            var defaults = TrainingConfig.WithDefaults(dataset.VocabSize);
            Console.WriteLine("\n--- Training configuration ---");
            int batchSize = CliPrompts.ReadInt(
                $"Batch size (default {defaults.BatchSize}): ",
                defaults.BatchSize,
                "Batch size: number of sequences per optimization step (higher = better throughput, more VRAM/RAM).");

            float learningRate = CliPrompts.ReadFloat(
                $"Learning rate (default {defaults.LearningRate:G}): ",
                defaults.LearningRate,
                "Learning rate: step size of Adam updates (too high = divergence, too low = slow training).");

            int maxEpochs = CliPrompts.ReadInt(
                $"Max epochs (default {defaults.MaxEpochs}): ",
                defaults.MaxEpochs,
                "Max epochs: how many passes over the dataset to run (higher = longer training, potentially better fit).");
            int logEvery = CliPrompts.ReadInt(
                $"Log every N epochs (default {defaults.LogEvery}): ",
                defaults.LogEvery,
                "How often to print progress updates (smaller = more feedback, tiny overhead).");
            var trainingConfig = defaults with
            {
                BatchSize = batchSize,
                LearningRate = learningRate,
                MaxEpochs = maxEpochs,
                LogEvery = logEvery
            };

            var model = new TinyGpt(dataset.VocabSize, modelConfig, rng);

            Console.WriteLine($"\nMode: {mode.ToDescription()}-level");
            Console.WriteLine($"Training on {dataset.TokenCount} tokens with vocab size {dataset.VocabSize} " +
                              $"(context={modelConfig.ContextLength}, dModel={modelConfig.EmbeddingDim}, layers={modelConfig.Layers})... " +
                              $"target avg loss: {trainingConfig.TargetAverageLoss:F2}");

            var trainer = new Trainer(model, dataset, trainingConfig, modelConfig, rng);
            var summary = trainer.Train();

            Console.WriteLine($"\nTraining complete. Final avg loss: {summary.FinalLoss:F4} after {summary.EpochsCompleted} epochs.");

            var generator = new TextGenerator(model, dataset, tokenizer, modelConfig, mode, rng);

            Console.WriteLine($"\nVocab size: {dataset.VocabSize}. Enter a prompt to generate. Type 'q' to quit.");
            Console.WriteLine($"Example: {CliExamples.ForMode(mode)}");

            generator.Repl();
        }

        private static TokenizationMode PromptForMode()
        {
            Console.WriteLine("Training mode? Enter 'word' or 'sentence' (default: word):");
            string? input = Console.ReadLine();
            return TokenizationModeExtensions.ParseOrDefault(input, TokenizationMode.Word);
        }

        private static string PromptForTrainingText(bool sentenceMode)
        {
            Console.WriteLine("Enter training text (leave empty to use default):");
            string? text = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(text))
            {
                return sentenceMode
                    ? "Hello world. This is a tiny demo."
                    : "hello world hello";
            }

            return text;
        }
    }
}
