using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace speech_to_windows_input
{
    public partial class Form1 : Form
    {
        /*
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        */
        public Form1()
        {
            InitializeComponent();
        }
        /*
        public static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 4)
                return GetWindowLongPtr32(hWnd, nIndex);
            else
                return GetWindowLongPtr64(hWnd, nIndex);
        }
        public static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 4)
                return SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
            else
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        }
        */
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                // Make sure Top Most property on form is set to false
                // Ref: https://www.codeproject.com/Articles/12877/Transparent-Click-Through-Forms
                // Ref: https://stackoverflow.com/a/10727337
                const int WS_EX_LAYERED = 0x80000;
                const int WS_EX_TRANSPARENT = 0x20;
                const int WS_EX_TOPMOST = 0x8;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED;
                cp.ExStyle |= WS_EX_TRANSPARENT;
                cp.ExStyle |= WS_EX_TOPMOST;
                return cp;
            }
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            /*
            const int GWL_EXSTYLE = -20;
            const int WS_EX_LAYERED = 0x80000;
            const int WS_EX_TRANSPARENT = 0x20;
            // const int WS_EX_NOACTIVATE = 0x8000000;
            // const int LWA_ALPHA = 0x2;
            IntPtr initialStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
            IntPtr newStyle = new IntPtr(initialStyle.ToInt64() | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            SetWindowLong(this.Handle, GWL_EXSTYLE, newStyle);
            // SetLayeredWindowAttributes(this.Handle, 0, 255, LWA_ALPHA);
            */
        }
    }
}
