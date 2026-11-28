"""按窗口标题抓取窗口位图（PrintWindow，不受遮挡影响）。用法: python _grab_win.py 标题关键字 输出.png"""
import ctypes
import sys
from ctypes import wintypes

from PIL import Image

u = ctypes.windll.user32
gdi = ctypes.windll.gdi32

keyword = sys.argv[1]
out = sys.argv[2]

EnumProc = ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
hits = []


def cb(h, _):
    n = u.GetWindowTextLengthW(h)
    if n:
        buf = ctypes.create_unicode_buffer(n + 1)
        u.GetWindowTextW(h, buf, n + 1)
        if keyword in buf.value and u.IsWindowVisible(h):
            hits.append((h, buf.value))
    return True


u.EnumWindows(EnumProc(cb), 0)
if not hits:
    print("NOT_FOUND")
    sys.exit(1)

hwnd = hits[0][0]
rect = wintypes.RECT()
u.GetWindowRect(hwnd, ctypes.byref(rect))
w, h = rect.right - rect.left, rect.bottom - rect.top

hdc = u.GetWindowDC(hwnd)
mdc = gdi.CreateCompatibleDC(hdc)
bmp = gdi.CreateCompatibleBitmap(hdc, w, h)
gdi.SelectObject(mdc, bmp)
# PW_RENDERFULLCONTENT = 2（含 DirectComposition 内容）
ok = u.PrintWindow(hwnd, mdc, 2)

class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [("biSize", wintypes.DWORD), ("biWidth", wintypes.LONG), ("biHeight", wintypes.LONG),
                ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
                ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", wintypes.LONG),
                ("biYPelsPerMeter", wintypes.LONG), ("biClrUsed", wintypes.DWORD), ("biClrImportant", wintypes.DWORD)]

bi = BITMAPINFOHEADER()
bi.biSize = ctypes.sizeof(BITMAPINFOHEADER)
bi.biWidth = w
bi.biHeight = -h  # 负数 = 自上而下
bi.biPlanes = 1
bi.biBitCount = 32
bi.biCompression = 0

buf = ctypes.create_string_buffer(w * h * 4)
gdi.GetDIBits(mdc, bmp, 0, h, buf, ctypes.byref(bi), 0)
img = Image.frombuffer("RGBA", (w, h), buf, "raw", "BGRA", 0, 1).convert("RGB")
img.save(out)
gdi.DeleteObject(bmp)
gdi.DeleteDC(mdc)
u.ReleaseDC(hwnd, hdc)
print(f"title={hits[0][1]} size={w}x{h} printwindow={ok} saved={out}")
