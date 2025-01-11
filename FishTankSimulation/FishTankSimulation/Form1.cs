namespace FishTankSimulation
{
    public partial class Form1 : Form
    {
        // Add FishTankSimulation instance and flag  
        private FishTank fishTankSimulation;
        private bool fishTankActive = false;
        private bool isProcessing = false;
        public Form1()
        {
            InitializeComponent();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void newTankToolStripMenuItem_Click(object sender, EventArgs e)
        {
            fishTankActive = true;

            // Initialize FishTankSimulation  
            fishTankSimulation = new FishTank(picScreen.Width, picScreen.Height);
            if (!tmrScreenUpdate.Enabled) { tmrScreenUpdate.Start(); }

        }

        private void tmrScreenUpdate_Tick(object sender, EventArgs e)
        {
            if (isProcessing)
                return;

            isProcessing = true;
            try
            {
                // Dispose of the previous image to prevent memory leaks    
                if (picScreen.Image != null)
                {
                    picScreen.Image.Dispose();
                    picScreen.Image = null;
                }

                if (fishTankActive)
                {
                    picScreen.Image = fishTankSimulation.GetFrame();
                }
            }
            finally
            {
                isProcessing = false;
            }
        }
    }
}