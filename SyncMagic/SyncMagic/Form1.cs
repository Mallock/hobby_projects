using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AnimatedGif;

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


        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Load settings  
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Load the IPAddress setting into txtIPAddress  
            txtIPAddress.Text = Properties.Settings.Default.IPAddress;
        }

        private void SaveSettings()
        {
            // Save the IPAddress from txtIPAddress to settings  
            Properties.Settings.Default.IPAddress = txtIPAddress.Text;
            Properties.Settings.Default.Save();
            tmrScreenUpdate.Stop();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Save settings when form is closing  
            SaveSettings();
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
                int deviceLoopMs = 40000;       // Target runtime of GIF on device (tuneable)
                const int safetyMs = 400;       // Headroom to avoid device looping old GIF
                double emaEncodeMs = 150;       // EMA of encode time
                const int minFrames = 10;
                const int maxFrames = 100;
                const int baseCaptureIntervalMs = 250; // baseline pacing for capture
                double emaUploadMs = 1200;      // Initial EMA upload estimate
                const double emaAlpha = 0.30;   // EMA smoothing factor
                const int targetPauseMs = 3000; // Desired max pause (encode+upload) in ms
                const double adjustMin = 0.5;   // Minimum scale step per cycle
                const double adjustMax = 1.25;  // Maximum scale step per cycle

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

                        // Add the resized frame to the list  
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

                    // Set GIF per-frame delay to stretch to the full device loop
                    int gifFrameDelayMs = Math.Max(1, deviceLoopMs / Math.Max(1, gifFrames.Count));

                    // Measure encode time
                    var encodeSw = Stopwatch.StartNew();
                    // Assemble frames into GIF with reduced quality
                    using (var gif = AnimatedGif.AnimatedGif.Create("screen.gif", gifFrameDelayMs))
                    {
                        foreach (var image in gifFrames)
                        {
                            gif.AddFrame(image, quality: GifQuality.Bit4);
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
                    Debug.WriteLine($"GIF encode: {encodeDuration.TotalMilliseconds:F0} ms, upload: {uploadDuration.TotalMilliseconds:F0} ms, pause total: {totalPause.TotalMilliseconds:F0} ms, frames: {gifFrames.Count}, loop: {deviceLoopMs} ms, capture window: {captureBudgetMs} ms, frameScale: {frameScale:F2}");

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

        private Bitmap ResizeToFixedSize(Bitmap source, int width, int height)
        {
            var resizedBitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(resizedBitmap))
            {
                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.InterpolationMode = InterpolationMode.Low;
                graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
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