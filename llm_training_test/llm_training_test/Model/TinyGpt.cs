using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TinyGptDemo.Utils;

namespace TinyGptDemo.Model
{
    public sealed class TinyGpt
    {
        private readonly int V;
        private readonly int Tctx;
        private readonly int D;
        private readonly int L;
        private readonly int Dhid;

        private readonly Param2D tokEmb;
        private readonly Param2D posEmb;
        private readonly Block[] blocks;
        private readonly Param2D Wout;
        private readonly Param1D Bout;

        private readonly Param1D Wstart;
        private readonly Param1D Wend;
        private readonly Param1D Bstart;
        private readonly Param1D Bend;

        private readonly Adam adam;

        private readonly object headGradLock = new();
        private readonly object embedGradLock = new();

        private readonly ParallelOptions parallelOpts =
            new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

        public TinyGpt(int vocabSize, ModelConfig config, Random rng)
        {
            V = vocabSize;
            Tctx = config.ContextLength;
            D = config.EmbeddingDim;
            L = config.Layers;
            Dhid = config.HiddenDim;

            tokEmb = new Param2D(V, D, rng, 0.02f);
            posEmb = new Param2D(Tctx, D, rng, 0.02f);

            blocks = new Block[L];
            for (int i = 0; i < L; i++)
                blocks[i] = new Block(D, Dhid, rng, parallelOpts);

            Wout = new Param2D(D, V, rng, 0.02f);
            Bout = new Param1D(V);

            Wstart = new Param1D(D);
            Wend = new Param1D(D);
            Bstart = new Param1D(1);
            Bend = new Param1D(1);

            adam = new Adam();
            adam.Add(tokEmb);
            adam.Add(posEmb);
            adam.Add(Wout);
            adam.Add(Bout);
            adam.Add(Wstart);
            adam.Add(Wend);
            adam.Add(Bstart);
            adam.Add(Bend);
            foreach (var block in blocks) block.Register(adam);
        }

        public ForwardCache Forward(int[,] X)
        {
            int B = X.GetLength(0);
            int T = X.GetLength(1);
            if (T > Tctx)
                throw new ArgumentException($"Sequence length {T} exceeds model context window {Tctx}.");

            var cache = new ForwardCache(B, T, D, V, L, Dhid);
            cache.CaptureInputIds(X);

            Parallel.For(0, B, parallelOpts, b =>
            {
                for (int t = 0; t < T; t++)
                {
                    Span<float> h0 = cache.GetHSpan(0, b, t);
                    ReadOnlySpan<float> tok = tokEmb.RowSpan(X[b, t]);
                    ReadOnlySpan<float> pos = posEmb.RowSpan(Math.Min(t, Tctx - 1));
                    for (int d = 0; d < D; d++)
                        h0[d] = tok[d] + pos[d];
                }
            });

            for (int l = 0; l < L; l++)
                blocks[l].Forward(cache, l);

            Parallel.For(0, B, parallelOpts, b =>
            {
                for (int t = 0; t < T; t++)
                {
                    ReadOnlySpan<float> h = cache.GetHSpan(L, b, t);

                    float s = Bstart.W[0];
                    float e = Bend.W[0];
                    for (int d = 0; d < D; d++)
                    {
                        s += h[d] * Wstart.W[d];
                        e += h[d] * Wend.W[d];
                    }
                    cache.StartLogits[b, t] = s;
                    cache.EndLogits[b, t] = e;

                    for (int v = 0; v < V; v++)
                    {
                        float sum = Bout.W[v];
                        for (int d = 0; d < D; d++)
                            sum += h[d] * Wout.W[d][v];
                        cache.Logits[b, t, v] = sum;
                    }
                }
            });

            return cache;
        }

        public void Backward(ForwardCache cache)
        {
            int B = cache.B;
            int T = cache.T;

            tokEmb.ZeroGrad();
            posEmb.ZeroGrad();
            Wout.ZeroGrad();
            Bout.ZeroGrad();
            Wstart.ZeroGrad();
            Wend.ZeroGrad();
            Bstart.ZeroGrad();
            Bend.ZeroGrad();
            foreach (var block in blocks) block.ZeroGrad();

            Parallel.For<HeadGrad>(0, B, parallelOpts,
                () => new HeadGrad(D, V),
                (b, _, local) =>
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> dh = cache.GetDHSpan(L, b, t);
                        dh.Clear();
                        ReadOnlySpan<float> h = cache.GetHSpan(L, b, t);

                        float gradStart = cache.DStartLogits[b, t];
                        float gradEnd = cache.DEndLogits[b, t];

                        local.Bstart += gradStart;
                        local.Bend += gradEnd;
                        for (int d = 0; d < D; d++)
                        {
                            local.Wstart[d] += h[d] * gradStart;
                            local.Wend[d] += h[d] * gradEnd;
                            dh[d] += Wstart.W[d] * gradStart;
                            dh[d] += Wend.W[d] * gradEnd;
                        }

                        for (int v = 0; v < V; v++)
                        {
                            float grad = cache.DLogits[b, t, v];
                            local.Bout[v] += grad;

                            for (int d = 0; d < D; d++)
                            {
                                local.Wout[d][v] += h[d] * grad;
                                dh[d] += Wout.W[d][v] * grad;
                            }
                        }
                    }

                    return local;
                },
                local =>
                {
                    lock (headGradLock)
                    {
                        Bstart.G[0] += local.Bstart;
                        Bend.G[0] += local.Bend;

                        for (int d = 0; d < D; d++)
                        {
                            Wstart.G[d] += local.Wstart[d];
                            Wend.G[d] += local.Wend[d];
                        }

                        for (int v = 0; v < V; v++)
                            Bout.G[v] += local.Bout[v];

                        for (int d = 0; d < D; d++)
                        {
                            Span<float> dst = Wout.GradRowSpan(d);
                            float[] src = local.Wout[d];
                            for (int v = 0; v < V; v++)
                                dst[v] += src[v];
                        }
                    }
                });

