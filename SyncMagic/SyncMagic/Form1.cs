using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AnimatedGif;
using System.Text;

namespace SyncMagic
{
    public partial class Form1 : Form
    {
        // Simulation instances  
        private AntFarm antFarm;
        private DigitalClockWeather clockWeather;
        private VillageSimulation simulation;
        private WeatherDisplay weatherDisplay;
        private BallSimulation ballSimulation;
        private PlanetSimulation planetSimulation;
        private OfficeSimulation officeSimulation;
        private NewsFeedScroller newsFeedScroller;
        private ArkanoidGame arkanoidGame;
        private FishTankSimulation fishTankSimulation;
        private SimpleFishTankSimulation simpleFishTankSimulation;

        // Image uploader  
        private ImageUploader imageUploader = new ImageUploader();

        // Simulation active flags  
        private bool antFarmActive = false;
        private bool clockActive = false;
        private bool villageActive = false;
        private bool weatherActive = false;
        private bool ballSimulationActive = false;
        private bool planetSimulationActive = false;
        private bool officeActive = false;
        private bool feedScrollActive = false;
        private bool arkanoidGameActive = false;
        private bool fishTankActive = false;
        private bool simpleFishTankActive = false;

        // Delegate to get frames from the active simulation  
        private Func<Bitmap> getFrame;

        // GIF recording cancellation token source  
        private CancellationTokenSource gifCancellationTokenSource;

        // MemoryStream to hold GIF image for PictureBox  
        private MemoryStream pictureBoxMemoryStream;
        // File to persist last used IP address
        private readonly string ipFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SyncMagic", "last_ip.txt");

        // UI controls for rotation and status bar mirroring
        private Button btnRotate;
        private CheckBox chkMirrorStatusBar;

        public Form1()
        {
            InitializeComponent();

            // Initialize simulations  
            antFarm = new AntFarm();
            clockWeather = new DigitalClockWeather();
            simulation = new VillageSimulation();
            ballSimulation = new BallSimulation();
            planetSimulation = new PlanetSimulation();
            officeSimulation = new OfficeSimulation();
            newsFeedScroller = new NewsFeedScroller();
            arkanoidGame = new ArkanoidGame();

            // Configure list of cities for weather  
            List<string> cities = new List<string>
            {
                "Helsinki", "Tampere", "Turku", "Oulu",
                "Jyv�skyl�", "Kangasala", "Rovaniemi", "M�ntt�-Vilppula"
            };
            weatherDisplay = new WeatherDisplay(cities);

            // Start the existing timer  
            tmrScreenUpdate.Start();

            // Load last saved IP (if any) and save on change/close
            LoadLastIp();
            txtIPAddress.Leave += (s, e) => { if (chkAutoSaveIP.Checked) SaveLastIp(); };
            btnSaveIP.Click += (s, e) => SaveLastIp();
            // Ensure we persist settings on close
            this.FormClosing += Form1_FormClosing;

            // --- UI: Rotation button and Mirror status bar checkbox (programmatic addition) ---
            btnRotate = new Button
            {
                Name = "btnRotate",
                Text = "Rotation: 0°",
                AutoSize = true,
            };
            // Try to place near the seamless mode button, fallback to top-right
            try
            {
                btnRotate.Location = new Point(btnSeamlessMode.Right + 10, btnSeamlessMode.Top);
            }
            catch
            {
                btnRotate.Location = new Point(10, 10);
            }
            btnRotate.Click += (s, e) =>
            {
                // Cycle 0 -> 90 -> 180 -> 270 -> 0
                switch (RenderOptions.RotationDegrees)
                {
                    case 0: RenderOptions.RotationDegrees = 90; break;
                    case 90: RenderOptions.RotationDegrees = 180; break;
                    case 180: RenderOptions.RotationDegrees = 270; break;
                    default: RenderOptions.RotationDegrees = 0; break;
                }
                btnRotate.Text = $"Rotation: {RenderOptions.RotationDegrees}°";
            };
            Controls.Add(btnRotate);

            chkMirrorStatusBar = new CheckBox
            {
                Name = "chkMirrorStatusBar",
                Text = "Mirror clock/temp",
                AutoSize = true,
            };
            try
            {
                chkMirrorStatusBar.Location = new Point(btnRotate.Left, btnRotate.Bottom + 6);
            }
            catch
            {
                chkMirrorStatusBar.Location = new Point(10, btnRotate.Bottom + 6);
            }
            chkMirrorStatusBar.CheckedChanged += (s, e) =>
            {
                // Only mirror the clock/temp status bar; do NOT mirror full frame here to avoid double-flip
                RenderOptions.MirrorStatusBar = chkMirrorStatusBar.Checked;
                // Ensure full-frame mirroring stays off when toggling status bar mirroring
                if (chkMirrorStatusBar.Checked)
                    RenderOptions.MirrorFrame = false;
            };
            Controls.Add(chkMirrorStatusBar);


        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Load settings  
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Only override the textbox if a non-empty setting exists.
            // This prevents wiping out the value we loaded from disk in LoadLastIp().
            var ip = Properties.Settings.Default.IPAddress;
            if (!string.IsNullOrWhiteSpace(ip))
            {
                txtIPAddress.Text = ip;
            }
        }

