using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class Program
{
    public static void Main()
    {
        // fixed seed for reproducibility
        const int Seed = 12345;
        var rng = new Random(Seed);

        // choose mode
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
        if (tokens.Count < 4)
        {
            Console.WriteLine("Need at least 4 tokens for training.");
            return;
        }

        // vocab and ids
        var vocab = tokens.Distinct().ToList();
        string eos = sentenceMode ? "<EOS_S>" : "<EOS_W>";
        if (!vocab.Contains(eos)) vocab.Add(eos);
        var tokenToId = new Dictionary<string, int>();
        var idToToken = new Dictionary<int, string>();
        for (int i = 0; i < vocab.Count; i++) { tokenToId[vocab[i]] = i; idToToken[i] = vocab[i]; }

        // append EOS once
        tokens.Add(eos);

        // convert tokens to ids
        int[] ids = tokens.Select(t => tokenToId[t]).ToArray();

        // tiny GPT-like hyperparameters
        int vocabSize = vocab.Count;
        int contextLen = 32;
        int dModel = 64;
        int nLayers = 2;
        int nHeads = 1;        // single head for simplicity
        int dHidden = dModel * 4;

        // training hyperparameters
        int batchSize = 16;
        double lr = 3e-3;
        double weightDecay = 0.0;
        int maxEpochs = 1500;
        int logEvery = 50;
        double targetAvgLoss = Math.Max(0.8, Math.Log(vocabSize) * 0.85);

        // dataset start indices
        var allStarts = Enumerable.Range(0, Math.Max(1, ids.Length - contextLen - 1)).ToArray();
        int stepsPerEpoch = Math.Max(1, allStarts.Length / Math.Max(1, batchSize));

        // model
        var model = new TinyGpt(vocabSize, contextLen, dModel, nLayers, nHeads, dHidden, rng);

        // meter
        var meter = new TrainingMeter(targetAvgLoss, maxHistoryPoints: 600, durationWindow: 200, maxEpochsCap: maxEpochs);

        // train
        Console.WriteLine($"\nMode: {(sentenceMode ? "sentence" : "word")}-level");
        Console.WriteLine($"Training on {tokens.Count} tokens with vocab size {vocabSize} (context={contextLen}, dModel={dModel}, layers={nLayers})... target avg loss: {targetAvgLoss:F2}");

        for (int epoch = 1; epoch <= maxEpochs; epoch++)
        {
            var swEpoch = Stopwatch.StartNew();
            double epochLoss = 0.0;

            for (int step = 0; step < stepsPerEpoch; step++)
            {
                // sample batch starts
                int bsz = Math.Min(batchSize, allStarts.Length);
                int[] starts = new int[bsz];
                for (int i = 0; i < bsz; i++)
                    starts[i] = allStarts[rng.Next(allStarts.Length)];

                // build batch
                int[,] x = new int[bsz, contextLen];
                int[,] y = new int[bsz, contextLen];
                for (int b = 0; b < bsz; b++)
                {
                    int s = starts[b];
                    for (int t = 0; t < contextLen; t++)
                    {
                        int xi = Math.Min(s + t, ids.Length - 2);
                        int yi = Math.Min(s + t + 1, ids.Length - 1);
                        x[b, t] = ids[xi];
                        y[b, t] = ids[yi];
                    }
                }

                // forward
                var fw = model.Forward(x);

                // loss and dLogits
                double loss = CrossEntropyLossAndGrad(fw.Logits, y, fw.DLogits);
                epochLoss += loss;

                // backward
                model.Backward(fw);

                // optimizer step
                model.AdamStep(lr, weightDecay);
            }

            swEpoch.Stop();

            double avgLoss = epochLoss / Math.Max(1, stepsPerEpoch);
            meter.RecordEpoch(epoch, avgLoss, swEpoch.Elapsed.TotalMilliseconds);

            if (epoch % logEvery == 0)
            {
                int estToGoal = meter.EstimatedEpochsToGoal();
                string estEpochsText = estToGoal == int.MaxValue ? "?" : estToGoal.ToString();
                int forEta = estToGoal == int.MaxValue ? Math.Min(200, meter.MaxEpochsCap - epoch) : Math.Min(estToGoal, meter.MaxEpochsCap - epoch);
                var eta = meter.EstimatedTimeLeft(forEta);
                Console.WriteLine($"Epoch {epoch}, Average Loss: {avgLoss:F4} | {meter.AvgEpochMs():F1} ms/epoch | est epochs to goal: {estEpochsText} | ETA: {FormatTimeSpan(eta)}");
            }
        }

        // generation
        Console.WriteLine($"\nVocab size: {vocabSize}. Enter a prompt to generate. Type 'q' to quit.");
        Console.WriteLine($"Example: {(sentenceMode ? "Hello world.|4|0.8" : "hello world|16|0.9")}");

        while (true)
        {
            Console.Write("\nPrompt|length|temperature: ");
            var line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Trim().ToLower() == "q") break;

            string prompt = line;
            int genLen = sentenceMode ? 4 : 16;
            double temperature = 1.0;
            var parts = line.Split('|');
            if (parts.Length >= 1) prompt = parts[0];
            if (parts.Length >= 2 && int.TryParse(parts[1], out int parsedLen)) genLen = Math.Max(1, parsedLen);
            if (parts.Length >= 3 && double.TryParse(parts[2], out double parsedTemp)) temperature = Math.Max(0.05, parsedTemp);

            var promptTokens = (sentenceMode ? TokenizeSentences(prompt) : TokenizeWords(prompt))
                .Where(t => tokenToId.ContainsKey(t)).ToList();
            if (promptTokens.Count == 0)
            {
                Console.WriteLine("Prompt contains no known tokens.");
                continue;
            }

            var context = promptTokens.Select(t => tokenToId[t]).ToList();

            for (int step = 0; step < genLen; step++)
            {
                // last context window
                var ctxSlice = new int[Math.Min(context.Count, contextLen)];
                int start = Math.Max(0, context.Count - contextLen);
                for (int i = 0; i < ctxSlice.Length; i++)
                    ctxSlice[i] = context[start + i];

                var xGen = new int[1, contextLen];
                int pad = contextLen - ctxSlice.Length;
                for (int i = 0; i < pad; i++) xGen[0, i] = tokenToId[eos];
                for (int i = 0; i < ctxSlice.Length; i++) xGen[0, pad + i] = ctxSlice[i];

                var fw = model.Forward(xGen);
                int T = contextLen;
                double[] logits = new double[vocabSize];
                for (int v = 0; v < vocabSize; v++)
                    logits[v] = fw.Logits[0, T - 1, v];

                if (Math.Abs(temperature - 1.0) > 1e-9)
                    for (int v = 0; v < vocabSize; v++) logits[v] /= temperature;

                var probs = Softmax1D(logits);
                int nextId = SampleFrom(probs, rng);
                context.Add(nextId);

                if (idToToken[nextId] == eos) break;
            }

            var outTokens = context.Select(id => idToToken[id]).ToList();
            Console.WriteLine(sentenceMode ? string.Join(" ", outTokens) : string.Join(" ", outTokens));
        }
    }

    // word tokenizer
    private static List<string> TokenizeWords(string text)
    {
        var list = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            if (char.IsPunctuation(text[i]) || char.IsSymbol(text[i])) { list.Add(text[i].ToString()); i++; continue; }
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && !char.IsPunctuation(text[i]) && !char.IsSymbol(text[i])) i++;
            list.Add(text.Substring(start, i - start));
        }
        return list;
    }

    // sentence tokenizer
    private static List<string> TokenizeSentences(string text)
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
                if (!string.IsNullOrWhiteSpace(s)) sentences.Add(s);
                start = end;
            }
        }
        if (start < text.Length)
        {
            string tail = text.Substring(start).Trim();
            if (!string.IsNullOrWhiteSpace(tail)) sentences.Add(tail);
        }
        return sentences;
    }

    // cross-entropy loss with in-place softmax grad
    private static double CrossEntropyLossAndGrad(double[,,] logits, int[,] targets, double[,,] dLogits)
    {
        int B = logits.GetLength(0), T = logits.GetLength(1), V = logits.GetLength(2);
        double loss = 0.0;
        Array.Clear(dLogits, 0, dLogits.Length);
        for (int b = 0; b < B; b++)
            for (int t = 0; t < T; t++)
            {
                double max = double.NegativeInfinity;
                for (int v = 0; v < V; v++) if (logits[b, t, v] > max) max = logits[b, t, v];
                double sum = 0.0;
                for (int v = 0; v < V; v++)
                {
                    double e = Math.Exp(logits[b, t, v] - max);
                    dLogits[b, t, v] = e;
                    sum += e;
                }
                if (sum == 0) sum = 1;
                for (int v = 0; v < V; v++) dLogits[b, t, v] /= sum;

                int y = targets[b, t];
                double p = Math.Max(1e-12, dLogits[b, t, y]);
                loss += -Math.Log(p);
                dLogits[b, t, y] -= 1.0;
            }
        return loss / (B * T);
    }

    // softmax
    private static double[] Softmax1D(double[] logits)
    {
        double max = logits[0];
        for (int i = 1; i < logits.Length; i++) if (logits[i] > max) max = logits[i];
        double sum = 0;
        var p = new double[logits.Length];
        for (int i = 0; i < logits.Length; i++) { double e = Math.Exp(logits[i] - max); p[i] = e; sum += e; }
        if (sum == 0) sum = 1;
        for (int i = 0; i < p.Length; i++) p[i] /= sum;
        return p;
    }

    // sampling
    private static int SampleFrom(double[] probs, Random rng)
    {
        double r = rng.NextDouble(), cum = 0.0;
        for (int i = 0; i < probs.Length; i++) { cum += probs[i]; if (r <= cum) return i; }
        return probs.Length - 1;
    }

    // ETA formatter
    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalSeconds < 1) return "<1s";
        if (ts.TotalMinutes < 1) return $"{(int)ts.TotalSeconds}s";
        if (ts.TotalHours < 1) return $"{(int)ts.TotalMinutes}m {(int)ts.Seconds}s";
        return $"{(int)ts.TotalHours}h {(int)ts.Minutes}m";
    }
}

