using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DashboardKiosk;

/// <summary>
/// 全局键盘/鼠标钩子：
/// - Esc / F11 在 WebView2 获得焦点时也能可靠响应（WebView2 会吞掉普通消息过滤器的按键）
/// - 双击（两次左键快速点击）用于切换全屏
/// </summary>
public class GlobalHooks : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F11 = 0x7A;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr extraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public Point pt; public uint mouseData; public uint flags; public uint time; public IntPtr extraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    public Action<int>? OnKeyDown;         // 虚拟键码
    public Action<int, int>? OnLButtonDown; // x, y

    private IntPtr _kbHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;
    private readonly HookProc _kbProc;
    private readonly HookProc _mouseProc;

    private DateTime _lastClick = DateTime.MinValue;
    private System.Drawing.Point _lastClickPos;

    public GlobalHooks()
    {
        _kbProc = KeyboardProc;
        _mouseProc = MouseProc;
        _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, GetModuleHandle(null), 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
        if (_kbHook == IntPtr.Zero) Logger.Log("键盘钩子安装失败: " + Marshal.GetLastWin32Error());
        if (_mouseHook == IntPtr.Zero) Logger.Log("鼠标钩子安装失败: " + Marshal.GetLastWin32Error());
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            var s = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            OnKeyDown?.Invoke((int)s.vkCode);
        }
        return CallNextHookEx(_kbHook, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
        {
            var s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var now = DateTime.Now;
            var pos = new System.Drawing.Point(s.pt.X, s.pt.Y);
            var dt = (now - _lastClick).TotalMilliseconds;
            var dist = Math.Abs(pos.X - _lastClickPos.X) + Math.Abs(pos.Y - _lastClickPos.Y);
            if (dt <= SystemInformation.DoubleClickTime && dist < 16)
            {
                OnLButtonDown?.Invoke(pos.X, pos.Y); // 双击的第二下
            }
            _lastClick = now;
            _lastClickPos = pos;
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
    }
}
