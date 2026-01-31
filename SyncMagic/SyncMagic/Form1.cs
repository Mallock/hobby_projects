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

        // GIF generator instance
        private GifGenerator gifGenerator;

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
                if (gifGenerator != null)
                {
                    gifGenerator.StopRecording();
                    gifGenerator = null;
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
            if (gifGenerator != null)
            {
                gifGenerator.StopRecording();
                gifGenerator = null;
            }

            // Dispose of existing image  
            if (picScreen.Image != null)
            {
                ImageAnimator.StopAnimate(picScreen.Image, OnFrameChanged);
                picScreen.Image.Dispose();
                picScreen.Image = null;
            }

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
            if (getFrame == null)
                return;

            gifGenerator = new GifGenerator(
                imageUploader,
                getFrame,
                (image) =>
                {
                    if (picScreen.Image != null)
                    {
                        ImageAnimator.StopAnimate(picScreen.Image, OnFrameChanged);
                        picScreen.Image.Dispose();
                        picScreen.Image = null;
                    }

                    picScreen.Image = image;
                    ImageAnimator.Animate(picScreen.Image, OnFrameChanged);
                    picScreen.Invalidate();
                },
                txtIPAddress
            );

            await gifGenerator.StartRecordingAsync(
                CancellationToken.None
            );
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