// tiny GPT-like model
public class TinyGpt
{
    // shapes and config
    int V, Tctx, D, L, Hheads, Dhid;
    Random rng;

    // parameters
    Param2D tokEmb;           // [V, D]
    Param2D posEmb;           // [Tctx, D]
    Block[] blocks;           // transformer blocks
    Param2D Wout;             // [D, V]
    Param1D Bout;             // [V]

    // optimizer
    Adam adam;

    public TinyGpt(int vocabSize, int contextLen, int dModel, int nLayers, int nHeads, int dHidden, Random rng)
    {
        V = vocabSize; Tctx = contextLen; D = dModel; L = nLayers; Hheads = nHeads; Dhid = dHidden; this.rng = rng;

        // init params
        tokEmb = new Param2D(V, D, rng, 0.02);
        posEmb = new Param2D(Tctx, D, rng, 0.02);
        blocks = new Block[L];
        for (int i = 0; i < L; i++)
            blocks[i] = new Block(D, Dhid, rng);

        Wout = new Param2D(D, V, rng, 0.02);
        Bout = new Param1D(V);

        // optimizer
        adam = new Adam();
        adam.Add(tokEmb); adam.Add(posEmb); adam.Add(Wout); adam.Add(Bout);
        foreach (var b in blocks) b.Register(adam);
    }