        private void SaveSettings()
        {
            // Save the IPAddress from txtIPAddress to settings  
            Properties.Settings.Default.IPAddress = txtIPAddress.Text;
            Properties.Settings.Default.Save();
            tmrScreenUpdate.Stop();
            // Also persist to disk for quick reuse
            SaveLastIp();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Save settings when form is closing  
            SaveSettings();
        }

        private async void btnSeamlessMode_Click(object? sender, EventArgs e)
        {
            // Medical-grade UX: validate, guard, clear status, disable during op
            lblOpsStatus.Text = string.Empty;
            btnSeamlessMode.Enabled = false;
            try
            {
                string ip = txtIPAddress.Text.Trim();
                if (string.IsNullOrWhiteSpace(ip))
                {
                    MessageBox.Show("Please enter the device IP address.", "SyncMagic", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Toggle stop if running
                if (gifCancellationTokenSource != null)
                {
                    StopGifRecording();
                    // Stop all simulations to ensure a clean state
                    DeactivateAllSimulationsExcept("");
                    btnSeamlessMode.Text = "Start Seamless Tank";
                    lblOpsStatus.Text = "Stopped.";
                    return;
                }

                // Probe device first
                var caps = await imageUploader.ProbeDeviceAsync(ip);
                var sb = new StringBuilder();
                sb.Append($"Model: {caps.Model}  Ver: {caps.Version}");
                if (caps.FreeSpaceImageKB.HasValue) sb.Append($"  Free /image: {caps.FreeSpaceImageKB.Value}KB");
                if (caps.FreeSpaceGifKB.HasValue) sb.Append($"  Free /gif: {caps.FreeSpaceGifKB.Value}KB");
                lblOpsStatus.Text = sb.ToString();

                // Start the Fish Tank with seamless adaptive recorder
                if (fishTankSimulation == null) fishTankSimulation = new FishTankSimulation();
                DeactivateAllSimulationsExcept("fishTank");
                btnSeamlessMode.Text = "Stop Seamless Tank";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start seamless mode: {ex.Message}", "SyncMagic", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnSeamlessMode.Enabled = true;
            }
        }

        private void btnUpdate_Click(object sender, EventArgs e)
        {
            // Toggle antFarmActive  
            antFarmActive = !antFarmActive;

            // Deactivate other simulations if necessary  
            if (antFarmActive)
            {
                DeactivateAllSimulationsExcept("antFarm");
            }
            else
            {
                DeactivateAllSimulationsExcept("");
            }
        }

        private void btnClock_Click(object sender, EventArgs e)
        {
            // Toggle clockActive  
            clockActive = !clockActive;

            if (clockActive)
            {
                DeactivateAllSimulationsExcept("clock");
            }
            else
            {
                DeactivateAllSimulationsExcept("");
            }
        }

        private void btnVillage_Click(object sender, EventArgs e)
        {
            // Toggle villageActive  
            villageActive = !villageActive;

            if (villageActive)
            {
                DeactivateAllSimulationsExcept("village");
            }
            else
            {
                DeactivateAllSimulationsExcept("");
            }
        }

        private void btnWeather_Click(object sender, EventArgs e)
        {
            // Toggle weatherActive  
            weatherActive = !weatherActive;

            if (weatherActive)
            {
                DeactivateAllSimulationsExcept("weather");
            }
            else
            {
                DeactivateAllSimulationsExcept("");
            }
        }

        private void btnBallSimulation_Click(object sender, EventArgs e)
        {
            // Toggle ballSimulationActive  
            ballSimulationActive = !ballSimulationActive;

            if (ballSimulationActive)
            {
                DeactivateAllSimulationsExcept("ballSimulation");
            }
            else
            {
                DeactivateAllSimulationsExcept("");
            }
        }

        private void btnPlanet_Click(object sender, EventArgs e)
        {
            // Toggle planetSimulationActive  
            planetSimulationActive = !planetSimulationActive;

            if (planetSimulationActive)
            {
                DeactivateAllSimulationsExcept("planetSimulation");
            }
            else
            {
                DeactivateAllSimulationsExcept("");
            }
        }

        private void btnOffice_Click(object sender, EventArgs e)
        {
            // Toggle officeActive  
            officeActive = !officeActive;

            if (officeActive)
            {
                DeactivateAllSimulationsExcept("office");
            }
            else
            {
                DeactivateAllSimulationsExcept("");
            }
        }

        private void btnRSS_Click(object sender, EventArgs e)
        {
            // Toggle feedScrollActive  
            feedScrollActive = !feedScrollActive;

            if (feedScrollActive)
            {
                DeactivateAllSimulationsExcept("feedScroll");
            }
            else
            {
                DeactivateAllSimulationsExcept("");
            }
        }

        private void btnArkanoid_Click(object sender, EventArgs e)
        {
            // Toggle arkanoidGameActive  
            arkanoidGameActive = !arkanoidGameActive;

            if (arkanoidGameActive)
            {
                DeactivateAllSimulationsExcept("arkanoidGame");
            }
            else
            {
                DeactivateAllSimulationsExcept("");
            }
        }

        private void btnGoldFish_Click(object sender, EventArgs e)
        {
            // Toggle fishTankActive  
            fishTankActive = !fishTankActive;

            if (fishTankActive)
            {
                // Initialize FishTankSimulation if null  
                if (fishTankSimulation == null)
                {
                    fishTankSimulation = new FishTankSimulation();
                }
                DeactivateAllSimulationsExcept("fishTank");
            }
            else
            {
                DeactivateAllSimulationsExcept("");
            }
        }

        private void btnFishTank_Click(object sender, EventArgs e)
        {
            // Toggle simpleFishTankActive  
            simpleFishTankActive = !simpleFishTankActive;

            if (simpleFishTankActive)
            {
                // Initialize SimpleFishTankSimulation if null  
                if (simpleFishTankSimulation == null)
                {
                    simpleFishTankSimulation = new SimpleFishTankSimulation();
                }
                DeactivateAllSimulationsExcept("simpleFishTank");
            }
            else
            {
                DeactivateAllSimulationsExcept("");
            }
        }

        private void DeactivateAllSimulationsExcept(string activeSimulation)
        {
            // Reset all simulation flags  
            antFarmActive = false;
            clockActive = false;
            villageActive = false;
            weatherActive = false;
            ballSimulationActive = false;
            planetSimulationActive = false;
            officeActive = false;
            feedScrollActive = false;
            arkanoidGameActive = false;
            fishTankActive = false;
            simpleFishTankActive = false;

            // Stop any existing GIF recording  
            StopGifRecording();

            // Dispose of existing image  
            if (picScreen.Image != null)
            {
                ImageAnimator.StopAnimate(picScreen.Image, OnFrameChanged);
                picScreen.Image.Dispose();
                picScreen.Image = null;
            }

            // Dispose previous MemoryStream  
            pictureBoxMemoryStream?.Dispose();
            pictureBoxMemoryStream = null;

            // Activate only the specified simulation and set getFrame delegate  
            switch (activeSimulation)
            {
                case "antFarm":
                    antFarmActive = true;
                    getFrame = antFarm.GetFarmImage;
                    break;
                case "clock":
                    clockActive = true;
                    getFrame = clockWeather.GetClock;
                    break;
                case "village":
                    villageActive = true;
                    getFrame = simulation.GetVillage;
                    break;
                case "weather":
                    weatherActive = true;
                    getFrame = weatherDisplay.GetWeatherBitmap;
                    break;
                case "ballSimulation":
                    ballSimulationActive = true;
                    getFrame = ballSimulation.GetBallPositions;
                    break;
                case "planetSimulation":
                    planetSimulationActive = true;
                    getFrame = planetSimulation.GetPlanetPositions;
                    break;
                case "office":
                    officeActive = true;
                    getFrame = officeSimulation.GetOfficeState;
                    break;
                case "feedScroll":
                    feedScrollActive = true;
                    getFrame = newsFeedScroller.GetNewsFeed;
                    break;
                case "arkanoidGame":
                    arkanoidGameActive = true;
                    getFrame = arkanoidGame.GetFrame;
                    break;
                case "fishTank":
                    fishTankActive = true;
                    getFrame = fishTankSimulation.GetFrame;
                    break;
                case "simpleFishTank":
                    simpleFishTankActive = true;
                    getFrame = simpleFishTankSimulation.GetFrame;
                    break;
                default:
                    getFrame = null;
                    break;
            }

            // Start GIF recording if a simulation is active  
            if (getFrame != null)
            {
                StartGifRecording();
            }
        }

        private async void StartGifRecording()
        {
            // Stop any existing GIF recording  
            StopGifRecording();

            // Initialize the cancellation token source  
            gifCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = gifCancellationTokenSource.Token;

            try
            {
                int deviceLoopMs = 60000;       // Target runtime of GIF on device (tuneable)
                const int safetyMs = 200;       // Headroom to avoid device looping old GIF
                double emaEncodeMs = 150;       // EMA of encode time
                const int minFrames = 10;
                const int maxFrames = 60;
                const int baseCaptureIntervalMs = 250; // baseline pacing for capture
                double emaUploadMs = 1200;      // Initial EMA upload estimate
                const double emaAlpha = 0.30;   // EMA smoothing factor
                const int targetPauseMs = 3000; // Desired max pause (encode+upload) in ms
                const double adjustMin = 1.5;   // Minimum scale step per cycle
                const double adjustMax = 2.25;  // Maximum scale step per cycle

                double frameScale = 1.0;        // Multiplier to adjust frames based on measured pause

                List<Bitmap> gifFrames = new List<Bitmap>();

                while (!cancellationToken.IsCancellationRequested)
                {
                    gifFrames.Clear();

                    // Compute capture budget so that encode+upload finishes before current device loop ends
                    int captureBudgetMs = deviceLoopMs - (int)Math.Ceiling(emaUploadMs) - safetyMs - (int)Math.Ceiling(emaEncodeMs);
                    if (captureBudgetMs < 1000) captureBudgetMs = 1000; // at least 1s of motion

                    // Fewer frames when budget is small (apply dynamic scaling to hit pause target)
                    int baseFrames = Math.Max(1, captureBudgetMs / baseCaptureIntervalMs);
                    int frameCount = (int)Math.Round(baseFrames * frameScale);
                    frameCount = Math.Clamp(frameCount, minFrames, maxFrames);
                    if (frameCount < 1) frameCount = 1;

                    // Distribute capture over captureBudgetMs
                    int captureIntervalMs = Math.Max(1, captureBudgetMs / frameCount);

                    // Collect frames for the GIF  
                    for (int i = 0; i < frameCount; i++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        // Get a frame from the active simulation  
                        Bitmap frame = getFrame.Invoke();

                        // Resize the frame to 240x240  
                        Bitmap resizedFrame = ResizeToFixedSize(frame, 240, 240);

                        // Apply rotation if requested
                        ApplyRotationInPlace(resizedFrame);

                        // Apply full-frame mirroring if requested (affects all apps, e.g., RSS)
                        if (RenderOptions.MirrorFrame)
                        {
                            resizedFrame.RotateFlip(RotateFlipType.RotateNoneFlipX);
                        }

                        // Add the resized/rotated frame to the list  
                        gifFrames.Add(resizedFrame);

                        // Dispose of the original frame  
                        frame.Dispose();

                        // Update the PictureBox with the latest resized frame  
                        if (this.InvokeRequired)
                        {
                            this.Invoke(new Action(() =>
                            {
                                if (picScreen.Image != null)
                                {
                                    ImageAnimator.StopAnimate(picScreen.Image, OnFrameChanged);
                                    picScreen.Image.Dispose();
                                    picScreen.Image = null;
                                }
                                picScreen.Image = (Bitmap)resizedFrame.Clone();
                                picScreen.Invalidate();
                            }));
                        }
                        else
                        {
                            if (picScreen.Image != null)
                            {
                                ImageAnimator.StopAnimate(picScreen.Image, OnFrameChanged);
                                picScreen.Image.Dispose();
                                picScreen.Image = null;
                            }
                            picScreen.Image = (Bitmap)resizedFrame.Clone();
                            picScreen.Invalidate();
                        }

                        // Wait between captures with small headroom
                        int waitMs = Math.Max(1, captureIntervalMs - 30);
                        await Task.Delay(waitMs, cancellationToken);
                    }

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // Build per-frame delays with easing and edge holds to mask upload pauses
                    // Estimate total pause we need to hide using EMA from previous cycles
                    int estPauseMs = (int)Math.Ceiling(emaUploadMs + emaEncodeMs);
                    estPauseMs = Math.Clamp(estPauseMs, 1000, 6000); // keep holds within sensible bounds
                    // Split holds evenly between start and end frames
                    int holdStartMs = estPauseMs / 2;
                    int holdEndMs = estPauseMs - holdStartMs;

                    // Compute delays for each frame so that:
                    // - First and last frames are completely still (extra hold time)
                    // - Interior frames use an ease-in-out timing distribution
                    // - Sum of all delays ~= deviceLoopMs
                    List<int> perFrameDelays = BuildEasedDelays(deviceLoopMs, gifFrames.Count, holdStartMs, holdEndMs);

                    // Measure encode time
                    var encodeSw = Stopwatch.StartNew();
                    // Assemble frames into GIF with full 256-color palette for richer colors
                    // Use per-frame delays to realize easing and start/end holds
                    using (var gif = AnimatedGif.AnimatedGif.Create("screen.gif", 10))
                    {
                        for (int i = 0; i < gifFrames.Count; i++)
                        {
                            var image = gifFrames[i];
                            var delay = perFrameDelays[i];
                            gif.AddFrame(image, delay, GifQuality.Bit8);
                        }
                    }
                    encodeSw.Stop();
                    var encodeDuration = encodeSw.Elapsed;

                    // Upload the GIF and measure actual upload time
                    var sw = Stopwatch.StartNew();
                    await imageUploader.UploadGifAsync(txtIPAddress, "screen.gif");
                    sw.Stop();
                    var uploadDuration = sw.Elapsed;

                    // Log measured pause components (encode + upload)
                    var totalPause = encodeDuration + uploadDuration;
                    Debug.WriteLine($"GIF encode: {encodeDuration.TotalMilliseconds:F0} ms, upload: {uploadDuration.TotalMilliseconds:F0} ms, pause total: {totalPause.TotalMilliseconds:F0} ms, frames: {gifFrames.Count}, loop: {deviceLoopMs} ms, capture window: {captureBudgetMs} ms, frameScale: {frameScale:F2}, holdStart: {holdStartMs} ms, holdEnd: {holdEndMs} ms");

                    // Update PictureBox to show the new GIF
                    if (this.InvokeRequired)
                    {
                        this.Invoke(new Action(() =>
                        {
                            UpdatePictureBoxWithGif();
                        }));
                    }
                    else
                    {
                        UpdatePictureBoxWithGif();
                    }

                    // Dispose frames after using them  
                    foreach (var img in gifFrames)
                    {
                        img.Dispose();
                    }
                    gifFrames.Clear();

                    // Update EMA for next cycle
                    emaUploadMs = emaAlpha * uploadDuration.TotalMilliseconds + (1 - emaAlpha) * emaUploadMs;
                    emaEncodeMs = emaAlpha * encodeDuration.TotalMilliseconds + (1 - emaAlpha) * emaEncodeMs;

                    // Adapt frame scaling to drive pause toward target
                    double s = targetPauseMs / Math.Max(1.0, totalPause.TotalMilliseconds);
                    s = Math.Clamp(s, adjustMin, adjustMax);
                    frameScale = Math.Clamp(frameScale * s, 0.1, 3.0);

                    // Immediately start capturing next segment; upload will finish before next loop end by design
                }
            }
            catch (OperationCanceledException)
            {
                // Task was canceled  
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private void StopGifRecording()
        {
            if (gifCancellationTokenSource != null)
            {
                gifCancellationTokenSource.Cancel();
                gifCancellationTokenSource.Dispose();
                gifCancellationTokenSource = null;
            }
        }

        private void LoadLastIp()
        {
            try
            {
                var dir = Path.GetDirectoryName(ipFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(ipFilePath))
                {
                    var txt = File.ReadAllText(ipFilePath).Trim();
                    if (!string.IsNullOrEmpty(txt)) txtIPAddress.Text = txt;
                }
            }
            catch
            {
                // ignore load errors
            }
        }

        private void SaveLastIp()
        {
            try
            {
                var dir = Path.GetDirectoryName(ipFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ipFilePath, txtIPAddress.Text.Trim());
            }
            catch
            {
                // ignore save errors
            }
        }

        private void btnSaveIP_Click(object? sender, EventArgs e)
        {
            SaveLastIp();
        }

        private Bitmap ResizeToFixedSize(Bitmap source, int width, int height)
        {
            // Render to 24bpp to help GIF quantization and keep colors vivid
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

        private void UpdatePictureBoxWithGif()
        {
            if (picScreen.Image != null)
            {
                ImageAnimator.StopAnimate(picScreen.Image, OnFrameChanged);
                picScreen.Image.Dispose();
                picScreen.Image = null;
            }

            // Dispose previous MemoryStream  
            pictureBoxMemoryStream?.Dispose();

            // Read the GIF file into a MemoryStream  
            byte[] imageData = File.ReadAllBytes("screen.gif");
            pictureBoxMemoryStream = new MemoryStream(imageData);

            picScreen.Image = Image.FromStream(pictureBoxMemoryStream);

            // Start the animation for the GIF  
            ImageAnimator.Animate(picScreen.Image, OnFrameChanged);

            // Force the PictureBox to repaint  
            picScreen.Invalidate();
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
                default:
                    break;
            }
        }

        // Build per-frame delays that fill totalLoopMs duration using:
        // - A still hold on the first and last frame (holdStartMs/holdEndMs)
        // - An ease-in-out timing across the interior frames (faster in the middle)
        private static List<int> BuildEasedDelays(int totalLoopMs, int frameCount, int holdStartMs, int holdEndMs)
        {
            var delays = new List<int>(Math.Max(frameCount, 1));

            if (frameCount <= 0)
            {
                return new List<int> { totalLoopMs };
            }

            if (frameCount == 1)
            {
                // Single frame: just show the still image the whole loop
                delays.Add(totalLoopMs);
                return delays;
            }

            // Ensure holds are not longer than the loop itself
            int maxHolds = Math.Min(totalLoopMs - 2, Math.Max(0, holdStartMs + holdEndMs));
            int sHold = Math.Min(holdStartMs, maxHolds / 2);
            int eHold = Math.Min(holdEndMs, maxHolds - sHold);

            // Interior movement budget
            int interiorFrames = Math.Max(0, frameCount - 2);
            int movementBudget = Math.Max(0, totalLoopMs - sHold - eHold);

            // No interior frames: split budget across ends
            if (interiorFrames == 0)
            {
                delays.Add(movementBudget / 2 + sHold);
                delays.Add(movementBudget - movementBudget / 2 + eHold);
                return delays;
            }

            // Build easing weights for interior frames so that timing is longer near edges and shorter in the middle
            // weight(t) = baseline + (1-baseline) * (0.5 + 0.5*cos(2πt)) where t in [0,1]
            // This yields more time at the edges (ease-in/out) while guaranteeing positive weights.
            double baseline = 0.25; // keep minimum share for middle frames
            double sumW = 0.0;
            double[] w = new double[interiorFrames];
            if (interiorFrames == 1)
            {
                w[0] = 1.0;
                sumW = 1.0;
            }
            else
            {
                for (int j = 0; j < interiorFrames; j++)
                {
                    double t = (double)j / (interiorFrames - 1);
                    double wt = baseline + (1.0 - baseline) * (0.5 + 0.5 * Math.Cos(2.0 * Math.PI * t));
                    w[j] = wt;
                    sumW += wt;
                }
            }

            // First frame gets the start hold, last frame gets the end hold
            delays.Add(Math.Max(1, sHold));

            // Distribute movement budget across interior frames according to easing weights
            int allocated = 0;
            for (int j = 0; j < interiorFrames; j++)
            {
                int d = (int)Math.Round(w[j] / sumW * movementBudget);
                if (d < 1) d = 1; // GIF frame delay must be >=1ms
                delays.Add(d);
                allocated += d;
            }

            // Last frame with end hold; adjust for rounding so total matches totalLoopMs
            int used = 0;
            foreach (var d in delays) used += d;
            int remaining = totalLoopMs - used - eHold;
            if (remaining < 0) remaining = 0;
            delays.Add(Math.Max(1, remaining + eHold));

            // Final correction: adjust last frame so sum equals totalLoopMs exactly
            int sum = 0;
            foreach (var d in delays) sum += d;
            int diff = totalLoopMs - sum;
            delays[delays.Count - 1] = Math.Max(1, delays[delays.Count - 1] + diff);

            return delays;
        }

        private void OnFrameChanged(object sender, EventArgs e)
        {
            // Invalidate the PictureBox to update the frame  
            picScreen.Invalidate();
        }

        private void tmrScreenUpdate_Tick(object sender, EventArgs e)
        {
            // Screen update timer tick event  
            // No need to implement anything here as GIF recording handles frame updates  
        }

        private void UploadTimer_Tick(object sender, EventArgs e)
        {
            // Image uploading is handled in StartGifRecording  
            // No need to implement anything here  
        }
    }
}