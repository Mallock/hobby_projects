using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using llm_training_test;

const int Seed = 12345; // fixed seed for reproducibility
var rng = new Random(Seed); // single RNG instance

// mode: word-level or sentence-level tokens
Console.WriteLine("Training mode? Enter 'word' or 'sentence' (default: word):");
string mode = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
bool sentenceMode = mode == "sentence";

// input text
Console.WriteLine("Enter training text (leave empty to use default):");
string trainingText = Console.ReadLine();
if (string.IsNullOrWhiteSpace(trainingText))
{
    trainingText = sentenceMode
        ? "Hello world. This is a tiny demo."
        : "hello world hello";
}

// tokenize
List<string> tokens = sentenceMode ? TokenizeSentences(trainingText) : TokenizeWords(trainingText);
if (tokens.Count < 2)
{
    Console.WriteLine("Training needs at least 2 tokens.");
    return;
}

// stop token
string stopToken = ChooseStopToken(tokens, sentenceMode ? "<EOS_S>" : "<EOS_W>");
var trainWithStop = new List<string>(tokens) { stopToken };

// vocab
var vocab = trainWithStop.Distinct().ToList();
int vocabSize = vocab.Count;

var tokenToIndex = new Dictionary<string, int>();
var indexToToken = new Dictionary<int, string>();
for (int i = 0; i < vocabSize; i++)
{
    tokenToIndex[vocab[i]] = i;
    indexToToken[i] = vocab[i];
}

// training indices
int T = trainWithStop.Count - 1;
int[] xIdxs = new int[T];
int[] yIdxs = new int[T];
for (int t = 0; t < T; t++)
{
    xIdxs[t] = tokenToIndex[trainWithStop[t]];
    yIdxs[t] = tokenToIndex[trainWithStop[t + 1]];
}

// model
int hiddenSize = 64;
var rnn = new StableRnn(vocabSize, hiddenSize, rng);

// training params
double learningRate = 0.05;
int totalEpochs = 0;
int initialEpochs = 1000;
int extraEpochBlock = 50;
int maxEpochs = 10000;

// training meter (ETA + epochs-to-goal estimator)
double targetAvgLoss = Math.Max(0.8, Math.Log(vocabSize) * 0.75); // heuristic target loss
var meter = new TrainingMeter(targetAvgLoss, maxHistoryPoints: 600, durationWindow: 200, maxEpochsCap: maxEpochs);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"\nMode: {(sentenceMode ? "sentence" : "word")}-level");
Console.WriteLine($"Training on {trainWithStop.Count} tokens with vocab size {vocabSize}... (stop token: '{stopToken}', target avg loss: {targetAvgLoss:F2})");

// async training with cancellation; prints loss with ETA and estimated epochs-to-goal
try
{
    var res = await TrainBlockAsync(rnn, xIdxs, yIdxs, learningRate, totalEpochs, initialEpochs, meter, cts.Token);
    totalEpochs += res.EpochsCompleted;

    while (!MatchesTraining(rnn, tokens, stopToken, tokenToIndex, indexToToken) && totalEpochs < maxEpochs && !cts.IsCancellationRequested)
    {
        res = await TrainBlockAsync(rnn, xIdxs, yIdxs, learningRate, totalEpochs, extraEpochBlock, meter, cts.Token);
        totalEpochs += res.EpochsCompleted;

        // simple LR decay if not improving (optional safety)
        if (meter.ShouldDecayLr() && learningRate > 0.005)
        {
            learningRate *= 0.8;
            meter.MarkLrDecayed();
            Console.WriteLine($"[info] decayed learning rate to {learningRate:F4}");
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nTraining cancelled.");
}

if (!cts.IsCancellationRequested)
{
    if (MatchesTraining(rnn, tokens, stopToken, tokenToIndex, indexToToken))
        Console.WriteLine($"\nMatched training sequence including stop token after {totalEpochs} total epochs.");
    else
        Console.WriteLine($"\nDid not fully match after {totalEpochs} epochs.");
}

// step-by-step predictions
Console.WriteLine("\nStep-by-step predictions on your training sequence (including stop):");
Console.WriteLine($"Sequence: \"{string.Join(sentenceMode ? " | " : " ", tokens)}\"  Stop: '{stopToken}'");
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

// generation
Console.WriteLine($"\nVocab (excluding stop): {(sentenceMode ? string.Join(" | ", vocab.Where(w => w != stopToken)) : string.Join(" ", vocab.Where(w => w != stopToken)))}");
Console.WriteLine($"Enter a {(sentenceMode ? "sentence" : "word")} prompt of known tokens (or 'q' to quit). Full prompt conditions the RNN state.");
Console.WriteLine($"You can also specify generation length and temperature, e.g.: {(sentenceMode ? "Hello world.|4|0.8" : "hello world|10|0.8")}");

while (true)
{
    Console.Write("\nPrompt|length|temperature: ");
    var line = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(line)) continue;
    if (line.Trim().ToLower() == "q") break;

    string prompt = line;
    int genLen = sentenceMode ? 4 : 15;
    double temperature = 1.0;

    var parts = line.Split('|');
    if (parts.Length >= 1) prompt = parts[0];
    if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedLen)) genLen = Math.Max(1, parsedLen);
    if (parts.Length >= 3 && double.TryParse(parts[2], out double parsedTemp)) temperature = Math.Max(0.05, parsedTemp);

    var promptTokens = (sentenceMode ? TokenizeSentences(prompt) : TokenizeWords(prompt)).Where(t => tokenToIndex.ContainsKey(t)).ToList();
    if (promptTokens.Count == 0)
    {
        Console.WriteLine("Your prompt contains no tokens from the training vocabulary. Try again.");
        continue;
    }

    // condition state on full prompt
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

        // temperature
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

        int nextIndex = SampleFrom(probs, rnn.Rng);
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
    Console.WriteLine(sentenceMode ? string.Join(" ", generated) : string.Join(" ", generated));
}