    public ForwardCache Forward(int[,] X)
    {
        int B = X.GetLength(0);
        int T = X.GetLength(1);
        var c = new ForwardCache(B, T, D, V, L, Dhid);

        // capture ids for embedding backprop
        c.CaptureInputIds(X);

        // embeddings: H[0] = tok + pos
        for (int b = 0; b < B; b++)
            for (int t = 0; t < T; t++)
                for (int d = 0; d < D; d++)
                    c.H[0, b, t, d] = tokEmb.W[X[b, t], d] + posEmb.W[t, d];

        // blocks
        for (int l = 0; l < L; l++)
            blocks[l].Forward(c, l);

        // output head: logits = H[L] @ Wout + Bout
        for (int b = 0; b < B; b++)
            for (int t = 0; t < T; t++)
                for (int v = 0; v < V; v++)
                {
                    double sum = Bout.W[v];
                    for (int d = 0; d < D; d++)
                        sum += c.H[L, b, t, d] * Wout.W[d, v];
                    c.Logits[b, t, v] = sum;
                }

        return c;
    }

    public void Backward(ForwardCache c)
    {
        int B = c.B, T = c.T;

        // zero grads
        tokEmb.ZeroGrad(); posEmb.ZeroGrad(); Wout.ZeroGrad(); Bout.ZeroGrad();
        foreach (var bl in blocks) bl.ZeroGrad();

        // output head grads
        for (int b = 0; b < B; b++)
            for (int t = 0; t < T; t++)
                for (int v = 0; v < V; v++)
                {
                    double g = c.DLogits[b, t, v];
                    Bout.G[v] += g;
                    for (int d = 0; d < D; d++)
                    {
                        Wout.G[d, v] += c.H[L, b, t, d] * g;
                        c.DH[L, b, t, d] += Wout.W[d, v] * g;
                    }
                }

        // blocks backward
        for (int l = L - 1; l >= 0; l--)
            blocks[l].Backward(c, l);

        // embedding grads
        for (int b = 0; b < B; b++)
            for (int t = 0; t < T; t++)
            {
                int id = c.XTokIds[b, t];
                for (int d = 0; d < D; d++)
                {
                    posEmb.G[t, d] += c.DH[0, b, t, d];
                    tokEmb.G[id, d] += c.DH[0, b, t, d];
                }
            }
    }

