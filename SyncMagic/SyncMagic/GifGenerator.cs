using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AnimatedGif;

namespace SyncMagic
{
    public class GifGenerator
    {
        private ImageUploader imageUploader;
        private CancellationTokenSource cancellationTokenSource;
        private Func<Bitmap> getFrame;
        private Action<Image> updatePictureBox;
        private MemoryStream currentMemoryStream;
        private System.Windows.Forms.TextBox ipAddressTextBox;

        // EMA smoothing constants
        private const double EmaAlpha = 0.30;
        private const int TargetPauseMs = 3000;
        private const double AdjustMin = 1.5;
        private const double AdjustMax = 2.25;

        // Frame capture settings
        private const int MinFrames = 10;
        private const int MaxFrames = 120;
        private const int BaseCaptureIntervalMs = 250;
        private const int DeviceLoopMs = 30000;
        private const int SafetyMs = 200;

        public GifGenerator(ImageUploader uploader, Func<Bitmap> frameProvider, Action<Image> pictureBoxUpdater, System.Windows.Forms.TextBox ipTextBox)
        {
            imageUploader = uploader;
            getFrame = frameProvider;
            updatePictureBox = pictureBoxUpdater;
            ipAddressTextBox = ipTextBox;
        }

        public async Task StartRecordingAsync(CancellationToken externalCancellationToken)
        {
            StopRecording();

            cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            double simulationTimelineMs = 0;
            Bitmap previousLastFrame = null;

            try
            {
                double emaEncodeMs = 150;
                double emaUploadMs = 1200;
                double frameScale = 1.0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    List<Bitmap> gifFrames = new();

                    // Calculate capture budget
                    int captureBudgetMs =
                        DeviceLoopMs
                        - (int)Math.Ceiling(emaUploadMs)
                        - (int)Math.Ceiling(emaEncodeMs)
                        - SafetyMs;

                    captureBudgetMs = Math.Max(1000, captureBudgetMs);

                    int baseFrames = Math.Max(1, captureBudgetMs / BaseCaptureIntervalMs);
                    int frameCount = (int)Math.Round(baseFrames * frameScale);
                    frameCount = Math.Clamp(frameCount, MinFrames, MaxFrames);

                    int captureIntervalMs = Math.Max(1, captureBudgetMs / frameCount);

                    // Add overlap frame
                    if (previousLastFrame != null)
                    {
                        gifFrames.Add((Bitmap)previousLastFrame.Clone());
                    }

                    // Capture frames
                    for (int i = 0; i < frameCount; i++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        Bitmap frame = getFrame.Invoke();
                        Bitmap resized = ResizeToFixedSize(frame, 240, 240);
                        frame.Dispose();

                        ApplyRotationInPlace(resized);

                        if (RenderOptions.MirrorFrame)
                            resized.RotateFlip(RotateFlipType.RotateNoneFlipX);

                        gifFrames.Add(resized);

                        // Update UI
                        updatePictureBox?.Invoke((Bitmap)resized.Clone());

                        simulationTimelineMs += captureIntervalMs;
                        await Task.Delay(captureIntervalMs, cancellationToken);
                    }

                    if (gifFrames.Count == 0)
                        continue;

                    // Store last frame for overlap
                    previousLastFrame?.Dispose();
                    previousLastFrame = (Bitmap)gifFrames[^1].Clone();

                    // Build frame delays
                    int estPauseMs = (int)Math.Ceiling(emaUploadMs + emaEncodeMs);
                    estPauseMs = Math.Clamp(estPauseMs, 1000, 6000);

                    int holdStartMs = estPauseMs / 2;
                    int holdEndMs = estPauseMs - holdStartMs;

                    var perFrameDelays = BuildStableEasedDelays(
                        DeviceLoopMs,
                        gifFrames.Count,
                        holdStartMs,
                        holdEndMs
                    );

                    // Encode GIF
                    var encodeSw = Stopwatch.StartNew();
                    using (var gif = AnimatedGif.AnimatedGif.Create("screen.gif", 10))
                    {
                        for (int i = 0; i < gifFrames.Count; i++)
                        {
                            gif.AddFrame(gifFrames[i], perFrameDelays[i], GifQuality.Bit8);
                        }
                    }
                    encodeSw.Stop();

                    // Upload GIF
                    var uploadSw = Stopwatch.StartNew();
                    await imageUploader.UploadGifAsync(ipAddressTextBox, "screen.gif");
                    uploadSw.Stop();

                    var encodeDuration = encodeSw.Elapsed;
                    var uploadDuration = uploadSw.Elapsed;
                    var totalPause = encodeDuration + uploadDuration;

                    Debug.WriteLine(
                        $"encode={encodeDuration.TotalMilliseconds:F0}ms " +
                        $"upload={uploadDuration.TotalMilliseconds:F0}ms " +
                        $"frames={gifFrames.Count} " +
                        $"scale={frameScale:F2}"
                    );

                    // Update picture box with new GIF
                    UpdatePictureBoxWithGif();

                    // Cleanup frames
                    foreach (var f in gifFrames)
                        f.Dispose();

                    gifFrames.Clear();

                    // Update EMA values
                    emaUploadMs =
                        EmaAlpha * uploadDuration.TotalMilliseconds +
                        (1 - EmaAlpha) * emaUploadMs;

                    emaEncodeMs =
                        EmaAlpha * encodeDuration.TotalMilliseconds +
                        (1 - EmaAlpha) * emaEncodeMs;

                    // Adapt frame scale
                    double s = TargetPauseMs / Math.Max(1, totalPause.TotalMilliseconds);
                    s = Math.Clamp(s, AdjustMin, AdjustMax);

                    frameScale = Math.Clamp(frameScale * s, 0.1, 3.0);
                }
            }
            catch (OperationCanceledException)
            {
                // Recording was cancelled
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GifGenerator error: {ex.Message}");
            }
        }

