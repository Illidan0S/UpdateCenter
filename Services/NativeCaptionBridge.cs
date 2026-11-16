using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
namespace UpdateCenter.Services;

// WindowChrome owns caption/resize/taskbar. HTMAXBUTTON opts into Snap on Win11,
// but custom buttons must also own the NC input: DefWindowProc would track and
// paint its own caption button at the system's (different) caption coordinates.
internal sealed class NativeCaptionBridge(Window window, Func<FrameworkElement> maximize)
{
    private bool _pressed;
    private bool _tracking;
    internal void Attach() => window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(Hook);
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return IntPtr.Zero;
        bool Over(bool client = false)
        {
            var button = maximize();
            var screen = new Point((short)(lParam.ToInt64() & 0xffff), (short)((lParam.ToInt64() >> 16) & 0xffff));
            if (client)
            {
                var point = new NativePoint { X = (int)screen.X, Y = (int)screen.Y };
                if (!ClientToScreen(hwnd, ref point)) return false;
                screen = new Point(point.X, point.Y);
            }
            return button.IsVisible && button.IsEnabled && new Rect(button.RenderSize).Contains(button.PointFromScreen(screen));
        }
        if (msg == 0x84 && Over()) { handled = true; return new IntPtr(9); } // HTMAXBUTTON
        if (msg == 0xA0 && wParam.ToInt32() == 9 && Over()) // WM_NCMOUSEMOVE
        {
            Visual(_pressed ? "SelectionBrush" : "SurfaceAltBrush");
            if (!_tracking)
            {
                var tracking = new MouseTracking { Size = (uint)Marshal.SizeOf<MouseTracking>(), Flags = 0x12, Window = hwnd }; // LEAVE | NONCLIENT
                _tracking = TrackMouseEvent(ref tracking);
            }
            handled = true;
        }
        if ((msg is 0xA1 or 0xA3) && wParam.ToInt32() == 9 && Over()) // NC down/double-click
        {
            _pressed = true; SetCapture(hwnd); Visual("SelectionBrush"); handled = true;
        }
        if (msg == 0x200 && _pressed) { Visual(Over(client: true) ? "SelectionBrush" : null); handled = true; }
        if ((msg == 0x202 && _pressed) || (msg == 0xA2 && (_pressed || wParam.ToInt32() == 9)))
        {
            var invoke = _pressed && Over(client: msg == 0x202);
            Cancel(hwnd); handled = true;
            if (invoke)
            {
                if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
                else SystemCommands.MaximizeWindow(window);
            }
        }
        if (msg == 0x2A2) { _tracking = false; if (!_pressed) Visual(null); } // NC leave
        if ((msg is 0x1F or 0x215) || (msg == 0x86 && wParam == IntPtr.Zero)) Cancel(hwnd);
        return IntPtr.Zero;
    }
    private void Visual(string? brush)
    {
        if (maximize() is not Control button) return;
        if (brush is null) button.ClearValue(Control.BackgroundProperty);
        else button.SetResourceReference(Control.BackgroundProperty, brush);
    }
    private void Cancel(IntPtr hwnd)
    {
        _pressed = false; Visual(null);
        if (GetCapture() == hwnd) ReleaseCapture();
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseTracking { public uint Size, Flags; public IntPtr Window; public uint HoverTime; }
    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TrackMouseEvent(ref MouseTracking tracking);
}