// async training block with ETA and epochs-to-goal estimation
static async Task<(int EpochsCompleted, double LastAvgLoss)> TrainBlockAsync(
    StableRnn rnn, int[] xIdxs, int[] yIdxs, double learningRate,
    int startEpoch, int epochsInBlock, TrainingMeter meter, CancellationToken ct)
{
    return await Task.Run(() =>
    {
        int completed = 0;
        double lastLoss = 0.0;

        for (int e = 0; e < epochsInBlock; e++)
        {
            ct.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            double loss = rnn.TrainSequence(xIdxs, yIdxs, learningRate);
            sw.Stop();

            completed++;
            double avgLoss = loss / Math.Max(1, xIdxs.Length);
            lastLoss = avgLoss;

            int epochNumber = startEpoch + completed;
            meter.RecordEpoch(epochNumber, avgLoss, sw.Elapsed.TotalMilliseconds);

            if (epochNumber % 50 == 0)
            {
                int estToGoal = meter.EstimatedEpochsToGoal();
                string estEpochsText = estToGoal == int.MaxValue ? "?" : estToGoal.ToString();

                // ETA based on smoothed epoch duration; if estToGoal unknown, show ETA for next 200 epochs
                int forEta = estToGoal == int.MaxValue ? Math.Min(200, meter.MaxEpochsCap - epochNumber) : Math.Min(estToGoal, meter.MaxEpochsCap - epochNumber);
                var eta = meter.EstimatedTimeLeft(forEta);
                var msPerEpoch = meter.AvgEpochMs();

                Console.WriteLine($"Epoch {epochNumber}, Average Loss: {avgLoss:F4} | {msPerEpoch:F1} ms/epoch | est epochs to goal: {estEpochsText} | ETA: {FormatTimeSpan(eta)}");
            }
        }

        return (completed, lastLoss);
    }, ct);
}

// word tokenizer
static List<string> TokenizeWords(string text)
{
    // split on whitespace; keep punctuation as separate tokens
    var list = new List<string>();
    int i = 0;
    while (i < text.Length)
    {
        if (char.IsWhiteSpace(text[i]))
        {
            i++;
            continue;
        }

        // punctuation token
        if (char.IsPunctuation(text[i]) || char.IsSymbol(text[i]))
        {
            list.Add(text[i].ToString());
            i++;
            continue;
        }

        // word token
        int start = i;
        while (i < text.Length && !char.IsWhiteSpace(text[i]) && !char.IsPunctuation(text[i]) && !char.IsSymbol(text[i]))
            i++;
        list.Add(text.Substring(start, i - start));
    }
    return list;
}

// sentence tokenizer
static List<string> TokenizeSentences(string text)
{
    var sentences = new List<string>();
    int start = 0;
    for (int i = 0; i < text.Length; i++)
    {
        char c = text[i];
        if (c == '.' || c == '!' || c == '?' || c == '…')
        {
            int end = i + 1;
            string s = text.Substring(start, end - start).Trim();
            if (!string.IsNullOrWhiteSpace(s))
                sentences.Add(s);
            start = end;
        }
    }
    if (start < text.Length)
    {
        string tail = text.Substring(start).Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            sentences.Add(tail);
    }
    return sentences;
}

