using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransformerNavigator
{
    public class ScreenRegionSelector : Form
    {
        public Rectangle? SelectedRegion { get; private set; }
        private Point _start;
        private Point _end;
        private bool _dragging;

        public ScreenRegionSelector()
        {
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            TopMost = true;
            ShowInTaskbar = false;
            Opacity = 0.25;
            BackColor = Color.Black;
            Cursor = Cursors.Cross;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _start = e.Location;
                _end = e.Location;
                Capture = true;
                Invalidate();
            }
            else if (e.Button == MouseButtons.Right)
            {
                // Right-click cancels
                SelectedRegion = null;
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                _end = e.Location;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                var rect = GetRect(_start, _end);
                if (rect.Width > 5 && rect.Height > 5)
                {
                    SelectedRegion = rect;
                    DialogResult = DialogResult.OK;
                }
                else
                {
                    SelectedRegion = null;
                    DialogResult = DialogResult.Cancel;
                }
                Close();
            }
        }

        private Rectangle GetRect(Point p1, Point p2)
        {
            int x = Math.Min(p1.X, p2.X);
            int y = Math.Min(p1.Y, p2.Y);
            int w = Math.Abs(p1.X - p2.X);
            int h = Math.Abs(p1.Y - p2.Y);
            return new Rectangle(x, y, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_dragging)
            {
                var rect = GetRect(_start, _end);
                using (var pen = new Pen(Color.Red, 2))
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }
                using (var b = new SolidBrush(Color.FromArgb(64, Color.White)))
                {
                    e.Graphics.FillRectangle(b, rect);
                }
            }
        }
    }
}
