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
        private const int MinFrames = 8;
        private const int MaxFrames = 60;  // Reduced for 1MB target
        private const int BaseCaptureIntervalMs = 250;
        private const int DeviceLoopMs = 30000;
        private const int SafetyMs = 200;

        // GIF optimization for 1MB target
        private const int TargetGifSizeBytes = 1024 * 1024;  // 1MB
        private const int MinGifSizeBytes = 800 * 1024;      // 800KB minimum for quality
        private const int PauseStartMs = 2000;               // 2 second pause at start
        private const int PauseEndMs = 2000;                 // 2 second pause at end

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

                    // Build frame delays with pause effect
                    int estPauseMs = (int)Math.Ceiling(emaUploadMs + emaEncodeMs);
                    estPauseMs = Math.Clamp(estPauseMs, 1000, 6000);

                    var perFrameDelays = BuildPauseEasedDelays(
                        DeviceLoopMs,
                        gifFrames.Count,
                        PauseStartMs,
                        PauseEndMs
                    );

                    // Encode GIF with quality optimization for 1MB target
                    var encodeSw = Stopwatch.StartNew();
                    double sizeRatio = EncodeOptimizedGif(gifFrames, perFrameDelays, "screen.gif");
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

                    // Adapt frame scale based on timing and file size
                    double timingScale = TargetPauseMs / Math.Max(1, totalPause.TotalMilliseconds);
                    timingScale = Math.Clamp(timingScale, AdjustMin, AdjustMax);

                    // Adjust based on file size headroom
                    // If size ratio < 0.78 (383KB vs 1MB = 0.38), we have room to add more frames
                    double sizeScale = 1.0;
                    if (sizeRatio < 0.78)  // Below 78% of target (less than 800KB sweet spot)
                    {
                        // Calculate how much headroom we have and add frames proportionally
                        double headroomFactor = (0.78 - sizeRatio) / 0.78;  // How much room available
                        sizeScale = 1.0 + (headroomFactor * 0.5);  // Add up to 50% more frames based on headroom
                    }
                    else if (sizeRatio > 1.0)  // Over 1MB limit
                    {
                        // Reduce frames to get back under limit
                        sizeScale = 0.95;  // Conservative reduction
                    }

                    frameScale = Math.Clamp(frameScale * timingScale * sizeScale, 0.1, 3.0);
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

        /// <summary>
        /// Builds frame delays with 2-second pause at start and end for slow-still effect during upload.
        /// Middle frames animate smoothly while pause frames move very slowly.
        /// </summary>
        private List<int> BuildPauseEasedDelays(
            int totalMs,
            int frameCount,
            int pauseStartMs,
            int pauseEndMs)
        {
            var delays = new List<int>(frameCount);

            if (frameCount <= 2)
            {
                delays.Add(totalMs / 2);
                if (frameCount == 2)
                    delays.Add(totalMs / 2);
                return delays;
            }

            // Allocate budget: pauseStart + middle smooth frames + pauseEnd
            int middleFrames = frameCount - 2;
            int middleBudget = totalMs - pauseStartMs - pauseEndMs;
            middleBudget = Math.Max(middleFrames * 10, middleBudget);

            // Pause start: slow movement (hold longer)
            delays.Add(pauseStartMs);

            // Middle frames: smooth cosine easing
            double[] weights = new double[middleFrames];
            double weightSum = 0;

            for (int i = 0; i < middleFrames; i++)
            {
                double t = (double)i / Math.Max(1, middleFrames - 1);
                double w = 0.5 - 0.5 * Math.Cos(Math.PI * t);
                weights[i] = w;
                weightSum += w;
            }

            for (int i = 0; i < middleFrames; i++)
            {
                int d = (int)Math.Round((weights[i] / weightSum) * middleBudget);
                d = Math.Max(10, d);
                delays.Add(d);
            }

            // Pause end: slow movement (hold longer)
            delays.Add(pauseEndMs);

            // Adjust for rounding
            int sum = delays.Sum();
            int diff = totalMs - sum;
            if (delays.Count > 0)
                delays[^1] += diff;

            return delays;
        }

        /// <summary>
        /// Encodes a GIF with optimization for 1MB target size.
        /// Returns size ratio to allow dynamic frame adjustment based on headroom.
        /// </summary>
        private double EncodeOptimizedGif(List<Bitmap> frames, List<int> frameDelays, string outputPath)
        {
            // Use Bit8 quality for optimal compression while maintaining smoothness
            var gifQuality = GifQuality.Bit8;

            try
            {
                using (var gif = AnimatedGif.AnimatedGif.Create(outputPath, 10))
                {
                    for (int i = 0; i < frames.Count; i++)
                    {
                        gif.AddFrame(frames[i], frameDelays[i], gifQuality);
                    }
                }

                // Check file size and calculate ratio
                long fileSize = new FileInfo(outputPath).Length;
                double sizeRatio = (double)fileSize / TargetGifSizeBytes;
                
                // Determine adjustment based on headroom
                string sizeStatus;
                if (fileSize > TargetGifSizeBytes)
                {
                    sizeStatus = $"TOO_LARGE ({fileSize / 1024}KB exceeds 1MB)";
                }
                else if (fileSize < MinGifSizeBytes)
                {
                    sizeStatus = $"HEADROOM ({fileSize / 1024}KB < 800KB min - can add frames)";
                }
                else
                {
                    sizeStatus = $"OPTIMAL ({fileSize / 1024}KB in 800KB-1MB range)";
                }

                Debug.WriteLine($"GIF encoded: {sizeStatus} | {frames.Count} frames | ratio={sizeRatio:F2}");

                return sizeRatio;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error encoding optimized GIF: {ex.Message}");
                throw;
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
