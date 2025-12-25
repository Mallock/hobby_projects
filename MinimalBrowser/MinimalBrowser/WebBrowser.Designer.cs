namespace MinimalBrowser
{
    partial class WebBrowser
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(WebBrowser));
            webView21 = new Microsoft.Web.WebView2.WinForms.WebView2();
            topPanel = new Panel();
            backButton = new Button();
            forwardButton = new Button();
            addressBar = new TextBox();
            goButton = new Button();
            chkTop = new CheckBox();
            ((System.ComponentModel.ISupportInitialize)webView21).BeginInit();
            topPanel.SuspendLayout();
            SuspendLayout();
            // 
            // webView21
            // 
            webView21.AllowExternalDrop = true;
            webView21.CreationProperties = null;
            webView21.DefaultBackgroundColor = Color.White;
            webView21.Dock = DockStyle.Fill;
            webView21.Location = new Point(0, 36);
            webView21.Name = "webView21";
            webView21.Size = new Size(800, 414);
            webView21.Source = new Uri("https://google.com", UriKind.Absolute);
            webView21.TabIndex = 4;
            webView21.ZoomFactor = 1D;
            // 
            // topPanel
            // 
            topPanel.BackColor = Color.FromArgb(240, 242, 248);
            topPanel.Controls.Add(chkTop);
            topPanel.Controls.Add(backButton);
            topPanel.Controls.Add(forwardButton);
            topPanel.Controls.Add(addressBar);
            topPanel.Controls.Add(goButton);
            topPanel.Dock = DockStyle.Top;
            topPanel.Location = new Point(0, 0);
            topPanel.Name = "topPanel";
            topPanel.Padding = new Padding(4);
            topPanel.Size = new Size(800, 36);
            topPanel.TabIndex = 5;
            // 
            // backButton
            // 
            backButton.Location = new Point(83, 6);
            backButton.Name = "backButton";
            backButton.Size = new Size(32, 23);
            backButton.TabIndex = 0;
            backButton.Text = "<";
            // 
            // forwardButton
            // 
            forwardButton.Location = new Point(119, 6);
            forwardButton.Name = "forwardButton";
            forwardButton.Size = new Size(32, 23);
            forwardButton.TabIndex = 1;
            forwardButton.Text = ">";
            // 
            // addressBar
            // 
            addressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            addressBar.Location = new Point(157, 6);
            addressBar.Name = "addressBar";
            addressBar.Size = new Size(636, 23);
            addressBar.TabIndex = 2;
            // 
            // goButton
            // 
            goButton.Location = new Point(690, 4);
            goButton.Name = "goButton";
            goButton.Size = new Size(40, 23);
            goButton.TabIndex = 3;
            goButton.Text = "Go";
            // 
            // chkTop
            // 
            chkTop.AutoSize = true;
            chkTop.Location = new Point(7, 9);
            chkTop.Name = "chkTop";
            chkTop.Size = new Size(73, 19);
            chkTop.TabIndex = 4;
            chkTop.Text = "Topmost";
            chkTop.UseVisualStyleBackColor = true;
            chkTop.CheckedChanged += chkTop_CheckedChanged;
            // 
            // WebBrowser
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(webView21);
            Controls.Add(topPanel);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "WebBrowser";
            Text = "Browser";
            Activated += WebBrowser_Activated;
            Deactivate += WebBrowser_Deactivate;
            ((System.ComponentModel.ISupportInitialize)webView21).EndInit();
            topPanel.ResumeLayout(false);
            topPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private Microsoft.Web.WebView2.WinForms.WebView2 webView21;
        private System.Windows.Forms.Panel topPanel;
        private System.Windows.Forms.TextBox addressBar;
        private System.Windows.Forms.Button backButton;
        private System.Windows.Forms.Button forwardButton;
        private System.Windows.Forms.Button goButton;
        private CheckBox chkTop;
    }
}