using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tesseract;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace MinimalBrowser.Services
{
    public sealed class OcrService
    {
        public async Task<string> OcrBitmapToMarkdownAsync(Bitmap bmp)
        {
            return await Task.Run(() =>
            {
                using var preprocessed = PreprocessForOCR(bmp);
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);

                using var engine = new TesseractEngine(@"./tessdata-main", "eng+fin", EngineMode.LstmOnly);
                using var img = Pix.LoadFromMemory(ms.ToArray());
                using var page = engine.Process(img, PageSegMode.SingleBlock);

                var pageConf = page.GetMeanConfidence();
                var lines = ExtractLines(page);
                if (lines.Count == 0) return string.Empty;

                var elements = MergeLinesIntoElements(lines);

                var sb = new StringBuilder();
                sb.AppendLine($"<!-- ocr-screen w={bmp.Width} h={bmp.Height} c={pageConf:0.##} -->");

                foreach (var el in elements)
                {
                    string meta = $"<!-- x={el.X} y={el.Y} w={el.W} h={el.H} c={el.Confidence:0.##} -->";
                    string text = EscapeMarkdown(el.Text);
                    if (el.Type == "heading")
                        sb.AppendLine($"# {text} {meta}");
                    else
                        sb.AppendLine($"{text} {meta}");
                    sb.AppendLine();
                }
                return sb.ToString();
            });
        }

        private static List<LineInfo> ExtractLines(Page page)
        {
            var lines = new List<LineInfo>();
            using var iter = page.GetIterator();
            iter.Begin();
            int blockIdx = -1, paraIdx = -1, lineIdx = -1;
            do
            {
                blockIdx++;
                paraIdx = -1;
                do
                {
                    paraIdx++;
                    lineIdx = -1;
                    do
                    {
                        lineIdx++;
                        var text = (iter.GetText(PageIteratorLevel.TextLine) ?? "").Trim();
                        if (string.IsNullOrEmpty(text)) continue;
                        if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out var rect))
                        {
                            var conf = iter.GetConfidence(PageIteratorLevel.TextLine);
                            lines.Add(new LineInfo
                            {
                                BlockIndex = blockIdx,
                                ParaIndex = paraIdx,
                                LineIndex = lineIdx,
                                Text = text,
                                X = rect.X1,
                                Y = rect.Y1,
                                W = rect.Width,
                                H = rect.Height,
                                Confidence = conf
                            });
                        }
                    }
                    while (iter.Next(PageIteratorLevel.TextLine));
                }
                while (iter.Next(PageIteratorLevel.Para));
            }
            while (iter.Next(PageIteratorLevel.Block));

            return lines;
        }

        private sealed class LineInfo
        {
            public int BlockIndex { get; set; }
            public int ParaIndex { get; set; }
            public int LineIndex { get; set; }
            public string Text { get; set; } = "";
            public int X { get; set; }
            public int Y { get; set; }
            public int W { get; set; }
            public int H { get; set; }
            public float Confidence { get; set; }
        }

        private sealed class MergedElement
        {
            public string Type { get; set; } = "paragraph";
            public string Text { get; set; } = "";
            public int X { get; set; }
            public int Y { get; set; }
            public int W { get; set; }
            public int H { get; set; }
            public float Confidence { get; set; }
            public List<LineInfo> Lines { get; } = new();
        }

        private static Bitmap PreprocessForOCR(Bitmap src, byte threshold = 160)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));

            var gray = new Bitmap(src.Width, src.Height, PixelFormat.Format8bppIndexed);
            var pal = gray.Palette;
            for (int i = 0; i < 256; i++)
                pal.Entries[i] = Color.FromArgb(i, i, i);
            gray.Palette = pal;

            var srcData = src.LockBits(new Rectangle(0, 0, src.Width, src.Height), ImageLockMode.ReadOnly, src.PixelFormat);
            var grayData = gray.LockBits(new Rectangle(0, 0, gray.Width, gray.Height), ImageLockMode.WriteOnly, gray.PixelFormat);

            unsafe
            {
                byte* srcPtr = (byte*)srcData.Scan0.ToPointer();
                byte* grayPtr = (byte*)grayData.Scan0.ToPointer();
                int srcStride = srcData.Stride;
                int grayStride = grayData.Stride;

                for (int y = 0; y < src.Height; y++)
                {
                    byte* s = srcPtr + y * srcStride;
                    byte* g = grayPtr + y * grayStride;

                    for (int x = 0; x < src.Width; x++)
                    {
                        byte r = s[x * 3 + 2];
                        byte gCol = s[x * 3 + 1];
                        byte b = s[x * 3 + 0];
                        byte lum = (byte)((0.299 * r + 0.587 * gCol + 0.114 * b) + 0.5);
                        g[x] = (lum > threshold) ? (byte)255 : (byte)0;
                    }
                }
            }

            src.UnlockBits(srcData);
            gray.UnlockBits(grayData);

            return gray;
        }

        private static List<MergedElement> MergeLinesIntoElements(List<LineInfo> lines)
        {
            var sorted = lines
                .OrderBy(l => l.Y)
                .ThenBy(l => l.X)
                .ToList();

            double avgHeight = sorted.Average(l => l.H);
            double headingThreshold = avgHeight * 1.35;

            var result = new List<MergedElement>();

            bool OverlapsHorizontally(LineInfo a, LineInfo b, double minOverlapRatio)
            {
                int left = Math.Max(a.X, b.X);
                int right = Math.Min(a.X + a.W, b.X + b.W);
                int overlap = Math.Max(0, right - left);
                int minWidth = Math.Min(a.W, b.W);
                return minWidth > 0 && ((double)overlap / minWidth) >= minOverlapRatio;
            }

            bool IsCloseVertically(LineInfo a, LineInfo b, double maxGap)
            {
                int gap = b.Y - (a.Y + a.H);
                return gap >= 0 && gap <= maxGap;
            }

            foreach (var line in sorted)
            {
                bool isHeading = line.H >= headingThreshold;

                if (result.Count == 0)
                {
                    result.Add(NewElementFromLine(line, isHeading ? "heading" : "paragraph"));
                    continue;
                }

                var last = result[^1];

                if (last.Type == "heading" && isHeading &&
                    IsCloseVertically(last.Lines[^1], line, Math.Max(10, avgHeight * 0.6)) &&
                    OverlapsHorizontally(last.Lines[^1], line, 0.4))
                {
                    AppendLine(last, line, " ");
                    continue;
                }

                if (last.Type == "paragraph" && !isHeading &&
                    IsCloseVertically(last.Lines[^1], line, Math.Max(10, avgHeight * 0.8)) &&
                    OverlapsHorizontally(last.Lines[^1], line, 0.3))
                {
                    AppendLine(last, line, " ");
                    continue;
                }

                result.Add(NewElementFromLine(line, isHeading ? "heading" : "paragraph"));
            }

            return result;

            static MergedElement NewElementFromLine(LineInfo l, string type)
            {
                var el = new MergedElement
                {
                    Type = type,
                    Text = l.Text,
                    X = l.X,
                    Y = l.Y,
                    W = l.W,
                    H = l.H,
                    Confidence = l.Confidence
                };
                el.Lines.Add(l);
                return el;
            }

            static void AppendLine(MergedElement el, LineInfo l, string joinWith)
            {
                el.Text = string.IsNullOrWhiteSpace(el.Text) ? l.Text : $"{el.Text}{joinWith}{l.Text}";
                int x1 = Math.Min(el.X, l.X);
                int y1 = Math.Min(el.Y, l.Y);
                int x2 = Math.Max(el.X + el.W, l.X + l.W);
                int y2 = Math.Max(el.Y + el.H, l.Y + l.H);
                el.X = x1; el.Y = y1; el.W = x2 - x1; el.H = y2 - y1;
                el.Lines.Add(l);
                el.Confidence = (float)(el.Lines.Average(li => li.Confidence));
            }
        }

        private static string EscapeMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text
                .Replace("\\", "\\\\")
                .Replace("`", "\\`")
                .Replace("*", "\\*")
                .Replace("_", "\\_")
                .Replace("{", "\\}")
                .Replace("}", "\\}")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace("(", "\\(")
                .Replace(")", "\\)")
                .Replace("#", "\\#")
                .Replace("+", "\\+")
                .Replace("-", "\\-")
                .Replace(".", "\\.")
                .Replace("!", "\\!");
        }
    }
}