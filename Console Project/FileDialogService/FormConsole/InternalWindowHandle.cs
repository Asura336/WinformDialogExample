using System.Runtime.InteropServices;
using System.Text;

namespace FormConsole
{
    internal class InternalWindowHandle : IWin32Window
    {
        public InternalWindowHandle(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }

        public override string ToString()
        {
            int len = GetWindowTextLength(Handle);
            var sb = new StringBuilder(len + 4);
            _ = GetWindowText(Handle, sb, len + 4);
            return sb.ToString();
        }


        [DllImport("User32.dll")]
        static extern int GetWindowTextLength([In] IntPtr hWnd);

        [DllImport("User32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText([In] IntPtr hWnd, [Out] StringBuilder lpString, [In] int nMaxCount);
    }
}