// choose EOS token not in tokens
static string ChooseStopToken(List<string> tokens, string preferred)
{
    if (!tokens.Contains(preferred)) return preferred;
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

// top-k utility
static IEnumerable<(string tk, double p)> TopK(double[] probs, Dictionary<int, string> indexToToken, int k)
{
    return probs
        .Select((p, idx) => (idx, p))
        .OrderByDescending(x => x.p)
        .Take(k)
        .Select(x => (indexToToken[x.idx], x.p));
}

// multinomial sampling
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

// exact-match checker
static bool MatchesTraining(StableRnn rnn, List<string> tokens, string stopToken,
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

// argmax
static int ArgMax(double[] arr)
{
    int idx = 0;
    double best = arr[0];
    for (int i = 1; i < arr.Length; i++)
        if (arr[i] > best) { best = arr[i]; idx = i; }
    return idx;
}

// timespan formatter for ETA
static string FormatTimeSpan(TimeSpan ts)
{
    if (ts.TotalSeconds < 1) return "<1s";
    if (ts.TotalMinutes < 1) return $"{(int)ts.TotalSeconds}s";
    if (ts.TotalHours < 1) return $"{(int)ts.TotalMinutes}m {(int)ts.Seconds}s";
    return $"{(int)ts.TotalHours}h {(int)ts.Minutes}m";
}

// training progress and estimation helper
public class TrainingMeter
{
    readonly double targetAvgLoss; // goal loss
    readonly int maxHistoryPoints; // max points to keep for regression
    readonly int durationWindow;   // rolling window for epoch duration smoothing
    readonly List<(int epoch, double avgLoss)> history = new();
    readonly Queue<double> durationsMs = new();
    double sumDurationsMs = 0.0;
    int lastDecayCheckEpoch = 0;
    public int MaxEpochsCap { get; }

    public TrainingMeter(double targetAvgLoss, int maxHistoryPoints, int durationWindow, int maxEpochsCap)
    {
        this.targetAvgLoss = targetAvgLoss;
        this.maxHistoryPoints = Math.Max(50, maxHistoryPoints);
        this.durationWindow = Math.Max(20, durationWindow);
        this.MaxEpochsCap = Math.Max(1, maxEpochsCap);
    }

    // record epoch metrics
    public void RecordEpoch(int epoch, double avgLoss, double epochMs)
    {
        history.Add((epoch, avgLoss));
        if (history.Count > maxHistoryPoints)
            history.RemoveAt(0);

        durationsMs.Enqueue(epochMs);
        sumDurationsMs += epochMs;
        while (durationsMs.Count > durationWindow)
            sumDurationsMs -= durationsMs.Dequeue();
    }

    // average epoch duration (smoothed)
    public double AvgEpochMs()
    {
        if (durationsMs.Count == 0) return 0.0;
        return sumDurationsMs / durationsMs.Count;
    }

    // estimate epochs to reach targetAvgLoss using linear regression on recent loss history
    public int EstimatedEpochsToGoal()
    {
        // need enough points
        if (history.Count < 20) return int.MaxValue;

        // use recent window
        int window = Math.Min(history.Count, Math.Max(60, maxHistoryPoints / 2));
        var slice = history.Skip(history.Count - window).ToArray();
        int m = slice.Length;

        // linear regression y = a + b x
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < m; i++)
        {
            double x = slice[i].epoch;
            double y = slice[i].avgLoss;
            sx += x;
            sy += y;
            sxx += x * x;
            sxy += x * y;
        }

        double denom = m * sxx - sx * sx;
        if (Math.Abs(denom) < 1e-9) return int.MaxValue;

        double slope = (m * sxy - sx * sy) / denom; // loss change per epoch
        double currentLoss = slice[m - 1].avgLoss;
        if (double.IsNaN(slope) || double.IsInfinity(slope)) return int.MaxValue;

        // require downward trend
        if (slope >= -1e-6) return int.MaxValue;

        // estimate to target
        double epochsNeeded = (currentLoss - targetAvgLoss) / -slope;
        if (epochsNeeded <= 0) return 0;

        // bound by cap
        if (epochsNeeded > 1e7) return int.MaxValue;

        return (int)Math.Ceiling(epochsNeeded);
    }

    // estimate time left from average epoch duration and remaining epochs
    public TimeSpan EstimatedTimeLeft(int remainingEpochs)
    {
        if (remainingEpochs <= 0) return TimeSpan.Zero;
        double msPerEpoch = AvgEpochMs();
        if (msPerEpoch <= 0) return TimeSpan.Zero;
        remainingEpochs = Math.Max(0, Math.Min(remainingEpochs, MaxEpochsCap));
        double totalMs = msPerEpoch * remainingEpochs;
        if (double.IsInfinity(totalMs) || double.IsNaN(totalMs)) return TimeSpan.Zero;
        // clamp to 1 day
        return TimeSpan.FromMilliseconds(Math.Min(totalMs, 24.0 * 3600 * 1000));
    }

    // simple LR decay heuristic: if last N losses didn't improve by small margin
    public bool ShouldDecayLr()
    {
        int n = 80;
        if (history.Count < n + 10) return false;
        var recent = history.Skip(Math.Max(0, history.Count - n)).Select(h => h.avgLoss).ToArray();
        var older = history.Skip(Math.Max(0, history.Count - n - 10)).Take(10).Select(h => h.avgLoss).ToArray();
        if (older.Length == 0) return false;
        double recentAvg = recent.Average();
        double olderAvg = older.Average();
        // if recent average is higher than older average by small margin, decay LR
        return recentAvg > olderAvg + 0.02 && (history.Last().epoch - lastDecayCheckEpoch) >= 200;
    }

    public void MarkLrDecayed()
    {
        lastDecayCheckEpoch = history.Count > 0 ? history.Last().epoch : 0;
    }
}
