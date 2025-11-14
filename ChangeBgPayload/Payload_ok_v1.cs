using EasyHook;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ChangeBgPayload
{
    public class EntryPoint1 /*: IEntryPoint*/
    {
        public EntryPoint1(RemoteHooking.IContext ctx, string ch) { }

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
        private const int GWL_STYLE = -16;
        private const int GCLP_HBRBACKGROUND = -10;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const uint WM_ERASEBKGND = 0x0014;
        private const uint WM_PAINT = 0x000F;
        private const uint WM_PRINTCLIENT = 0x0318;
        private const int WM_CTLCOLORSTATIC = 0x0138;
        private const int WM_CTLCOLORDLG = 0x0136;
        private const int TRANSPARENT = 1;
        private const int RGN_DIFF = 4;
        private const uint DCX_CACHE = 0x00000002;
        private const uint DCX_CLIPSIBLINGS = 0x00000010;
        private const uint DCX_CLIPCHILDREN = 0x00000008;
        private const uint DCX_INTERSECTRGN = 0x00000080;

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
        [DllImport("user32.dll")] static extern IntPtr GetDCEx(IntPtr h, IntPtr hrgn, uint flags);
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
        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        static extern int GetWindowLong32(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        static extern int SetWindowLong32(IntPtr h, int i, int v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        static extern IntPtr SetWindowLongPtr64(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetClassLong", SetLastError = true)]
        static extern IntPtr SetClassLong32(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr", SetLastError = true)]
        static extern IntPtr SetClassLongPtr64(IntPtr h, int i, IntPtr v);

        static IntPtr GetWindowLongCompat(IntPtr h, int i)
            => IntPtr.Size == 8 ? GetWindowLongPtr64(h, i) : new IntPtr(GetWindowLong32(h, i));
        static IntPtr SetWindowLongCompat(IntPtr h, int i, IntPtr v)
            => IntPtr.Size == 8 ? SetWindowLongPtr64(h, i, v) : new IntPtr(SetWindowLong32(h, i, v.ToInt32()));
        static IntPtr SetClassBrushCompat(IntPtr h, int i, IntPtr v)
            => IntPtr.Size == 8 ? SetClassLongPtr64(h, i, v) : SetClassLong32(h, i, v);

        // ===== Estado =====
        private static uint s_color = 0xF8F5ED; /* 0xC2A67D; */ // COLORREF 0x00BBGGRR (para RRGGBB=E6F5FF)
        private static IntPtr s_brush = IntPtr.Zero;
        private static int s_bevelWidth = 2; // Ajuste este valor conforme o BevelWidth do seu TPanel (padrão é frequentemente 2 ou 1)
        private IntPtr _rootForm = IntPtr.Zero;
        private IntPtr _mainPanel = IntPtr.Zero;
        private readonly Dictionary<IntPtr, WndProcDelegate> _delegates = new Dictionary<IntPtr, WndProcDelegate>();
        private readonly Dictionary<IntPtr, IntPtr> _oldProc = new Dictionary<IntPtr, IntPtr>();
        private readonly HashSet<IntPtr> _classBrushDone = new HashSet<IntPtr>();
        private readonly string[] _containers = { "TPANEL", "TSCROLLBOX", "TFRAME", "TTABSHEET", "TGROUPBOX", "TPAGECONTROL", "TFORM" };
        private readonly string[] _blacklist = { "TEDIT", "TBUTTON", "TCHECKBOX", "TRADIOBUTTON", "TLISTVIEW", "TDBGRID", "TCOMBOBOX", "TMEMO", "TLISTBOX", "TSTRINGGRID", "TSPEEDBUTTON", "TTOOLBAR", "TSTATUSBAR", "TMAINMENU" };

        private void Log(string s)
        {
            try { System.IO.File.AppendAllText(@"C:\temp\payload_log.txt", DateTime.Now.ToString("s") + " " + s + Environment.NewLine); } catch { }
        }

        // ===== Run =====
        public void Run(RemoteHooking.IContext ctx, string ch)
        {
            uint? initialColor = TryGetColorFromFile();
            if (initialColor.HasValue)
            {
                s_color = initialColor.Value;
                Log($"Initial color loaded: {s_color:X6}");
            }
            s_brush = CreateSolidBrush(s_color);

            // Espera ativa pelo form real (ignora TApplication)
            _rootForm = WaitForTopLevelForm();
            if (_rootForm == IntPtr.Zero) { Log("Root form not found; exiting."); return; }
            Log($"RootForm=0x{_rootForm.ToInt64():X} class={GetCls(_rootForm)}");
            TrySubclass(_rootForm, isContainer: false, isMain: true);
            TrySetClassBrushOnce(_rootForm);
            // Encontra o maior TPanel uma vez
            _mainPanel = FindLargestPanel(_rootForm);
            if (_mainPanel != IntPtr.Zero)
            {
                Log($"MainPanel found: 0x{_mainPanel.ToInt64():X}");
            }
            else
            {
                Log("No TPanel found.");
            }
            int checkCounter = 0;
            // Varre e mantém
            while (IsWindow(_rootForm))
            {
                try
                {
                    checkCounter++;
                    if (checkCounter % 6 == 0) // Aproximadamente a cada 4.8s (800ms * 6)
                    {
                        uint? newColor = TryGetColorFromFile();
                        if (newColor.HasValue && newColor.Value != s_color)
                        {
                            Log($"Color changed from {s_color:X6} to {newColor.Value:X6}");
                            s_color = newColor.Value;
                            if (s_brush != IntPtr.Zero)
                            {
                                DeleteObject(s_brush);
                                s_brush = IntPtr.Zero;
                            }
                            s_brush = CreateSolidBrush(s_color);
                            // Reaplicar brush nas classes
                            foreach (var h in _classBrushDone)
                            {
                                SetClassBrushCompat(h, GCLP_HBRBACKGROUND, s_brush);
                                Log($"Re-set class brush on 0x{h.ToInt64():X}");
                            }
                            // Forçar repaint
                            InvalidateRect(_rootForm, IntPtr.Zero, true);
                            if (_mainPanel != IntPtr.Zero)
                            {
                                InvalidateRect(_mainPanel, IntPtr.Zero, true);
                            }
                        }
                    }
                    ScanAndHookAllFrom(_rootForm);
                    InvalidateRect(_rootForm, IntPtr.Zero, false);
                }
                catch (Exception ex) { Log("Loop " + ex); }
                Thread.Sleep(800);
            }
        }

        private IntPtr FindLargestPanel(IntPtr root)
        {
            IntPtr largest = IntPtr.Zero;
            long maxArea = 0;
            Action<IntPtr> scan = null;
            scan = (parent) =>
            {
                EnumChildWindows(parent, (h, l) =>
                {
                    if (IsWindow(h) && IsWindowVisible(h))
                    {
                        string up = GetCls(h).ToUpperInvariant();
                        if (up.Contains("TPANEL"))
                        {
                            GetClientRect(h, out RECT r);
                            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                            if (area > maxArea)
                            {
                                maxArea = area;
                                largest = h;
                                Log($"Found larger TPanel 0x{h.ToInt64():X} area={area}");
                            }
                        }
                        scan(h); // Recurse
                    }
                    return true;
                }, IntPtr.Zero);
            };
            scan(root);
            return largest;
        }

        // ===== Espera por TForm/TFr top-level =====
        private IntPtr WaitForTopLevelForm(int timeoutMs = 15000)
        {
            uint my = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            IntPtr found = IntPtr.Zero;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                found = FindTopLevelFormOnce(my);
                if (found != IntPtr.Zero) return found;
                if (sw.ElapsedMilliseconds % 2000 < 50) Log("Waiting for root form...");
                Thread.Sleep(100);
            }
            // Última tentativa
            return FindTopLevelFormOnce(my);
        }

        private IntPtr FindTopLevelFormOnce(uint myPid)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pid != myPid || !IsWindowVisible(h)) return true;
                string cls = GetCls(h).ToUpperInvariant();
                if (cls == "TAPPLICATION") return true; // ignora
                if ((cls.StartsWith("TFORM") || cls.StartsWith("TFR")) && HasCaption(h)) { result = h; return false; }
                // fallback: qualquer topo visível com caption
                if (HasCaption(h)) { result = h; return false; }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private bool HasCaption(IntPtr h)
        {
            var sb = new StringBuilder(256);
            return GetWindowText(h, sb, sb.Capacity) > 0;
        }

        // ===== Scan/Hook =====
        private void ScanAndHookAllFrom(IntPtr root)
        {
            EnumChildWindows(root, (h, l) =>
            {
                TryHookContainer(h);
                EnumChildWindows(h, (c, l2) => { TryHookContainer(c); return true; }, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        private void TryHookContainer(IntPtr h)
        {
            if (!IsWindow(h) || !IsWindowVisible(h)) return;
            string up = GetCls(h).ToUpperInvariant();
            foreach (var bad in _blacklist) if (up.Contains(bad)) return;
            bool isCont = false; foreach (var c in _containers) if (up.Contains(c)) { isCont = true; break; }
            if (!isCont) return;
            bool isPanel = up.Contains("TPANEL");
            if (isPanel && h != _mainPanel) return; // Skip other TPanels
            TrySubclass(h, isContainer: true, isMain: false);
            EnsureClipChildren(h);
            TrySetClassBrushOnce(h);
        }

        private void TrySubclass(IntPtr h, bool isContainer, bool isMain)
        {
            if (_oldProc.ContainsKey(h)) return;
            var del = new WndProcDelegate(HookedProc);
            IntPtr ptr = Marshal.GetFunctionPointerForDelegate(del);
            IntPtr old = SetWindowLongCompat(h, GWL_WNDPROC, ptr);
            if (old == IntPtr.Zero) { Log($"Subclass FAIL 0x{h.ToInt64():X} le={Marshal.GetLastWin32Error()} class={GetCls(h)}"); return; }
            _delegates[h] = del; _oldProc[h] = old;
            Log($"Subclass OK: 0x{h.ToInt64():X} class={GetCls(h)} isContainer={isContainer} isMain={isMain}");
            InvalidateRect(h, IntPtr.Zero, true); // força primeiro repaint
        }

        private void EnsureClipChildren(IntPtr h)
        {
            try
            {
                long style = GetWindowLongCompat(h, GWL_STYLE).ToInt64();
                if ((style & WS_CLIPCHILDREN) == 0)
                {
                    SetWindowLongCompat(h, GWL_STYLE, new IntPtr(style | WS_CLIPCHILDREN));
                    Log($"WS_CLIPCHILDREN set on 0x{h.ToInt64():X} class={GetCls(h)}");
                }
            }
            catch (Exception ex) { Log("EnsureClipChildren " + ex); }
        }

        private void TrySetClassBrushOnce(IntPtr h)
        {
            if (_classBrushDone.Contains(h)) return;
            try
            {
                IntPtr prev = SetClassBrushCompat(h, GCLP_HBRBACKGROUND, s_brush);
                _classBrushDone.Add(h);
                Log($"SetClassBrush hwnd=0x{h.ToInt64():X} prev=0x{prev.ToInt64():X} class={GetCls(h)}");
            }
            catch (Exception ex) { Log("TrySetClassBrushOnce " + ex); }
        }

        private string GetCls(IntPtr h)
        {
            var sb = new StringBuilder(128);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private string MsgName(uint msg)
        {
            if (msg == WM_ERASEBKGND) return "WM_ERASEBKGND";
            if (msg == WM_PAINT) return "WM_PAINT";
            if (msg == WM_PRINTCLIENT) return "WM_PRINTCLIENT";
            return $"0x{msg:X}";
        }

        // ===== Hook WndProc =====
        private IntPtr HookedProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            try
            {
                bool isMain = (h == _rootForm);
                string up = GetCls(h).ToUpperInvariant();
                bool isPanel = up.Contains("TPANEL");
                bool isContainer = IsContainerClass(up);
                if (isMain)
                {
                    // Form raiz: pinta ANTES (erase e paint), sem overlay depois
                    if (msg == WM_ERASEBKGND)
                    {
                        PaintFormErase(h, w);
                        return new IntPtr(1);
                    }
                    if (msg == WM_PAINT || msg == WM_PRINTCLIENT)
                    {
                        PaintFormPrePaint(h);
                        if (_oldProc.TryGetValue(h, out var oldF))
                            return CallWindowProc(oldF, h, msg, w, l);
                    }
                }
                else if (isContainer)
                {
                    // *** TRATAMENTO ESPECIAL PARA TPANEL ***
                    if (isPanel)
                    {
                        // Log de debug
                        if (msg == WM_ERASEBKGND || msg == WM_PAINT || msg == WM_PRINTCLIENT)
                        {
                            Log($"TPanel 0x{h.ToInt64():X} recebeu msg={MsgName(msg)}");
                        }
                        if (msg == WM_ERASEBKGND)
                        {
                            // NÃO pinta aqui - apenas indica que tratamos
                            Log($"TPanel 0x{h.ToInt64():X} WM_ERASEBKGND interceptado");
                            return new IntPtr(1);
                        }

                        if (msg == WM_PAINT)
                        {
                            Log($"TPanel 0x{h.ToInt64():X} WM_PAINT - iniciando pintura");

                            PAINTSTRUCT ps;
                            IntPtr hdc = BeginPaint(h, out ps);
                            try
                            {
                                GetClientRect(h, out RECT rc);
                                Log($"TPanel 0x{h.ToInt64():X} ClientRect: L={rc.Left} T={rc.Top} R={rc.Right} B={rc.Bottom}");

                                if (_oldProc.TryGetValue(h, out var oldP))
                                {
                                    CallWindowProc(oldP, h, WM_PAINT, hdc, IntPtr.Zero);
                                    Log($"TPanel 0x{h.ToInt64():X} Original WndProc called");
                                }

                                RECT inner = rc;
                                int bw = s_bevelWidth;
                                inner.Left += bw;
                                inner.Top += bw;
                                inner.Right -= bw;
                                inner.Bottom -= bw;
                                if (inner.Left >= inner.Right || inner.Top >= inner.Bottom)
                                {
                                    Log($"TPanel 0x{h.ToInt64():X} Área interna muito pequena, pulando pintura");
                                    return IntPtr.Zero;
                                }

                                IntPtr rgnClient = CreateRectRgn(inner.Left, inner.Top, inner.Right, inner.Bottom);

                                int childCount = 0;
                                EnumChildWindows(h, (child, lp) =>
                                {
                                    if (!IsWindow(child) || !IsWindowVisible(child)) return true;
                                    if (!GetWindowRect(child, out RECT rcChildScr)) return true;

                                    RECT rcChild = rcChildScr;
                                    MapWindowPoints(IntPtr.Zero, h, ref rcChild, 2);

                                    if (rcChild.Right <= inner.Left || rcChild.Bottom <= inner.Top || rcChild.Left >= inner.Right || rcChild.Top >= inner.Bottom) return true;

                                    IntPtr rgnChild = CreateRectRgn(rcChild.Left, rcChild.Top, rcChild.Right, rcChild.Bottom);
                                    CombineRgn(rgnClient, rgnClient, rgnChild, RGN_DIFF);
                                    DeleteObject(rgnChild);
                                    childCount++;
                                    return true;
                                }, IntPtr.Zero);

                                Log($"TPanel 0x{h.ToInt64():X} Filhos processados: {childCount}");

                                SelectClipRgn(hdc, rgnClient);
                                if (s_brush == IntPtr.Zero) s_brush = CreateSolidBrush(s_color);
                                FillRect(hdc, ref inner, s_brush);
                                SelectClipRgn(hdc, IntPtr.Zero);
                                DeleteObject(rgnClient);

                                Log($"TPanel 0x{h.ToInt64():X} Fundo pintado");
                            }
                            finally
                            {
                                EndPaint(h, ref ps);
                            }

                            return IntPtr.Zero;
                        }

                        if (msg == WM_PRINTCLIENT)
                        {
                            Log($"TPanel 0x{h.ToInt64():X} WM_PRINTCLIENT");
                            IntPtr hdc = w;
                            if (_oldProc.TryGetValue(h, out var oldP))
                            {
                                CallWindowProc(oldP, h, msg, w, l);
                            }
                            PaintWithClipping(h, hdc);
                            return IntPtr.Zero;
                        }
                    }
                    else
                    {
                        // Outros contêineres: ERASE-only com clipping
                        if (msg == WM_ERASEBKGND)
                        {
                            PaintEraseExcludingChildren(h, w);
                            return new IntPtr(1);
                        }
                    }
                    // CTLCOLOR para textos/owner-draw sem HWND
                    if (msg == WM_CTLCOLORSTATIC || msg == WM_CTLCOLORDLG)
                    {
                        try { SetBkMode(w, TRANSPARENT); } catch { }
                        return s_brush;
                    }
                }
                if (_oldProc.TryGetValue(h, out var def))
                    return CallWindowProc(def, h, msg, w, l);
            }
            catch (Exception ex) { Log("HookedProc " + ex); }
            return IntPtr.Zero;
        }

        private bool IsContainerClass(string up)
        {
            foreach (var b in _blacklist) if (up.Contains(b)) return false;
            foreach (var c in _containers) if (up.Contains(c)) return true;
            return false;
        }

        // ===== Helpers de pintura =====
        // Form/root — ERASE usando hdc do wParam se existir
        private void PaintFormErase(IntPtr h, IntPtr wParamHdc)
        {
            IntPtr hdc = wParamHdc;
            bool created = false;
            if (hdc == IntPtr.Zero) { hdc = GetDC(h); created = true; }
            GetClientRect(h, out RECT rc);
            if (s_brush == IntPtr.Zero) s_brush = CreateSolidBrush(s_color);
            FillRect(hdc, ref rc, s_brush);
            if (created) ReleaseDC(h, hdc);
        }

        // Form/root — PRE-PAINT com BeginPaint/EndPaint
        private void PaintFormPrePaint(IntPtr h)
        {
            PAINTSTRUCT ps;
            IntPtr hdc = BeginPaint(h, out ps);
            try
            {
                GetClientRect(h, out RECT rc);
                if (s_brush == IntPtr.Zero) s_brush = CreateSolidBrush(s_color);
                FillRect(hdc, ref rc, s_brush);
            }
            finally
            {
                EndPaint(h, ref ps);
            }
        }

        // ERASE (containers) — pinta só fundo livre (exclui filhos com HWND)
        private void PaintEraseExcludingChildren(IntPtr parent, IntPtr wParamHdc)
        {
            IntPtr hdc = wParamHdc;
            bool created = false;
            if (hdc == IntPtr.Zero) { hdc = GetDC(parent); created = true; }
            GetClientRect(parent, out RECT rcClient);
            IntPtr rgnClient = CreateRectRgn(rcClient.Left, rcClient.Top, rcClient.Right, rcClient.Bottom);
            EnumChildWindows(parent, (child, l) =>
            {
                if (!IsWindow(child) || !IsWindowVisible(child)) return true;
                if (!GetWindowRect(child, out RECT rcChildScr)) return true;
                RECT rcChild = rcChildScr;
                MapWindowPoints(IntPtr.Zero, parent, ref rcChild, 2);
                IntPtr rgnChild = CreateRectRgn(rcChild.Left, rcChild.Top, rcChild.Right, rcChild.Bottom);
                CombineRgn(rgnClient, rgnClient, rgnChild, RGN_DIFF);
                DeleteObject(rgnChild);
                return true;
            }, IntPtr.Zero);
            if (s_brush == IntPtr.Zero) s_brush = CreateSolidBrush(s_color);
            SelectClipRgn(hdc, rgnClient);
            FillRect(hdc, ref rcClient, s_brush);
            SelectClipRgn(hdc, IntPtr.Zero);
            DeleteObject(rgnClient);
            if (created) ReleaseDC(parent, hdc);
        }

        // Método simplificado para pintar com clipping (usado no PRINTCLIENT)
        private void PaintWithClipping(IntPtr parent, IntPtr hdc)
        {
            try
            {
                GetClientRect(parent, out RECT rcClient);
                RECT inner = rcClient;
                int bw = s_bevelWidth;
                inner.Left += bw;
                inner.Top += bw;
                inner.Right -= bw;
                inner.Bottom -= bw;
                if (inner.Left >= inner.Right || inner.Top >= inner.Bottom) return;

                IntPtr rgnClient = CreateRectRgn(inner.Left, inner.Top, inner.Right, inner.Bottom);

                EnumChildWindows(parent, (child, l) =>
                {
                    if (!IsWindow(child) || !IsWindowVisible(child)) return true;
                    if (!GetWindowRect(child, out RECT rcChildScr)) return true;

                    RECT rcChild = rcChildScr;
                    MapWindowPoints(IntPtr.Zero, parent, ref rcChild, 2);

                    if (rcChild.Right <= inner.Left || rcChild.Bottom <= inner.Top || rcChild.Left >= inner.Right || rcChild.Top >= inner.Bottom) return true;

                    IntPtr rgnChild = CreateRectRgn(rcChild.Left, rcChild.Top, rcChild.Right, rcChild.Bottom);
                    CombineRgn(rgnClient, rgnClient, rgnChild, RGN_DIFF);
                    DeleteObject(rgnChild);
                    return true;
                }, IntPtr.Zero);

                if (s_brush == IntPtr.Zero) s_brush = CreateSolidBrush(s_color);

                SelectClipRgn(hdc, rgnClient);
                FillRect(hdc, ref inner, s_brush);
                SelectClipRgn(hdc, IntPtr.Zero);

                DeleteObject(rgnClient);
            }
            catch (Exception ex)
            {
                Log("PaintWithClipping ex: " + ex);
            }
        }

        // ===== Cor via arquivo =====
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
    }
}