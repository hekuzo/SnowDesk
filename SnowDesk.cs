// SnowDesk — снег на рабочем столе Windows.
// Снег падает поверх всего экрана, копится сугробами на верхних краях окон,
// на панели задач и внизу экрана. Если окно двигают, меняют размер,
// сворачивают или закрывают — снег с него осыпается вниз (на панель задач).
//
// Клик по снежинке в трее — панель управления.
// Режим игры (Ctrl+Alt+S или в панели):
//   ЛКМ по сугробу      — слепить снежок; тащите его по снегу — он растёт
//   ЛКМ по снежку       — поднять; отпустить — уронить, резко дёрнуть — бросить
//   снежок на снежок    — получится снеговик (2 шара — лицо, 3 — шляпа, шарф, руки)
//   ПКМ по сугробу      — подбросить снег, ПКМ по снежку — разбить
//   брошенный за край экрана снежок вылетает с другой стороны
//
// Файлы: SnowDesk.cs (снег и физика), SnowPainter.cs (снеговик), ControlPanel.cs (панель)
// Сборка: build.bat (нужен только встроенный в Windows .NET Framework 4.x)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SnowDesk
{
    static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int flags);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint p);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint p);
    }

    class Flake
    {
        public float X, Y, VX, VY, Size, Phase, Depth, Mass;
        public bool Clump, Dead;
    }

    // Сугроб на верхнем крае одного окна (или на "земле" внизу монитора)
    class Pile
    {
        public Rectangle R;
        public float[] H;      // высота снега по столбцам (px)
        public float MaxH;
        public float Corner;   // радиус скругления углов окна (Windows 11)
        public bool Ground, Taskbar, Seen, Alive = true;
        public float OvL, OvR; // снег, съехавший с левого/правого края
        public float TopY { get { return Ground ? R.Bottom : R.Top; } }
    }

    // Снежок / шар снеговика
    class Ball
    {
        public const int Falling = 0, Resting = 1, Held = 2;
        public float X, Y, R, VX, VY, Area, Angle;
        public int State;
        public Pile RestPile;
        public Rectangle RestRect;
        public Ball RestBall, Above;
        public bool Dead, Hidden, Thrown;
        public int Role; // 0 — просто шар, 1 — голова, 2 — туловище с руками, 3 — голова со шляпой
        public BallLook Look;
    }

    struct Seg { public Pile P; public int X1, X2, Y; }
    struct Win { public IntPtr H; public Rectangle R; public bool Taskbar; }

    class SnowForm : Form
    {
        const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
                  WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x08000000, WS_CAPTION = 0x00C00000;
        const int HotkeyId = 0x5157;

        Rectangle vs;
        Rectangle[] screens = new Rectangle[0];
        float scale = 1f;
        int col = 2;
        float slope;
        float cornerRadius;

        IntPtr memDC, dib, oldBmp, bits;
        Bitmap bmp;
        Graphics g;
        Bitmap[] sprites;
        const float SpriteMin = 1f, SpriteStep = 0.5f;

        readonly List<Flake> flakes = new List<Flake>();
        readonly List<Ball> balls = new List<Ball>();
        readonly Dictionary<IntPtr, Pile> piles = new Dictionary<IntPtr, Pile>();
        readonly List<Pile> groundPiles = new List<Pile>();
        readonly List<Win> wins = new List<Win>();
        readonly List<Seg> segs = new List<Seg>();
        List<int> iv = new List<int>(), ivTmp = new List<int>();
        readonly List<IntPtr> removeKeys = new List<IntPtr>();
        readonly StringBuilder clsBuf = new StringBuilder(256);
        readonly List<PointF> top = new List<PointF>();
        readonly List<PointF> bottom = new List<PointF>();
        readonly List<PointF> run = new List<PointF>();
        float[] disp = new float[2048], dtmp = new float[2048];
        NativeMethods.EnumWindowsProc enumProc;
        readonly Random rnd = new Random();
        readonly Comparison<Ball> byDepth = delegate (Ball a, Ball b) { return b.Y.CompareTo(a.Y); };

        System.Windows.Forms.Timer timer;
        readonly Stopwatch sw = new Stopwatch();
        double lastT;
        float t, wind;
        int topmostCounter;
        IntPtr myHandle;

        NotifyIcon tray;
        IntPtr hIcon;
        ControlPanel panel;

        int intensity = 1;          // 0 лёгкий, 1 обычный, 2 метель
        bool snowing = true, hideFullscreen = true, hiddenNow, initialFill = true;
        bool drifts = true;         // копится ли снег сугробами
        float accum = 2.2f;         // скорость накопления сугробов
        float windUser = 1f;        // сила ветра (множитель)
        bool firstRun;
        static readonly int[] FlakesPer1080p = { 170, 400, 950 };
        static readonly float[] WindMul = { 0.6f, 1f, 2.4f };
        static readonly float[] FallMul = { 0.9f, 1f, 1.45f };

        // интерактив
        bool playMode, interactive;
        Ball held;
        float curX, curY, curVX, curVY;
        bool curInit;

        Pen outlinePen;
        readonly Color pileTop = Color.FromArgb(252, 255, 255, 255);
        readonly Color pileBottom = Color.FromArgb(252, 210, 226, 247);

        // режим самопроверки: --bench
        bool bench;
        int benchFrames;
        double benchWork;
        string benchWrapLog = "";
        public bool BenchPending;

        public SnowForm(bool bench)
        {
            this.bench = bench;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(0, 0);
            Size = new Size(1, 1);
            TopMost = true;
            Text = "SnowDesk";

            try { scale = NativeMethods.GetDpiForSystem() / 96f; } catch { scale = 1f; }
            if (scale < 1f) scale = 1f;
            col = Math.Max(2, (int)Math.Round(2 * scale));
            slope = 0.42f * col;
            cornerRadius = IsWindows11() ? 8f * scale : 0f;

            outlinePen = new Pen(Color.FromArgb(150, 130, 155, 195), Math.Max(1f, scale));
            outlinePen.LineJoin = LineJoin.Round;

            if (!bench) LoadSettings();
            enumProc = OnEnumWindow;
            BuildSprites();
            SetupBuffers();
            panel = new ControlPanel(this, scale);
            SetupTray();
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        }

        public static bool IsWindows11()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    int build;
                    return k != null && int.TryParse(Convert.ToString(k.GetValue("CurrentBuildNumber")), out build) && build >= 22000;
                }
            }
            catch { return false; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            myHandle = Handle;
            // Ctrl+Alt+S — режим игры со снегом
            NativeMethods.RegisterHotKey(myHandle, HotkeyId, 0x0002 | 0x0001 | 0x4000, 0x53);
            NativeMethods.timeBeginPeriod(1);
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 15;
            timer.Tick += OnTick;
            sw.Start();
            timer.Start();
            if (firstRun)
                tray.ShowBalloonTip(5000, "SnowDesk запущен",
                    "Нажмите на снежинку в трее, чтобы открыть панель. Ctrl+Alt+S — играть со снегом.", ToolTipIcon.Info);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyId) SetPlayMode(!playMode, true);
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (timer != null) timer.Stop();
            NativeMethods.UnregisterHotKey(myHandle, HotkeyId);
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (hIcon != IntPtr.Zero) NativeMethods.DestroyIcon(hIcon);
            if (panel != null) panel.Dispose();
            NativeMethods.timeEndPeriod(1);
            FreeBuffers();
            base.OnFormClosed(e);
        }

        // ------------------------------------------------------- API для панели

        public bool PlayMode { get { return playMode; } }
        public int Intensity { get { return intensity; } set { intensity = Math.Max(0, Math.Min(2, value)); SaveSettings(); } }
        public bool Snowing { get { return snowing; } set { snowing = value; SaveSettings(); } }
        public bool HideFullscreen { get { return hideFullscreen; } set { hideFullscreen = value; SaveSettings(); } }
        public bool Drifts
        {
            get { return drifts; }
            set
            {
                if (drifts == value) return;
                drifts = value;
                if (!drifts)
                {
                    // сугробы осыпаются и улетают вниз
                    foreach (Pile p in piles.Values) Spill(p);
                    foreach (Pile p in groundPiles) Array.Clear(p.H, 0, p.H.Length);
                }
                SaveSettings();
            }
        }
        public bool Autostart { get { return IsAutostart(); } set { SetAutostart(value); } }
        public float AccumRate { get { return accum; } set { accum = Math.Max(0.5f, Math.Min(6f, value)); SaveSettings(); } }
        public float WindRate { get { return windUser; } set { windUser = Math.Max(0f, Math.Min(3f, value)); SaveSettings(); } }
        public int BallCount { get { return balls.Count; } }
        public int SnowmanCount
        {
            get
            {
                int n = 0;
                foreach (Ball b in balls) if (b.Role == 1 || b.Role == 3) n++;
                return n;
            }
        }
        public void ExitApp() { Close(); }

        public void SetPlayMode(bool on, bool tip)
        {
            playMode = on;
            if (!on && held != null) ReleaseHeld();
            if (!tip) return;
            if (on)
                tray.ShowBalloonTip(6000, "Режим игры со снегом",
                    "ЛКМ по сугробу — слепить снежок, катайте его по снегу — растёт.\n" +
                    "Положите шар на шар — будет снеговик. Дёрните мышь — бросок.\n" +
                    "ПКМ — подбросить снег / разбить снежок. Выход: Ctrl+Alt+S",
                    ToolTipIcon.Info);
            else
                tray.ShowBalloonTip(2000, "SnowDesk", "Режим игры выключен — снег снова не мешает кликам.", ToolTipIcon.Info);
        }

        // ------------------------------------------------------------ настройки

        static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnowDesk", "settings.txt"); }
        }

        void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) { firstRun = true; return; }
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    float f;
                    bool fok = float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
                    switch (k)
                    {
                        case "intensity": if (fok) intensity = Math.Max(0, Math.Min(2, (int)f)); break;
                        case "snowing": snowing = v == "1"; break;
                        case "hideFullscreen": hideFullscreen = v == "1"; break;
                        case "drifts": drifts = v == "1"; break;
                        case "accum": if (fok) accum = Math.Max(0.5f, Math.Min(6f, f)); break;
                        case "wind": if (fok) windUser = Math.Max(0f, Math.Min(3f, f)); break;
                    }
                }
            }
            catch { }
        }

        void SaveSettings()
        {
            if (bench) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath,
                    "intensity=" + intensity + "\r\n" +
                    "snowing=" + (snowing ? "1" : "0") + "\r\n" +
                    "hideFullscreen=" + (hideFullscreen ? "1" : "0") + "\r\n" +
                    "drifts=" + (drifts ? "1" : "0") + "\r\n" +
                    "accum=" + accum.ToString("0.00", CultureInfo.InvariantCulture) + "\r\n" +
                    "wind=" + windUser.ToString("0.00", CultureInfo.InvariantCulture) + "\r\n");
            }
            catch { }
        }

        // ---------------------------------------------------------------- буфер

        void SetupBuffers()
        {
            FreeBuffers();
            vs = SystemInformation.VirtualScreen;
            Screen[] all = Screen.AllScreens;
            screens = new Rectangle[all.Length];
            for (int i = 0; i < all.Length; i++) screens[i] = all[i].Bounds;

            memDC = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            NativeMethods.BITMAPINFOHEADER bi = new NativeMethods.BITMAPINFOHEADER();
            bi.biSize = 40;
            bi.biWidth = vs.Width;
            bi.biHeight = -vs.Height; // top-down
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            dib = NativeMethods.CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = NativeMethods.SelectObject(memDC, dib);
            bmp = new Bitmap(vs.Width, vs.Height, vs.Width * 4, PixelFormat.Format32bppPArgb, bits);
            g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;

            groundPiles.Clear();
            foreach (Rectangle b in screens)
            {
                Pile p = NewPile(b, 30f * scale);
                p.Ground = true;
                groundPiles.Add(p);
            }
            foreach (Pile p in piles.Values) p.Alive = false;
            piles.Clear();
            flakes.Clear();
            balls.Clear();
            held = null;
            initialFill = true;
        }

        void FreeBuffers()
        {
            if (g != null) { g.Dispose(); g = null; }
            if (bmp != null) { bmp.Dispose(); bmp = null; }
            if (memDC != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memDC, oldBmp);
                NativeMethods.DeleteDC(memDC);
                memDC = IntPtr.Zero;
            }
            if (dib != IntPtr.Zero) { NativeMethods.DeleteObject(dib); dib = IntPtr.Zero; }
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            BeginInvoke(new MethodInvoker(SetupBuffers));
        }

        void BuildSprites()
        {
            int n = 18;
            sprites = new Bitmap[n];
            for (int i = 0; i < n; i++)
            {
                float d = (SpriteMin + i * SpriteStep);
                int sz = (int)Math.Ceiling(d) + 4;
                Bitmap b = new Bitmap(sz, sz, PixelFormat.Format32bppPArgb);
                using (Graphics gg = Graphics.FromImage(b))
                {
                    gg.SmoothingMode = SmoothingMode.AntiAlias;
                    gg.Clear(Color.Transparent);
                    float c = sz / 2f;
                    // лёгкая тень — чтобы снег был виден на белых окнах
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(55, 60, 80, 115)))
                        gg.FillEllipse(sb, c - d / 2f - 1f, c - d / 2f - 0.6f, d + 2f, d + 2f);
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                        gg.FillEllipse(sb, c - d / 2f, c - d / 2f, d, d);
                }
                sprites[i] = b;
            }
        }

        // ---------------------------------------------------------------- трей

        void SetupTray()
        {
            tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Text = "SnowDesk — нажмите, чтобы открыть";
            tray.MouseClick += delegate { panel.TogglePanel(); };
            tray.Visible = true;
        }

        Icon MakeIcon()
        {
            using (Bitmap b = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics gg = Graphics.FromImage(b))
            {
                gg.SmoothingMode = SmoothingMode.AntiAlias;
                gg.Clear(Color.Transparent);
                using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                    Color.FromArgb(255, 70, 160, 245), Color.FromArgb(255, 30, 80, 170), 90f))
                    gg.FillEllipse(br, 1, 1, 30, 30);
                using (Pen pen = new Pen(Color.White, 2.4f))
                using (Pen thin = new Pen(Color.White, 1.6f))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    thin.StartCap = thin.EndCap = LineCap.Round;
                    for (int i = 0; i < 6; i++)
                    {
                        double a = i * Math.PI / 3 + Math.PI / 6;
                        float cx = 16, cy = 16;
                        gg.DrawLine(pen, cx, cy, cx + (float)Math.Cos(a) * 11, cy + (float)Math.Sin(a) * 11);
                        float bx = cx + (float)Math.Cos(a) * 7, by = cy + (float)Math.Sin(a) * 7;
                        for (int s = -1; s <= 1; s += 2)
                        {
                            double a2 = a + s * Math.PI / 4;
                            gg.DrawLine(thin, bx, by, bx + (float)Math.Cos(a2) * 4, by + (float)Math.Sin(a2) * 4);
                        }
                    }
                }
                hIcon = b.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static bool IsAutostart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("SnowDesk") != null;
            }
            catch { return false; }
        }

        static void SetAutostart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("SnowDesk", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("SnowDesk", false);
                }
            }
            catch { }
        }

        // ---------------------------------------------------------------- цикл

        void OnTick(object sender, EventArgs e)
        {
            double now = sw.Elapsed.TotalSeconds;
            float dt = (float)(now - lastT);
            lastT = now;
            if (dt <= 0f) return;
            if (dt > 0.05f) dt = 0.05f;

            if (hideFullscreen && IsFullscreenForeground())
            {
                if (!hiddenNow)
                {
                    hiddenNow = true;
                    if (held != null) ReleaseHeld();
                    SetInteractive(false);
                    g.Clear(Color.Transparent);
                    Present();
                }
                return;
            }
            hiddenNow = false;

            if (++topmostCounter >= 60)
            {
                topmostCounter = 0;
                NativeMethods.SetWindowPos(myHandle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }

            UpdateSurfaces();
            if (BenchPending) TickBenchSetup();
            UpdateCursor(dt);
            Step(dt);
            StepBalls(dt);
            Render();
            Present();

            if (bench)
            {
                benchFrames++;
                benchWork += sw.Elapsed.TotalSeconds - now;
                if (now > 10.0) FinishBench(now);
            }
        }

        void FinishBench(double now)
        {
            bench = false;
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            int pileCount = 0;
            float snowMax = 0f;
            foreach (Pile p in piles.Values)
            {
                pileCount++;
                foreach (float h in p.H) if (h > snowMax) snowMax = h;
            }
            string report = string.Format(
                "frames={0} fps={1:F1} avgWorkMs={2:F2} flakes={3} balls={10} windows={4} segs={5} piles={6} maxSnow={7:F1}px screen={8} scale={9} corner={11}\r\n",
                benchFrames, benchFrames / now, benchWork * 1000.0 / benchFrames, flakes.Count, wins.Count, segs.Count,
                pileCount, snowMax, vs, scale, balls.Count, cornerRadius) + benchWrapLog + "\r\n";
            File.WriteAllText(Path.Combine(dir, "bench.txt"), report);
            bmp.Save(Path.Combine(dir, "bench.png"), ImageFormat.Png);
            using (Bitmap pb = panel.RenderToBitmap()) pb.Save(Path.Combine(dir, "bench_panel.png"), ImageFormat.Png);
            Close();
        }

        // В тестовом режиме: насыпать снег, слепить снеговика и проверить отрисовку
        void BenchSetup()
        {
            Pile tb = null;
            foreach (Pile p in piles.Values) if (p.Taskbar) tb = p;
            if (tb == null) return;
            for (int i = 0; i < tb.H.Length; i++) tb.H[i] = Math.Min(tb.MaxH, 6f * scale + 4f * scale * (float)Math.Sin(i * 0.05));
            foreach (Pile p in piles.Values)
                if (!p.Taskbar)
                    for (int i = 0; i < p.H.Length; i++) p.H[i] = 7f * scale;
            float[] xs = { 0.3f, 0.45f };
            foreach (float xf in xs)
            {
                float x = tb.R.Left + tb.R.Width * xf;
                float[] radii = xf < 0.4f ? new float[] { 34f, 25f, 17f } : new float[] { 26f, 18f };
                foreach (float r in radii)
                {
                    Ball b = new Ball();
                    b.R = r * scale;
                    b.Area = (float)Math.PI * b.R * b.R;
                    b.X = x;
                    b.Y = tb.TopY - 300f * scale;
                    b.State = Ball.Falling;
                    balls.Add(b);
                    for (int k = 0; k < 40; k++) StepBalls(0.02f);
                }
            }

            // бросок вправо за край экрана — должен вылететь слева
            Ball w = new Ball();
            w.R = 12f * scale;
            w.Area = (float)Math.PI * w.R * w.R;
            w.X = vs.Right - 60f * scale;
            w.Y = vs.Top + 200f * scale;
            w.VX = 1500f * scale;
            w.VY = -400f * scale;
            w.State = Ball.Falling;
            balls.Add(w);
            benchWrapLog = "wrapTest:";
            for (int k = 0; k < 8; k++)
            {
                StepBalls(0.02f);
                benchWrapLog += " x=" + (int)w.X + (w.Dead ? "(dead)" : "");
            }
        }

        void TickBenchSetup()
        {
            if (!BenchPending || t < 1f) return;
            BenchPending = false;
            BenchSetup();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            BenchPending = bench;
            if (bench)
            {
                Form test = new Form();
                test.Text = "SnowDesk bench window";
                test.StartPosition = FormStartPosition.Manual;
                test.TopMost = true;
                test.ShowInTaskbar = false;
                test.Bounds = new Rectangle(vs.Left + 900, vs.Top + 500, 500, 300);
                test.Show();
                FormClosed += delegate { test.Close(); };
            }
        }

        bool IsFullscreenForeground()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == myHandle) return false;
            clsBuf.Length = 0;
            NativeMethods.GetClassName(fg, clsBuf, 256);
            string c = clsBuf.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
            int style = NativeMethods.GetWindowLong(fg, -16);
            if ((style & WS_CAPTION) == WS_CAPTION) return false; // обычное окно с заголовком
            if ((style & 0x01000000) != 0) return false;          // WS_MAXIMIZE — просто развёрнутое окно
            NativeMethods.RECT r;
            if (!NativeMethods.GetWindowRect(fg, out r)) return false;
            foreach (Rectangle b in screens)
                if (r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom) return true;
            return false;
        }

        // ------------------------------------------------------- окна и края

        bool OnEnumWindow(IntPtr h, IntPtr l)
        {
            if (h == myHandle) return true;
            if (!NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h)) return true;
            int cloaked;
            if (NativeMethods.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;

            clsBuf.Length = 0;
            NativeMethods.GetClassName(h, clsBuf, 256);
            string cls = clsBuf.ToString();
            bool tb = cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
            if (!tb)
            {
                if (cls == "Progman" || cls == "WorkerW") return true;
                int ex = NativeMethods.GetWindowLong(h, -20);
                if ((ex & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)) != 0) return true;
                if (NativeMethods.GetWindowTextLength(h) == 0) return true;
            }

            NativeMethods.RECT r;
            if (NativeMethods.DwmGetWindowAttribute(h, 9, out r, 16) != 0)
                NativeMethods.GetWindowRect(h, out r);
            Rectangle rc = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (rc.Width < 60 || rc.Height < 30) return true;
            if (!rc.IntersectsWith(vs)) return true;

            Win w;
            w.H = h;
            w.R = rc;
            w.Taskbar = tb;
            wins.Add(w);
            return true;
        }

        Pile NewPile(Rectangle r, float maxH)
        {
            Pile p = new Pile();
            p.R = r;
            p.H = new float[Math.Max(1, (r.Width + col - 1) / col)];
            p.MaxH = maxH;
            return p;
        }

        bool IsAtScreenTop(Rectangle r)
        {
            foreach (Rectangle b in screens)
                if (r.Top <= b.Top + 2 && r.Right > b.Left && r.Left < b.Right) return true;
            return false;
        }

        void UpdateSurfaces()
        {
            wins.Clear();
            NativeMethods.EnumWindows(enumProc, IntPtr.Zero);

            // панель задач всегда поверх обычных окон — ставим её в начало Z-порядка,
            // чтобы развёрнутые окна не "закрывали" её верхний край
            int front = 0;
            for (int i = 0; i < wins.Count; i++)
            {
                if (!wins[i].Taskbar) continue;
                Win tb = wins[i];
                wins.RemoveAt(i);
                wins.Insert(front++, tb);
            }

            foreach (Pile p in piles.Values) p.Seen = false;
            segs.Clear();

            for (int i = 0; i < wins.Count; i++)
            {
                Win w = wins[i];
                Pile p;
                if (!piles.TryGetValue(w.H, out p))
                {
                    p = NewPile(w.R, (w.Taskbar ? 26f : 12f) * scale);
                    p.Taskbar = w.Taskbar;
                    p.Corner = w.Taskbar ? 0f : cornerRadius;
                    piles[w.H] = p;
                }
                else if (p.R != w.R)
                {
                    // окно сдвинули / изменили размер — снег осыпается
                    Spill(p);
                    p.R = w.R;
                    p.H = new float[Math.Max(1, (w.R.Width + col - 1) / col)];
                    p.OvL = p.OvR = 0f;
                }
                p.Seen = true;

                // развёрнутые окна (верх у края экрана) — снег на них не лежит
                if (!w.Taskbar && IsAtScreenTop(w.R)) continue;
                AddVisibleSegs(p, w.R.Left, w.R.Right, w.R.Top, i);
            }

            removeKeys.Clear();
            foreach (KeyValuePair<IntPtr, Pile> kv in piles)
                if (!kv.Value.Seen) removeKeys.Add(kv.Key);
            foreach (IntPtr k in removeKeys)
            {
                Pile p = piles[k];
                Spill(p); // окно закрыли/свернули
                p.Alive = false;
                piles.Remove(k);
            }

            // "земля" внизу каждого монитора (если панель задач скрыта)
            for (int i = 0; i < groundPiles.Count; i++)
            {
                Pile p = groundPiles[i];
                AddVisibleSegs(p, p.R.Left, p.R.Right, p.R.Bottom, wins.Count);
            }
        }

        // Добавляет видимые (не перекрытые окнами выше по Z) участки верхнего края
        void AddVisibleSegs(Pile p, int x1, int x2, int y, int zIndex)
        {
            if (x1 < vs.Left) x1 = vs.Left;
            if (x2 > vs.Right) x2 = vs.Right;
            if (x2 - x1 < 4) return;
            iv.Clear();
            iv.Add(x1);
            iv.Add(x2);
            for (int j = 0; j < zIndex && iv.Count > 0; j++)
            {
                Rectangle rj = wins[j].R;
                if (rj.Top < y && rj.Bottom >= y) Subtract(rj.Left, rj.Right);
            }
            for (int k = 0; k < iv.Count; k += 2)
            {
                if (iv[k + 1] - iv[k] < 4) continue;
                Seg s;
                s.P = p;
                s.X1 = iv[k];
                s.X2 = iv[k + 1];
                s.Y = y;
                segs.Add(s);
            }
        }

        void Subtract(int a, int b)
        {
            ivTmp.Clear();
            for (int k = 0; k < iv.Count; k += 2)
            {
                int s = iv[k], e = iv[k + 1];
                if (b <= s || a >= e) { ivTmp.Add(s); ivTmp.Add(e); continue; }
                if (a > s) { ivTmp.Add(s); ivTmp.Add(a); }
                if (b < e) { ivTmp.Add(b); ivTmp.Add(e); }
            }
            List<int> tmp = iv; iv = ivTmp; ivTmp = tmp;
        }

        bool SegCovers(Pile p, float x)
        {
            for (int s = 0; s < segs.Count; s++)
                if (segs[s].P == p && x >= segs[s].X1 && x < segs[s].X2) return true;
            return false;
        }

        // ------------------------------------------------------------ физика снега

        int RegularTarget()
        {
            double area = (double)vs.Width * vs.Height / (1920.0 * 1080.0);
            return (int)(FlakesPer1080p[intensity] * area);
        }

        void InitFlake(Flake f, bool anywhere)
        {
            double d = rnd.NextDouble();
            f.Depth = (float)(0.3 + 0.7 * d * d);
            f.Size = (1.3f + 3.4f * f.Depth * f.Depth) * scale;
            f.VY = (20f + 60f * f.Depth) * scale;
            f.Phase = (float)(rnd.NextDouble() * Math.PI * 2);
            f.X = vs.Left - 150 + (float)rnd.NextDouble() * (vs.Width + 300);
            f.Y = anywhere ? vs.Top + (float)rnd.NextDouble() * vs.Height : vs.Top - 10 - (float)rnd.NextDouble() * 80f;
            f.VX = wind * f.Depth;
            f.Mass = f.Size * f.Size * 0.55f * accum;
            f.Clump = false;
            f.Dead = false;
        }

        void SpawnClump(float x, float y, float mass, float vx, float vy)
        {
            if (flakes.Count > 8000) return;
            Flake f = new Flake();
            f.Clump = true;
            f.X = x;
            f.Y = y;
            f.VX = vx;
            f.VY = vy;
            f.Mass = mass;
            f.Depth = 1f;
            float s = (float)Math.Sqrt(mass) * 0.6f;
            f.Size = Math.Max(1.6f * scale, Math.Min(5.5f * scale, s));
            flakes.Add(f);
        }

        void Spill(Pile p)
        {
            if (p.Ground) return;
            int n = p.H.Length;
            float total = 0f;
            for (int i = 0; i < n; i++) total += p.H[i];
            if (total * col < 2f) { Array.Clear(p.H, 0, n); return; }

            int chunk = 3;
            if (n / chunk > 350) chunk = n / 350 + 1;
            if (flakes.Count > 5000) chunk *= 4;
            for (int i = 0; i < n; i += chunk)
            {
                float a = 0f;
                int cnt = 0;
                for (int j = i; j < i + chunk && j < n; j++) { a += p.H[j]; cnt++; }
                float area = a * col;
                if (area < 1.5f) continue;
                float x = p.R.Left + (i + cnt * 0.5f) * col;
                float y = p.R.Top - (a / cnt) * (float)rnd.NextDouble();
                SpawnClump(x, y, area,
                    (float)(rnd.NextDouble() - 0.5) * 50f * scale,
                    (float)rnd.NextDouble() * 60f * scale);
            }
            Array.Clear(p.H, 0, n);
        }

        public void ShakeWindows()
        {
            foreach (Pile p in piles.Values)
                if (!p.Taskbar) Spill(p);
            foreach (Ball b in balls)
                if (b.State == Ball.Resting && b.RestPile != null && !b.RestPile.Taskbar && !b.RestPile.Ground) Fall(b);
        }

        public void ClearAll()
        {
            foreach (Pile p in piles.Values) Array.Clear(p.H, 0, p.H.Length);
            foreach (Pile p in groundPiles) Array.Clear(p.H, 0, p.H.Length);
            flakes.RemoveAll(delegate (Flake f) { return f.Clump; });
            balls.Clear();
            held = null;
        }

        void Step(float dt)
        {
            t += dt;
            wind = (float)(Math.Sin(t * 0.13) * 18 + Math.Sin(t * 0.41 + 1.0) * 9 + 6) * scale * WindMul[intensity] * windUser;

            int regular = 0;
            for (int i = 0; i < flakes.Count; i++) if (!flakes[i].Clump) regular++;
            int target = snowing ? RegularTarget() : 0;
            while (regular < target)
            {
                Flake f = new Flake();
                InitFlake(f, initialFill);
                flakes.Add(f);
                regular++;
            }
            initialFill = false;

            float fall = FallMul[intensity];
            int count = flakes.Count;
            for (int i = 0; i < count; i++)
            {
                Flake f = flakes[i];
                float prevY = f.Y;
                if (f.Clump)
                {
                    f.VY = Math.Min(f.VY + 900f * scale * dt, 700f * scale);
                    f.VX *= (1f - Math.Min(1f, 1.2f * dt));
                    f.X += f.VX * dt;
                    f.Y += f.VY * dt;
                }
                else
                {
                    f.VX += (wind * f.Depth - f.VX) * Math.Min(1f, dt * 1.5f);
                    float sway = (float)Math.Sin(t * (0.7f + f.Depth) + f.Phase) * (10f + 20f * f.Depth) * scale;
                    f.X += (f.VX + sway) * dt;
                    f.Y += f.VY * fall * dt;
                }

                // летит вверх (подброшенный снег) — ни с чем не сталкивается
                if (f.Y < prevY)
                {
                    if (f.X < vs.Left - 300 || f.X > vs.Right + 300) f.Dead = true;
                    continue;
                }

                // столкновение с краями окон / сугробами (если сугробы выключены — снег пролетает насквозь)
                int xi = (int)f.X;
                float best = float.MaxValue;
                int bi = -1;
                for (int s = 0; drifts && s < segs.Count; s++)
                {
                    Seg sg = segs[s];
                    if (xi < sg.X1 || xi >= sg.X2 || prevY > sg.Y + 1) continue;
                    Pile p = sg.P;
                    int c = (xi - p.R.Left) / col;
                    if (c < 0 || c >= p.H.Length) continue;
                    float sy = sg.Y - p.H[c];
                    if (f.Y >= sy && sy < best) { best = sy; bi = s; }
                }

                if (bi >= 0)
                {
                    int radius = f.Clump ? Math.Max(3, (int)(f.Size / col) + 2) : 3;
                    Deposit(segs[bi].P, f.X, f.Mass, radius);
                    if (f.Clump || !snowing || regular > target) f.Dead = true;
                    else InitFlake(f, false);
                }
                else if (f.Y > vs.Bottom + 20 || f.X < vs.Left - 300 || f.X > vs.Right + 300)
                {
                    if (f.Clump || !snowing) f.Dead = true;
                    else InitFlake(f, false);
                }
            }
            flakes.RemoveAll(delegate (Flake f) { return f.Dead; });

            // снег, съехавший с краёв окон, падает вниз
            foreach (Pile p in piles.Values)
            {
                if (p.OvL >= 4f)
                {
                    SpawnClump(p.R.Left - 2f * scale, p.R.Top - p.H[0] + p.Corner, p.OvL, -18f * scale, 0f);
                    p.OvL = 0f;
                }
                if (p.OvR >= 4f)
                {
                    SpawnClump(p.R.Right + 2f * scale, p.R.Top - p.H[p.H.Length - 1] + p.Corner, p.OvR, 18f * scale, 0f);
                    p.OvR = 0f;
                }
            }
        }

        void Deposit(Pile p, float sx, float area, int r)
        {
            if (!drifts) return;
            float[] h = p.H;
            int n = h.Length;
            int c = (int)((sx - p.R.Left) / col);
            if (c < 0) c = 0;
            if (c >= n) c = n - 1;
            float wsum = 0f;
            for (int k = -r; k <= r; k++) wsum += r + 1 - Math.Abs(k);
            float perCol = area / col / wsum * (0.7f + 0.6f * (float)rnd.NextDouble());
            for (int k = -r; k <= r; k++)
            {
                int idx = c + k;
                float amt = perCol * (r + 1 - Math.Abs(k));
                if (idx < 0) { if (!p.Ground) p.OvL += amt * col; }
                else if (idx >= n) { if (!p.Ground) p.OvR += amt * col; }
                else h[idx] += amt;
            }
            RelaxAround(p, c, r);
        }

        void RelaxAround(Pile p, int c, int r)
        {
            int reach = r + (int)(p.MaxH / slope) + 6;
            Relax(p, c - reach, c + reach);
        }

        void Relax(Pile p, int a, int b)
        {
            float[] h = p.H;
            int n = h.Length;
            if (a < 0) a = 0;
            if (b > n - 1) b = n - 1;
            if (b < a) return;
            float S = slope;
            for (int it = 0; it < 3; it++)
            {
                for (int i = a; i < b; i++) Balance(h, i, S);
                for (int i = b - 1; i >= a; i--) Balance(h, i, S);
            }
            if (!p.Ground)
            {
                // на краю окна сугроб не может нависать — излишек съезжает
                if (a == 0 && h[0] > S) { p.OvL += (h[0] - S) * col; h[0] = S; }
                if (b == n - 1 && h[n - 1] > S) { p.OvR += (h[n - 1] - S) * col; h[n - 1] = S; }
            }
            for (int i = a; i <= b; i++)
            {
                if (h[i] > p.MaxH) h[i] = p.MaxH;
                else if (h[i] < 0f) h[i] = 0f;
            }
        }

        static void Balance(float[] h, int i, float S)
        {
            float d = h[i] - h[i + 1];
            if (d > S) { float tt = (d - S) * 0.5f; h[i] -= tt; h[i + 1] += tt; }
            else if (d < -S) { float tt = (-d - S) * 0.5f; h[i + 1] -= tt; h[i] += tt; }
        }

        // Забирает снег вокруг x в радиусе rad (px); возвращает площадь (px²)
        float TakeSnow(Pile p, float x, float rad, float maxPerCol)
        {
            int c = (int)((x - p.R.Left) / col);
            int half = Math.Max(1, (int)(rad / col));
            float got = 0f;
            for (int k = c - half; k <= c + half; k++)
            {
                if (k < 0 || k >= p.H.Length) continue;
                float w = 1f - Math.Abs(k - c) / (float)(half + 1) * 0.5f;
                float take = Math.Min(p.H[k], maxPerCol * w);
                if (take <= 0f) continue;
                p.H[k] -= take;
                got += take * col;
            }
            if (got > 0f) RelaxAround(p, c, half);
            return got;
        }

        // ---------------------------------------------------------- интерактив

        int SegAt(float px, float py)
        {
            float tol = 4f * scale;
            for (int s = 0; s < segs.Count; s++)
            {
                Seg sg = segs[s];
                if (px < sg.X1 || px >= sg.X2) continue;
                Pile p = sg.P;
                int c = (int)((px - p.R.Left) / col);
                if (c < 0 || c >= p.H.Length) continue;
                float h = p.H[c];
                if (h < 1f) continue;
                if (py >= sg.Y - h - tol && py <= sg.Y + 2f) return s;
            }
            return -1;
        }

        Ball BallAt(float px, float py)
        {
            Ball best = null;
            for (int i = 0; i < balls.Count; i++)
            {
                Ball b = balls[i];
                if (b.Dead || b.Hidden || b == held) continue;
                float dx = px - b.X, dy = py - b.Y, rr = b.R + 2f * scale;
                if (dx * dx + dy * dy <= rr * rr && (best == null || b.Y < best.Y)) best = b;
            }
            return best;
        }

        void SetInteractive(bool want)
        {
            if (want == interactive || myHandle == IntPtr.Zero) return;
            interactive = want;
            int ex = NativeMethods.GetWindowLong(myHandle, -20);
            ex = want ? (ex & ~WS_EX_TRANSPARENT) : (ex | WS_EX_TRANSPARENT);
            NativeMethods.SetWindowLong(myHandle, -20, ex);
            Cursor = want ? Cursors.Hand : Cursors.Default;
        }

        void UpdateCursor(float dt)
        {
            NativeMethods.POINT cp;
            NativeMethods.GetCursorPos(out cp);
            float nx = cp.X, ny = cp.Y;
            if (!curInit) { curX = nx; curY = ny; curInit = true; }
            float dx = nx - curX, dy = ny - curY;
            curVX = curVX * 0.55f + dx / dt * 0.45f;
            curVY = curVY * 0.55f + dy / dt * 0.45f;
            curX = nx;
            curY = ny;

            if (held != null)
            {
                held.X = nx;
                held.Y = ny;
                held.Angle += dx / Math.Max(4f, held.R); // снежок катится
                RollAbsorb(held, (float)Math.Sqrt(dx * dx + dy * dy));
            }

            bool want = false;
            if (playMode)
                want = held != null || BallAt(nx, ny) != null || SegAt(nx, ny) >= 0;
            SetInteractive(want);
        }

        // Катаем снежок по сугробу — он собирает снег и растёт
        void RollAbsorb(Ball b, float moveDist)
        {
            if (moveDist < 0.5f) return;
            for (int s = 0; s < segs.Count; s++)
            {
                Seg sg = segs[s];
                if (b.X < sg.X1 || b.X >= sg.X2) continue;
                Pile p = sg.P;
                int c = (int)((b.X - p.R.Left) / col);
                if (c < 0 || c >= p.H.Length) continue;
                float sy = sg.Y - p.H[c];
                if (b.Y + b.R < sy - 2f * scale || b.Y - b.R > sg.Y) continue;
                float got = TakeSnow(p, b.X, b.R * 0.7f, Math.Min(moveDist, 40f) * 0.06f);
                if (got > 0f) GrowBall(b, got * 0.9f);
                return;
            }
        }

        void GrowBall(Ball b, float area)
        {
            float maxR = 75f * scale;
            b.Area = Math.Min(b.Area + area, (float)Math.PI * maxR * maxR);
            b.R = (float)Math.Sqrt(b.Area / Math.PI);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!playMode) return;
            float px = curX, py = curY;
            Ball b = BallAt(px, py);
            if (e.Button == MouseButtons.Left)
            {
                if (b != null)
                {
                    held = b;
                }
                else
                {
                    int s = SegAt(px, py);
                    if (s < 0 || balls.Count >= 40) return;
                    float area = TakeSnow(segs[s].P, px, 14f * scale, 1000f);
                    if (area < 10f * scale * scale) return;
                    held = new Ball();
                    held.Area = 0f;
                    GrowBall(held, area * 0.9f);
                    balls.Add(held);
                }
                held.State = Ball.Held;
                held.RestPile = null;
                held.RestBall = null;
                held.Thrown = false;
                held.X = px;
                held.Y = py;
                Capture = true;
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (b != null) BreakBall(b, 1f);
                else
                {
                    int s = SegAt(px, py);
                    if (s >= 0) KickSnow(segs[s], px);
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && held != null) ReleaseHeld();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && held != null) ReleaseHeld();
        }

        void ReleaseHeld()
        {
            Ball b = held;
            held = null;
            if (b == null) return;
            float max = 2600f * scale;
            float vx = Math.Max(-max, Math.Min(max, curVX));
            float vy = Math.Max(-max, Math.Min(max, curVY));
            b.State = Ball.Falling;
            b.VX = vx;
            b.VY = vy;
            b.Thrown = vx * vx + vy * vy > 500f * scale * 500f * scale;
            if (Capture) Capture = false;
        }

        // ПКМ по сугробу — снег взлетает вверх
        void KickSnow(Seg sg, float px)
        {
            Pile p = sg.P;
            int c = (int)((px - p.R.Left) / col);
            if (c < 0 || c >= p.H.Length) return;
            float baseY = sg.Y - p.H[c];
            float area = TakeSnow(p, px, 18f * scale, 1000f);
            if (area < 2f) return;
            int n = Math.Max(3, Math.Min(35, (int)(area / 6f)));
            for (int i = 0; i < n; i++)
            {
                SpawnClump(px + (float)(rnd.NextDouble() - 0.5) * 30f * scale, baseY - 2f, area / n,
                    (float)(rnd.NextDouble() - 0.5) * 320f * scale,
                    -(250f + (float)rnd.NextDouble() * 420f) * scale);
            }
        }

        void BreakBall(Ball b, float power)
        {
            b.Dead = true;
            if (b == held) held = null;
            int n = Math.Max(5, Math.Min(60, (int)(b.Area / 10f)));
            for (int i = 0; i < n; i++)
            {
                double a = rnd.NextDouble() * Math.PI * 2;
                float rr = (float)rnd.NextDouble() * b.R;
                SpawnClump(b.X + (float)Math.Cos(a) * rr, b.Y + (float)Math.Sin(a) * rr, b.Area / n,
                    (float)(rnd.NextDouble() - 0.5) * 300f * scale * power,
                    -(80f + (float)rnd.NextDouble() * 300f) * scale * power);
            }
        }

        void Fall(Ball b)
        {
            b.State = Ball.Falling;
            b.RestPile = null;
            b.RestBall = null;
            b.VY = 0f;
            b.VX = (float)(rnd.NextDouble() - 0.5) * 40f * scale;
            b.Hidden = false;
        }

        // ------------------------------------------------------------ снежки

        void StepBalls(float dt)
        {
            if (balls.Count == 0) return;
            balls.Sort(byDepth); // снизу вверх: опоры обновляются раньше тех, кто на них стоит

            for (int i = 0; i < balls.Count; i++) { balls[i].Above = null; balls[i].Role = 0; }
            for (int i = 0; i < balls.Count; i++)
            {
                Ball b = balls[i];
                if (b.State == Ball.Resting && b.RestBall != null && !b.RestBall.Dead && b.RestBall.State == Ball.Resting)
                    b.RestBall.Above = b;
            }

            for (int i = 0; i < balls.Count; i++)
            {
                Ball b = balls[i];
                if (b.Dead || b.State == Ball.Held) continue;

                if (b.State == Ball.Resting)
                {
                    if (b.RestBall != null)
                    {
                        Ball s = b.RestBall;
                        if (s.Dead || s.State != Ball.Resting) { Fall(b); continue; }
                        b.X = s.X;
                        b.Y = s.Y - s.R - b.R * 0.78f;
                        b.Hidden = s.Hidden;
                    }
                    else if (b.RestPile != null)
                    {
                        Pile p = b.RestPile;
                        if (!p.Alive || p.R != b.RestRect) { Fall(b); continue; }
                        int c = (int)((b.X - p.R.Left) / col);
                        if (c < 0 || c >= p.H.Length) { Fall(b); continue; }
                        float sy = p.TopY - p.H[c];
                        if (b.Y + b.R * 0.85f < sy - 3f * scale) { Fall(b); continue; } // снег под ним убрали
                        b.Hidden = !SegCovers(p, b.X);
                    }
                    else Fall(b);
                    continue;
                }

                // падение / полёт
                float prevY = b.Y;
                b.VY = Math.Min(b.VY + 1500f * scale * dt, 1600f * scale);
                b.VX *= (1f - Math.Min(1f, 0.3f * dt));
                b.X += b.VX * dt;
                b.Y += b.VY * dt;
                b.Angle += b.VX * dt / Math.Max(4f, b.R); // вращается в полёте
                // улетел за правый край — вылетает слева, и наоборот
                if (b.X >= vs.Right) b.X -= vs.Width;
                else if (b.X < vs.Left) b.X += vs.Width;
                if (b.Y < prevY) continue; // летит вверх

                float bestY = float.MaxValue;
                int bestSeg = -1;
                Ball bestBall = null;

                for (int s = 0; s < segs.Count; s++)
                {
                    Seg sg = segs[s];
                    if (b.X < sg.X1 || b.X >= sg.X2) continue;
                    if (prevY + b.R * 0.85f > sg.Y + 1) continue;
                    Pile p = sg.P;
                    int c = (int)((b.X - p.R.Left) / col);
                    if (c < 0 || c >= p.H.Length) continue;
                    float sy = sg.Y - p.H[c];
                    float cy = sy + b.R * 0.15f - b.R;
                    if (b.Y >= cy && cy < bestY) { bestY = cy; bestSeg = s; bestBall = null; }
                }

                for (int k = 0; k < balls.Count; k++)
                {
                    Ball o = balls[k];
                    if (o == b || o.Dead || o.Hidden || o.State != Ball.Resting || o.Above != null) continue;
                    float ty = o.Y - o.R - b.R * 0.78f;
                    float dx = b.X - o.X;
                    if (prevY > ty + 1f || b.Y < ty) continue;
                    float reach = (o.R + b.R) * 0.55f;
                    if (Math.Abs(dx) < reach)
                    {
                        if (ty < bestY) { bestY = ty; bestBall = o; bestSeg = -1; }
                    }
                    else if (Math.Abs(dx) < (o.R + b.R) * 0.95f && bestBall == null && bestSeg < 0)
                    {
                        // задел край шара — скатывается вбок
                        float sgn = dx >= 0 ? 1f : -1f;
                        b.X = o.X + sgn * (o.R + b.R) * 0.96f;
                        b.VX = sgn * Math.Max(Math.Abs(b.VX), 90f * scale);
                    }
                }

                float speed = (float)Math.Sqrt(b.VX * b.VX + b.VY * b.VY);
                bool splat = b.Thrown && speed > 900f * scale;

                if (bestBall != null)
                {
                    if (splat) { BreakBall(b, 0.6f); continue; }
                    b.State = Ball.Resting;
                    b.RestBall = bestBall;
                    bestBall.Above = b;
                    b.X = bestBall.X;
                    b.Y = bestY;
                    b.VX = b.VY = 0f;
                    b.Thrown = false;
                }
                else if (bestSeg >= 0)
                {
                    Pile p = segs[bestSeg].P;
                    if (splat)
                    {
                        b.Dead = true;
                        Deposit(p, b.X, b.Area * 0.6f, Math.Max(2, (int)(b.R / col)));
                        int n = Math.Max(4, Math.Min(30, (int)(b.Area * 0.4f / 10f)));
                        for (int k = 0; k < n; k++)
                            SpawnClump(b.X + (float)(rnd.NextDouble() - 0.5) * b.R * 2f, bestY + b.R * 0.5f, b.Area * 0.4f / n,
                                (float)(rnd.NextDouble() - 0.5) * 250f * scale + b.VX * 0.2f,
                                -(100f + (float)rnd.NextDouble() * 250f) * scale);
                        continue;
                    }
                    b.State = Ball.Resting;
                    b.RestPile = p;
                    b.RestRect = p.R;
                    b.Y = bestY;
                    b.VX = b.VY = 0f;
                    b.Thrown = false;
                }
                else if (b.Y - b.R > vs.Bottom)
                {
                    b.Dead = true;
                }
            }
            balls.RemoveAll(delegate (Ball x) { return x.Dead; });

            // роли для снеговика
            for (int i = 0; i < balls.Count; i++)
            {
                Ball b = balls[i];
                if (b.State != Ball.Resting || b.RestPile == null) continue;
                int n = 1;
                Ball topBall = b, mid = null;
                while (topBall.Above != null && n < 10) { mid = topBall; topBall = topBall.Above; n++; }
                if (n >= 2) topBall.Role = 1;
                if (n >= 3) { topBall.Role = 3; mid.Role = 2; }
            }
        }

        // --------------------------------------------------------- отрисовка

        void Render()
        {
            g.Clear(Color.Transparent);
            for (int s = 0; s < segs.Count; s++) DrawSeg(segs[s]);

            for (int i = 0; i < balls.Count; i++)
            {
                Ball b = balls[i];
                if (b.Hidden || b == held) continue;
                DrawBall(b, 0f);
                if (b.State == Ball.Falling)
                {
                    // на стыке краёв видно обе половинки
                    if (b.X + b.R > vs.Right - 1) DrawBall(b, -vs.Width);
                    if (b.X - b.R < vs.Left + 1) DrawBall(b, vs.Width);
                }
            }
            if (held != null) DrawBall(held, 0f);

            int ox = -vs.Left, oy = -vs.Top;
            for (int i = 0; i < flakes.Count; i++)
            {
                Flake f = flakes[i];
                int si = (int)Math.Round((f.Size - SpriteMin) / SpriteStep);
                if (si < 0) si = 0;
                if (si >= sprites.Length) si = sprites.Length - 1;
                Bitmap sp = sprites[si];
                int x = (int)f.X + ox - sp.Width / 2, y = (int)f.Y + oy - sp.Height / 2;
                if (x < -sp.Width || y < -sp.Height || x > vs.Width || y > vs.Height) continue;
                g.DrawImageUnscaled(sp, x, y);
            }
            g.Flush(FlushIntention.Sync);
        }

        void DrawBall(Ball b, float shift)
        {
            if (b.Look == null) b.Look = SnowPainter.MakeLook(rnd.Next());
            bool resting = b.State == Ball.Resting;
            int shadow = !resting ? 0 : (b.RestBall != null ? 2 : 1);
            SnowPainter.DrawBall(g, b.Look, b.X - vs.Left + shift, b.Y - vs.Top, b.R, b.Angle, resting, b.Role, shadow);
        }

        // насколько основание снега опускается на скруглённом углу окна
        static float CornerDrop(Pile p, float x)
        {
            float cr = p.Corner;
            if (cr <= 0f) return 0f;
            float d = Math.Min(x - p.R.Left, p.R.Right - x);
            if (d >= cr) return 0f;
            if (d < 0f) d = 0f;
            float u = cr - d;
            return cr - (float)Math.Sqrt(cr * cr - u * u);
        }

        void DrawSeg(Seg s)
        {
            Pile p = s.P;
            float[] H = p.H;
            int n = H.Length;
            int c1 = (s.X1 - p.R.Left) / col;
            int c2 = (s.X2 - 1 - p.R.Left) / col;
            if (c1 < 0) c1 = 0;
            if (c2 > n - 1) c2 = n - 1;
            if (c2 < c1) return;
            int m = c2 - c1 + 1;

            float raw = 0f;
            for (int c = c1; c <= c2; c++) if (H[c] > raw) raw = H[c];
            if (raw < 0.4f) return;

            if (disp.Length < m) { disp = new float[m * 2]; dtmp = new float[m * 2]; }

            // сглаживание (два прохода 1-2-1) — убирает "иголки"
            for (int i = 0; i < m; i++)
            {
                int c = c1 + i;
                dtmp[i] = (H[Math.Max(c - 1, 0)] + 2f * H[c] + H[Math.Min(c + 1, n - 1)]) * 0.25f;
            }
            for (int i = 0; i < m; i++)
                disp[i] = (dtmp[Math.Max(i - 1, 0)] + 2f * dtmp[i] + dtmp[Math.Min(i + 1, m - 1)]) * 0.25f;

            bool leftEnd = s.X1 <= p.R.Left;
            bool rightEnd = s.X2 >= p.R.Right;

            // скруглённые края сугроба (четверть окружности)
            if (!p.Ground)
            {
                float rc = Math.Max(10f * scale, p.Corner * 1.5f);
                int ik = Math.Min(m - 1, (int)(rc / col));
                if (leftEnd && ik > 0)
                {
                    float h0 = disp[ik];
                    for (int i = 0; i < ik; i++)
                    {
                        float d = i * col + col * 0.5f;
                        float u = 1f - d / rc;
                        disp[i] = h0 * (float)Math.Sqrt(Math.Max(0f, 1f - u * u));
                    }
                }
                if (rightEnd && ik > 0)
                {
                    float h0 = disp[m - 1 - ik];
                    for (int i = m - 1; i > m - 1 - ik; i--)
                    {
                        float x = p.R.Left + (c1 + i) * col + col * 0.5f;
                        float d = Math.Max(0f, p.R.Right - x);
                        float u = 1f - d / rc;
                        disp[i] = h0 * (float)Math.Sqrt(Math.Max(0f, 1f - u * u));
                    }
                }
            }

            float ox = -vs.Left, oy = -vs.Top;
            float Y = s.Y;
            float maxh = 0f;
            for (int i = 0; i < m; i++) if (disp[i] > maxh) maxh = disp[i];
            if (maxh < 0.4f) return;

            top.Clear();
            bottom.Clear();
            float xl = s.X1, xr = s.X2;
            top.Add(new PointF(xl + ox, Y + CornerDrop(p, xl) - (leftEnd ? 0f : disp[0]) + oy));
            for (int i = 0; i < m; i++)
            {
                float x = p.R.Left + (c1 + i) * col + col * 0.5f;
                if (x < xl) x = xl;
                if (x > xr) x = xr;
                top.Add(new PointF(x + ox, Y + CornerDrop(p, x) - disp[i] + oy));
            }
            top.Add(new PointF(xr + ox, Y + CornerDrop(p, xr) - (rightEnd ? 0f : disp[m - 1]) + oy));

            bottom.Add(new PointF(xr + ox, Y + CornerDrop(p, xr) + oy + 1f));
            for (int i = m - 1; i >= 0; i--)
            {
                float x = p.R.Left + (c1 + i) * col + col * 0.5f;
                if (x < xl) x = xl;
                if (x > xr) x = xr;
                bottom.Add(new PointF(x + ox, Y + CornerDrop(p, x) + oy + 1f));
            }
            bottom.Add(new PointF(xl + ox, Y + CornerDrop(p, xl) + oy + 1f));

            RectangleF gr = new RectangleF(xl + ox, Y - maxh + oy - 1f, Math.Max(1f, xr - xl), maxh + p.Corner + 3f);
            using (GraphicsPath path = new GraphicsPath())
            using (LinearGradientBrush br = new LinearGradientBrush(gr, pileTop, pileBottom, 90f))
            {
                path.AddCurve(top.ToArray(), 0.5f);
                path.AddLines(bottom.ToArray());
                path.CloseFigure();
                g.FillPath(br, path);
            }

            // контур только там, где есть снег
            using (GraphicsPath path = new GraphicsPath())
            {
                run.Clear();
                for (int i = 0; i < m; i++)
                {
                    if (disp[i] > 0.35f) run.Add(top[i + 1]);
                    else { FlushRun(path); }
                }
                FlushRun(path);
                if (path.PointCount > 0) g.DrawPath(outlinePen, path);
            }
        }

        void FlushRun(GraphicsPath path)
        {
            if (run.Count > 2) { path.StartFigure(); path.AddCurve(run.ToArray(), 0.5f); }
            else if (run.Count == 2) { path.StartFigure(); path.AddLines(run.ToArray()); }
            run.Clear();
        }

        void Present()
        {
            IntPtr screenDC = NativeMethods.GetDC(IntPtr.Zero);
            NativeMethods.POINT dst = new NativeMethods.POINT(vs.Left, vs.Top);
            NativeMethods.SIZE sz = new NativeMethods.SIZE(vs.Width, vs.Height);
            NativeMethods.POINT src = new NativeMethods.POINT(0, 0);
            NativeMethods.BLENDFUNCTION bf = new NativeMethods.BLENDFUNCTION();
            bf.BlendOp = 0;
            bf.SourceConstantAlpha = 255;
            bf.AlphaFormat = 1; // AC_SRC_ALPHA
            NativeMethods.UpdateLayeredWindow(myHandle, screenDC, ref dst, ref sz, memDC, ref src, 0, ref bf, 2);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                if (!NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4)))
                    NativeMethods.SetProcessDPIAware();
            }
            catch { try { NativeMethods.SetProcessDPIAware(); } catch { } }

            bool bench = Array.IndexOf(args, "--bench") >= 0;
            bool created;
            using (Mutex m = new Mutex(true, "SnowDesk_SingleInstance_Mutex", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SnowForm(bench));
            }
        }
    }
}
