using System;
using System.Drawing;
using System.Windows.Forms;
using TransformerNavigator;

namespace MinimalBrowser.Services
{
    public sealed class ScreenCaptureService
    {
        public Rectangle? SelectRegion(IWin32Window owner)
        {
            using var selector = new ScreenRegionSelector
            {
                Opacity = 0.25f
            };
            return selector.ShowDialog(owner) == DialogResult.OK ? selector.SelectedRegion : (Rectangle?)null;
        }

        public Bitmap CaptureRegion(Rectangle region)
        {
            var bmp = new Bitmap(region.Width, region.Height);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(region.Location, Point.Empty, region.Size);
            return bmp;
        }
    }
}