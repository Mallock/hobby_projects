using System;
using System.Collections.Generic;
using System.Linq;
using llm_training_test;

Console.WriteLine("Enter training text (leave empty to use default):");
string trainingText = Console.ReadLine();
if (string.IsNullOrWhiteSpace(trainingText))
    trainingText = "hello world hello";

// Tokenize into words
var tokens = Tokenize(trainingText);
if (tokens.Count < 2)
{
    Console.WriteLine("Training needs at least 2 words.");
    return;
}

// Choose stop token
string stopToken = ChooseStopToken(tokens);
var trainWithStop = new List<string>(tokens) { stopToken };

// Build vocabulary
var vocab = trainWithStop.Distinct().ToList();
int vocabSize = vocab.Count;

var tokenToIndex = new Dictionary<string, int>();
var indexToToken = new Dictionary<int, string>();
for (int i = 0; i < vocabSize; i++)
{
    tokenToIndex[vocab[i]] = i;
    indexToToken[i] = vocab[i];
}

// Prepare training indices
int T = trainWithStop.Count - 1;
int[] xIdxs = new int[T];
int[] yIdxs = new int[T];
for (int t = 0; t < T; t++)
{
    xIdxs[t] = tokenToIndex[trainWithStop[t]];
    yIdxs[t] = tokenToIndex[trainWithStop[t + 1]];
}

// Create RNN
int hiddenSize = 64;
var rnn = new SimpleRnn(vocabSize, hiddenSize);

// Training
double learningRate = 0.05;
int totalEpochs = 0;
int initialEpochs = 1000;
int extraEpochBlock = 50;
int maxEpochs = 10000;

Console.WriteLine($"\nTraining on {trainWithStop.Count} tokens with vocab size {vocabSize}... (stop token: '{stopToken}')");
TrainBlock(initialEpochs);
while (!MatchesTraining(rnn, tokens, stopToken, tokenToIndex, indexToToken) && totalEpochs < maxEpochs)
{
    TrainBlock(extraEpochBlock);
}

if (MatchesTraining(rnn, tokens, stopToken, tokenToIndex, indexToToken))
    Console.WriteLine($"\nMatched training sequence including stop token after {totalEpochs} total epochs.");
else
    Console.WriteLine($"\nDid not fully match after {totalEpochs} epochs.");

// Step-by-step predictions
Console.WriteLine("\nStep-by-step predictions on your training sentence (including stop):");
Console.WriteLine($"Sentence: \"{string.Join(" ", tokens)}\"  Stop: '{stopToken}'");
{
    double[] h = rnn.ZeroHidden();
    for (int i = 0; i < trainWithStop.Count - 1; i++)
    {
        int x = tokenToIndex[trainWithStop[i]];
        int y = tokenToIndex[trainWithStop[i + 1]];
        var probs = rnn.StepProbs(x, h, out double[] hNext);
        var top3 = TopK(probs, indexToToken, 3);
        Console.WriteLine($"Input '{indexToToken[x]}' -> predicts: {string.Join(", ", top3.Select(t => $"'{t.tk}': {t.p:F2}"))} | actual next: '{indexToToken[y]}'");
        h = hNext;
    }
}

// Interactive generation (word-level)
Console.WriteLine("\nVocab (excluding stop): " + string.Join(" ", vocab.Where(w => w != stopToken)));
Console.WriteLine("Enter a prompt of known words (or 'q' to quit). Full prompt conditions the RNN state.");
Console.WriteLine("You can also specify generation length and temperature, e.g.: mitä kuuluu|10|0.8");