    public void AdamStep(double lr, double wd)
    {
        adam.Step(lr, wd);
    }

    // transformer block (single-head)
    class Block
    {
        int D, Dhid;

        // layer norms (pre-LN)
        Param1D ln1_g, ln1_b;
        Param1D ln2_g, ln2_b;

        // projections
        Param2D Wq, Wk, Wv, Wo;

        // MLP
        Param2D W1, W2;
        Param1D b1, b2;

        public Block(int dModel, int dHidden, Random rng)
        {
            D = dModel; Dhid = dHidden;

            ln1_g = new Param1D(D, 1.0); ln1_b = new Param1D(D, 0.0);
            ln2_g = new Param1D(D, 1.0); ln2_b = new Param1D(D, 0.0);

            double s = 0.02;
            Wq = new Param2D(D, D, rng, s);
            Wk = new Param2D(D, D, rng, s);
            Wv = new Param2D(D, D, rng, s);
            Wo = new Param2D(D, D, rng, s);

            W1 = new Param2D(D, Dhid, rng, s);
            W2 = new Param2D(Dhid, D, rng, s);
            b1 = new Param1D(Dhid);
            b2 = new Param1D(D);
        }

        public void Register(Adam opt)
        {
            opt.Add(ln1_g); opt.Add(ln1_b);
            opt.Add(ln2_g); opt.Add(ln2_b);
            opt.Add(Wq); opt.Add(Wk); opt.Add(Wv); opt.Add(Wo);
            opt.Add(W1); opt.Add(W2); opt.Add(b1); opt.Add(b2);
        }

        public void ZeroGrad()
        {
            ln1_g.ZeroGrad(); ln1_b.ZeroGrad();
            ln2_g.ZeroGrad(); ln2_b.ZeroGrad();
            Wq.ZeroGrad(); Wk.ZeroGrad(); Wv.ZeroGrad(); Wo.ZeroGrad();
            W1.ZeroGrad(); W2.ZeroGrad(); b1.ZeroGrad(); b2.ZeroGrad();
        }

        public void Forward(ForwardCache c, int l)
        {
            int B = c.B, T = c.T;
            double scale = 1.0 / Math.Sqrt(D);

            // LN1 on H[l] -> LN1Out
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                    LayerNormForwardVec(c.H, l, b, t, D, ln1_g.W, ln1_b.W, c.LN1Out, c.LN1Mean, c.LN1InvStd, c.LN1NormZ);

            // Q,K,V
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                    for (int d = 0; d < D; d++)
                    {
                        double q = 0, k = 0, v = 0;
                        for (int u = 0; u < D; u++)
                        {
                            double x = c.LN1Out[l, b, t, u];
                            q += x * Wq.W[u, d];
                            k += x * Wk.W[u, d];
                            v += x * Wv.W[u, d];
                        }
                        c.Q[l, b, t, d] = q;
                        c.K[l, b, t, d] = k;
                        c.Val[l, b, t, d] = v;
                    }

            // attention scores with causal mask -> softmax -> Scores
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                {
                    double max = double.NegativeInfinity;
                    for (int s = 0; s < T; s++)
                    {
                        double score = 0;
                        for (int d = 0; d < D; d++)
                            score += c.Q[l, b, t, d] * c.K[l, b, s, d];
                        if (s > t) score = double.NegativeInfinity;
                        c.Scores[l, b, t, s] = score * scale;
                        if (c.Scores[l, b, t, s] > max) max = c.Scores[l, b, t, s];
                    }
                    double sum = 0;
                    for (int s = 0; s < T; s++)
                    {
                        double e = double.IsNegativeInfinity(c.Scores[l, b, t, s]) ? 0 : Math.Exp(c.Scores[l, b, t, s] - max);
                        c.Scores[l, b, t, s] = e;
                        sum += e;
                    }
                    if (sum == 0) sum = 1;
                    for (int s = 0; s < T; s++)
                        c.Scores[l, b, t, s] /= sum;
                }

            // attention output = Scores @ Val
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                    for (int d = 0; d < D; d++)
                    {
                        double sum = 0;
                        for (int s = 0; s < T; s++)
                            sum += c.Scores[l, b, t, s] * c.Val[l, b, s, d];
                        c.AttnOut[l, b, t, d] = sum;
                    }

            // project with Wo and residual
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                {
                    double[] tmp = new double[D];
                    for (int d = 0; d < D; d++)
                    {
                        double sum = 0;
                        for (int u = 0; u < D; u++)
                            sum += c.AttnOut[l, b, t, u] * Wo.W[u, d];
                        tmp[d] = sum;
                    }
                    for (int d = 0; d < D; d++)
                        c.H[l, b, t, d] += tmp[d];
                }

            // LN2 on H[l] -> LN2Out
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                    LayerNormForwardVec(c.H, l, b, t, D, ln2_g.W, ln2_b.W, c.LN2Out, c.LN2Mean, c.LN2InvStd, c.LN2NormZ);

            // MLP: ReLU(W1*x + b1), then W2 and residual to H[l+1]
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                {
                    for (int h = 0; h < Dhid; h++)
                    {
                        double sum = b1.W[h];
                        for (int u = 0; u < D; u++)
                            sum += c.LN2Out[l, b, t, u] * W1.W[u, h];
                        c.M1[l, b, t, h] = sum;
                        c.M1Mask[l, b, t, h] = sum > 0 ? 1.0 : 0.0;
                    }

                    double[] tmp = new double[D];
                    for (int d = 0; d < D; d++)
                    {
                        double sum = b2.W[d];
                        for (int h = 0; h < Dhid; h++)
                            sum += (c.M1[l, b, t, h] > 0 ? c.M1[l, b, t, h] : 0.0) * W2.W[h, d];
                        tmp[d] = sum;
                    }
                    for (int d = 0; d < D; d++)
                        c.H[l + 1, b, t, d] = c.H[l, b, t, d] + tmp[d];
                }
        }

