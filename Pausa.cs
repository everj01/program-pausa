// Pausa — recordatorio periódico ultraligero para Windows 10/11
// Requiere solo .NET Framework 4.8 (ya viene instalado en Windows).
// Compilar: ejecutar compilar.bat  (no hace falta Visual Studio)
// Código compatible con C# 5 para que lo compile el csc.exe incluido en Windows.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using WinTimer = System.Windows.Forms.Timer;

namespace Pausa
{
    // ───────────────────────────── Arranque ─────────────────────────────
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new System.Threading.Mutex(true, "Pausa_Recordatorio_UnicaInstancia", out created))
            {
                if (!created) return; // ya hay una abierta

                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var g = Graphics.FromHwnd(IntPtr.Zero)) Ui.Scale = g.DpiX / 96f;

                bool startHidden = false;
                foreach (var a in args)
                    if (a.Equals("/min", StringComparison.OrdinalIgnoreCase)) startHidden = true;

                AutoStart.RefreshPath();
                var form = new MainForm(startHidden);
                if (!startHidden) form.Show();
                Application.Run(new ApplicationContext());
                GC.KeepAlive(form);
            }
        }
    }

    // ───────────────────────────── Estilo ─────────────────────────────
    static class Ui
    {
        public static float Scale = 1f;
        public static int S(float v) { return (int)Math.Round(v * Scale); }

        public static readonly Color Bg = Color.FromArgb(17, 19, 23);
        public static readonly Color Surface = Color.FromArgb(27, 30, 36);
        public static readonly Color SurfaceHover = Color.FromArgb(36, 40, 48);
        public static readonly Color Border = Color.FromArgb(44, 49, 58);
        public static readonly Color Track = Color.FromArgb(34, 38, 46);
        public static readonly Color Text = Color.FromArgb(236, 238, 242);
        public static readonly Color Muted = Color.FromArgb(132, 139, 151);
        public static readonly Color Accent = Color.FromArgb(70, 214, 160);
        public static readonly Color Amber = Color.FromArgb(242, 182, 80);
        public static readonly Color OnAccent = Color.FromArgb(12, 24, 19);

        public static Font F(float size, FontStyle style, params string[] families)
        {
            foreach (var name in families)
            {
                try
                {
                    var ff = new FontFamily(name);
                    if (ff.IsStyleAvailable(style)) return new Font(ff, size, style, GraphicsUnit.Point);
                }
                catch (ArgumentException) { }
            }
            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
        }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Prep(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static Icon MakeIcon(Color c, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float w = Math.Max(2f, size * 0.17f);
                var r = new RectangleF(w / 2 + 0.5f, w / 2 + 0.5f, size - w - 1, size - w - 1);
                using (var p = new Pen(Color.FromArgb(85, c), w)) g.DrawEllipse(p, r);
                using (var p = new Pen(c, w)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawArc(p, r, -90, 270); }
                float dd = size * 0.24f;
                using (var b = new SolidBrush(c)) g.FillEllipse(b, (size - dd) / 2, (size - dd) / 2, dd, dd);
            }
            Icon ic = Icon.FromHandle(bmp.GetHicon()); // se crean pocas y viven toda la sesión
            bmp.Dispose();
            return ic;
        }
    }

    // ───────────────────────────── Win32 ─────────────────────────────
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);

        // Modo oscuro + esquinas redondeadas + borde fino (Windows 11). En Windows 10 se ignora.
        public static void StyleWindow(IntPtr h)
        {
            try
            {
                int dark = 1; DwmSetWindowAttribute(h, 20, ref dark, 4);
                int round = 2; DwmSetWindowAttribute(h, 33, ref round, 4);
                int border = ColorTranslator.ToWin32(Ui.Border); DwmSetWindowAttribute(h, 34, ref border, 4);
            }
            catch { }
        }

        // Devuelve al sistema la memoria que no se usa mientras la app está oculta.
        public static void TrimMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                using (var p = Process.GetCurrentProcess())
                    SetProcessWorkingSetSize(p.Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
            catch { }
        }
    }

    // ───────────────────────────── Ajustes (portable) ─────────────────────────────
    static class Settings
    {
        static string ExeDirFile() { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pausa.ini"); }
        static string AppDataFile()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pausa", "Pausa.ini");
        }

        public static void Load(ref string message, ref int minutes)
        {
            foreach (var path in new[] { ExeDirFile(), AppDataFile() })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1);
                        int n;
                        if (k == "mensaje") message = v;
                        else if (k == "minutos" && int.TryParse(v, out n)) minutes = Math.Max(1, Math.Min(240, n));
                    }
                    return;
                }
                catch { }
            }
        }

        public static void Save(string message, int minutes)
        {
            string content = "mensaje=" + (message ?? "").Replace("\r", " ").Replace("\n", " ") + "\r\nminutos=" + minutes + "\r\n";
            try { File.WriteAllText(ExeDirFile(), content, Encoding.UTF8); return; }
            catch { }
            try
            {
                string p = AppDataFile();
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, content, Encoding.UTF8);
            }
            catch { }
        }
    }

    static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Pausa";
        static string Command() { return "\"" + Application.ExecutablePath + "\" /min"; }

        public static bool IsEnabled()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(Name) != null; }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(Name, Command());
                    else k.DeleteValue(Name, false);
                }
            }
            catch { }
        }

        // Si moviste el .exe de carpeta, actualiza la ruta del inicio automático.
        public static void RefreshPath() { if (IsEnabled()) Set(true); }
    }

    // ───────────────────────────── Controles dibujados a mano ─────────────────────────────
    class UiControl : Control
    {
        protected bool hover, pressed;

        public UiControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected RectangleF Frame() { return new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f); }
    }

    class PillButton : UiControl
    {
        public bool Primary;
        static Font font;

        public PillButton() { if (font == null) font = Ui.F(10.5f, FontStyle.Regular, "Segoe UI Semibold", "Segoe UI"); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Ui.Prep(g);
            Color fill, fg;
            if (Primary)
            {
                fill = pressed ? Ui.Blend(Ui.Accent, Color.Black, 0.12f) : hover ? Ui.Blend(Ui.Accent, Color.White, 0.14f) : Ui.Accent;
                fg = Ui.OnAccent;
            }
            else
            {
                fill = pressed ? Ui.Border : hover ? Ui.SurfaceHover : Ui.Surface;
                fg = Ui.Text;
            }
            var r = Frame();
            using (var path = Ui.Round(r, r.Height / 2))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (!Primary) using (var p = new Pen(Ui.Border)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Text, font, ClientRectangle, fg, fill,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    class Chip : UiControl
    {
        bool selected;
        public bool Selected { get { return selected; } set { if (selected != value) { selected = value; Invalidate(); } } }
        static Font font;

        public Chip() { if (font == null) font = Ui.F(10f, FontStyle.Regular, "Segoe UI Semibold", "Segoe UI"); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Ui.Prep(g);
            Color fill = selected ? Ui.Blend(Ui.Bg, Ui.Accent, 0.16f) : hover ? Ui.SurfaceHover : Ui.Surface;
            Color border = selected ? Ui.Blend(Ui.Bg, Ui.Accent, 0.6f) : Ui.Border;
            Color fg = selected ? Ui.Accent : hover ? Ui.Text : Ui.Muted;
            using (var path = Ui.Round(Frame(), Ui.S(10)))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen(border)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Text, font, ClientRectangle, fg, fill,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    class Stepper : UiControl
    {
        int value = 45;
        public int Minimum = 1, Maximum = 240;
        public event EventHandler ValueChanged;
        int hot, held;
        readonly WinTimer repeat = new WinTimer();
        static Font font;

        public Stepper()
        {
            if (font == null) font = Ui.F(10.5f, FontStyle.Regular, "Segoe UI Semibold", "Segoe UI");
            Cursor = Cursors.Default;
            repeat.Tick += delegate { repeat.Interval = 70; if (held != 0) Value += held; };
        }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(Minimum, Math.Min(Maximum, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                var h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        int Zone(Point p) { if (p.X < Height) return -1; if (p.X > Width - Height) return 1; return 0; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int z = Zone(e.Location);
            if (z != hot) { hot = z; Cursor = z == 0 ? Cursors.Default : Cursors.Hand; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hot = 0; held = 0; repeat.Stop(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            held = Zone(e.Location);
            if (held != 0) { Value += held; repeat.Interval = 380; repeat.Start(); }
        }
        protected override void OnMouseUp(MouseEventArgs e) { held = 0; repeat.Stop(); base.OnMouseUp(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { Value += e.Delta > 0 ? 1 : -1; base.OnMouseWheel(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Ui.Prep(g);
            var r = Frame();
            using (var path = Ui.Round(r, Ui.S(10)))
            {
                using (var b = new SolidBrush(Ui.Surface)) g.FillPath(b, path);
                if (hot != 0)
                {
                    var state = g.Save();
                    g.SetClip(path);
                    float x = hot < 0 ? 0 : Width - Height;
                    using (var b = new SolidBrush(pressed ? Ui.Border : Ui.SurfaceHover)) g.FillRectangle(b, x, 0, Height, Height);
                    g.Restore(state);
                }
                using (var p = new Pen(Ui.Border)) g.DrawPath(p, path);
            }
            float c = Height / 2f, arm = Ui.S(5);
            using (var p = new Pen(value > Minimum ? Ui.Text : Ui.Border, Math.Max(1.5f, Ui.Scale * 1.6f)))
                g.DrawLine(p, c - arm, c, c + arm, c);
            float px = Width - c;
            using (var p = new Pen(value < Maximum ? Ui.Text : Ui.Border, Math.Max(1.5f, Ui.Scale * 1.6f)))
            {
                g.DrawLine(p, px - arm, c, px + arm, c);
                g.DrawLine(p, px, c - arm, px, c + arm);
            }
            var mid = new Rectangle(Height, 0, Width - Height * 2, Height);
            TextRenderer.DrawText(g, value + " min", font, mid, Ui.Text, Ui.Surface,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing) { if (disposing) repeat.Dispose(); base.Dispose(disposing); }
    }

    class ToggleSwitch : UiControl
    {
        bool on;
        public event EventHandler CheckedChanged;
        static Font font;

        public ToggleSwitch() { if (font == null) font = Ui.F(10f, FontStyle.Regular, "Segoe UI"); }

        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Ui.Prep(g);
            TextRenderer.DrawText(g, Text, font, new Rectangle(0, 0, Width - Ui.S(56), Height), hover ? Ui.Text : Ui.Muted, BackColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            float w = Ui.S(40), h = Ui.S(22);
            var track = new RectangleF(Width - w - 1, (Height - h) / 2f, w, h);
            using (var path = Ui.Round(track, h / 2))
            {
                using (var b = new SolidBrush(on ? Ui.Accent : Ui.Track)) g.FillPath(b, path);
                if (!on) using (var p = new Pen(Ui.Border)) g.DrawPath(p, path);
            }
            float k = h - Ui.S(8);
            float kx = on ? track.Right - k - Ui.S(4) : track.X + Ui.S(4);
            using (var b = new SolidBrush(on ? Ui.OnAccent : Ui.Muted)) g.FillEllipse(b, kx, track.Y + Ui.S(4), k, k);
        }
    }

    class TitleButton : UiControl
    {
        public readonly bool IsClose;
        public TitleButton(bool isClose) { IsClose = isClose; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Ui.Prep(g);
            if (hover)
            {
                Color bg = IsClose ? Color.FromArgb(196, 43, 28) : Ui.SurfaceHover;
                using (var path = Ui.Round(Frame(), Ui.S(8)))
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
            }
            float cx = Width / 2f, cy = Height / 2f, a = Ui.S(5);
            using (var p = new Pen(hover ? Color.White : Ui.Muted, Math.Max(1.2f, Ui.Scale * 1.3f)))
            {
                if (IsClose) { g.DrawLine(p, cx - a, cy - a, cx + a, cy + a); g.DrawLine(p, cx - a, cy + a, cx + a, cy - a); }
                else g.DrawLine(p, cx - a, cy, cx + a, cy);
            }
        }
    }

    class InputBox : UiControl
    {
        public readonly TextBox Box;

        public InputBox()
        {
            Cursor = Cursors.IBeam;
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Ui.Surface,
                ForeColor = Ui.Text,
                Font = Ui.F(10.5f, FontStyle.Regular, "Segoe UI"),
                MaxLength = 140
            };
            Box.HandleCreated += delegate { try { Native.SendMessage(Box.Handle, 0x1501, (IntPtr)1, "Escribe tu recordatorio…"); } catch { } };
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Controls.Add(Box);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int pad = Ui.S(14);
            Box.SetBounds(pad, (Height - Box.Height) / 2, Width - pad * 2, Box.Height);
            base.OnLayout(e);
        }

        protected override void OnMouseDown(MouseEventArgs e) { Box.Focus(); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Ui.Prep(g);
            using (var path = Ui.Round(Frame(), Ui.S(10)))
            {
                using (var b = new SolidBrush(Ui.Surface)) g.FillPath(b, path);
                using (var p = new Pen(Box.Focused ? Ui.Blend(Ui.Bg, Ui.Accent, 0.75f) : Ui.Border)) g.DrawPath(p, path);
            }
        }
    }

    // Menú del icono de la bandeja en modo oscuro
    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Ui.Surface; } }
        public override Color MenuBorder { get { return Ui.Border; } }
        public override Color MenuItemBorder { get { return Ui.SurfaceHover; } }
        public override Color MenuItemSelected { get { return Ui.SurfaceHover; } }
        public override Color ImageMarginGradientBegin { get { return Ui.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Ui.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Ui.Surface; } }
        public override Color SeparatorDark { get { return Ui.Border; } }
        public override Color SeparatorLight { get { return Ui.Surface; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Ui.Text; base.OnRenderItemText(e); }
    }

    // ───────────────────────────── Ventana principal ─────────────────────────────
    enum RunState { Stopped, Running, Paused }

    class MainForm : Form
    {
        static readonly int[] Presets = { 15, 30, 45, 60 };

        RunState state = RunState.Stopped;
        DateTime target;
        TimeSpan pausedLeft;
        int minutes = 45;
        string message = "Levántate, estira las piernas y toma agua.";
        bool exiting, balloonShown;

        readonly WinTimer ticker = new WinTimer();
        NotifyIcon tray;
        ToolStripMenuItem miToggle;
        Icon icoRun, icoPause, icoStop;
        RunState? iconState;
        string lastTip;
        int lastSecond = -1;

        TitleButton btnMin, btnClose;
        InputBox input;
        Stepper stepper;
        Chip[] chips;
        ToggleSwitch autoStart;
        PillButton btnMain, btnReset;
        Font fTitle, fTime, fStatus, fLabel;

        AlertForm alert;
        SoundPlayer player;

        static int S(float v) { return Ui.S(v); }
        static Rectangle R(int x, int y, int w, int h) { return new Rectangle(S(x), S(y), S(w), S(h)); }

        public MainForm(bool startHidden)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Ui.Bg;
            DoubleBuffered = true;
            KeyPreview = true;
            Text = "Pausa";
            ClientSize = new Size(S(360), S(554));

            Settings.Load(ref message, ref minutes);

            fTitle = Ui.F(10.5f, FontStyle.Regular, "Segoe UI Semibold", "Segoe UI");
            fTime = Ui.F(34f, FontStyle.Regular, "Segoe UI Light", "Segoe UI");
            fStatus = Ui.F(9.5f, FontStyle.Regular, "Segoe UI");
            fLabel = Ui.F(9f, FontStyle.Regular, "Segoe UI");

            int small = SystemInformation.SmallIconSize.Width;
            icoRun = Ui.MakeIcon(Ui.Accent, small);
            icoPause = Ui.MakeIcon(Ui.Amber, small);
            icoStop = Ui.MakeIcon(Color.FromArgb(150, 156, 168), small);
            Icon = Ui.MakeIcon(Ui.Accent, S(32));

            BuildControls();
            BuildTray();

            ticker.Interval = 500;
            ticker.Tick += delegate { OnTick(); };

            if (startHidden) StartTimer();
            RefreshUi(true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000; // WS_MINIMIZEBOX: permite minimizar desde la barra de tareas
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.StyleWindow(Handle); }

        void BuildControls()
        {
            btnMin = new TitleButton(false) { Bounds = R(270, 8, 40, 32) };
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            btnClose = new TitleButton(true) { Bounds = R(314, 8, 40, 32) };
            btnClose.Click += delegate { HideToTray(); };

            input = new InputBox { Bounds = R(24, 308, 312, 44) };
            input.Box.Text = message;
            input.Box.TextChanged += delegate { message = input.Box.Text.Trim(); };
            input.Box.Leave += delegate { Settings.Save(message, minutes); };
            input.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ActiveControl = null; Settings.Save(message, minutes); }
            };

            stepper = new Stepper { Bounds = R(24, 388, 124, 40) };
            stepper.Value = minutes;
            stepper.ValueChanged += delegate { OnMinutesChanged(); };

            chips = new Chip[Presets.Length];
            for (int i = 0; i < Presets.Length; i++)
            {
                int n = Presets[i];
                var c = new Chip { Text = n.ToString(), Bounds = R(158 + i * 46, 388, 40, 40) };
                c.Click += delegate { stepper.Value = n; };
                chips[i] = c;
            }
            UpdateChips();

            autoStart = new ToggleSwitch { Text = "Iniciar con Windows", Bounds = R(24, 444, 312, 28) };
            autoStart.Checked = AutoStart.IsEnabled();
            autoStart.CheckedChanged += delegate { AutoStart.Set(autoStart.Checked); };

            btnMain = new PillButton { Primary = true, Bounds = R(24, 486, 204, 44) };
            btnMain.Click += delegate { ToggleRun(); };
            btnReset = new PillButton { Text = "Reiniciar", Bounds = R(236, 486, 100, 44) };
            btnReset.Click += delegate { ResetTimer(); };

            Controls.Add(btnMin); Controls.Add(btnClose); Controls.Add(input); Controls.Add(stepper);
            Controls.AddRange(chips);
            Controls.Add(autoStart); Controls.Add(btnMain); Controls.Add(btnReset);
        }

        void BuildTray()
        {
            var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = Ui.F(9.5f, FontStyle.Regular, "Segoe UI") };
            var miOpen = new ToolStripMenuItem("Abrir Pausa", null, delegate { ShowMain(); });
            miToggle = new ToolStripMenuItem("Iniciar", null, delegate { ToggleRun(); });
            var miReset = new ToolStripMenuItem("Reiniciar", null, delegate { ResetTimer(); });
            var miExit = new ToolStripMenuItem("Salir", null, delegate { ExitApp(); });
            foreach (var mi in new[] { miOpen, miToggle, miReset, miExit })
                mi.Padding = new Padding(S(6), S(4), S(6), S(4));
            menu.Items.AddRange(new ToolStripItem[] { miOpen, miToggle, miReset, new ToolStripSeparator(), miExit });

            tray = new NotifyIcon { Icon = icoStop, Text = "Pausa", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (Visible && WindowState != FormWindowState.Minimized) HideToTray(); else ShowMain();
            };
        }

        // ───── Lógica del temporizador ─────
        TimeSpan Remaining()
        {
            if (state == RunState.Running) { var r = target - DateTime.Now; return r < TimeSpan.Zero ? TimeSpan.Zero : r; }
            if (state == RunState.Paused) return pausedLeft;
            return TimeSpan.FromMinutes(minutes);
        }

        void ToggleRun()
        {
            if (state == RunState.Stopped) StartTimer();
            else if (state == RunState.Running) PauseTimer();
            else ResumeTimer();
        }

        void StartTimer()
        {
            target = DateTime.Now.AddMinutes(minutes);
            state = RunState.Running;
            ticker.Start();
            Settings.Save(message, minutes);
            RefreshUi(true);
        }

        void PauseTimer()
        {
            pausedLeft = target - DateTime.Now;
            if (pausedLeft < TimeSpan.Zero) pausedLeft = TimeSpan.Zero;
            state = RunState.Paused;
            ticker.Stop(); // sin reloj corriendo: 0 % de CPU
            RefreshUi(true);
        }

        void ResumeTimer()
        {
            target = DateTime.Now + pausedLeft;
            state = RunState.Running;
            ticker.Start();
            RefreshUi(true);
        }

        void ResetTimer()
        {
            state = RunState.Stopped;
            ticker.Stop();
            RefreshUi(true);
        }

        void OnMinutesChanged()
        {
            minutes = stepper.Value;
            UpdateChips();
            if (state == RunState.Running) target = DateTime.Now.AddMinutes(minutes);
            else if (state == RunState.Paused) pausedLeft = TimeSpan.FromMinutes(minutes);
            RefreshUi(true);
        }

        void UpdateChips() { for (int i = 0; i < chips.Length; i++) chips[i].Selected = Presets[i] == minutes; }

        void OnTick()
        {
            if (state != RunState.Running) return;
            var now = DateTime.Now;
            if (now >= target)
            {
                // El siguiente ciclo cuenta desde ahora (si la PC estuvo suspendida, avisa una sola vez).
                target = now.AddMinutes(minutes);
                ShowAlert();
            }
            RefreshUi(false);
        }

        void RefreshUi(bool force)
        {
            int sec = (int)Math.Ceiling(Remaining().TotalSeconds);
            bool onScreen = Visible && WindowState != FormWindowState.Minimized;
            if (onScreen && (force || sec != lastSecond))
            {
                Invalidate(R(60, 50, 240, 230));
                Invalidate(R(14, 10, 120, 30));
            }
            lastSecond = sec;

            if (force)
            {
                string label = state == RunState.Running ? "Pausar" : state == RunState.Paused ? "Reanudar" : "Iniciar";
                btnMain.Text = label;
                miToggle.Text = label;
            }

            if (iconState != state)
            {
                iconState = state;
                tray.Icon = state == RunState.Running ? icoRun : state == RunState.Paused ? icoPause : icoStop;
            }
            string tip = state == RunState.Running ? "Pausa · próximo aviso " + target.ToString("HH:mm")
                       : state == RunState.Paused ? "Pausa · en pausa"
                       : "Pausa · detenido";
            if (tip != lastTip) { lastTip = tip; tray.Text = tip; }
        }

        static string Format(int s)
        {
            if (s < 0) s = 0;
            int h = s / 3600, m = (s % 3600) / 60, x = s % 60;
            return h > 0 ? string.Format("{0}:{1:00}:{2:00}", h, m, x) : string.Format("{0:00}:{1:00}", m, x);
        }

        Color StateColor() { return state == RunState.Paused ? Ui.Amber : Ui.Accent; }

        // ───── Dibujo ─────
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Prep(g);
            Color ac = StateColor();

            // Barra de título
            float dot = S(8);
            using (var b = new SolidBrush(state == RunState.Stopped ? Ui.Muted : ac)) g.FillEllipse(b, S(24), S(24) - dot / 2, dot, dot);
            TextRenderer.DrawText(g, "Pausa", fTitle, new Rectangle(S(40), 0, S(150), S(48)), Ui.Text, Ui.Bg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            // Anillo de progreso
            float pw = S(10);
            var ring = new RectangleF(S(76) + pw / 2, S(60) + pw / 2, S(208) - pw, S(208) - pw);
            using (var p = new Pen(Ui.Track, pw)) g.DrawEllipse(p, ring);
            double total = minutes * 60.0;
            double left = Remaining().TotalSeconds;
            float sweep = (float)(360.0 * Math.Max(0, Math.Min(1, left / total)));
            if (sweep > 0.5f)
            {
                Color arc = state == RunState.Stopped ? Ui.Blend(Ui.Track, ac, 0.45f) : ac;
                using (var p = new Pen(arc, pw)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawArc(p, ring, -90, sweep); }
            }

            TextRenderer.DrawText(g, Format((int)Math.Ceiling(left)), fTime, R(76, 124, 208, 60), Ui.Text, Ui.Bg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            string status = state == RunState.Running ? "Próximo aviso · " + target.ToString("HH:mm")
                          : state == RunState.Paused ? "En pausa" : "Listo para empezar";
            TextRenderer.DrawText(g, status, fStatus, R(76, 186, 208, 24), state == RunState.Paused ? Ui.Amber : Ui.Muted, Ui.Bg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            // Etiquetas
            TextRenderer.DrawText(g, "MENSAJE", fLabel, new Point(S(25), S(286)), Ui.Muted, Ui.Bg, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "REPETIR CADA", fLabel, new Point(S(25), S(366)), Ui.Muted, Ui.Bg, TextFormatFlags.NoPadding);
        }

        // Arrastrar la ventana desde la barra de título
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < S(48))
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
            }
            else ActiveControl = null;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) HideToTray();
            base.OnKeyDown(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            input.Box.SelectionStart = input.Box.TextLength;
            ActiveControl = null;
        }

        // ───── Aviso ─────
        void ShowAlert()
        {
            if (alert != null && !alert.IsDisposed) alert.Close();
            string text = string.IsNullOrEmpty(message) ? "Es hora de tomar un descanso." : message;
            alert = new AlertForm(text);
            alert.FormClosed += delegate { if (!Visible) Native.TrimMemory(); };
            alert.Show();
            PlaySound();
        }

        void PlaySound()
        {
            try
            {
                string wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Media\Windows Notify System Generic.wav");
                if (File.Exists(wav))
                {
                    if (player == null) player = new SoundPlayer(wav);
                    player.Play();
                    return;
                }
            }
            catch { }
            SystemSounds.Asterisk.Play();
        }

        // ───── Bandeja / salida ─────
        void ShowMain()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            RefreshUi(true);
        }

        void HideToTray()
        {
            Settings.Save(message, minutes);
            Hide();
            if (!balloonShown)
            {
                balloonShown = true;
                tray.ShowBalloonTip(3000, "Pausa sigue activa", "Te avisaré a tiempo. Haz clic en el icono para abrirla.", ToolTipIcon.None);
            }
            Native.TrimMemory();
        }

        void ExitApp()
        {
            exiting = true;
            Settings.Save(message, minutes);
            ticker.Stop();
            tray.Visible = false;
            tray.Dispose();
            if (alert != null && !alert.IsDisposed) alert.Close();
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // la X o Alt+F4 la mandan a la bandeja
                HideToTray();
                return;
            }
            if (!exiting) { Settings.Save(message, minutes); tray.Visible = false; }
            base.OnFormClosing(e);
        }
    }

    // ───────────────────────────── Ventana de aviso ─────────────────────────────
    class AlertForm : Form
    {
        readonly string text;
        readonly WinTimer anim = new WinTimer();
        readonly Font fHead, fMsg;
        readonly Rectangle msgRect;
        readonly int targetTop;
        int phase;

        public AlertForm(string message)
        {
            text = message;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Ui.Surface;
            DoubleBuffered = true;
            Opacity = 0;
            Cursor = Cursors.Hand;

            fHead = Ui.F(9f, FontStyle.Regular, "Segoe UI");
            fMsg = Ui.F(13f, FontStyle.Regular, "Segoe UI Semibold", "Segoe UI");

            int w = Ui.S(380), pad = Ui.S(22), textW = w - pad * 2;
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            Size ms = TextRenderer.MeasureText(text, fMsg, new Size(textW, int.MaxValue), flags);
            msgRect = new Rectangle(pad, pad + Ui.S(30), textW, Math.Min(ms.Height, Ui.S(150)));
            int btnTop = msgRect.Bottom + Ui.S(18);
            ClientSize = new Size(w, btnTop + Ui.S(36) + pad);

            var ok = new PillButton { Primary = true, Text = "Listo", BackColor = Ui.Surface, Bounds = new Rectangle(w - pad - Ui.S(96), btnTop, Ui.S(96), Ui.S(36)) };
            ok.Click += delegate { FadeOut(); };
            Controls.Add(ok);

            var wa = Screen.PrimaryScreen.WorkingArea;
            targetTop = wa.Bottom - Height - Ui.S(16);
            Location = new Point(wa.Right - Width - Ui.S(16), targetTop + Ui.S(16));

            anim.Interval = 15;
            anim.Tick += delegate { Animate(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } } // no te roba el foco mientras escribes

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x8; // WS_EX_TOOLWINDOW | WS_EX_TOPMOST
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.StyleWindow(Handle); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); phase = 1; anim.Start(); }
        protected override void OnMouseClick(MouseEventArgs e) { FadeOut(); base.OnMouseClick(e); }

        void FadeOut() { phase = -1; anim.Start(); }

        void Animate()
        {
            if (phase > 0)
            {
                Opacity = Math.Min(1.0, Opacity + 0.08);
                int d = targetTop - Top;
                if (d < 0) Top += Math.Min(-1, d / 4);
                if (Opacity >= 1.0 && Top <= targetTop) { Top = targetTop; anim.Stop(); }
            }
            else
            {
                Opacity = Math.Max(0, Opacity - 0.12);
                if (Opacity <= 0) { anim.Stop(); Close(); }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Ui.Prep(g);
            int pad = Ui.S(22);
            float dot = Ui.S(8);
            using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, pad, pad + Ui.S(9) - dot / 2, dot, dot);
            var head = new Rectangle(pad + Ui.S(16), pad, ClientSize.Width - pad * 2 - Ui.S(16), Ui.S(18));
            TextRenderer.DrawText(g, "Momento de una pausa", fHead, head, Ui.Muted, Ui.Surface,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, DateTime.Now.ToString("HH:mm"), fHead, head, Ui.Muted, Ui.Surface,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, text, fMsg, msgRect, Ui.Text, Ui.Surface,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { anim.Dispose(); fHead.Dispose(); fMsg.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
