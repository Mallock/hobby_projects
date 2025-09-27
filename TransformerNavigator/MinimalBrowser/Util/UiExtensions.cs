using System;
using System.Windows.Forms;

namespace MinimalBrowser.Util
{
    public static class UiExtensions
    {
        public static void UI(this Control ctrl, Action action)
        {
            if (ctrl.InvokeRequired) ctrl.BeginInvoke(action);
            else action();
        }
    }
}