        public void Backward(ForwardCache c, int l)
        {
            int B = c.B, T = c.T, Dm = D;
            double scale = 1.0 / Math.Sqrt(Dm);

            // MLP backward
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                {
                    // d tmp from residual to H[l]
                    double[] dtmp = new double[Dm];
                    for (int d = 0; d < Dm; d++)
                        dtmp[d] = c.DH[l + 1, b, t, d];

                    // grads b2
                    for (int d = 0; d < Dm; d++)
                        b2.G[d] += dtmp[d];

                    // dM1 via W2^T and ReLU
                    for (int h = 0; h < Dhid; h++)
                    {
                        double grad = 0;
                        for (int d = 0; d < Dm; d++)
                        {
                            W2.G[h, d] += (c.M1[l, b, t, h] > 0 ? c.M1[l, b, t, h] : 0.0) * dtmp[d];
                            grad += W2.W[h, d] * dtmp[d];
                        }
                        if (c.M1Mask[l, b, t, h] == 0.0) grad = 0;
                        c.dM1[l, b, t, h] = grad;
                    }

                    // grads W1, b1 and to LN2Out
                    for (int h = 0; h < Dhid; h++)
                    {
                        b1.G[h] += c.dM1[l, b, t, h];
                        for (int u = 0; u < Dm; u++)
                        {
                            W1.G[u, h] += c.LN2Out[l, b, t, u] * c.dM1[l, b, t, h];
                            c.dLN2Out[l, b, t, u] += W1.W[u, h] * c.dM1[l, b, t, h];
                        }
                    }

                    // residual path to H[l]
                    for (int d = 0; d < Dm; d++)
                        c.DH[l, b, t, d] += c.DH[l + 1, b, t, d];
                }

            // LN2 backward
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                    LayerNormBackwardVec(c.H, l, b, t, Dm, c.dLN2Out, c.LN2NormZ, c.LN2InvStd, ln2_g, ln2_b, c.dA2);

            // attention projection Wo and AttnOut grad
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                {
                    double[] dtmp = new double[Dm];
                    for (int d = 0; d < Dm; d++)
                        dtmp[d] = c.DH[l, b, t, d];

                    for (int d = 0; d < Dm; d++)
                        for (int u = 0; u < Dm; u++)
                            Wo.G[u, d] += c.AttnOut[l, b, t, u] * dtmp[d];

                    for (int u = 0; u < Dm; u++)
                    {
                        double sum = 0;
                        for (int d = 0; d < Dm; d++)
                            sum += Wo.W[u, d] * dtmp[d];
                        c.dAttnOut[l, b, t, u] += sum;
                    }
                }

            // attention: out = Scores @ Val
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                    for (int s = 0; s < T; s++)
                    {
                        double dsProb = 0;
                        for (int d = 0; d < Dm; d++)
                        {
                            dsProb += c.dAttnOut[l, b, t, d] * c.Val[l, b, s, d];
                            c.dVal[l, b, s, d] += c.Scores[l, b, t, s] * c.dAttnOut[l, b, t, d];
                        }
                        c.dScores[l, b, t, s] += dsProb;
                    }

            // softmax grad back to pre-softmax (row-wise)
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                {
                    double dot = 0;
                    for (int s = 0; s < T; s++) dot += c.dScores[l, b, t, s] * c.Scores[l, b, t, s];
                    for (int s = 0; s < T; s++)
                    {
                        double p = c.Scores[l, b, t, s];
                        double dZ = p * (c.dScores[l, b, t, s] - dot) * scale;
                        c.dScores[l, b, t, s] = dZ;
                    }
                }

            // scores = Q K^T -> dQ and dK
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                    for (int s = 0; s < T; s++)
                    {
                        double ds = c.dScores[l, b, t, s];
                        if (s > t) continue;
                        for (int d = 0; d < Dm; d++)
                        {
                            c.dQ[l, b, t, d] += ds * c.K[l, b, s, d];
                            c.dK[l, b, s, d] += ds * c.Q[l, b, t, d];
                        }
                    }

            // back Q,K,Val linear projections and accumulate to LN1Out
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                    for (int u = 0; u < Dm; u++)
                    {
                        double x = c.LN1Out[l, b, t, u];

                        for (int d = 0; d < Dm; d++)
                        {
                            Wq.G[u, d] += x * c.dQ[l, b, t, d];
                            Wk.G[u, d] += x * c.dK[l, b, t, d];
                            Wv.G[u, d] += x * c.dVal[l, b, t, d];
                        }

                        double sumQ = 0, sumK = 0, sumV = 0;
                        for (int d = 0; d < Dm; d++)
                        {
                            sumQ += Wq.W[u, d] * c.dQ[l, b, t, d];
                            sumK += Wk.W[u, d] * c.dK[l, b, t, d];
                            sumV += Wv.W[u, d] * c.dVal[l, b, t, d];
                        }
                        c.dLN1Out[l, b, t, u] += sumQ + sumK + sumV;
                    }

            // LN1 backward
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                    LayerNormBackwardVec(c.H, l, b, t, Dm, c.dLN1Out, c.LN1NormZ, c.LN1InvStd, ln1_g, ln1_b, c.dA1);

            // add dA1 and dA2 to DH[l]
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                    for (int d = 0; d < Dm; d++)
                        c.DH[l, b, t, d] += c.dA1[l, b, t, d] + c.dA2[l, b, t, d];
        }