        public void StopRecording()
        {
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
        }

        public void Dispose()
        {
            currentMemoryStream?.Dispose();
            StopRecording();
        }

        public Bitmap ResizeToFixedSize(Bitmap source, int width, int height)
        {
            var resizedBitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(resizedBitmap))
            {
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, 0, 0, width, height);
            }
            return resizedBitmap;
        }

        public void UpdatePictureBoxWithGif()
        {
            try
            {
                // Dispose previous MemoryStream
                currentMemoryStream?.Dispose();

                // Read the GIF file into a MemoryStream  
                byte[] imageData = File.ReadAllBytes("screen.gif");
                currentMemoryStream = new MemoryStream(imageData);
                var image = Image.FromStream(currentMemoryStream);
                
                updatePictureBox?.Invoke(image);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating picture box with GIF: {ex.Message}");
            }
        }

        private void ApplyRotationInPlace(Bitmap bmp)
        {
            int deg = RenderOptions.RotationDegrees % 360;
            if (deg < 0) deg += 360;
            switch (deg)
            {
                case 90:
                    bmp.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    break;
                case 180:
                    bmp.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    break;
                case 270:
                    bmp.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    break;
            }
        }

        private List<int> BuildStableEasedDelays(
            int totalMs,
            int frameCount,
            int holdStartMs,
            int holdEndMs)
        {
            var delays = new List<int>(frameCount);

            if (frameCount == 1)
            {
                delays.Add(totalMs);
                return delays;
            }

            int motionFrames = Math.Max(1, frameCount - 2);
            int motionBudget = totalMs - holdStartMs - holdEndMs;
            motionBudget = Math.Max(motionFrames * 10, motionBudget);

            // Cosine ease in/out
            double[] weights = new double[motionFrames];
            double weightSum = 0;

            for (int i = 0; i < motionFrames; i++)
            {
                double t = (double)i / (motionFrames - 1);
                double w = 0.5 - 0.5 * Math.Cos(Math.PI * t);
                weights[i] = w;
                weightSum += w;
            }

            delays.Add(holdStartMs);

            for (int i = 0; i < motionFrames; i++)
            {
                int d = (int)Math.Round((weights[i] / weightSum) * motionBudget);
                d = Math.Max(10, d);
                delays.Add(d);
            }

            delays.Add(holdEndMs);

            // Trim if off by rounding
            int sum = delays.Sum();
            int diff = totalMs - sum;
            delays[^1] += diff;

            return delays;
        }
    }
}
