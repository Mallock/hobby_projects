using System.Drawing;
using System.Drawing.Drawing2D;

namespace MinimalBrowser
{
    public partial class WebBrowser : Form
    {
        public WebBrowser()
        {
            InitializeComponent();

            this.FormBorderStyle = FormBorderStyle.None;

        }

        private void WebBrowser_Activated(object sender, EventArgs e)
        {
            this.FormBorderStyle = FormBorderStyle.Sizable;
        }

        private void WebBrowser_Deactivate(object sender, EventArgs e)
        {

            this.FormBorderStyle = FormBorderStyle.None;

        }
    }
}