        // layer norm forward on vector
        static void LayerNormForwardVec(double[,,,] H, int l, int b, int t, int D,
                                        double[] gamma, double[] beta,
                                        double[,,,] Y, double[,,] mean, double[,,] invstd, double[,,,] normZ)
        {
            double m = 0;
            for (int d = 0; d < D; d++) m += H[l, b, t, d];
            m /= D;
            double v = 0;
            for (int d = 0; d < D; d++) { double u = H[l, b, t, d] - m; v += u * u; }
            v /= D;
            double istd = 1.0 / Math.Sqrt(v + 1e-5);
            mean[l, b, t] = m;
            invstd[l, b, t] = istd;
            for (int d = 0; d < D; d++)
            {
                double z = (H[l, b, t, d] - m) * istd;
                normZ[l, b, t, d] = z;
                Y[l, b, t, d] = z * gamma[d] + beta[d];
            }
        }

        // layer norm backward on vector
        static void LayerNormBackwardVec(double[,,,] H, int l, int b, int t, int D,
                                         double[,,,] dY, double[,,,] normZ, double[,,] invstd,
                                         Param1D gamma, Param1D beta, double[,,,] dA)
        {
            double istd = invstd[l, b, t];
            double sumDyG = 0, sumDyGz = 0;
            for (int d = 0; d < D; d++)
            {
                double dy = dY[l, b, t, d];
                double g = gamma.W[d];
                sumDyG += dy * g;
                sumDyGz += dy * g * normZ[l, b, t, d];
            }

            for (int d = 0; d < D; d++)
            {
                double dy = dY[l, b, t, d];
                double z = normZ[l, b, t, d];
                gamma.G[d] += dy * z;
                beta.G[d] += dy;

                double dyg = dy * gamma.W[d];
                double dx = (1.0 / D) * istd * (D * dyg - sumDyG - z * sumDyGz);
                dA[l, b, t, d] += dx;
            }
        }
    }

    // forward cache
    public class ForwardCache
    {
        public int B, T, D, V, L, Dhid;

        // logits
        public double[,,] Logits;   // [B,T,V]
        public double[,,] DLogits;  // [B,T,V]

        // hidden states
        public double[,,,] H;       // [L+1,B,T,D]
        public double[,,,] DH;      // [L+1,B,T,D]

