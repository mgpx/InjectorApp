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
    public class EntryPoint : IEntryPoint
    {
        public EntryPoint(RemoteHooking.IContext ctx, string ch) { }
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
        [StructLayout(LayoutKind.Sequential)]
        private struct LOGBRUSH
        {
            public uint lbStyle;
            public uint lbColor; // COLORREF 0x00BBGGRR
            public IntPtr lbHatch;
        }

        // ===== Color Dialog (ChooseColor) =====
        [StructLayout(LayoutKind.Sequential)]
        private struct CHOOSECOLOR
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public uint rgbResult;
            public IntPtr lpCustColors;
            public uint Flags;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
        }

        private const int GWL_WNDPROC = -4;
        private const int GCLP_HBRBACKGROUND = -10;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const uint WM_ERASEBKGND = 0x0014;
        private const uint WM_PAINT = 0x000F;
        private const int WM_CTLCOLORSTATIC = 0x0138;
        private const int WM_CTLCOLORDLG = 0x0136;
        private const int TRANSPARENT = 1;
        private const int RGN_DIFF = 4;
        private const int GWL_STYLE = -16;
        private const int WM_COMMAND = 0x0111;
        private const uint CC_RGBINIT = 0x00000001;
        private const uint CC_FULLOPEN = 0x00000002;


        private const int WM_SETTEXT = 0x000C;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int VK_RETURN = 0x0D;


        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ChooseColorW")]
        private static extern bool ChooseColorW(ref CHOOSECOLOR cc);

        
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
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out LOGBRUSH lpvObject);
        [DllImport("gdi32.dll")] static extern uint SetBkColor(IntPtr hdc, uint crColor);
        [DllImport("user32.dll")] static extern IntPtr WindowFromDC(IntPtr hdc);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
        private static extern int MessageBoxW(IntPtr hWnd,string lpText,string lpCaption, uint uType);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

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
        private static int LOWORD(IntPtr value)
            => (int)((long)value & 0xFFFF);
        // ===== Estado geral =====
        private static uint s_color = 0xFFF5E6; // COLORREF 0x00BBGGRR (begezinho)
        private static IntPtr s_brush = IntPtr.Zero;
        private static int s_bevelWidth = 2;
        private IntPtr _mainPanel = IntPtr.Zero;
        private readonly HashSet<IntPtr> _hookedTopLevels = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, WndProcDelegate> _delegates = new Dictionary<IntPtr, WndProcDelegate>();
        private readonly Dictionary<IntPtr, IntPtr> _oldProc = new Dictionary<IntPtr, IntPtr>();
        private readonly HashSet<IntPtr> _classBrushApplied = new HashSet<IntPtr>();
        // Containers que pintam fundo no WM_ERASEBKGND
        private readonly string[] _eraseBgContainers = {
"TPANEL",
"TSCROLLBOX",
"TFRAME",
"TTABSHEET",
"TGROUPBOX",
"TPAGECONTROL",
"TSQLPANELGRID" // painéis da TSQLGrid
        };
        private readonly string[] _blacklist = {
"TEDIT", "TBUTTON", "TCHECKBOX", "TRADIOBUTTON",
"TLISTVIEW", "TCOMBOBOX", "TMEMO", "TLISTBOX",
"TSPEEDBUTTON", "TTOOLBAR", "TSTATUSBAR", "TMAINMENU"
};
        private readonly uint _myPid = (uint)Process.GetCurrentProcess().Id;

        // buffer estático para as 16 cores personalizadas do ChooseColor
        private static IntPtr s_customColors = IntPtr.Zero;

        // auto-login
        private static bool s_autoLoginDone = false;

        // flag por thread: estamos processando mensagem de um TFRel?
        [ThreadStatic]
        private static bool s_inTFRelPaint;

        private static volatile bool s_bypassWhenTFRelOpen = false;
        private void Log(string s)
        {
            try { System.IO.File.AppendAllText(@"C:\temp\payload_log.txt", DateTime.Now.ToString("s") + " " + s + Environment.NewLine); } catch { }
        }
        // ===== helpers de TFRel / região a ignorar =====
        // true se o HWND (ou algum pai) for TFRel
        private static bool IsInsideTFRelWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            var sb = new StringBuilder(128);
            IntPtr cur = hwnd;
            while (cur != IntPtr.Zero)
            {
                sb.Clear();
                GetClassName(cur, sb, sb.Capacity);
                string cls = sb.ToString().ToUpperInvariant();
                if (cls.StartsWith("TFREL"))
                    return true;
                cur = GetParent(cur);
            }
            return false;
        }
        // true se NÃO devemos mexer nesse HDC
        // - se estiver dentro de TFRel (flag de thread) -> ignora tudo
        // - se o HDC pertencer a uma janela TFRel ou filho -> ignora
        private static bool ShouldIgnoreDc(IntPtr hdc)
        {
            if (hdc == IntPtr.Zero)
                return true;
            // se estamos dentro do WndProc de TFRel nesta thread, ignora tudo
            if (s_inTFRelPaint)
                return true;
            IntPtr hwnd = WindowFromDC(hdc);
            // memory DC fora de TFRel: pode mexer
            if (hwnd == IntPtr.Zero)
                return false;
            // se a janela associada está em TFRel, não mexe
            return IsInsideTFRelWindow(hwnd);
        }
        // ===== Hook de GetSysColor (cores muito claras -> bege) =====
        private const int COLOR_WINDOW = 5;
        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
        private delegate uint GetSysColorDelegate(int nIndex);
        private static GetSysColorDelegate _getSysColorOriginal;
        private static LocalHook _getSysColorHook;
        private static uint GetSysColor_Hooked(int nIndex)
        {
            if (s_bypassWhenTFRelOpen)
                return _getSysColorOriginal(nIndex);

            try
            {
                uint orig = _getSysColorOriginal(nIndex);
                uint c = orig & 0x00FFFFFFu;
                if (c == 0x00FFFFFFu || c == 0x00FCFCFCu || c == 0x00F0F0F0u)
                    return s_color;
                return orig;
            }
            catch { return _getSysColorOriginal(nIndex); }
        }
        // ===== Hook de FillRect (branco -> bege, exceto TFRel) =====
        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
        private delegate int FillRectDelegate(IntPtr hdc, ref RECT lprc, IntPtr hbr);
        private static FillRectDelegate _fillRectOriginal;
        private static LocalHook _fillRectHook;
        private static int FillRect_Hooked(IntPtr hdc, ref RECT lprc, IntPtr hbr)
        {
            if (s_bypassWhenTFRelOpen)
                return _fillRectOriginal(hdc, ref lprc, hbr);

            try
            {
                if (hbr != IntPtr.Zero)
                {
                    if (GetObject(hbr, Marshal.SizeOf<LOGBRUSH>(), out LOGBRUSH lb) != 0)
                    {
                        uint color = lb.lbColor & 0x00FFFFFFu;
                        if (color == 0x00FFFFFFu || color == 0x00FCFCFCu || color == 0x00F0F0F0u)
                            return _fillRectOriginal(hdc, ref lprc, s_brush);
                    }
                }
            }
            catch { }
            return _fillRectOriginal(hdc, ref lprc, hbr);
        }
        // ===== Hook de SetBkColor (branco -> bege, exceto TFRel) =====
        [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)]
        private delegate uint SetBkColorDelegate(IntPtr hdc, uint crColor);
        private static SetBkColorDelegate _setBkColorOriginal;
        private static LocalHook _setBkColorHook;
        private static uint SetBkColor_Hooked(IntPtr hdc, uint crColor)
        {
            if (s_bypassWhenTFRelOpen)
                return _setBkColorOriginal(hdc, crColor);

            try
            {
                uint c = crColor & 0x00FFFFFFu;
                if (c == 0x00FFFFFFu || c == 0x00FCFCFCu || c == 0x00F0F0F0u)
                    crColor = s_color;
            }
            catch { }
            return _setBkColorOriginal(hdc, crColor);
        }
        // ===== EntryPoint principal =====
        public void Run(RemoteHooking.IContext ctx, string ch)
        {
            Log("Payload iniciado");
            // Carrega cor inicial do arquivo, se existir
            uint? init = TryGetColorFromFile();
            if (init.HasValue) s_color = init.Value;
            s_brush = CreateSolidBrush(s_color);
            // Hook GetSysColor
            try
            {
                IntPtr addr = LocalHook.GetProcAddress("user32.dll", "GetSysColor");
                _getSysColorOriginal = (GetSysColorDelegate)
                Marshal.GetDelegateForFunctionPointer(addr, typeof(GetSysColorDelegate));
                _getSysColorHook = LocalHook.Create(
                addr,
                new GetSysColorDelegate(GetSysColor_Hooked),
                null);
                _getSysColorHook.ThreadACL.SetExclusiveACL(new int[] { 0 });
                Log("Hook GetSysColor instalado com sucesso");
            }
            catch (Exception ex)
            {
                Log("Falha ao instalar hook GetSysColor: " + ex);
            }
            // Hook FillRect
            try
            {
                IntPtr addr = LocalHook.GetProcAddress("user32.dll", "FillRect");
                _fillRectOriginal = (FillRectDelegate)
                Marshal.GetDelegateForFunctionPointer(addr, typeof(FillRectDelegate));
                _fillRectHook = LocalHook.Create(
                addr,
                new FillRectDelegate(FillRect_Hooked),
                null);
                _fillRectHook.ThreadACL.SetExclusiveACL(new int[] { 0 });
                Log("Hook FillRect instalado com sucesso");
            }
            catch (Exception ex)
            {
                Log("Falha ao instalar hook FillRect: " + ex);
            }
            // Hook SetBkColor
            try
            {
                IntPtr addr = LocalHook.GetProcAddress("gdi32.dll", "SetBkColor");
                _setBkColorOriginal = (SetBkColorDelegate)
                Marshal.GetDelegateForFunctionPointer(addr, typeof(SetBkColorDelegate));
                _setBkColorHook = LocalHook.Create(
                addr,
                new SetBkColorDelegate(SetBkColor_Hooked),
                null);
                _setBkColorHook.ThreadACL.SetExclusiveACL(new int[] { 0 });
                Log("Hook SetBkColor instalado com sucesso");
            }
            catch (Exception ex)
            {
                Log("Falha ao instalar hook SetBkColor: " + ex);
            }
            // Procura um form top-level e começa a subclassear
            IntPtr first = WaitForTopLevelForm();
            if (first == IntPtr.Zero) { Log("Form não encontrado"); return; }
            HookTopLevel(first);

            //MenuManager.EnsureAjustesMenu(first, Log);

            int cycle = 0;
            while (true)
            {
                try
                {
                    cycle++;
                    // Recarrega cor de arquivo, se mudar em tempo de execução
                    uint? newColor = TryGetColorFromFile();
                    if (newColor.HasValue && newColor.Value != s_color)
                    {
                        Log($"COR ALTERADA → {newColor:X6}");
                        s_color = newColor.Value;
                        if (s_brush != IntPtr.Zero) DeleteObject(s_brush);
                        s_brush = CreateSolidBrush(s_color);
                        _classBrushApplied.Clear();
                        foreach (var f in _hookedTopLevels.Where(IsWindow))
                            InvalidateRect(f, IntPtr.Zero, true);
                    }
                    // Descobre novas janelas top-level do mesmo processo
                    // <<< DETECÇÃO DE TFRel ABERTO (nova parte) >>>
                    bool temTFRelAberto = false;
                    EnumWindows((h, _) =>
                    {
                        if (!IsWindowVisible(h)) return true;
                        GetWindowThreadProcessId(h, out uint pid);
                        if (pid != _myPid) return true;


                        // garante que o top-level esteja hookado
                        HookTopLevel(h);

                        // Garante que esse top-level tenha o menu Ajustes, se tiver menu principal
                        MenuManager.EnsureAjustesMenu(h, Log);

                        // Tenta auto-login se for o TFrLogin
                        TryAutoLoginIfLoginForm(h);

                        string cls = GetCls(h).ToUpperInvariant();
                        if (cls.Contains("FREL")) // cobre TFRel, TfrPreview, TFRelPreview, etc.
                        {
                            temTFRelAberto = true;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);

                    if (temTFRelAberto != s_bypassWhenTFRelOpen)
                    {
                        s_bypassWhenTFRelOpen = temTFRelAberto;
                        Log(temTFRelAberto ? "TFRel aberto → bypass total ativado" : "TFRel fechado → bege volta ao normal");

                        if (!temTFRelAberto)
                        {
                            // repinta tudo com bege quando fecha o relatório
                            foreach (var root in _hookedTopLevels.Where(IsWindow))
                                InvalidateRect(root, IntPtr.Zero, true);
                        }
                    }
                    // Descobre e atualiza o "main panel" maior
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
                    // Varre hierarquia para subclassear containers e aplicar brush
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


                if ((cls.StartsWith("TFORM") || cls.StartsWith("TFR") || cls.Contains("TFRINTERATIVO") || cls.StartsWith("TFREL")) && HasCaption(h))
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
            // se esse controle estiver dentro de um TFRel, não mexe
            if (IsInsideTFRelWindow(h)) return;
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
            // não trocar brush de nada que esteja em TFRel
            if (IsInsideTFRelWindow(h)) return;
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
        private const int IDM_CONFIG_COR = 0x9001;
        private IntPtr HookedProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            try
            {
                if (s_bypassWhenTFRelOpen && _oldProc.TryGetValue(h, out var old))
                    return CallWindowProc(old, h, msg, w, l);

                if (!IsWindow(h)) return IntPtr.Zero;

                // Trata comandos de menu antes de qualquer lógica de pintura
                if (msg == WM_COMMAND)
                {
                    int raw = LOWORD(w);              // agora vindo como 0..65535
                    ushort cmd = (ushort)raw;         // 16 bits sem sinal

                    //Log($"WM_COMMAND em hwnd=0x{h.ToInt64():X} cls={GetCls(h)} id={raw} (0x{cmd:X4})");

                    if (cmd == IDM_CONFIG_COR)        // IDM_CONFIG_COR = 0x9001
                    {
                        Log("WM_COMMAND de Config Cor capturado");
                        //MessageBoxW(h, "Você clicou", "Config Cor", 0);
                        ShowColorDialogAndSave(h);
                        return IntPtr.Zero; // já tratamos
                    }
                }



                string cls = GetCls(h).ToUpperInvariant();
                bool isTFRel = cls.StartsWith("TFREL");
                // TFRel: não mexe em nada, só marca a flag de que estamos dentro dele
                if (isTFRel && _oldProc.TryGetValue(h, out var oldRel))
                {
                    bool prev = s_inTFRelPaint;
                    s_inTFRelPaint = true;
                    try
                    {
                        return CallWindowProc(oldRel, h, msg, w, l);
                    }
                    finally
                    {
                        s_inTFRelPaint = prev;
                    }
                }
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
                    if (msg == WM_ERASEBKGND) return (IntPtr)1;
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
                if (_oldProc.TryGetValue(h, out var old2))
                    return CallWindowProc(old2, h, msg, w, l);
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Log("HookedProc error: " + ex);
                return IntPtr.Zero;
            }
        }


        private void ShowColorDialogAndSave(IntPtr owner)
        {
            try
            {
                // Inicializa o buffer de cores personalizadas uma vez
                if (s_customColors == IntPtr.Zero)
                {
                    s_customColors = Marshal.AllocHGlobal(sizeof(uint) * 16);
                    for (int i = 0; i < 16; i++)
                    {
                        // inicializa como branco
                        Marshal.WriteInt32(s_customColors, i * 4, unchecked((int)0x00FFFFFF));
                    }
                }

                CHOOSECOLOR cc = new CHOOSECOLOR();
                cc.lStructSize = Marshal.SizeOf<CHOOSECOLOR>();
                cc.hwndOwner = owner;
                cc.hInstance = IntPtr.Zero;
                cc.rgbResult = s_color;          // cor atual (COLORREF 0x00BBGGRR)
                cc.lpCustColors = s_customColors;
                cc.Flags = CC_RGBINIT | CC_FULLOPEN;

                if (!ChooseColorW(ref cc))
                {
                    // usuário cancelou
                    return;
                }

                uint chosen = cc.rgbResult & 0x00FFFFFFu;

                // Salva no arquivo para manter compatibilidade com sua lógica existente
                SaveColorToFile(chosen);

                // Já aplica imediatamente na memória também (sem esperar o loop)
                if (chosen != s_color)
                {
                    s_color = chosen;

                    if (s_brush != IntPtr.Zero)
                        DeleteObject(s_brush);

                    s_brush = CreateSolidBrush(s_color);

                    _classBrushApplied.Clear();

                    foreach (var root in _hookedTopLevels.Where(IsWindow).ToList())
                        InvalidateRect(root, IntPtr.Zero, true);
                }
            }
            catch (Exception ex)
            {
                Log("Erro ao exibir color dialog: " + ex);
            }
        }

        private void SaveColorToFile(uint color)
        {
            try
            {
                string path = @"C:\temp\bg_color.txt";

                int r = (int)(color & 0xFF);
                int g = (int)((color >> 8) & 0xFF);
                int b = (int)((color >> 16) & 0xFF);

                string hex = $"{r:X2}{g:X2}{b:X2}"; // mesmo formato: RRGGBB

                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);

                System.IO.File.WriteAllText(path, hex);

                Log($"Cor salva em arquivo: {hex}");
            }
            catch (Exception ex)
            {
                Log("Erro ao salvar cor em arquivo: " + ex);
            }
        }

        private void TryAutoLoginIfLoginForm(IntPtr hLogin)
        {
            try
            {
                if (s_autoLoginDone) return;
                if (!IsWindow(hLogin)) return;

                string cls = GetCls(hLogin).ToUpperInvariant();
                if (cls != "TFRLOGIN")
                    return;

                Log($"AutoLogin: detectado TFrLogin hwnd=0x{hLogin.ToInt64():X}");

                IntPtr hUser = IntPtr.Zero;
                IntPtr hPass = IntPtr.Zero;

                // procura os TSQLEd pelos estilos que você passou
                EnumChildWindows(hLogin, (child, _) =>
                {
                    if (!IsWindow(child) || !IsWindowVisible(child)) return true;

                    string ccls = GetCls(child).ToUpperInvariant();
                    if (ccls != "TSQLED")
                        return true;

                    long styleLong = GetWindowLongCompat(child, GWL_STYLE).ToInt64();
                    uint style = (uint)(styleLong & 0xFFFFFFFF);

                    // estilos
                    // usuário: 0x540000C2
                    // senha:   0x540000E8
                    if (style == 0x540000C2 && hUser == IntPtr.Zero)
                    {
                        hUser = child;
                        Log($"AutoLogin: TSQLEd usuário encontrado hwnd=0x{child.ToInt64():X} style=0x{style:X8}");
                    }
                    else if (style == 0x540000E8 && hPass == IntPtr.Zero)
                    {
                        hPass = child;
                        Log($"AutoLogin: TSQLEd senha encontrado   hwnd=0x{child.ToInt64():X} style=0x{style:X8}");
                    }

                    return true;
                }, IntPtr.Zero);

                if (hUser == IntPtr.Zero || hPass == IntPtr.Zero)
                {
                    Log("AutoLogin: não encontrou todos os edits (user/senha). Abortando.");
                    return;
                }

                // <<< USUÁRIO E SENHA FIXOS>>>
                const string USERNAME = "9999";
                const string PASSWORD = "INTERATIVO";

                SendMessage(hUser, WM_SETTEXT, IntPtr.Zero, USERNAME);
                SendMessage(hPass, WM_SETTEXT, IntPtr.Zero, PASSWORD);

                // foca no campo senha
                SetFocus(hPass);

                // simula Enter no campo senha
                //deixa desabilitado por enquanto
                //PostMessage(hPass, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                //PostMessage(hPass, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);

                s_autoLoginDone = true;
                Log("AutoLogin: usuário/senha preenchidos e Enter enviado.");
            }
            catch (Exception ex)
            {
                Log("AutoLogin error: " + ex);
            }
        }

    }
}