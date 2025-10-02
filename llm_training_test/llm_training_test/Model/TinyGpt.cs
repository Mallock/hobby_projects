using System;
using System.Runtime.InteropServices;

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
        private readonly Adam adam;

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
                blocks[i] = new Block(D, Dhid, rng);

            Wout = new Param2D(D, V, rng, 0.02f);
            Bout = new Param1D(V);

            adam = new Adam();
            adam.Add(tokEmb);
            adam.Add(posEmb);
            adam.Add(Wout);
            adam.Add(Bout);
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

            for (int b = 0; b < B; b++)
            {
                for (int t = 0; t < T; t++)
                {
                    Span<float> h0 = cache.GetHSpan(0, b, t);
                    ReadOnlySpan<float> tok = tokEmb.RowSpan(X[b, t]);
                    ReadOnlySpan<float> pos = posEmb.RowSpan(t);

                    for (int d = 0; d < D; d++)
                        h0[d] = tok[d] + pos[d];
                }
            }

            for (int l = 0; l < L; l++)
                blocks[l].Forward(cache, l);

            for (int b = 0; b < B; b++)
            {
                for (int t = 0; t < T; t++)
                {
                    ReadOnlySpan<float> h = cache.GetHSpan(L, b, t);

                    for (int v = 0; v < V; v++)
                    {
                        float sum = Bout.W[v];
                        ReadOnlySpan<float> wRow = Wout.RowSpan(d: v, transposed: true); // treat columns as outputs
                        for (int d = 0; d < D; d++)
                            sum += h[d] * wRow[d];
                        cache.Logits[b, t, v] = sum;
                    }
                }
            }

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
            foreach (var block in blocks) block.ZeroGrad();

            for (int b = 0; b < B; b++)
            {
                for (int t = 0; t < T; t++)
                {
                    ReadOnlySpan<float> h = cache.GetHSpan(L, b, t);
                    Span<float> dh = cache.GetDHSpan(L, b, t);

                    for (int v = 0; v < V; v++)
                    {
                        float grad = cache.DLogits[b, t, v];
                        Bout.G[v] += grad;

                        ReadOnlySpan<float> wCol = Wout.RowSpan(d: v, transposed: true);
                        Span<float> gCol = Wout.GradRowSpan(d: v, transposed: true);

                        for (int d = 0; d < D; d++)
                        {
                            gCol[d] += h[d] * grad;
                            dh[d] += wCol[d] * grad;
                        }
                    }
                }
            }

            for (int l = L - 1; l >= 0; l--)
                blocks[l].Backward(cache, l);

            for (int b = 0; b < B; b++)
            {
                for (int t = 0; t < T; t++)
                {
                    Span<float> dh = cache.GetDHSpan(0, b, t);
                    Span<float> posGrad = posEmb.GradRowSpan(t);
                    Span<float> tokGrad = tokEmb.GradRowSpan(cache.XTokIds[b, t]);

                    for (int d = 0; d < D; d++)
                    {
                        posGrad[d] += dh[d];
                        tokGrad[d] += dh[d];
                    }
                }
            }
        }

        public void AdamStep(float lr, float wd) => adam.Step(lr, wd);

        private sealed class Block
        {
            private readonly int D;
            private readonly int Dhid;

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

            public Block(int dModel, int dHidden, Random rng)
            {
                D = dModel;
                Dhid = dHidden;

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

                for (int b = 0; b < B; b++)
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
                }

                for (int b = 0; b < B; b++)
                {
                    for (int t = 0; t < T; t++)
                    {
                        for (int s = 0; s < T; s++)
                        {
                            float score = 0f;
                            Span<float> q = cache.GetQSpan(l, b, t);
                            Span<float> k = cache.GetKSpan(l, b, s);

                            for (int d = 0; d < D; d++)
                                score += q[d] * k[d];

                            cache.Scores[l, b, t, s] = s > t ? float.NegativeInfinity : score * scale;
                        }

                        float max = float.NegativeInfinity;
                        for (int s = 0; s < T; s++)
                        {
                            float val = cache.Scores[l, b, t, s];
                            if (val > max) max = val;
                        }

                        float sum = 0f;
                        for (int s = 0; s < T; s++)
                        {
                            float val = cache.Scores[l, b, t, s];
                            float exp = float.IsNegativeInfinity(val) ? 0f : MathF.Exp(val - max);
                            cache.Scores[l, b, t, s] = exp;
                            sum += exp;
                        }

                        if (sum == 0f) sum = 1f;

                        for (int s = 0; s < T; s++)
                            cache.Scores[l, b, t, s] /= sum;
                    }
                }

                for (int b = 0; b < B; b++)
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> attnOut = cache.GetAttnOutSpan(l, b, t);

                        for (int d = 0; d < D; d++)
                        {
                            float sum = 0f;
                            for (int s = 0; s < T; s++)
                                sum += cache.Scores[l, b, t, s] * cache.Val[l, b, s, d];
                            attnOut[d] = sum;
                        }
                    }
                }

                for (int b = 0; b < B; b++)
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> attnOut = cache.GetAttnOutSpan(l, b, t);
                        Span<float> h = cache.GetHSpan(l, b, t);
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
                            hNext[d] = h[d] + tmp[d];
                    }
                }

                for (int b = 0; b < B; b++)
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
                }
            }

            public void Backward(ForwardCache cache, int l)
            {
                int B = cache.B;
                int T = cache.T;
                float scale = 1f / MathF.Sqrt(D);

                for (int b = 0; b < B; b++)
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> dhNext = cache.GetDHSpan(l + 1, b, t);
                        Span<float> dh = cache.GetDHSpan(l, b, t);

                        for (int d = 0; d < D; d++)
                            dh[d] += dhNext[d];

                        Span<float> ln2Out = cache.GetLN2OutSpan(l, b, t);
                        Span<float> dLn2Out = cache.GetDLN2OutSpan(l, b, t);
                        Span<float> m1 = cache.GetM1Span(l, b, t);
                        Span<float> mask = cache.GetM1MaskSpan(l, b, t);
                        Span<float> tmp = cache.GetTmpSpan(l, b, t);

                        for (int dOut = 0; dOut < D; dOut++)
                        {
                            float grad = dhNext[dOut];
                            tmp[dOut] = grad;
                            b2.G[dOut] += grad;
                        }

                        Span<float> dHidden = cache.GetDM1Span(l, b, t);
                        dHidden.Clear();

                        for (int dOut = 0; dOut < D; dOut++)
                        {
                            float grad = tmp[dOut];
                            for (int hIdx = 0; hIdx < Dhid; hIdx++)
                            {
                                float act = m1[hIdx] > 0f ? m1[hIdx] : 0f;
                                W2.G[hIdx][dOut] += act * grad;
                                dHidden[hIdx] += W2.W[hIdx][dOut] * grad;
                            }
                        }

                        for (int hIdx = 0; hIdx < Dhid; hIdx++)
                            if (mask[hIdx] == 0f) dHidden[hIdx] = 0f;

                        for (int hIdx = 0; hIdx < Dhid; hIdx++)
                        {
                            float grad = dHidden[hIdx];
                            b1.G[hIdx] += grad;
                            for (int dIn = 0; dIn < D; dIn++)
                            {
                                W1.G[dIn][hIdx] += ln2Out[dIn] * grad;
                                dLn2Out[dIn] += W1.W[dIn][hIdx] * grad;
                            }
                        }
                    }
                }

                for (int b = 0; b < B; b++)
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> h = cache.GetHSpan(l, b, t);
                        Span<float> dLn2Out = cache.GetDLN2OutSpan(l, b, t);
                        Span<float> norm = cache.GetLN2NormZSpan(l, b, t);

                        ref float invStd = ref cache.GetLN2InvStdRef(l, b, t);
                        LayerNormBackwardVec(h, dLn2Out, norm, invStd, ln2_g, ln2_b, cache.GetDA2Span(l, b, t));
                    }
                }

                for (int b = 0; b < B; b++)
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> dh = cache.GetDA2Span(l, b, t);
                        Span<float> attnOut = cache.GetAttnOutSpan(l, b, t);
                        Span<float> dAttn = cache.GetDAttnOutSpan(l, b, t);
                        dAttn.Clear();

                        for (int dOut = 0; dOut < D; dOut++)
                        {
                            float grad = cache.GetDHSpan(l, b, t)[dOut];
                            for (int dIn = 0; dIn < D; dIn++)
                            {
                                W2D(Wo, dIn, dOut, attnOut[dIn], grad);
                                dAttn[dIn] += Wo.W[dIn][dOut] * grad;
                            }
                        }

                        void W2D(Param2D wo, int i, int j, float a, float g) => wo.G[i][j] += a * g;
                    }
                }

                for (int b = 0; b < B; b++)
                {
                    for (int t = 0; t < T; t++)
                    {
                        for (int s = 0; s < T; s++)
                        {
                            float ds = 0f;
                            Span<float> dAttn = cache.GetDAttnOutSpan(l, b, t);
                            Span<float> val = cache.GetVSpan(l, b, s);

                            for (int d = 0; d < D; d++)
                            {
                                ds += dAttn[d] * val[d];
                                cache.dVal[l, b, s, d] += cache.Scores[l, b, t, s] * dAttn[d];
                            }

                            cache.dScores[l, b, t, s] += ds;
                        }
                    }
                }

                for (int b = 0; b < B; b++)
                {
                    for (int t = 0; t < T; t++)
                    {
                        float dot = 0f;
                        for (int s = 0; s < T; s++)
                            dot += cache.dScores[l, b, t, s] * cache.Scores[l, b, t, s];

                        for (int s = 0; s < T; s++)
                        {
                            float p = cache.Scores[l, b, t, s];
                            cache.dScores[l, b, t, s] = p * (cache.dScores[l, b, t, s] - dot) * scale;
                        }
                    }
                }

                for (int b = 0; b < B; b++)
                {
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
                }

                for (int b = 0; b < B; b++)
                {
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
                                Wq.G[dIn][dOut] += x * gradQ;
                                Wk.G[dIn][dOut] += x * gradK;
                                Wv.G[dIn][dOut] += x * gradV;

                                dLn1Out[dIn] += Wq.W[dIn][dOut] * gradQ;
                                dLn1Out[dIn] += Wk.W[dIn][dOut] * gradK;
                                dLn1Out[dIn] += Wv.W[dIn][dOut] * gradV;
                            }
                        }
                    }
                }

                for (int b = 0; b < B; b++)
                {
                    for (int t = 0; t < T; t++)
                    {
                        Span<float> h = cache.GetHSpan(l, b, t);
                        Span<float> dLn1Out = cache.GetDLN1OutSpan(l, b, t);
                        Span<float> norm = cache.GetLN1NormZSpan(l, b, t);

                        ref float invStd = ref cache.GetLN1InvStdRef(l, b, t);
                        LayerNormBackwardVec(h, dLn1Out, norm, invStd, ln1_g, ln1_b, cache.GetDA1Span(l, b, t));

                        Span<float> dh = cache.GetDHSpan(l, b, t);
                        Span<float> da1 = cache.GetDA1Span(l, b, t);
                        Span<float> da2 = cache.GetDA2Span(l, b, t);

                        for (int d = 0; d < D; d++)
                            dh[d] += da1[d] + da2[d];
                    }
                }
            }

            private static void LayerNormForwardVec(
                Span<float> input,
                Span<float> output,
                Span<float> normZ,
                ref float mean,
                ref float invStd,
                ReadOnlySpan<float> gamma,
                ReadOnlySpan<float> beta)
            {
                float m = 0f;
                for (int i = 0; i < input.Length; i++)
                    m += input[i];
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

            private static void LayerNormBackwardVec(
                Span<float> input,
                Span<float> dOutput,
                Span<float> normZ,
                float invStd,
                Param1D gamma,
                Param1D beta,
                Span<float> dInput)
            {
                int D = input.Length;
                Span<float> gammaSpan = gamma.W;
                Span<float> gammaGrad = gamma.G;
                Span<float> betaGrad = beta.G;

                float sumDyG = 0f;
                float sumDyGz = 0f;

                for (int i = 0; i < D; i++)
                {
                    float dy = dOutput[i];
                    float g = gammaSpan[i];

                    sumDyG += dy * g;
                    sumDyGz += dy * g * normZ[i];

                    gammaGrad[i] += dy * normZ[i];
                    betaGrad[i] += dy;
                }

                float scale = invStd / D;

                for (int i = 0; i < D; i++)
                {
                    float dy = dOutput[i];
                    float g = gammaSpan[i];
                    float z = normZ[i];

                    float dyg = dy * g;
                    float dx = D * dyg - sumDyG - z * sumDyGz;
                    dInput[i] += scale * dx;
                }
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
                B = b;
                T = t;
                D = d;
                V = v;
                L = l;
                Dhid = dhid;

                Logits = new float[B, T, V];
                DLogits = new float[B, T, V];

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

        private sealed class Param2D : IParam
        {
            public float[][] W;
            public float[][] G;
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

            public ReadOnlySpan<float> RowSpan(int index) => W[index];
            public Span<float> GradRowSpan(int index) => G[index];

            public ReadOnlySpan<float> RowSpan(int d, bool transposed) =>
                transposed ? GetColumn(d) : W[d];

            public Span<float> GradRowSpan(int d, bool transposed) =>
                transposed ? GetGradColumn(d) : G[d];

            private ReadOnlySpan<float> GetColumn(int col)
            {
                var column = new float[W.Length];
                for (int i = 0; i < W.Length; i++)
                    column[i] = W[i][col];
                return column;
            }

            private Span<float> GetGradColumn(int col)
            {
                var column = new float[G.Length];
                for (int i = 0; i < G.Length; i++)
                    column[i] = G[i][col];
                return column;
            }

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
            private readonly System.Collections.Generic.List<IParam> parameters = new();

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