            for (int l = L - 1; l >= 0; l--)
                blocks[l].Backward(cache, l);

            Parallel.For<EmbedGrad>(0, B, parallelOpts,
                () => new EmbedGrad(Tctx, D, V),
                (b, _, grad) =>
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> dh = cache.GetDHSpan(0, b, t);
                        int posIdx = Math.Min(t, Tctx - 1);
                        int tokIdx = cache.XTokIds[b, t];

                        float[] posRow = grad.Pos[posIdx];
                        float[] tokRow = grad.Tok[tokIdx];

                        for (int d = 0; d < D; d++)
                        {
                            float val = dh[d];
                            posRow[d] += val;
                            tokRow[d] += val;
                        }
                    }

                    return grad;
                },
                grad =>
                {
                    lock (embedGradLock)
                    {
                        for (int i = 0; i < Tctx; i++)
                        {
                            Span<float> dst = posEmb.GradRowSpan(i);
                            float[] src = grad.Pos[i];
                            for (int d = 0; d < D; d++)
                                dst[d] += src[d];
                        }

                        for (int i = 0; i < V; i++)
                        {
                            Span<float> dst = tokEmb.GradRowSpan(i);
                            float[] src = grad.Tok[i];
                            for (int d = 0; d < D; d++)
                                dst[d] += src[d];
                        }
                    }
                });
        }

        public void AdamStep(float lr, float wd) => adam.Step(lr, wd);

        public sealed class KvCache
        {
            public readonly int Layers;
            public readonly int Tctx;
            public readonly int D;
            public readonly float[,,] K; // [L, T, D]
            public readonly float[,,] V; // [L, T, D]

            public KvCache(int layers, int tctx, int d)
            {
                Layers = layers;
                Tctx = tctx;
                D = d;
                K = new float[layers, tctx, d];
                V = new float[layers, tctx, d];
            }
        }

        public int[] Generate(int[] prompt, int maxNewTokens, int? eosToken = null)
        {
            if (prompt.Length > Tctx) throw new ArgumentException("Prompt exceeds context length.");
            var kv = new KvCache(L, Tctx, D);

            var logits = new float[V];
            int pos = 0;

            var h = new float[D];

            for (int i = 0; i < prompt.Length; i++)
            {
                int token = prompt[i];
                ReadOnlySpan<float> tok = tokEmb.RowSpan(token);
                ReadOnlySpan<float> posRow = posEmb.RowSpan(pos);
                for (int d = 0; d < D; d++) h[d] = tok[d] + posRow[d];

                for (int l = 0; l < L; l++)
                    blocks[l].ForwardNext(h, pos, l, kv);

                for (int v = 0; v < V; v++)
                {
                    float sum = Bout.W[v];
                    for (int d = 0; d < D; d++)
                        sum += h[d] * Wout.W[d][v];
                    logits[v] = sum;
                }

                pos++;
            }

            var result = new List<int>(prompt.Length + maxNewTokens);
            result.AddRange(prompt);

            for (int step = 0; step < maxNewTokens; step++)
            {
                int next = Argmax(logits);
                if (eosToken.HasValue && next == eosToken.Value) break;
                result.Add(next);

                if (pos >= Tctx) break;

                ReadOnlySpan<float> tok = tokEmb.RowSpan(next);
                ReadOnlySpan<float> posRow = posEmb.RowSpan(pos);
                for (int d = 0; d < D; d++) h[d] = tok[d] + posRow[d];

                for (int l = 0; l < L; l++)
                    blocks[l].ForwardNext(h, pos, l, kv);

                for (int v = 0; v < V; v++)
                {
                    float sum = Bout.W[v];
                    for (int d = 0; d < D; d++)
                        sum += h[d] * Wout.W[d][v];
                    logits[v] = sum;
                }

                pos++;
            }

            return result.ToArray();

            static int Argmax(float[] arr)
            {
                int idx = 0;
                float best = arr[0];
                for (int i = 1; i < arr.Length; i++)
                    if (arr[i] > best) { best = arr[i]; idx = i; }
                return idx;
            }
        }

        public (int start, int end) PredictSpan(int[] tokens)
        {
            var X = new int[1, tokens.Length];
            for (int i = 0; i < tokens.Length; i++) X[0, i] = tokens[i];
            var cache = Forward(X);
            int bestS = 0, bestE = 0;
            float sMax = float.NegativeInfinity;
            float eMax = float.NegativeInfinity;
            for (int t = 0; t < tokens.Length; t++)
            {
                float s = cache.StartLogits[0, t];
                float e = cache.EndLogits[0, t];
                if (s > sMax) { sMax = s; bestS = t; }
                if (e > eMax) { eMax = e; bestE = t; }
            }
            if (bestE < bestS) bestE = bestS;
            return (bestS, bestE);
        }

        private sealed class Block
        {
            private readonly int D;
            private readonly int Dhid;
            private readonly ParallelOptions options;

            private readonly Param1D ln1_g;
            private readonly Param1D ln1_b;
            private readonly Param1D ln2_g;
            private readonly Param1D ln2_b;

            private readonly Param2D Wq;
            private readonly Param2D Wk;
            private readonly Param2D Wv;
            private readonly Param2D Wo;

            private readonly Param2D W1;
            private readonly Param2D W2;
            private readonly Param1D b1;
            private readonly Param1D b2;

            private readonly object gradLock = new();

            public Block(int dModel, int dHidden, Random rng, ParallelOptions options)
            {
                D = dModel;
                Dhid = dHidden;
                this.options = options;

                ln1_g = new Param1D(D, 1f);
                ln1_b = new Param1D(D, 0f);
                ln2_g = new Param1D(D, 1f);
                ln2_b = new Param1D(D, 0f);

                float scale = 0.02f;

                Wq = new Param2D(D, D, rng, scale);
                Wk = new Param2D(D, D, rng, scale);
                Wv = new Param2D(D, D, rng, scale);
                Wo = new Param2D(D, D, rng, scale);

                W1 = new Param2D(D, Dhid, rng, scale);
                W2 = new Param2D(Dhid, D, rng, scale);
                b1 = new Param1D(Dhid);
                b2 = new Param1D(D);
            }

            public void Register(Adam optimizer)
            {
                optimizer.Add(ln1_g);
                optimizer.Add(ln1_b);
                optimizer.Add(ln2_g);
                optimizer.Add(ln2_b);
                optimizer.Add(Wq);
                optimizer.Add(Wk);
                optimizer.Add(Wv);
                optimizer.Add(Wo);
                optimizer.Add(W1);
                optimizer.Add(W2);
                optimizer.Add(b1);
                optimizer.Add(b2);
            }

            public void ZeroGrad()
            {
                ln1_g.ZeroGrad();
                ln1_b.ZeroGrad();
                ln2_g.ZeroGrad();
                ln2_b.ZeroGrad();
                Wq.ZeroGrad();
                Wk.ZeroGrad();
                Wv.ZeroGrad();
                Wo.ZeroGrad();
                W1.ZeroGrad();
                W2.ZeroGrad();
                b1.ZeroGrad();
                b2.ZeroGrad();
            }

            public void Forward(ForwardCache cache, int l)
            {
                int B = cache.B;
                int T = cache.T;
                float scale = 1f / MathF.Sqrt(D);

                Parallel.For(0, B, options, b =>
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> h = cache.GetHSpan(l, b, t);
                        Span<float> ln1Out = cache.GetLN1OutSpan(l, b, t);
                        Span<float> ln1Norm = cache.GetLN1NormZSpan(l, b, t);
                        ref float mean = ref cache.GetLN1MeanRef(l, b, t);
                        ref float invStd = ref cache.GetLN1InvStdRef(l, b, t);

                        LayerNormForwardVec(h, ln1Out, ln1Norm, ref mean, ref invStd, ln1_g.W, ln1_b.W);

                        Span<float> q = cache.GetQSpan(l, b, t);
                        Span<float> k = cache.GetKSpan(l, b, t);
                        Span<float> v = cache.GetVSpan(l, b, t);

                        for (int dOut = 0; dOut < D; dOut++)
                        {
                            float sumQ = 0f, sumK = 0f, sumV = 0f;
                            for (int dIn = 0; dIn < D; dIn++)
                            {
                                float x = ln1Out[dIn];
                                sumQ += x * Wq.W[dIn][dOut];
                                sumK += x * Wk.W[dIn][dOut];
                                sumV += x * Wv.W[dIn][dOut];
                            }
                            q[dOut] = sumQ;
                            k[dOut] = sumK;
                            v[dOut] = sumV;
                        }
                    }
                });

                Parallel.For(0, B, options, b =>
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> scoresRow = MemoryMarshal.CreateSpan(ref cache.Scores[l, b, t, 0], T);
                        float max = float.NegativeInfinity;

                        for (int s = 0; s < T; s++)
                        {
                            float score = Simd.Dot(cache.GetQSpan(l, b, t), cache.GetKSpan(l, b, s));
                            if (s > t) score = float.NegativeInfinity;
                            float scaled = score * scale;
                            scoresRow[s] = scaled;
                            if (scaled > max) max = scaled;
                        }

                        float sum = 0f;
                        for (int s = 0; s < T; s++)
                        {
                            float val = scoresRow[s];
                            float exp = float.IsNegativeInfinity(val) ? 0f : MathF.Exp(val - max);
                            scoresRow[s] = exp;
                            sum += exp;
                        }

                        if (sum == 0f) sum = 1f;
                        for (int s = 0; s < T; s++)
                            scoresRow[s] /= sum;
                    }
                });

                Parallel.For(0, B, options, b =>
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> attnOut = cache.GetAttnOutSpan(l, b, t);
                        attnOut.Clear();

                        for (int s = 0; s < T; s++)
                        {
                            float weight = cache.Scores[l, b, t, s];
                            Span<float> val = cache.GetVSpan(l, b, s);
                            for (int d = 0; d < D; d++)
                                attnOut[d] += weight * val[d];
                        }
                    }
                });

                Parallel.For(0, B, options, b =>
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> attnOut = cache.GetAttnOutSpan(l, b, t);
                        Span<float> hPrev = cache.GetHSpan(l, b, t);
                        Span<float> tmp = cache.GetTmpSpan(l, b, t);
                        tmp.Clear();

                        for (int dOut = 0; dOut < D; dOut++)
                        {
                            float sum = 0f;
                            for (int dIn = 0; dIn < D; dIn++)
                                sum += attnOut[dIn] * Wo.W[dIn][dOut];
                            tmp[dOut] = sum;
                        }

                        Span<float> hNext = cache.GetHSpan(l + 1, b, t);
                        for (int d = 0; d < D; d++)
                            hNext[d] = hPrev[d] + tmp[d];
                    }
                });

                Parallel.For(0, B, options, b =>
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> h = cache.GetHSpan(l, b, t);
                        Span<float> ln2Out = cache.GetLN2OutSpan(l, b, t);
                        Span<float> ln2Norm = cache.GetLN2NormZSpan(l, b, t);
                        ref float mean = ref cache.GetLN2MeanRef(l, b, t);
                        ref float invStd = ref cache.GetLN2InvStdRef(l, b, t);

                        LayerNormForwardVec(h, ln2Out, ln2Norm, ref mean, ref invStd, ln2_g.W, ln2_b.W);

                        Span<float> m1 = cache.GetM1Span(l, b, t);
                        Span<float> mask = cache.GetM1MaskSpan(l, b, t);

                        for (int hIdx = 0; hIdx < Dhid; hIdx++)
                        {
                            float sum = b1.W[hIdx];
                            for (int dIn = 0; dIn < D; dIn++)
                                sum += ln2Out[dIn] * W1.W[dIn][hIdx];
                            m1[hIdx] = sum;
                            mask[hIdx] = sum > 0f ? 1f : 0f;
                        }

                        Span<float> tmp = cache.GetTmpSpan(l, b, t);
                        tmp.Clear();

                        for (int dOut = 0; dOut < D; dOut++)
                        {
                            float sum = b2.W[dOut];
                            for (int hIdx = 0; hIdx < Dhid; hIdx++)
                            {
                                float act = m1[hIdx] > 0f ? m1[hIdx] : 0f;
                                sum += act * W2.W[hIdx][dOut];
                            }
                            tmp[dOut] = sum;
                        }

                        Span<float> hNext = cache.GetHSpan(l + 1, b, t);
                        for (int d = 0; d < D; d++)
                            hNext[d] += tmp[d];
                    }
                });
            }

            public void Backward(ForwardCache cache, int l)
            {
                int B = cache.B;
                int T = cache.T;
                float scale = 1f / MathF.Sqrt(D);

                Parallel.For<BlockGrad>(0, B, options,
                    () => new BlockGrad(D, Dhid),
                    (b, _, grad) =>
                    {
                        for (int t = 0; t < T; t++)
                        {
                            Span<float> dh = cache.GetDHSpan(l, b, t);
                            dh.Clear();
                            Span<float> dhNext = cache.GetDHSpan(l + 1, b, t);
                            for (int d = 0; d < D; d++)
                                dh[d] += dhNext[d];

                            cache.GetDLN2OutSpan(l, b, t).Clear();
                            cache.GetDLN1OutSpan(l, b, t).Clear();
                            cache.GetDA1Span(l, b, t).Clear();
                            cache.GetDA2Span(l, b, t).Clear();
                            cache.GetDQSpan(l, b, t).Clear();
                            cache.GetDKSpan(l, b, t).Clear();
                            cache.GetDValSpan(l, b, t).Clear();
                            cache.GetDAttnOutSpan(l, b, t).Clear();
                            cache.GetDM1Span(l, b, t).Clear();
                            MemoryMarshal.CreateSpan(ref cache.dScores[l, b, t, 0], T).Clear();
                        }

                        for (int t = 0; t < T; t++)
                        {
                            Span<float> dhNext = cache.GetDHSpan(l + 1, b, t);
                            Span<float> ln2Out = cache.GetLN2OutSpan(l, b, t);
                            Span<float> dLn2Out = cache.GetDLN2OutSpan(l, b, t);
                            Span<float> m1 = cache.GetM1Span(l, b, t);
                            Span<float> mask = cache.GetM1MaskSpan(l, b, t);
                            Span<float> dHidden = cache.GetDM1Span(l, b, t);

                            for (int dOut = 0; dOut < D; dOut++)
                                grad.B2[dOut] += dhNext[dOut];

                            for (int dOut = 0; dOut < D; dOut++)
                            {
                                float gradVal = dhNext[dOut];
                                for (int hIdx = 0; hIdx < Dhid; hIdx++)
                                {
                                    float act = m1[hIdx] > 0f ? m1[hIdx] : 0f;
                                    grad.W2[hIdx][dOut] += act * gradVal;
                                    dHidden[hIdx] += W2.W[hIdx][dOut] * gradVal;
                                }
                            }

                            for (int hIdx = 0; hIdx < Dhid; hIdx++)
                            {
                                if (mask[hIdx] == 0f)
                                    dHidden[hIdx] = 0f;
                            }

                            for (int hIdx = 0; hIdx < Dhid; hIdx++)
                            {
                                float gradVal = dHidden[hIdx];
                                grad.B1[hIdx] += gradVal;
                                for (int dIn = 0; dIn < D; dIn++)
                                {
                                    grad.W1[dIn][hIdx] += ln2Out[dIn] * gradVal;
                                    dLn2Out[dIn] += W1.W[dIn][hIdx] * gradVal;
                                }
                            }
                        }

                        for (int t = 0; t < T; t++)
                        {
                            Span<float> h = cache.GetHSpan(l, b, t);
                            Span<float> dLn2Out = cache.GetDLN2OutSpan(l, b, t);
                            Span<float> norm = cache.GetLN2NormZSpan(l, b, t);
                            float invStd = cache.GetLN2InvStdRef(l, b, t);
                            Span<float> da2 = cache.GetDA2Span(l, b, t);

                            LayerNormBackwardVec(h, dLn2Out, norm, invStd, ln2_g.W, grad.Ln2Gamma, grad.Ln2Beta, da2);
                        }

                        for (int t = 0; t < T; t++)
                        {
                            Span<float> dh = cache.GetDHSpan(l, b, t);
                            Span<float> attnOut = cache.GetAttnOutSpan(l, b, t);
                            Span<float> dAttn = cache.GetDAttnOutSpan(l, b, t);

                            for (int dOut = 0; dOut < D; dOut++)
                            {
                                float gradVal = dh[dOut];
                                for (int dIn = 0; dIn < D; dIn++)
                                {
                                    grad.Wo[dIn][dOut] += attnOut[dIn] * gradVal;
                                    dAttn[dIn] += Wo.W[dIn][dOut] * gradVal;
                                }
                            }
                        }

                        for (int t = 0; t < T; t++)
                        {
                            Span<float> dAttn = cache.GetDAttnOutSpan(l, b, t);

                            for (int s = 0; s < T; s++)
                            {
                                float weight = cache.Scores[l, b, t, s];
                                Span<float> val = cache.GetVSpan(l, b, s);
                                float ds = 0f;

                                for (int d = 0; d < D; d++)
                                {
                                    ds += dAttn[d] * val[d];
                                    cache.dVal[l, b, s, d] += weight * dAttn[d];
                                }

                                cache.dScores[l, b, t, s] += ds;
                            }
                        }

                        for (int t = 0; t < T; t++)
                        {
                            Span<float> scoresRow = MemoryMarshal.CreateSpan(ref cache.Scores[l, b, t, 0], T);
                            Span<float> dScoresRow = MemoryMarshal.CreateSpan(ref cache.dScores[l, b, t, 0], T);

                            float dot = 0f;
                            for (int s = 0; s < T; s++)
                                dot += dScoresRow[s] * scoresRow[s];

                            for (int s = 0; s < T; s++)
                            {
                                float p = scoresRow[s];
                                float gradVal = p * (dScoresRow[s] - dot) * scale;
                                dScoresRow[s] = s > t ? 0f : gradVal;
                            }
                        }

                        for (int t = 0; t < T; t++)
                        {
                            for (int s = 0; s < T; s++)
                            {
                                if (s > t) continue;
                                float ds = cache.dScores[l, b, t, s];
                                Span<float> dq = cache.GetDQSpan(l, b, t);
                                Span<float> dk = cache.GetDKSpan(l, b, s);

                                for (int d = 0; d < D; d++)
                                {
                                    dq[d] += ds * cache.K[l, b, s, d];
                                    dk[d] += ds * cache.Q[l, b, t, d];
                                }
                            }
                        }

                        for (int t = 0; t < T; t++)
                        {
                            Span<float> ln1Out = cache.GetLN1OutSpan(l, b, t);
                            Span<float> dLn1Out = cache.GetDLN1OutSpan(l, b, t);
                            Span<float> dq = cache.GetDQSpan(l, b, t);
                            Span<float> dk = cache.GetDKSpan(l, b, t);
                            Span<float> dv = cache.GetDValSpan(l, b, t);

                            for (int dOut = 0; dOut < D; dOut++)
                            {
                                float gradQ = dq[dOut];
                                float gradK = dk[dOut];
                                float gradV = dv[dOut];

                                for (int dIn = 0; dIn < D; dIn++)
                                {
                                    float x = ln1Out[dIn];
                                    grad.Wq[dIn][dOut] += x * gradQ;
                                    grad.Wk[dIn][dOut] += x * gradK;
                                    grad.Wv[dIn][dOut] += x * gradV;

                                    dLn1Out[dIn] += Wq.W[dIn][dOut] * gradQ;
                                    dLn1Out[dIn] += Wk.W[dIn][dOut] * gradK;
                                    dLn1Out[dIn] += Wv.W[dIn][dOut] * gradV;
                                }
                            }
                        }

                        for (int t = 0; t < T; t++)
                        {
                            Span<float> h = cache.GetHSpan(l, b, t);
                            Span<float> dLn1Out = cache.GetDLN1OutSpan(l, b, t);
                            Span<float> norm = cache.GetLN1NormZSpan(l, b, t);
                            float invStd = cache.GetLN1InvStdRef(l, b, t);
                            Span<float> da1 = cache.GetDA1Span(l, b, t);

                            LayerNormBackwardVec(h, dLn1Out, norm, invStd, ln1_g.W, grad.Ln1Gamma, grad.Ln1Beta, da1);
                        }

                        for (int t = 0; t < T; t++)
                        {
                            Span<float> dh = cache.GetDHSpan(l, b, t);
                            Span<float> da1 = cache.GetDA1Span(l, b, t);
                            Span<float> da2 = cache.GetDA2Span(l, b, t);

                            for (int d = 0; d < D; d++)
                                dh[d] += da1[d] + da2[d];
                        }

                        return grad;
                    },
                    grad =>
                    {
                        lock (gradLock)
                        {
                            grad.AccumulateInto(ln1_g, ln1_b, ln2_g, ln2_b,
                                Wq, Wk, Wv, Wo, W1, W2, b1, b2);
                        }
                    });
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void LayerNormForwardVec(
                Span<float> input, Span<float> output, Span<float> normZ,
                ref float mean, ref float invStd,
                ReadOnlySpan<float> gamma, ReadOnlySpan<float> beta)
            {
                float m = 0f;
                for (int i = 0; i < input.Length; i++) m += input[i];
                m /= input.Length;

                float variance = 0f;
                for (int i = 0; i < input.Length; i++)
                {
                    float u = input[i] - m;
                    variance += u * u;
                }
                variance /= input.Length;

                float istd = 1f / MathF.Sqrt(variance + 1e-5f);
                mean = m;
                invStd = istd;

                for (int i = 0; i < input.Length; i++)
                {
                    float z = (input[i] - m) * istd;
                    normZ[i] = z;
                    output[i] = z * gamma[i] + beta[i];
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void LayerNormBackwardVec(
                Span<float> input,
                Span<float> dOutput,
                Span<float> normZ,
                float invStd,
                ReadOnlySpan<float> gamma,
                float[] gammaGrad,
                float[] betaGrad,
                Span<float> dInput)
            {
                int D = input.Length;

                float sumDyG = 0f;
                float sumDyGz = 0f;

                for (int i = 0; i < D; i++)
                {
                    float dy = dOutput[i];
                    float g = gamma[i];
                    sumDyG += dy * g;
                    sumDyGz += dy * g * normZ[i];

                    gammaGrad[i] += dy * normZ[i];
                    betaGrad[i] += dy;
                }

                float scale = invStd / D;
                for (int i = 0; i < D; i++)
                {
                    float dy = dOutput[i];
                    float g = gamma[i];
                    float z = normZ[i];

                    float dyg = dy * g;
                    float dx = D * dyg - sumDyG - z * sumDyGz;
                    dInput[i] += scale * dx;
                }
            }

            public void ForwardNext(float[] h, int t, int l, KvCache kv)
            {
                float scale = 1f / MathF.Sqrt(D);

                float m1 = 0f;
                for (int i = 0; i < D; i++) m1 += h[i];
                m1 /= D;

                float var1 = 0f;
                for (int i = 0; i < D; i++)
                {
                    float u = h[i] - m1;
                    var1 += u * u;
                }
                var1 /= D;
                float invStd1 = 1f / MathF.Sqrt(var1 + 1e-5f);

                var ln1Out = new float[D];
                for (int i = 0; i < D; i++)
                {
                    float z = (h[i] - m1) * invStd1;
                    ln1Out[i] = z * ln1_g.W[i] + ln1_b.W[i];
                }

                var q = new float[D];
                var k = new float[D];
                var v = new float[D];

                for (int dOut = 0; dOut < D; dOut++)
                {
                    float sumQ = 0f, sumK = 0f, sumV = 0f;
                    for (int dIn = 0; dIn < D; dIn++)
                    {
                        float x = ln1Out[dIn];
                        sumQ += x * Wq.W[dIn][dOut];
                        sumK += x * Wk.W[dIn][dOut];
                        sumV += x * Wv.W[dIn][dOut];
                    }
                    q[dOut] = sumQ;
                    k[dOut] = sumK;
                    v[dOut] = sumV;
                }

                for (int d = 0; d < D; d++)
                {
                    kv.K[l, t, d] = k[d];
                    kv.V[l, t, d] = v[d];
                }

                var scores = new float[t + 1];
                float max = float.NegativeInfinity;
                for (int s = 0; s <= t; s++)
                {
                    float sc = 0f;
                    for (int d = 0; d < D; d++) sc += q[d] * kv.K[l, s, d];
                    sc *= scale;
                    scores[s] = sc;
                    if (sc > max) max = sc;
                }

                float sumExp = 0f;
                for (int s = 0; s <= t; s++)
                {
                    float e = MathF.Exp(scores[s] - max);
                    scores[s] = e;
                    sumExp += e;
                }
                float invSum = 1f / (sumExp == 0f ? 1f : sumExp);

                var attnOut = new float[D];
                for (int s = 0; s <= t; s++)
                {
                    float w = scores[s] * invSum;
                    for (int d = 0; d < D; d++)
                        attnOut[d] += w * kv.V[l, s, d];
                }

                var tmp = new float[D];
                for (int dOut = 0; dOut < D; dOut++)
                {
                    float sum = 0f;
                    for (int dIn = 0; dIn < D; dIn++)
                        sum += attnOut[dIn] * Wo.W[dIn][dOut];
                    tmp[dOut] = sum;
                }
                for (int d = 0; d < D; d++) h[d] = h[d] + tmp[d];

                float m2 = 0f;
                for (int i = 0; i < D; i++) m2 += h[i];
                m2 /= D;

                float var2 = 0f;
                for (int i = 0; i < D; i++)
                {
                    float u = h[i] - m2;
                    var2 += u * u;
                }
                var2 /= D;
                float invStd2 = 1f / MathF.Sqrt(var2 + 1e-5f);

                var ln2Out = new float[D];
                for (int i = 0; i < D; i++)
                {
                    float z = (h[i] - m2) * invStd2;
                    ln2Out[i] = z * ln2_g.W[i] + ln2_b.W[i];
                }

                var m1vec = new float[Dhid];
                var mask = new float[Dhid];
                for (int hIdx = 0; hIdx < Dhid; hIdx++)
                {
                    float sum = b1.W[hIdx];
                    for (int dIn = 0; dIn < D; dIn++)
                        sum += ln2Out[dIn] * W1.W[dIn][hIdx];
                    m1vec[hIdx] = sum;
                    mask[hIdx] = sum > 0f ? 1f : 0f;
                }

                Array.Clear(tmp, 0, D);
                for (int dOut = 0; dOut < D; dOut++)
                {
                    float sum = b2.W[dOut];
                    for (int hIdx = 0; hIdx < Dhid; hIdx++)
                    {
                        float act = m1vec[hIdx] > 0f ? m1vec[hIdx] : 0f;
                        sum += act * W2.W[hIdx][dOut];
                    }
                    tmp[dOut] = sum;
                }

                for (int d = 0; d < D; d++) h[d] += tmp[d];
            }
        }

        public sealed class ForwardCache
        {
            public int B { get; }
            public int T { get; }
            public int D { get; }
            public int V { get; }
            public int L { get; }
            public int Dhid { get; }

            public float[,,] Logits;
            public float[,,] DLogits;

            public float[,] StartLogits;
            public float[,] EndLogits;
            public float[,] DStartLogits;
            public float[,] DEndLogits;

            public float[,,,] H;
            public float[,,,] DH;

            public float[,,,] LN1Out;
            public float[,,] LN1Mean;
            public float[,,] LN1InvStd;
            public float[,,,] LN1NormZ;
            public float[,,,] dLN1Out;
            public float[,,,] dA1;

            public float[,,,] LN2Out;
            public float[,,] LN2Mean;
            public float[,,] LN2InvStd;
            public float[,,,] LN2NormZ;
            public float[,,,] dLN2Out;
            public float[,,,] dA2;

            public float[,,,] Q;
            public float[,,,] K;
            public float[,,,] Val;
            public float[,,,] dQ;
            public float[,,,] dK;
            public float[,,,] dVal;
            public float[,,,] AttnOut;
            public float[,,,] dAttnOut;
            public float[,,,] Scores;
            public float[,,,] dScores;

            public float[,,,] M1;
            public float[,,,] dM1;
            public float[,,,] M1Mask;

            public float[,,,] Tmp;

            public int[,] XTokIds;

            public ForwardCache(int b, int t, int d, int v, int l, int dhid)
            {
                B = b; T = t; D = d; V = v; L = l; Dhid = dhid;

                Logits = new float[B, T, V];
                DLogits = new float[B, T, V];

                StartLogits = new float[B, T];
                EndLogits = new float[B, T];
                DStartLogits = new float[B, T];
                DEndLogits = new float[B, T];

                H = new float[L + 1, B, T, D];
                DH = new float[L + 1, B, T, D];

                LN1Out = new float[L, B, T, D];
                LN1Mean = new float[L, B, T];
                LN1InvStd = new float[L, B, T];
                LN1NormZ = new float[L, B, T, D];
                dLN1Out = new float[L, B, T, D];
                dA1 = new float[L, B, T, D];

                LN2Out = new float[L, B, T, D];
                LN2Mean = new float[L, B, T];
                LN2InvStd = new float[L, B, T];
                LN2NormZ = new float[L, B, T, D];
                dLN2Out = new float[L, B, T, D];
                dA2 = new float[L, B, T, D];

                Q = new float[L, B, T, D];
                K = new float[L, B, T, D];
                Val = new float[L, B, T, D];
                dQ = new float[L, B, T, D];
                dK = new float[L, B, T, D];
                dVal = new float[L, B, T, D];
                AttnOut = new float[L, B, T, D];
                dAttnOut = new float[L, B, T, D];
                Scores = new float[L, B, T, T];
                dScores = new float[L, B, T, T];

                M1 = new float[L, B, T, Dhid];
                dM1 = new float[L, B, T, Dhid];
                M1Mask = new float[L, B, T, Dhid];

                Tmp = new float[L, B, T, D];

                XTokIds = new int[B, T];
            }

            public void CaptureInputIds(int[,] X)
            {
                for (int b = 0; b < B; b++)
                    for (int t = 0; t < T; t++)
                        XTokIds[b, t] = X[b, t];
            }

            public Span<float> GetHSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref H[layer, b, t, 0], D);

            public Span<float> GetDHSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref DH[layer, b, t, 0], D);

            public Span<float> GetLN1OutSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref LN1Out[layer, b, t, 0], D);

            public Span<float> GetLN1NormZSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref LN1NormZ[layer, b, t, 0], D);

            public Span<float> GetDLN1OutSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref dLN1Out[layer, b, t, 0], D);

            public Span<float> GetDA1Span(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref dA1[layer, b, t, 0], D);

            public ref float GetLN1MeanRef(int layer, int b, int t) =>
                ref LN1Mean[layer, b, t];

            public ref float GetLN1InvStdRef(int layer, int b, int t) =>
                ref LN1InvStd[layer, b, t];

            public Span<float> GetLN2OutSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref LN2Out[layer, b, t, 0], D);

            public Span<float> GetLN2NormZSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref LN2NormZ[layer, b, t, 0], D);

            public Span<float> GetDLN2OutSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref dLN2Out[layer, b, t, 0], D);

            public Span<float> GetDA2Span(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref dA2[layer, b, t, 0], D);

            public ref float GetLN2MeanRef(int layer, int b, int t) =>
                ref LN2Mean[layer, b, t];

            public ref float GetLN2InvStdRef(int layer, int b, int t) =>
                ref LN2InvStd[layer, b, t];

            public Span<float> GetQSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref Q[layer, b, t, 0], D);

            public Span<float> GetKSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref K[layer, b, t, 0], D);

            public Span<float> GetVSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref Val[layer, b, t, 0], D);

            public Span<float> GetDQSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref dQ[layer, b, t, 0], D);

            public Span<float> GetDKSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref dK[layer, b, t, 0], D);

            public Span<float> GetDValSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref dVal[layer, b, t, 0], D);

            public Span<float> GetAttnOutSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref AttnOut[layer, b, t, 0], D);

            public Span<float> GetDAttnOutSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref dAttnOut[layer, b, t, 0], D);

            public Span<float> GetM1Span(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref M1[layer, b, t, 0], Dhid);

            public Span<float> GetDM1Span(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref dM1[layer, b, t, 0], Dhid);

            public Span<float> GetM1MaskSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref M1Mask[layer, b, t, 0], Dhid);

            public Span<float> GetTmpSpan(int layer, int b, int t) =>
                MemoryMarshal.CreateSpan(ref Tmp[layer, b, t, 0], D);
        }

        private sealed class HeadGrad
        {
            public float[][] Wout { get; }
            public float[] Bout { get; }

            public float[] Wstart { get; }
            public float[] Wend { get; }
            public float Bstart { get; set; }
            public float Bend { get; set; }

            public HeadGrad(int D, int V)
            {
                Wout = new float[D][];
                for (int i = 0; i < D; i++)
                    Wout[i] = new float[V];
                Bout = new float[V];

                Wstart = new float[D];
                Wend = new float[D];
                Bstart = 0f;
                Bend = 0f;
            }
        }

        private sealed class EmbedGrad
        {
            public float[][] Pos { get; }
            public float[][] Tok { get; }

            public EmbedGrad(int posCount, int D, int vocab)
            {
                Pos = InitMatrix(posCount, D);
                Tok = InitMatrix(vocab, D);
            }

            private static float[][] InitMatrix(int rows, int cols)
            {
                var m = new float[rows][];
                for (int i = 0; i < rows; i++)
                    m[i] = new float[cols];
                return m;
            }
        }

        private sealed class BlockGrad
        {
            public float[][] Wq { get; }
            public float[][] Wk { get; }
            public float[][] Wv { get; }
            public float[][] Wo { get; }
            public float[][] W1 { get; }
            public float[][] W2 { get; }
            public float[] B1 { get; }
            public float[] B2 { get; }

            public float[] Ln1Gamma { get; }
            public float[] Ln1Beta { get; }
            public float[] Ln2Gamma { get; }
            public float[] Ln2Beta { get; }

            public BlockGrad(int D, int Dhid)
            {
                Wq = InitMatrix(D, D);
                Wk = InitMatrix(D, D);
                Wv = InitMatrix(D, D);
                Wo = InitMatrix(D, D);
                W1 = InitMatrix(D, Dhid);
                W2 = InitMatrix(Dhid, D);
                B1 = new float[Dhid];
                B2 = new float[D];

                Ln1Gamma = new float[D];
                Ln1Beta = new float[D];
                Ln2Gamma = new float[D];
                Ln2Beta = new float[D];
            }

            private static float[][] InitMatrix(int rows, int cols)
            {
                var m = new float[rows][];
                for (int i = 0; i < rows; i++)
                    m[i] = new float[cols];
                return m;
            }

            public void AccumulateInto(
                Param1D ln1_g, Param1D ln1_b,
                Param1D ln2_g, Param1D ln2_b,
                Param2D WqGlobal, Param2D WkGlobal, Param2D WvGlobal, Param2D WoGlobal,
                Param2D W1Global, Param2D W2Global,
                Param1D b1Global, Param1D b2Global)
            {
                AddVector(ln1_g.G, Ln1Gamma);
                AddVector(ln1_b.G, Ln1Beta);
                AddVector(ln2_g.G, Ln2Gamma);
                AddVector(ln2_b.G, Ln2Beta);

                AddMatrix(WqGlobal, Wq);
                AddMatrix(WkGlobal, Wk);
                AddMatrix(WvGlobal, Wv);
                AddMatrix(WoGlobal, Wo);
                AddMatrix(W1Global, W1);
                AddMatrix(W2Global, W2);

                AddVector(b1Global.G, B1);
                AddVector(b2Global.G, B2);
            }

            private static void AddVector(Span<float> dst, float[] src)
            {
                for (int i = 0; i < src.Length; i++)
                    dst[i] += src[i];
            }

            private static void AddMatrix(Param2D dest, float[][] src)
            {
                for (int i = 0; i < src.Length; i++)
                {
                    Span<float> dstRow = dest.GradRowSpan(i);
                    float[] srcRow = src[i];
                    for (int j = 0; j < srcRow.Length; j++)
                        dstRow[j] += srcRow[j];
                }
            }
        }

        private interface IParam
        {
            void AdamStep(float lr, float wd, float beta1, float beta2, float eps, int t);
            void ZeroGrad();
        }

        private sealed class Param1D : IParam
        {
            public float[] W;
            public float[] G;
            private readonly float[] m;
            private readonly float[] v;

            public Param1D(int n, float init = 0f)
            {
                W = new float[n];
                G = new float[n];
                m = new float[n];
                v = new float[n];
                for (int i = 0; i < n; i++) W[i] = init;
            }

            public void ZeroGrad() => Array.Clear(G, 0, G.Length);

            public void AdamStep(float lr, float wd, float beta1, float beta2, float eps, int t)
            {
                float bc1 = 1f - MathF.Pow(beta1, t);
                float bc2 = 1f - MathF.Pow(beta2, t);

                for (int i = 0; i < W.Length; i++)
                {
                    float grad = G[i];
                    if (wd > 0f) grad += wd * W[i];

                    m[i] = beta1 * m[i] + (1f - beta1) * grad;
                    v[i] = beta2 * v[i] + (1f - beta2) * grad * grad;

                    float mHat = m[i] / bc1;
                    float vHat = v[i] / bc2;

                    W[i] -= lr * (mHat / (MathF.Sqrt(vHat) + eps));
                }

                ZeroGrad();
            }
        }

        public sealed class Param2D : IParam
        {
            public float[][] W { get; }
            public float[][] G { get; }
            private readonly float[][] m;
            private readonly float[][] v;

            public Param2D(int rows, int cols, Random rng, float scale)
            {
                W = new float[rows][];
                G = new float[rows][];
                m = new float[rows][];
                v = new float[rows][];

                for (int i = 0; i < rows; i++)
                {
                    W[i] = new float[cols];
                    G[i] = new float[cols];
                    m[i] = new float[cols];
                    v[i] = new float[cols];

                    if (scale > 0f)
                        for (int j = 0; j < cols; j++)
                            W[i][j] = (float)((rng.NextDouble() * 2 - 1) * scale);
                }
            }

            public ReadOnlySpan<float> RowSpan(int row) => W[row];
            public Span<float> GradRowSpan(int row) => G[row];

            public void ZeroGrad()
            {
                for (int i = 0; i < G.Length; i++)
                    Array.Clear(G[i], 0, G[i].Length);
            }

            public void AdamStep(float lr, float wd, float beta1, float beta2, float eps, int t)
            {
                float bc1 = 1f - MathF.Pow(beta1, t);
                float bc2 = 1f - MathF.Pow(beta2, t);

                for (int i = 0; i < W.Length; i++)
                {
                    for (int j = 0; j < W[i].Length; j++)
                    {
                        float grad = G[i][j];
                        if (wd > 0f) grad += wd * W[i][j];

                        m[i][j] = beta1 * m[i][j] + (1f - beta1) * grad;
                        v[i][j] = beta2 * v[i][j] + (1f - beta2) * grad * grad;

                        float mHat = m[i][j] / bc1;
                        float vHat = v[i][j] / bc2;

                        W[i][j] -= lr * (mHat / (MathF.Sqrt(vHat) + eps));
                    }
                }

                ZeroGrad();
            }
        }

        private sealed class Adam
        {
            private const float Beta1 = 0.9f;
            private const float Beta2 = 0.999f;
            private const float Eps = 1e-8f;

            private int t;
            private readonly List<IParam> parameters = new();

            public void Add(IParam param) => parameters.Add(param);

            public void Step(float lr, float wd)
            {
                t++;
                foreach (var param in parameters)
                    param.AdamStep(lr, wd, Beta1, Beta2, Eps, t);
            }
        }
    }
}