        // LN1 caches
        public double[,,,] LN1Out;  // [L,B,T,D]
        public double[,,] LN1Mean;  // [L,B,T]
        public double[,,] LN1InvStd;// [L,B,T]
        public double[,,,] LN1NormZ;// [L,B,T,D]
        public double[,,,] dLN1Out; // [L,B,T,D]
        public double[,,,] dA1;     // [L,B,T,D]

        // LN2 caches
        public double[,,,] LN2Out;  // [L,B,T,D]
        public double[,,] LN2Mean;  // [L,B,T]
        public double[,,] LN2InvStd;// [L,B,T]
        public double[,,,] LN2NormZ;// [L,B,T,D]
        public double[,,,] dLN2Out; // [L,B,T,D]
        public double[,,,] dA2;     // [L,B,T,D]

        // attention tensors
        public double[,,,] Q;       // [L,B,T,D]
        public double[,,,] K;       // [L,B,T,D]
        public double[,,,] Val;     // [L,B,T,D]
        public double[,,,] dQ;      // [L,B,T,D]
        public double[,,,] dK;      // [L,B,T,D]
        public double[,,,] dVal;    // [L,B,T,D]
        public double[,,,] AttnOut; // [L,B,T,D]
        public double[,,,] dAttnOut;// [L,B,T,D]
        public double[,,,] Scores;  // [L,B,T,T]
        public double[,,,] dScores; // [L,B,T,T]

        // MLP
        public double[,,,] M1;      // [L,B,T,Dhid]
        public double[,,,] dM1;     // [L,B,T,Dhid]
        public double[,,,] M1Mask;  // [L,B,T,Dhid]

        // ids for embedding grads
        public int[,] XTokIds;      // [B,T]

        public ForwardCache(int B, int T, int D, int V, int L, int Dhid)
        {
            this.B = B; this.T = T; this.D = D; this.V = V; this.L = L; this.Dhid = Dhid;

            Logits = new double[B, T, V];
            DLogits = new double[B, T, V];

            H = new double[L + 1, B, T, D];
            DH = new double[L + 1, B, T, D];

            LN1Out = new double[L, B, T, D];
            LN1Mean = new double[L, B, T];
            LN1InvStd = new double[L, B, T];
            LN1NormZ = new double[L, B, T, D];
            dLN1Out = new double[L, B, T, D];
            dA1 = new double[L, B, T, D];

            LN2Out = new double[L, B, T, D];
            LN2Mean = new double[L, B, T];
            LN2InvStd = new double[L, B, T];
            LN2NormZ = new double[L, B, T, D];
            dLN2Out = new double[L, B, T, D];
            dA2 = new double[L, B, T, D];

            Q = new double[L, B, T, D];
            K = new double[L, B, T, D];
            Val = new double[L, B, T, D];
            dQ = new double[L, B, T, D];
            dK = new double[L, B, T, D];
            dVal = new double[L, B, T, D];

            Scores = new double[L, B, T, T];
            dScores = new double[L, B, T, T];

            AttnOut = new double[L, B, T, D];
            dAttnOut = new double[L, B, T, D];

            M1 = new double[L, B, T, Dhid];
            dM1 = new double[L, B, T, Dhid];
            M1Mask = new double[L, B, T, Dhid];

            XTokIds = new int[B, T];
        }

        public void CaptureInputIds(int[,] X)
        {
            for (int b = 0; b < B; b++)
                for (int t = 0; t < T; t++)
                    XTokIds[b, t] = X[b, t];
        }
    }
}

// optimizer (Adam)
public class Adam
{
    const double beta1 = 0.9;
    const double beta2 = 0.999;
    const double eps = 1e-8;
    int t = 0;
    List<IParam> ps = new();

    public void Add(IParam p) => ps.Add(p);

    public void Step(double lr, double wd)
    {
        t++;
        foreach (var p in ps) p.AdamStep(lr, wd, beta1, beta2, eps, t);
    }
}

// parameter interface
public interface IParam { void AdamStep(double lr, double wd, double b1, double b2, double eps, int t); void ZeroGrad(); }

// 1D parameter
public class Param1D : IParam
{
    public double[] W, G, m, v;
    public Param1D(int n, double init = 0.0)
    {
        W = new double[n]; G = new double[n]; m = new double[n]; v = new double[n];
        for (int i = 0; i < n; i++) W[i] = init;
    }
    public void ZeroGrad() => Array.Clear(G, 0, G.Length);
    public void AdamStep(double lr, double wd, double b1, double b2, double eps, int t)
    {
        double bc1 = 1 - Math.Pow(b1, t);
        double bc2 = 1 - Math.Pow(b2, t);
        for (int i = 0; i < W.Length; i++)
        {
            double g = G[i];
            if (wd > 0) g += wd * W[i];
            m[i] = b1 * m[i] + (1 - b1) * g;
            v[i] = b2 * v[i] + (1 - b2) * g * g;
            double mhat = m[i] / bc1;
            double vhat = v[i] / bc2;
            W[i] -= lr * (mhat / (Math.Sqrt(vhat) + eps));
        }
        ZeroGrad();
    }
}

