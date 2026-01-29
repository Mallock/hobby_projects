namespace SyncMagic
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            label1 = new Label();
            txtIPAddress = new TextBox();
            chkAutoSaveIP = new CheckBox();
            btnSaveIP = new Button();
            groupBox1 = new GroupBox();
            picScreen = new PictureBox();
            btnUpdate = new Button();
            tmrScreenUpdate = new System.Windows.Forms.Timer(components);
            btnClock = new Button();
            btnVillage = new Button();
            btnWeather = new Button();
            btnBallSimulation = new Button();
            btnPlanet = new Button();
            btnOffice = new Button();
            btnRSS = new Button();
            btnArkanoid = new Button();
            btnGoldFish = new Button();
            btnFishTank = new Button();
            button1 = new Button();
            btnSeamlessMode = new Button();
            lblOpsStatus = new Label();
            groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)picScreen).BeginInit();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(12, 30);
            label1.Name = "label1";
            label1.Size = new Size(62, 15);
            label1.TabIndex = 0;
            label1.Text = "IP Address";
            // 
            // txtIPAddress
            // 
            txtIPAddress.Location = new Point(80, 22);
            txtIPAddress.Name = "txtIPAddress";
            txtIPAddress.Size = new Size(100, 23);
            txtIPAddress.TabIndex = 1;
            txtIPAddress.Text = "123.123.123.123";
            // 
            // chkAutoSaveIP
            // 
            chkAutoSaveIP.Location = new Point(190, 24);
            chkAutoSaveIP.Name = "chkAutoSaveIP";
            chkAutoSaveIP.Size = new Size(100, 20);
            chkAutoSaveIP.TabIndex = 2;
            chkAutoSaveIP.Text = "Auto-save IP";
            chkAutoSaveIP.Checked = true;
            chkAutoSaveIP.UseVisualStyleBackColor = true;
            // 
            // btnSaveIP
            // 
            btnSaveIP.Location = new Point(300, 22);
            btnSaveIP.Name = "btnSaveIP";
            btnSaveIP.Size = new Size(60, 23);
            btnSaveIP.TabIndex = 3;
            btnSaveIP.Text = "Save IP";
            btnSaveIP.UseVisualStyleBackColor = true;
            btnSaveIP.Click += btnSaveIP_Click;
            // 
            // groupBox1
            // 
            groupBox1.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            groupBox1.Controls.Add(picScreen);
            groupBox1.Location = new Point(548, 12);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(240, 240);
            groupBox1.TabIndex = 2;
            groupBox1.TabStop = false;
            groupBox1.Text = "Screen";
            // 
            // picScreen
            // 
            picScreen.Dock = DockStyle.Fill;
            picScreen.Location = new Point(3, 19);
            picScreen.Name = "picScreen";
            picScreen.Size = new Size(234, 218);
            picScreen.SizeMode = PictureBoxSizeMode.StretchImage;
            picScreen.TabIndex = 0;
            picScreen.TabStop = false;
            // 
            // btnUpdate
            // 
            btnUpdate.Location = new Point(12, 72);
            btnUpdate.Name = "btnUpdate";
            btnUpdate.Size = new Size(75, 23);
            btnUpdate.TabIndex = 3;
            btnUpdate.Text = "Ant Farm";
            btnUpdate.UseVisualStyleBackColor = true;
            btnUpdate.Click += btnUpdate_Click;
            // 
            // tmrScreenUpdate
            // 
            tmrScreenUpdate.Tick += tmrScreenUpdate_Tick;
            // 
            // btnClock
            // 
            btnClock.Location = new Point(12, 101);
            btnClock.Name = "btnClock";
            btnClock.Size = new Size(75, 23);
            btnClock.TabIndex = 4;
            btnClock.Text = "Clock";
            btnClock.UseVisualStyleBackColor = true;
            btnClock.Click += btnClock_Click;
            // 
            // btnVillage
            // 
            btnVillage.Location = new Point(12, 130);
            btnVillage.Name = "btnVillage";
            btnVillage.Size = new Size(75, 23);
            btnVillage.TabIndex = 5;
            btnVillage.Text = "Village";
            btnVillage.UseVisualStyleBackColor = true;
            btnVillage.Click += btnVillage_Click;
            // 
            // btnWeather
            // 
            btnWeather.Location = new Point(12, 159);
            btnWeather.Name = "btnWeather";
            btnWeather.Size = new Size(75, 23);
            btnWeather.TabIndex = 6;
            btnWeather.Text = "Weather";
            btnWeather.UseVisualStyleBackColor = true;
            btnWeather.Click += btnWeather_Click;
            // 
            // btnBallSimulation
            // 
            btnBallSimulation.Location = new Point(12, 188);
            btnBallSimulation.Name = "btnBallSimulation";
            btnBallSimulation.Size = new Size(75, 23);
            btnBallSimulation.TabIndex = 7;
            btnBallSimulation.Text = "Ball";
            btnBallSimulation.UseVisualStyleBackColor = true;
            btnBallSimulation.Click += btnBallSimulation_Click;
            // 
            // btnPlanet
            // 
            btnPlanet.Location = new Point(12, 217);
            btnPlanet.Name = "btnPlanet";
            btnPlanet.Size = new Size(75, 23);
            btnPlanet.TabIndex = 8;
            btnPlanet.Text = "Planet";
            btnPlanet.UseVisualStyleBackColor = true;
            btnPlanet.Click += btnPlanet_Click;
            // 
            // btnOffice
            // 
            btnOffice.Location = new Point(12, 246);
            btnOffice.Name = "btnOffice";
            btnOffice.Size = new Size(75, 23);
            btnOffice.TabIndex = 9;
            btnOffice.Text = "Office";
            btnOffice.UseVisualStyleBackColor = true;
            btnOffice.Click += btnOffice_Click;
            // 
            // btnRSS
            // 
            btnRSS.Location = new Point(12, 275);
            btnRSS.Name = "btnRSS";
            btnRSS.Size = new Size(75, 23);
            btnRSS.TabIndex = 10;
            btnRSS.Text = "RSS";
            btnRSS.UseVisualStyleBackColor = true;
            btnRSS.Click += btnRSS_Click;
            // 
            // btnArkanoid
            // 
            btnArkanoid.Location = new Point(12, 304);
            btnArkanoid.Name = "btnArkanoid";
            btnArkanoid.Size = new Size(75, 23);
            btnArkanoid.TabIndex = 11;
            btnArkanoid.Text = "Arkanoid";
            btnArkanoid.UseVisualStyleBackColor = true;
            btnArkanoid.Click += btnArkanoid_Click;
            // 
            // btnGoldFish
            // 
            btnGoldFish.Location = new Point(12, 333);
            btnGoldFish.Name = "btnGoldFish";
            btnGoldFish.Size = new Size(75, 23);
            btnGoldFish.TabIndex = 12;
            btnGoldFish.Text = "Goldfish";
            btnGoldFish.UseVisualStyleBackColor = true;
            btnGoldFish.Click += btnGoldFish_Click;
            // 
            // btnFishTank
            // 
            btnFishTank.Location = new Point(12, 362);
            btnFishTank.Name = "btnFishTank";
            btnFishTank.Size = new Size(75, 23);
            btnFishTank.TabIndex = 13;
            btnFishTank.Text = "FishTank";
            btnFishTank.UseVisualStyleBackColor = true;
            btnFishTank.Click += btnFishTank_Click;
            // 
            // button1
            // 
            button1.Location = new Point(12, 391);
            button1.Name = "button1";
            button1.Size = new Size(96, 23);
            button1.TabIndex = 14;
            button1.Text = "FishTank GIF";
            button1.UseVisualStyleBackColor = true;
           
            // 
            // btnSeamlessMode
            // 
            btnSeamlessMode.Location = new Point(114, 391);
            btnSeamlessMode.Name = "btnSeamlessMode";
            btnSeamlessMode.Size = new Size(130, 23);
            btnSeamlessMode.TabIndex = 15;
            btnSeamlessMode.Text = "Start Seamless Tank";
            btnSeamlessMode.UseVisualStyleBackColor = true;
            btnSeamlessMode.Click += btnSeamlessMode_Click;
            // 
            // lblOpsStatus
            // 
            lblOpsStatus.AutoSize = true;
            lblOpsStatus.Location = new Point(380, 26);
            lblOpsStatus.Name = "lblOpsStatus";
            lblOpsStatus.Size = new Size(0, 15);
            lblOpsStatus.TabIndex = 16;
            lblOpsStatus.Text = "";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(lblOpsStatus);
            Controls.Add(btnSeamlessMode);
            Controls.Add(button1);
            Controls.Add(btnFishTank);
            Controls.Add(btnGoldFish);
            Controls.Add(btnArkanoid);
            Controls.Add(btnRSS);
            Controls.Add(btnOffice);
            Controls.Add(btnPlanet);
            Controls.Add(btnBallSimulation);
            Controls.Add(btnWeather);
            Controls.Add(btnVillage);
            Controls.Add(btnClock);
            Controls.Add(btnUpdate);
            Controls.Add(groupBox1);
            Controls.Add(txtIPAddress);
            Controls.Add(chkAutoSaveIP);
            Controls.Add(btnSaveIP);
            Controls.Add(label1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "SyncMagic";
            Load += Form1_Load;
            groupBox1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)picScreen).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private TextBox txtIPAddress;
        private CheckBox chkAutoSaveIP;
        private Button btnSaveIP;
        private GroupBox groupBox1;
        private PictureBox picScreen;
        private Button btnUpdate;
        private System.Windows.Forms.Timer tmrScreenUpdate;
        private Button btnClock;
        private Button btnVillage;
        private Button btnWeather;
        private Button btnBallSimulation;
        private Button btnPlanet;
        private Button btnOffice;
        private Button btnRSS;
        private Button btnArkanoid;
        private Button btnGoldFish;
        private Button btnFishTank;
        private Button button1;
        private Button btnSeamlessMode;
        private Label lblOpsStatus;
    }
}