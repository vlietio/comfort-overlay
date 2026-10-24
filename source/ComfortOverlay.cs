// Comfort Overlay - a click-through motion-sickness overlay (frame box, edge crosshairs, center dot)
// for games running in windowed / borderless-windowed mode. Build with build.cmd.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class ComfortOverlay : Form
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    static readonly Color Key = Color.FromArgb(255, 0, 255);
    static readonly Color[] Palette = { Color.White, Color.Yellow, Color.Cyan, Color.Lime, Color.HotPink };
    static readonly string[] PaletteNames = { "White", "Yellow", "Cyan", "Green", "Pink" };

    // Hotkeys (all Ctrl+Alt+...)
    static readonly Keys[] HotKeys = {
        Keys.O, Keys.B, Keys.L, Keys.D, Keys.K, Keys.M, Keys.OemPeriod, Keys.Oemcomma,
        Keys.Oemplus, Keys.OemMinus, Keys.OemCloseBrackets, Keys.OemOpenBrackets
    };

    // Settings
    bool enabled, showBox, showLines, showDot;
    double boxScale, gapScale, opacity;
    int thickness, colorIndex, monitor;

    NotifyIcon tray;
    Timer topTimer;
    string settingsPath;

    public ComfortOverlay()
    {
        settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComfortOverlay", "settings.ini");
        ResetDefaults();
        LoadSettings();

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Key;
        TransparencyKey = Key;
        TopMost = true;
        DoubleBuffered = true;
        Text = "Comfort Overlay";

        tray = new NotifyIcon();
        try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { tray.Icon = MakeIcon(); }
        tray.Text = "Comfort Overlay (right-click for settings)";
        tray.ContextMenuStrip = new ContextMenuStrip();
        tray.ContextMenuStrip.Opening += delegate { BuildMenu(tray.ContextMenuStrip); };
        BuildMenu(tray.ContextMenuStrip);
        tray.Visible = true;

        topTimer = new Timer();
        topTimer.Interval = 2000;
        topTimer.Tick += delegate { KeepOnTop(); };
        topTimer.Start();

        Apply(false);
    }

    void ResetDefaults()
    {
        enabled = true; showBox = true; showLines = true; showDot = true;
        boxScale = 0.55; gapScale = 0.10; opacity = 0.7;
        thickness = 6; colorIndex = 0; monitor = -1; // -1 = main monitor
    }

    // ---------- window behaviour ----------
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // LAYERED | TRANSPARENT (click-through) | TOOLWINDOW (no alt-tab) | NOACTIVATE | TOPMOST
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000 | 0x8;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        for (int i = 0; i < HotKeys.Length; i++)
            RegisterHotKey(Handle, i + 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)HotKeys[i]);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        tray.ShowBalloonTip(6000, "Comfort Overlay is running",
            "Ctrl+Alt+O shows/hides it. Right-click the tray icon for settings and Exit.", ToolTipIcon.Info);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        for (int i = 0; i < HotKeys.Length; i++) UnregisterHotKey(Handle, i + 1);
        topTimer.Stop();
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY) { OnHotkey(m.WParam.ToInt32() - 1); return; }
        base.WndProc(ref m);
    }

    void OnHotkey(int index)
    {
        if (index < 0 || index >= HotKeys.Length) return;
        switch (HotKeys[index])
        {
            case Keys.O: enabled = !enabled; break;
            case Keys.B: showBox = !showBox; break;
            case Keys.L: showLines = !showLines; break;
            case Keys.D: showDot = !showDot; break;
            case Keys.OemPeriod: thickness = Math.Min(40, thickness + 1); break;
            case Keys.Oemcomma: thickness = Math.Max(1, thickness - 1); break;
            case Keys.K: colorIndex = (colorIndex + 1) % Palette.Length; break;
            case Keys.M: monitor = (CurrentScreenIndex() + 1) % Screen.AllScreens.Length; break;
            case Keys.Oemplus: opacity = Clamp(Math.Round(opacity + 0.1, 1), 0.2, 1.0); break;
            case Keys.OemMinus: opacity = Clamp(Math.Round(opacity - 0.1, 1), 0.2, 1.0); break;
            case Keys.OemCloseBrackets: boxScale = Clamp(Math.Round(boxScale + 0.05, 2), 0.25, 0.95); break;
            case Keys.OemOpenBrackets: boxScale = Clamp(Math.Round(boxScale - 0.05, 2), 0.25, 0.95); break;
        }
        Apply(true);
    }

    int CurrentScreenIndex()
    {
        Screen[] screens = Screen.AllScreens;
        if (monitor >= 0 && monitor < screens.Length) return monitor;
        for (int i = 0; i < screens.Length; i++) if (screens[i].Primary) return i;
        return 0;
    }

    static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }

    void Apply(bool save)
    {
        Screen[] screens = Screen.AllScreens;
        if (monitor >= screens.Length) monitor = -1;
        Bounds = (monitor >= 0 ? screens[monitor] : Screen.PrimaryScreen).Bounds;
        Opacity = opacity;
        Invalidate();
        KeepOnTop();
        if (save) SaveSettings();
    }

    void KeepOnTop()
    {
        if (IsHandleCreated)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE|NOMOVE|NOACTIVATE
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

    // ---------- drawing ----------
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None; // no anti-aliasing, so no pink fringes around the color key
        g.Clear(Key);
        if (!enabled) return;

        int w = ClientSize.Width, h = ClientSize.Height, cx = w / 2, cy = h / 2;
        // Thickness is in "1080p pixels" so it looks the same on 1440p / 4K screens.
        int t = Math.Max(1, (int)Math.Round(thickness * h / 1080.0)), edge = Math.Max(2, t / 3);
        List<Rectangle> bars = new List<Rectangle>();

        if (showLines)
        {
            int gap = (int)(Math.Min(w, h) * gapScale);
            bars.Add(new Rectangle(0, cy - t / 2, cx - gap, t));             // left
            bars.Add(new Rectangle(cx + gap, cy - t / 2, w - cx - gap, t));  // right
            bars.Add(new Rectangle(cx - t / 2, 0, t, cy - gap));             // top
            bars.Add(new Rectangle(cx - t / 2, cy + gap, t, h - cy - gap));  // bottom
        }
        if (showBox)
        {
            int bw = (int)(w * boxScale), bh = (int)(h * boxScale);
            int x0 = cx - bw / 2, y0 = cy - bh / 2;
            bars.Add(new Rectangle(x0, y0, bw, t));
            bars.Add(new Rectangle(x0, y0 + bh - t, bw, t));
            bars.Add(new Rectangle(x0, y0, t, bh));
            bars.Add(new Rectangle(x0 + bw - t, y0, t, bh));
        }

        // Dark outline first, then the colored fill, so it stays visible on light and dark scenes.
        using (SolidBrush outline = new SolidBrush(Color.Black))
        using (SolidBrush fill = new SolidBrush(Palette[colorIndex]))
        {
            foreach (Rectangle r in bars) { Rectangle o = r; o.Inflate(edge, edge); g.FillRectangle(outline, o); }
            foreach (Rectangle r in bars) g.FillRectangle(fill, r);
            if (showDot)
            {
                int d = Math.Max(6, (int)(t * 2.5));
                g.FillEllipse(outline, cx - d / 2 - edge, cy - d / 2 - edge, d + edge * 2, d + edge * 2);
                g.FillEllipse(fill, cx - d / 2, cy - d / 2, d, d);
            }
        }
    }

    // ---------- tray menu ----------
    static void AddItem(ToolStripItemCollection items, string text, bool isChecked, EventHandler onClick)
    {
        ToolStripMenuItem it = new ToolStripMenuItem(text);
        it.Checked = isChecked;
        it.Click += onClick;
        items.Add(it);
    }

    void BuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();
        AddItem(menu.Items, "Show overlay\tCtrl+Alt+O", enabled, delegate { enabled = !enabled; Apply(true); });
        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu.Items, "Frame box\tCtrl+Alt+B", showBox, delegate { showBox = !showBox; Apply(true); });
        AddItem(menu.Items, "Edge crosshair lines\tCtrl+Alt+L", showLines, delegate { showLines = !showLines; Apply(true); });
        AddItem(menu.Items, "Center dot	Ctrl+Alt+D", showDot, delegate { showDot = !showDot; Apply(true); });
        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem op = new ToolStripMenuItem("Opacity\tCtrl+Alt+ - / =");
        foreach (int p in new int[] { 20, 30, 40, 50, 60, 70, 80, 90, 100 })
        {
            int pp = p;
            AddItem(op.DropDownItems, p + "%", Math.Abs(opacity * 100 - p) < 1, delegate { opacity = pp / 100.0; Apply(true); });
        }
        menu.Items.Add(op);

        ToolStripMenuItem box = new ToolStripMenuItem("Box size\tCtrl+Alt+ [ / ]");
        foreach (int p in new int[] { 30, 40, 50, 55, 60, 70, 80, 90 })
        {
            int pp = p;
            AddItem(box.DropDownItems, p + "% of screen", Math.Abs(boxScale * 100 - p) < 1, delegate { boxScale = pp / 100.0; Apply(true); });
        }
        menu.Items.Add(box);

        ToolStripMenuItem thick = new ToolStripMenuItem("Line thickness	Ctrl+Alt+ , / .");
        foreach (int p in new int[] { 1, 2, 3, 4, 6, 8, 10, 12, 14, 17, 20, 25, 30 })
        {
            int pp = p;
            AddItem(thick.DropDownItems, p + " px", thickness == p, delegate { thickness = pp; Apply(true); });
        }
        menu.Items.Add(thick);

        ToolStripMenuItem gap = new ToolStripMenuItem("Gap in the middle");
        string[] gapNames = { "None", "Small", "Medium", "Large", "Extra large" };
        double[] gapValues = { 0.0, 0.05, 0.10, 0.18, 0.28 };
        for (int i = 0; i < gapValues.Length; i++)
        {
            double gv = gapValues[i];
            AddItem(gap.DropDownItems, gapNames[i], Math.Abs(gapScale - gv) < 0.001, delegate { gapScale = gv; Apply(true); });
        }
        menu.Items.Add(gap);

        ToolStripMenuItem col = new ToolStripMenuItem("Color\tCtrl+Alt+K");
        for (int i = 0; i < Palette.Length; i++)
        {
            int ci = i;
            AddItem(col.DropDownItems, PaletteNames[i], colorIndex == i, delegate { colorIndex = ci; Apply(true); });
        }
        menu.Items.Add(col);

        Screen[] screens = Screen.AllScreens;
        if (screens.Length > 1)
        {
            ToolStripMenuItem mon = new ToolStripMenuItem("Monitor	Ctrl+Alt+M");
            AddItem(mon.DropDownItems, "Main monitor", monitor < 0, delegate { monitor = -1; Apply(true); });
            for (int i = 0; i < screens.Length; i++)
            {
                int mi = i;
                string label = "Monitor " + (i + 1) + " (" + screens[i].Bounds.Width + "x" + screens[i].Bounds.Height + ")" + (screens[i].Primary ? " - main" : "");
                AddItem(mon.DropDownItems, label, monitor == i, delegate { monitor = mi; Apply(true); });
            }
            menu.Items.Add(mon);
        }

        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu.Items, "Reset to defaults", false, delegate { ResetDefaults(); Apply(true); });
        AddItem(menu.Items, "Exit", false, delegate { Close(); });
    }

    static Icon MakeIcon()
    {
        Bitmap bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 40, 40))) g.FillRectangle(b, 0, 0, 32, 32);
            using (Pen p = new Pen(Color.White, 3)) g.DrawRectangle(p, 7, 7, 18, 18);
            using (SolidBrush b = new SolidBrush(Color.Cyan))
            {
                g.FillRectangle(b, 0, 15, 11, 3); g.FillRectangle(b, 21, 15, 11, 3);
                g.FillRectangle(b, 15, 0, 3, 11); g.FillRectangle(b, 15, 21, 3, 11);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ---------- settings file ----------
    void LoadSettings()
    {
        try
        {
            if (!File.Exists(settingsPath)) return;
            foreach (string line in File.ReadAllLines(settingsPath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
                switch (k)
                {
                    case "enabled": enabled = v == "1"; break;
                    case "box": showBox = v == "1"; break;
                    case "lines": showLines = v == "1"; break;
                    case "dot": showDot = v == "1"; break;
                    case "boxScale": boxScale = double.Parse(v, inv); break;
                    case "gapScale": gapScale = double.Parse(v, inv); break;
                    case "opacity": opacity = double.Parse(v, inv); break;
                    case "thickness": thickness = int.Parse(v, inv); break;
                    case "color": colorIndex = int.Parse(v, inv) % Palette.Length; break;
                    case "monitor": monitor = int.Parse(v, inv); break;
                }
            }
        }
        catch { ResetDefaults(); }
    }

    void SaveSettings()
    {
        try
        {
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            File.WriteAllLines(settingsPath, new string[] {
                "enabled=" + (enabled ? "1" : "0"),
                "box=" + (showBox ? "1" : "0"),
                "lines=" + (showLines ? "1" : "0"),
                "dot=" + (showDot ? "1" : "0"),
                "boxScale=" + boxScale.ToString(inv),
                "gapScale=" + gapScale.ToString(inv),
                "opacity=" + opacity.ToString(inv),
                "thickness=" + thickness.ToString(inv),
                "color=" + colorIndex.ToString(inv),
                "monitor=" + monitor.ToString(inv)
            });
        }
        catch { }
    }

    public static void LogError(string text)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ComfortOverlay");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now + "\r\n" + text + "\r\n\r\n");
        }
        catch { }
    }

    // ---------- entry point ----------
    [STAThread]
    public static void Main()
    {
        try { Run(); }
        catch (Exception ex)
        {
            LogError(ex.ToString());
            MessageBox.Show("Comfort Overlay failed to start:\n\n" + ex.Message, "Comfort Overlay");
        }
    }

    public static void Run()
    {
        bool created;
        using (System.Threading.Mutex mutex = new System.Threading.Mutex(true, "ComfortOverlay_SingleInstance", out created))
        {
            if (!created)
            {
                MessageBox.Show("Comfort Overlay is already running - look for its icon in the system tray.", "Comfort Overlay");
                return;
            }
            SetProcessDPIAware();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e) { LogError(e.Exception.ToString()); };
            Application.EnableVisualStyles();
            Application.Run(new ComfortOverlay());
        }
    }
}