// 2D parameter
public class Param2D : IParam
{
    public double[,] W, G, m, v;
    int R, C;
    public Param2D(int rows, int cols, Random rng = null, double scale = 0.0)
    {
        R = rows; C = cols;
        W = new double[R, C]; G = new double[R, C]; m = new double[R, C]; v = new double[R, C];
        if (rng != null && scale > 0)
            for (int i = 0; i < R; i++)
                for (int j = 0; j < C; j++)
                    W[i, j] = (rng.NextDouble() * 2 - 1) * scale;
    }
    public void ZeroGrad() => Array.Clear(G, 0, G.Length);
    public void AdamStep(double lr, double wd, double b1, double b2, double eps, int t)
    {
        double bc1 = 1 - Math.Pow(b1, t);
        double bc2 = 1 - Math.Pow(b2, t);
        for (int i = 0; i < R; i++)
            for (int j = 0; j < C; j++)
            {
                double g = G[i, j];
                if (wd > 0) g += wd * W[i, j];
                m[i, j] = b1 * m[i, j] + (1 - b1) * g;
                v[i, j] = b2 * v[i, j] + (1 - b2) * g * g;
                double mhat = m[i, j] / bc1;
                double vhat = v[i, j] / bc2;
                W[i, j] -= lr * (mhat / (Math.Sqrt(vhat) + eps));
            }
        ZeroGrad();
    }
}

// training meter for ETA and epoch estimates
public class TrainingMeter
{
    readonly double targetAvgLoss;
    readonly int maxHistoryPoints;
    readonly int durationWindow;
    readonly List<(int epoch, double avgLoss)> history = new();
    readonly Queue<double> durationsMs = new();
    double sumDurationsMs = 0.0;
    public int MaxEpochsCap { get; }

    public TrainingMeter(double targetAvgLoss, int maxHistoryPoints, int durationWindow, int maxEpochsCap)
    {
        this.targetAvgLoss = targetAvgLoss;
        this.maxHistoryPoints = Math.Max(50, maxHistoryPoints);
        this.durationWindow = Math.Max(20, durationWindow);
        this.MaxEpochsCap = Math.Max(1, maxEpochsCap);
    }

    public void RecordEpoch(int epoch, double avgLoss, double epochMs)
    {
        history.Add((epoch, avgLoss));
        if (history.Count > maxHistoryPoints) history.RemoveAt(0);
        durationsMs.Enqueue(epochMs);
        sumDurationsMs += epochMs;
        while (durationsMs.Count > durationWindow) sumDurationsMs -= durationsMs.Dequeue();
    }

    public double AvgEpochMs() => durationsMs.Count == 0 ? 0.0 : sumDurationsMs / durationsMs.Count;

    public int EstimatedEpochsToGoal()
    {
        if (history.Count < 20) return int.MaxValue;
        int window = Math.Min(history.Count, Math.Max(60, maxHistoryPoints / 2));
        var slice = history.Skip(history.Count - window).ToArray();
        int m = slice.Length;

        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < m; i++) { sx += slice[i].epoch; sy += slice[i].avgLoss; sxx += slice[i].epoch * slice[i].epoch; sxy += slice[i].epoch * slice[i].avgLoss; }
        double denom = m * sxx - sx * sx;
        if (Math.Abs(denom) < 1e-9) return int.MaxValue;

        double slope = (m * sxy - sx * sy) / denom;
        double current = slice[m - 1].avgLoss;
        if (slope >= -1e-6) return int.MaxValue;

        double need = (current - targetAvgLoss) / -slope;
        if (need <= 0) return 0;
        if (need > 1e7) return int.MaxValue;
        return (int)Math.Ceiling(need);
    }

    public TimeSpan EstimatedTimeLeft(int remainingEpochs)
    {
        if (remainingEpochs <= 0) return TimeSpan.Zero;
        double msPerEpoch = AvgEpochMs();
        if (msPerEpoch <= 0) return TimeSpan.Zero;
        remainingEpochs = Math.Max(0, Math.Min(remainingEpochs, MaxEpochsCap));
        double totalMs = msPerEpoch * remainingEpochs;
        if (double.IsNaN(totalMs) || double.IsInfinity(totalMs)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(Math.Min(totalMs, 24.0 * 3600 * 1000));
    }
}