while (true)
{
    Console.Write("\nPrompt|length|temperature: ");
    var line = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(line)) continue;
    if (line.Trim().ToLower() == "q") break;

    string prompt = line;
    int genLen = 15;
    double temperature = 1.0;

    var parts = line.Split('|');
    if (parts.Length >= 1) prompt = parts[0];
    if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedLen)) genLen = Math.Max(1, parsedLen);
    if (parts.Length >= 3 && double.TryParse(parts[2], out double parsedTemp)) temperature = Math.Max(0.05, parsedTemp);

    var promptTokens = Tokenize(prompt).Where(t => tokenToIndex.ContainsKey(t)).ToList();
    if (promptTokens.Count == 0)
    {
        Console.WriteLine("Your prompt contains no words from the training vocabulary. Try again.");
        continue;
    }

    // Condition hidden state on the prompt
    double[] hGen = rnn.ZeroHidden();
    for (int i = 0; i < promptTokens.Count - 1; i++)
    {
        int xi = tokenToIndex[promptTokens[i]];
        rnn.StepProbs(xi, hGen, out hGen);
    }

    int currentIndex = tokenToIndex[promptTokens.Last()];
    var generated = new List<string>(promptTokens);
    bool stopped = false;

    for (int step = 0; step < genLen; step++)
    {
        var probs = rnn.StepProbs(currentIndex, hGen, out hGen);

        if (Math.Abs(temperature - 1.0) > 1e-9)
        {
            double invT = 1.0 / temperature;
            double sum = 0.0;
            for (int i = 0; i < probs.Length; i++)
            {
                probs[i] = Math.Pow(Math.Max(probs[i], 1e-12), invT);
                sum += probs[i];
            }
            for (int i = 0; i < probs.Length; i++) probs[i] /= sum;
        }

        int nextIndex = SampleFrom(probs, rnn.Random);
        string nextToken = indexToToken[nextIndex];
        if (nextToken == stopToken)
        {
            stopped = true;
            break;
        }
        generated.Add(nextToken);
        currentIndex = nextIndex;
    }

    Console.WriteLine($"Generated ({genLen} tokens, temp={temperature}):");
    Console.WriteLine(string.Join(" ", generated));
}

// Local functions
void TrainBlock(int epochsInBlock)
{
    for (int e = 0; e < epochsInBlock; e++)
    {
        double loss = rnn.TrainSequence(xIdxs, yIdxs, learningRate);
        totalEpochs++;
        if (totalEpochs % 50 == 0)
            Console.WriteLine($"Epoch {totalEpochs}, Average Loss: {loss / xIdxs.Length:F4}");
    }
}

static List<string> Tokenize(string text)
{
    return text
        .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .ToList();
}

static string ChooseStopToken(List<string> tokens)
{
    var candidates = new[] { "<EOS>", "<END>", "<STOP>", "<∎>" };
    foreach (var c in candidates)
        if (!tokens.Contains(c)) return c;
    string candidate = "<EOS>";
    int i = 1;
    while (tokens.Contains(candidate))
    {
        candidate = $"<EOS{i}>";
        i++;
    }
    return candidate;
}

static IEnumerable<(string tk, double p)> TopK(double[] probs, Dictionary<int, string> indexToToken, int k)
{
    return probs
        .Select((p, idx) => (idx, p))
        .OrderByDescending(x => x.p)
        .Take(k)
        .Select(x => (indexToToken[x.idx], x.p));
}

static int SampleFrom(double[] probs, Random rnd)
{
    double r = rnd.NextDouble();
    double cum = 0.0;
    for (int i = 0; i < probs.Length; i++)
    {
        cum += probs[i];
        if (r <= cum) return i;
    }
    return probs.Length - 1;
}

static bool MatchesTraining(SimpleRnn rnn, List<string> tokens, string stopToken,
    Dictionary<string, int> tokenToIndex, Dictionary<int, string> indexToToken)
{
    if (tokens.Count == 0) return false;

    double[] h = rnn.ZeroHidden();
    int currentIndex = tokenToIndex[tokens[0]];

    for (int i = 1; i < tokens.Count; i++)
    {
        var probs = rnn.StepProbs(currentIndex, h, out h);
        int predicted = ArgMax(probs);
        if (indexToToken[predicted] != tokens[i]) return false;
        currentIndex = predicted;
    }

    {
        var probs = rnn.StepProbs(currentIndex, h, out h);
        int predicted = ArgMax(probs);
        if (indexToToken[predicted] != stopToken) return false;
    }

    return true;
}

static int ArgMax(double[] arr)
{
    int idx = 0;
    double best = arr[0];
    for (int i = 1; i < arr.Length; i++)
        if (arr[i] > best) { best = arr[i]; idx = i; }
    return idx;
}