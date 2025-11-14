using EasyHook;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ChangeBgPayload
{
    public class EntryPoint2 /*: IEntryPoint*/
    {
        public EntryPoint2(RemoteHooking.IContext ctx, string ch) { }

        // ===== WinAPI =====
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT
        {
            public IntPtr hdc; public int fErase; public RECT rcPaint;
            public int fRestore; public int fIncUpdate;
            public byte r1, r2, r3, r4, r5, r6, r7, r8;
        }

        private const int GWL_WNDPROC = -4;
        private const int GCLP_HBRBACKGROUND = -10;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const uint WM_ERASEBKGND = 0x0014;
        private const uint WM_PAINT = 0x000F;
        private const uint WM_PRINTCLIENT = 0x0318;
        private const int WM_CTLCOLORSTATIC = 0x0138;
        private const int WM_CTLCOLORDLG = 0x0136;
        private const int TRANSPARENT = 1;
        private const int RGN_DIFF = 4;
        private const int GWL_STYLE = -16;

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern int MapWindowPoints(IntPtr from, IntPtr to, ref RECT r, int points);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr h, IntPtr rc, bool erase);
        [DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr prev, IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
        [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint c);
        [DllImport("user32.dll")] static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr hbr);
        [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
        [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int fn);
        [DllImport("gdi32.dll")] static extern int SelectClipRgn(IntPtr hdc, IntPtr rgn);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

        // Get/SetWindowLong compat
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] static extern int GetWindowLong32(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr h, int i, int v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetClassLong")] static extern IntPtr SetClassLong32(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")] static extern IntPtr SetClassLongPtr64(IntPtr h, int i, IntPtr v);

        static IntPtr GetWindowLongCompat(IntPtr h, int i)
            => IntPtr.Size == 8 ? GetWindowLongPtr64(h, i) : new IntPtr(GetWindowLong32(h, i));
        static IntPtr SetWindowLongCompat(IntPtr h, int i, IntPtr v)
            => IntPtr.Size == 8 ? SetWindowLongPtr64(h, i, v) : new IntPtr(SetWindowLong32(h, i, v.ToInt32()));
        static IntPtr SetClassBrushCompat(IntPtr h, int i, IntPtr v)
            => IntPtr.Size == 8 ? SetClassLongPtr64(h, i, v) : SetClassLong32(h, i, v);

        // ===== Estado =====
        private static uint s_color = 0xFFF5E6; // COLORREF 0x00BBGGRR
        private static IntPtr s_brush = IntPtr.Zero;
        private static int s_bevelWidth = 2;

        private IntPtr _mainPanel = IntPtr.Zero;

        private readonly HashSet<IntPtr> _hookedTopLevels = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, WndProcDelegate> _delegates = new Dictionary<IntPtr, WndProcDelegate>();
        private readonly Dictionary<IntPtr, IntPtr> _oldProc = new Dictionary<IntPtr, IntPtr>();
        private readonly HashSet<IntPtr> _classBrushApplied = new HashSet<IntPtr>();

        // Todos os containers que pintam fundo no WM_ERASEBKGND (inclui TSQLGRID)
        private readonly string[] _eraseBgContainers = { "TPANEL", "TSCROLLBOX", "TFRAME", "TTABSHEET", "TGROUPBOX", "TPAGECONTROL", "TSQLGRID", "TDBGRID", "TSTRINGGRID" };

        private readonly string[] _blacklist = { "TEDIT", "TBUTTON", "TCHECKBOX", "TRADIOBUTTON", "TLISTVIEW", "TCOMBOBOX", "TMEMO", "TLISTBOX", "TSPEEDBUTTON", "TTOOLBAR", "TSTATUSBAR", "TMAINMENU" };

        private readonly uint _myPid = (uint)Process.GetCurrentProcess().Id;

        private void Log(string s)
        {
            try { System.IO.File.AppendAllText(@"C:\temp\payload_log.txt", DateTime.Now.ToString("s") + " " + s + Environment.NewLine); } catch { }
        }

        public void Run(RemoteHooking.IContext ctx, string ch)
        {
            Log("Payload iniciado");

            uint? init = TryGetColorFromFile();
            if (init.HasValue) s_color = init.Value;
            s_brush = CreateSolidBrush(s_color);

            IntPtr first = WaitForTopLevelForm();
            if (first == IntPtr.Zero) { Log("Form não encontrado"); return; }
            HookTopLevel(first);

            int cycle = 0;
            while (true)
            {
                try
                {
                    cycle++;

                    uint? newColor = TryGetColorFromFile();
                    if (newColor.HasValue && newColor.Value != s_color)
                    {
                        Log($"COR ALTERADA → {newColor:X6}");
                        s_color = newColor.Value;
                        if (s_brush != IntPtr.Zero) DeleteObject(s_brush);
                        s_brush = CreateSolidBrush(s_color);
                        _classBrushApplied.Clear();
                        foreach (var f in _hookedTopLevels.Where(IsWindow)) InvalidateRect(f, IntPtr.Zero, true);
                    }

                    EnumWindows((h, _) =>
                    {
                        if (!IsWindow(h)) return true;
                        GetWindowThreadProcessId(h, out uint pid);
                        if (pid != _myPid || !IsWindowVisible(h) || _hookedTopLevels.Contains(h)) return true;
                        string cls = GetCls(h).ToUpperInvariant();
                        if (cls.StartsWith("TFORM") || cls.StartsWith("TFR") || cls.Contains("TFRINTERATIVO") || HasCaption(h))
                            HookTopLevel(h);
                        return true;
                    }, IntPtr.Zero);

                    if (cycle % 6 == 0)
                    {
                        IntPtr cand = FindLargestPanel();
                        if (cand != IntPtr.Zero && cand != _mainPanel)
                        {
                            Log($"NOVO MAIN PANEL 0x{cand.ToInt64():X}");
                            _mainPanel = cand;
                            if (IsWindow(_mainPanel)) InvalidateRect(_mainPanel, IntPtr.Zero, true);
                        }
                    }

                    foreach (var root in _hookedTopLevels.ToList())
                    {
                        if (IsWindow(root))
                        {
                            ScanAndHookAllFrom(root);
                            InvalidateRect(root, IntPtr.Zero, false);
                        }
                        else _hookedTopLevels.Remove(root);
                    }
                }
                catch (Exception ex) { Log("Loop error: " + ex); }
                Thread.Sleep(800);
            }
        }

        // ====================== FUNÇÕES AUXILIARES ======================
        private IntPtr WaitForTopLevelForm(int timeoutMs = 20000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                IntPtr f = FindTopLevelFormOnce(_myPid);
                if (f != IntPtr.Zero) return f;
                Thread.Sleep(100);
            }
            return FindTopLevelFormOnce(_myPid);
        }

        private IntPtr FindTopLevelFormOnce(uint pid)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h)) return true;
                GetWindowThreadProcessId(h, out uint p);
                if (p != pid) return true;
                string cls = GetCls(h).ToUpperInvariant();
                if (cls == "TAPPLICATION") return true;
                if ((cls.StartsWith("TFORM") || cls.StartsWith("TFR") || cls.Contains("TFRINTERATIVO")) && HasCaption(h))
                {
                    result = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private bool HasCaption(IntPtr h)
        {
            var sb = new StringBuilder(256);
            return GetWindowText(h, sb, sb.Capacity) > 0;
        }

        private string GetCls(IntPtr h)
        {
            var sb = new StringBuilder(128);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private uint? TryGetColorFromFile()
        {
            try
            {
                string path = @"C:\temp\bg_color.txt";
                if (!System.IO.File.Exists(path)) return null;
                var t = System.IO.File.ReadAllText(path).Trim();
                if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
                if (t.Length == 6)
                {
                    int r = Convert.ToInt32(t.Substring(0, 2), 16);
                    int g = Convert.ToInt32(t.Substring(2, 2), 16);
                    int b = Convert.ToInt32(t.Substring(4, 2), 16);
                    return (uint)(r | (g << 8) | (b << 16)); // COLORREF 0x00BBGGRR
                }
            }
            catch { }
            return null;
        }

        private IntPtr FindLargestPanel()
        {
            IntPtr largest = IntPtr.Zero;
            long maxArea = 0;
            foreach (var root in _hookedTopLevels)
            {
                if (!IsWindow(root)) continue;
                EnumChildWindows(root, (h, _) =>
                {
                    if (!IsWindowVisible(h)) return true;
                    string cls = GetCls(h).ToUpperInvariant();
                    if (cls.Contains("TPANEL"))
                    {
                        GetClientRect(h, out RECT r);
                        long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                        if (area > maxArea)
                        {
                            maxArea = area;
                            largest = h;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            return largest;
        }

        private void HookTopLevel(IntPtr h)
        {
            if (!IsWindow(h) || _hookedTopLevels.Contains(h)) return;
            _hookedTopLevels.Add(h);
            TrySubclass(h, false, true);
            TrySetClassBrushOnce(h);
            Log($"Hooked top-level 0x{h.ToInt64():X} {GetCls(h)}");
        }

        private void ScanAndHookAllFrom(IntPtr root)
        {
            if (!IsWindow(root)) return;
            EnumChildWindows(root, (h, _) =>
            {
                TryHookContainer(h);
                EnumChildWindows(h, (c, l2) => { TryHookContainer(c); return true; }, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        private void TryHookContainer(IntPtr h)
        {
            if (!IsWindow(h) || !IsWindowVisible(h)) return;
            string cls = GetCls(h).ToUpperInvariant();
            if (_blacklist.Any(b => cls.Contains(b))) return;
            if (!_eraseBgContainers.Any(c => cls.Contains(c))) return;

            TrySubclass(h, true, false);
            EnsureClipChildren(h);
            TrySetClassBrushOnce(h);
        }

        private void TrySubclass(IntPtr h, bool isContainer, bool isMain)
        {
            if (_oldProc.ContainsKey(h) || !IsWindow(h)) return;

            var del = new WndProcDelegate(HookedProc);
            IntPtr ptr = Marshal.GetFunctionPointerForDelegate(del);
            IntPtr old = SetWindowLongCompat(h, GWL_WNDPROC, ptr);
            if (old == IntPtr.Zero) return;

            _delegates[h] = del;
            _oldProc[h] = old;
            InvalidateRect(h, IntPtr.Zero, true);
        }

        private void EnsureClipChildren(IntPtr h)
        {
            if (!IsWindow(h)) return;
            try
            {
                long style = GetWindowLongCompat(h, GWL_STYLE).ToInt64();
                if ((style & WS_CLIPCHILDREN) == 0)
                    SetWindowLongCompat(h, GWL_STYLE, new IntPtr(style | WS_CLIPCHILDREN));
            }
            catch { }
        }

        private void TrySetClassBrushOnce(IntPtr h)
        {
            if (_classBrushApplied.Contains(h) || !IsWindow(h)) return;
            try
            {
                SetClassBrushCompat(h, GCLP_HBRBACKGROUND, s_brush);
                _classBrushApplied.Add(h);
            }
            catch { }
        }

        // ===== Pintura =====
        private void PaintFormErase(IntPtr h, IntPtr dcParam)
        {
            IntPtr hdc = dcParam != IntPtr.Zero ? dcParam : GetDC(h);
            bool created = dcParam == IntPtr.Zero;
            GetClientRect(h, out RECT rc);
            FillRect(hdc, ref rc, s_brush);
            if (created) ReleaseDC(h, hdc);
        }

        private void PaintEraseExcludingChildren(IntPtr h, IntPtr dcParam)
        {
            IntPtr hdc = dcParam != IntPtr.Zero ? dcParam : GetDC(h);
            bool created = dcParam == IntPtr.Zero;
            GetClientRect(h, out RECT rc);
            IntPtr rgn = CreateRectRgn(rc.Left, rc.Top, rc.Right, rc.Bottom);

            EnumChildWindows(h, (child, _) =>
            {
                if (!IsWindowVisible(child)) return true;
                GetWindowRect(child, out RECT rcChild);
                MapWindowPoints(IntPtr.Zero, h, ref rcChild, 2);
                IntPtr rgnChild = CreateRectRgn(rcChild.Left, rcChild.Top, rcChild.Right, rcChild.Bottom);
                CombineRgn(rgn, rgn, rgnChild, RGN_DIFF);
                DeleteObject(rgnChild);
                return true;
            }, IntPtr.Zero);

            SelectClipRgn(hdc, rgn);
            FillRect(hdc, ref rc, s_brush);
            SelectClipRgn(hdc, IntPtr.Zero);
            DeleteObject(rgn);
            if (created) ReleaseDC(h, hdc);
        }

        private void PaintMainPanelPost(IntPtr h, IntPtr hdc)
        {
            GetClientRect(h, out RECT rc);
            RECT inner = rc;
            inner.Left += s_bevelWidth;
            inner.Top += s_bevelWidth;
            inner.Right -= s_bevelWidth;
            inner.Bottom -= s_bevelWidth;
            if (inner.Left >= inner.Right || inner.Top >= inner.Bottom) return;

            IntPtr rgn = CreateRectRgn(inner.Left, inner.Top, inner.Right, inner.Bottom);

            EnumChildWindows(h, (child, _) =>
            {
                if (!IsWindowVisible(child)) return true;
                GetWindowRect(child, out RECT rcChild);
                MapWindowPoints(IntPtr.Zero, h, ref rcChild, 2);
                IntPtr rgnChild = CreateRectRgn(rcChild.Left, rcChild.Top, rcChild.Right, rcChild.Bottom);
                CombineRgn(rgn, rgn, rgnChild, RGN_DIFF);
                DeleteObject(rgnChild);
                return true;
            }, IntPtr.Zero);

            SelectClipRgn(hdc, rgn);
            FillRect(hdc, ref inner, s_brush);
            SelectClipRgn(hdc, IntPtr.Zero);
            DeleteObject(rgn);
        }

        // ===== WndProc =====
        private IntPtr HookedProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            try
            {
                if (!IsWindow(h)) return IntPtr.Zero;

                string cls = GetCls(h).ToUpperInvariant();
                bool isTopLevel = _hookedTopLevels.Contains(h);
                bool isMainPanel = (h == _mainPanel);
                bool isEraseContainer = _eraseBgContainers.Any(c => cls.Contains(c));

                if (isTopLevel && msg == WM_ERASEBKGND)
                {
                    PaintFormErase(h, w);
                    return (IntPtr)1;
                }

                if (isMainPanel)
                {
                    if (msg == WM_ERASEBKGND) return (IntPtr)1; // não pinta no erase (bevel vem depois)

                    if (msg == WM_PAINT)
                    {
                        PAINTSTRUCT ps;
                        IntPtr hdc = BeginPaint(h, out ps);
                        try
                        {
                            if (_oldProc.TryGetValue(h, out var oldP))
                                CallWindowProc(oldP, h, WM_PAINT, hdc, IntPtr.Zero);
                            PaintMainPanelPost(h, hdc);
                        }
                        finally { EndPaint(h, ref ps); }
                        return IntPtr.Zero;
                    }
                }
                else if (isEraseContainer && msg == WM_ERASEBKGND)
                {
                    PaintEraseExcludingChildren(h, w);
                    return (IntPtr)1;
                }

                if (msg == WM_CTLCOLORSTATIC || msg == WM_CTLCOLORDLG)
                {
                    SetBkMode(w, TRANSPARENT);
                    return s_brush;
                }

                if (_oldProc.TryGetValue(h, out var old))
                    return CallWindowProc(old, h, msg, w, l);

                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Log("HookedProc error: " + ex);
                return IntPtr.Zero;
            }
        }
    }
}