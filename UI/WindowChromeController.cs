using System.Runtime.InteropServices;

namespace VeditorWindow.UI;

internal enum WindowChromeHitTarget
{
    Client = 1,
    Caption = 2,
    Minimize = 8,
    Maximize = 9,
    Left = 10,
    Right = 11,
    Top = 12,
    TopLeft = 13,
    TopRight = 14,
    Bottom = 15,
    BottomLeft = 16,
    BottomRight = 17,
    Close = 20
}

internal static class WindowChromeController
{
    internal const int WmSize = 0x0005;
    internal const int WmGetMinMaxInfo = 0x0024;
    internal const int WmNcHitTest = 0x0084;
    internal const int WmNcLButtonUp = 0x00A2;
    internal const int WmNcRButtonUp = 0x00A5;
    internal const int SizeMinimized = 1;

    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmWindowCornerRound = 2;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const uint MonitorDefaultToNearest = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        internal NativePoint Reserved;
        internal NativePoint MaxSize;
        internal NativePoint MaxPosition;
        internal NativePoint MinTrackSize;
        internal NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal int Size;
        internal Rectangle Monitor;
        internal Rectangle WorkArea;
        internal uint Flags;
    }

    internal static WindowChromeHitTarget CalculateHitTarget(
        Size clientSize,
        Point point,
        int resizeBorder,
        int titleBarHeight,
        Rectangle minimizeBounds,
        Rectangle maximizeBounds,
        Rectangle closeBounds,
        bool maximized)
    {
        //== native window hit testing ========================================
        if (closeBounds.Contains(point))
        {
            return WindowChromeHitTarget.Close;
        }

        if (maximizeBounds.Contains(point))
        {
            return WindowChromeHitTarget.Maximize;
        }

        if (minimizeBounds.Contains(point))
        {
            return WindowChromeHitTarget.Minimize;
        }

        if (!maximized)
        {
            var onLeft = point.X < resizeBorder;
            var onRight = point.X >= clientSize.Width - resizeBorder;
            var onTop = point.Y < resizeBorder;
            var onBottom = point.Y >= clientSize.Height - resizeBorder;

            if (onTop && onLeft) return WindowChromeHitTarget.TopLeft;
            if (onTop && onRight) return WindowChromeHitTarget.TopRight;
            if (onBottom && onLeft) return WindowChromeHitTarget.BottomLeft;
            if (onBottom && onRight) return WindowChromeHitTarget.BottomRight;
            if (onLeft) return WindowChromeHitTarget.Left;
            if (onRight) return WindowChromeHitTarget.Right;
            if (onTop) return WindowChromeHitTarget.Top;
            if (onBottom) return WindowChromeHitTarget.Bottom;
        }

        return point.Y < titleBarHeight
            ? WindowChromeHitTarget.Caption
            : WindowChromeHitTarget.Client;
        //=====================================================================
    }

    internal static void ApplyDwmAttributes(Form form)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        var cornerPreference = DwmWindowCornerRound;
        _ = DwmSetWindowAttribute(
            form.Handle,
            DwmWindowCornerPreference,
            ref cornerPreference,
            Marshal.SizeOf<int>());

        var borderColor = DwmColorNone;
        _ = DwmSetWindowAttribute(
            form.Handle,
            DwmBorderColor,
            ref borderColor,
            Marshal.SizeOf<uint>());
    }

    internal static Point GetPointFromMessageLParam(IntPtr lParam)
    {
        var value = unchecked((long)lParam);
        return new Point(unchecked((short)(value & 0xFFFF)), unchecked((short)((value >> 16) & 0xFFFF)));
    }

    internal static void FlushComposedFrame()
    {
        //== desktop composition synchronization =============================
        if (OperatingSystem.IsWindowsVersionAtLeast(6))
        {
            _ = DwmFlush();
        }
        //=====================================================================
    }

    internal static void ApplyMaximizedWorkingArea(Form form, IntPtr minMaxInfoPointer)
    {
        //== maximized window bounds ==========================================
        var monitor = MonitorFromWindow(form.Handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(minMaxInfoPointer);
        minMaxInfo.MaxPosition.X = monitorInfo.WorkArea.Left - monitorInfo.Monitor.Left;
        minMaxInfo.MaxPosition.Y = monitorInfo.WorkArea.Top - monitorInfo.Monitor.Top;
        minMaxInfo.MaxSize.X = monitorInfo.WorkArea.Width;
        minMaxInfo.MaxSize.Y = monitorInfo.WorkArea.Height;
        minMaxInfo.MaxTrackSize = minMaxInfo.MaxSize;
        Marshal.StructureToPtr(minMaxInfo, minMaxInfoPointer, fDeleteOld: false);
        //=====================================================================
    }

    internal static void ShowSystemMenu(Form form, Point screenPoint)
    {
        var menu = GetSystemMenu(form.Handle, false);
        if (menu == IntPtr.Zero)
        {
            return;
        }

        var command = TrackPopupMenuEx(menu, 0x0100, screenPoint.X, screenPoint.Y, form.Handle, IntPtr.Zero);
        if (command != 0)
        {
            _ = SendMessage(form.Handle, 0x0112, (IntPtr)command, IntPtr.Zero);
        }
    }

    internal static void BeginWindowDrag(Form form)
    {
        //== native window movement ==========================================
        // Nested WinForms controls can consume the client-area mouse message
        // before borderless chrome is treated as a caption by Windows.
        ReleaseCapture();
        _ = SendMessage(
            form.Handle,
            0x00A1,
            (IntPtr)(int)WindowChromeHitTarget.Caption,
            IntPtr.Zero);
        //=====================================================================
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref uint value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool revert);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr hMenu,
        uint flags,
        int x,
        int y,
        IntPtr hWnd,
        IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();
}
