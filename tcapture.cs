// Tcapture - Windows 11 Snipping Tool style screen capture utility
// Build: build.cmd  (uses the .NET Framework 4.x csc.exe that ships with Windows)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TCapture
{
    enum SnipMode { Rectangle, Window, FullScreen, Freeform }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int s);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
        public const int WM_HOTKEY = 0x0312;

        public static void EnableDpi()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        // Visible top-level windows in Z-order (topmost first), screen coordinates.
        public static List<Rectangle> TopLevelWindows(IntPtr exclude)
        {
            var list = new List<Rectangle>();
            EnumWindows((h, l) =>
            {
                if (h == exclude || !IsWindowVisible(h) || IsIconic(h)) return true;
                if ((GetWindowLong(h, -20) & 0x80) != 0) return true; // WS_EX_TOOLWINDOW
                int cloaked;
                if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                if (GetWindowTextLength(h) == 0) return true;
                RECT r;
                if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0) GetWindowRect(h, out r);
                var rc = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
                if (rc.Width > 8 && rc.Height > 8) list.Add(rc);
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Native.EnableDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            using (new System.Threading.Mutex(true, "tcapture_SingleInstance", out created))
            {
                if (!created) { MessageBox.Show("Tcapture가 이미 실행 중입니다.", "Tcapture"); return; }
                Application.Run(new MainForm());
            }
        }

        public static Bitmap GrabVirtualScreen(out Rectangle bounds)
        {
            bounds = SystemInformation.VirtualScreen;
            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }

        public static Icon MakeIcon()
        {
            var b = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(Color.FromArgb(0, 120, 212))) g.FillEllipse(br, 1, 1, 30, 30);
                using (var p = new Pen(Color.White, 3)) { g.DrawLine(p, 9, 9, 23, 23); g.DrawLine(p, 23, 9, 9, 23); }
                using (var br = new SolidBrush(Color.White)) { g.FillEllipse(br, 5, 19, 8, 8); g.FillEllipse(br, 19, 19, 8, 8); }
            }
            return Icon.FromHandle(b.GetHicon());
        }
    }

    // ---------------------------------------------------------------- main toolbar window
    class MainForm : Form
    {
        const int HOTKEY_ID = 0x5443;
        SnipMode mode = SnipMode.Rectangle;
        int delay = 0;
        readonly NotifyIcon tray;
        readonly ToolStripDropDownButton modeBtn, delayBtn;
        bool autoSave = true;

        public MainForm()
        {
            Text = "Tcapture";
            Icon = Program.MakeIcon();
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 70);
            Font = new Font("Segoe UI", 10f);

            var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, ImageScalingSize = new Size(20, 20), Padding = new Padding(6) };
            var newBtn = new ToolStripButton("＋ 새 캡처") { Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 95, 184) };
            newBtn.Click += (s, e) => StartSnip();
            modeBtn = new ToolStripDropDownButton();
            foreach (SnipMode m in Enum.GetValues(typeof(SnipMode)))
            {
                var mm = m;
                modeBtn.DropDownItems.Add(ModeName(m), null, (s, e) => { mode = mm; UpdateLabels(); });
            }
            delayBtn = new ToolStripDropDownButton();
            foreach (int d in new[] { 0, 3, 5, 10 })
            {
                var dd = d;
                delayBtn.DropDownItems.Add(d == 0 ? "지연 없음" : d + "초 지연", null, (s, e) => { delay = dd; UpdateLabels(); });
            }
            var opt = new ToolStripDropDownButton("⋯");
            var autoItem = new ToolStripMenuItem("스크린샷 폴더에 자동 저장") { Checked = autoSave, CheckOnClick = true };
            autoItem.CheckedChanged += (s, e) => autoSave = autoItem.Checked;
            opt.DropDownItems.Add(autoItem);
            opt.DropDownItems.Add("저장 폴더 열기", null, (s, e) => { Directory.CreateDirectory(SaveDir); System.Diagnostics.Process.Start(SaveDir); });
            opt.DropDownItems.Add("이미지 열기...", null, (s, e) => OpenImage());
            opt.DropDownItems.Add(new ToolStripSeparator());
            opt.DropDownItems.Add("종료", null, (s, e) => ExitApp());
            ts.Items.AddRange(new ToolStripItem[] { newBtn, new ToolStripSeparator(), modeBtn, delayBtn, opt });
            Controls.Add(ts);

            var hint = new Label { Text = "Ctrl+Shift+S 또는 PrtScn으로 어디서든 캡처 · Esc로 취소", Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 9f) };
            Controls.Add(hint);

            tray = new NotifyIcon { Icon = Icon, Text = "Tcapture", Visible = true };
            var cm = new ContextMenuStrip();
            cm.Items.Add("새 캡처", null, (s, e) => StartSnip());
            cm.Items.Add("열기", null, (s, e) => ShowMain());
            cm.Items.Add("종료", null, (s, e) => ExitApp());
            tray.ContextMenuStrip = cm;
            tray.DoubleClick += (s, e) => ShowMain();
            UpdateLabels();
        }

        static string ModeName(SnipMode m)
        {
            switch (m)
            {
                case SnipMode.Rectangle: return "▭ 사각형";
                case SnipMode.Window: return "🗔 창";
                case SnipMode.FullScreen: return "🖵 전체 화면";
                default: return "✎ 자유형";
            }
        }

        void UpdateLabels()
        {
            modeBtn.Text = ModeName(mode);
            delayBtn.Text = delay == 0 ? "⏱ 지연 없음" : "⏱ " + delay + "초";
        }

        public static string SaveDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RegisterHotKey(Handle, HOTKEY_ID, 0x0002 | 0x0004 | 0x4000, (uint)Keys.S); // Ctrl+Shift+S, NOREPEAT
            Native.RegisterHotKey(Handle, HOTKEY_ID + 1, 0x4000, (uint)Keys.PrintScreen);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY) { StartSnip(); return; }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                tray.ShowBalloonTip(1500, "Tcapture", "트레이에서 계속 실행 중입니다. Ctrl+Shift+S로 캡처하세요.", ToolTipIcon.Info);
                return;
            }
            base.OnFormClosing(e);
        }

        void ShowMain() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        void ExitApp()
        {
            Native.UnregisterHotKey(Handle, HOTKEY_ID);
            Native.UnregisterHotKey(Handle, HOTKEY_ID + 1);
            tray.Visible = false;
            Application.Exit();
        }

        bool snipping;

        void StartSnip()
        {
            if (snipping) return;
            snipping = true;
            bool wasVisible = Visible;
            Hide();
            int wait = Math.Max(delay * 1000, 250); // let our own window disappear
            var t = new Timer { Interval = wait };
            t.Tick += (s, e) =>
            {
                t.Stop(); t.Dispose();
                Rectangle vb;
                var shot = Program.GrabVirtualScreen(out vb);
                if (mode == SnipMode.FullScreen) { Finish(shot, wasVisible); return; }
                var ov = new OverlayForm(shot, vb, mode);
                ov.FormClosed += (s2, e2) =>
                {
                    var result = ov.Result;
                    shot.Dispose();
                    if (result != null) Finish(result, wasVisible);
                    else { snipping = false; if (wasVisible) ShowMain(); }
                };
                ov.Show();
                ov.Activate();
            };
            t.Start();
        }

        void Finish(Bitmap img, bool wasVisible)
        {
            snipping = false;
            try { Clipboard.SetImage(img); } catch { }
            string saved = null;
            if (autoSave)
            {
                try
                {
                    Directory.CreateDirectory(SaveDir);
                    saved = Path.Combine(SaveDir, "tcapture " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png");
                    img.Save(saved, ImageFormat.Png);
                }
                catch { saved = null; }
            }
            var ed = new EditorForm(img, saved, Icon);
            ed.NewSnip += () => StartSnip();
            ed.Show();
            ed.Activate();
            if (wasVisible) Show();
        }

        void OpenImage()
        {
            using (var dlg = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Bitmap b;
                using (var src = Image.FromFile(dlg.FileName)) b = new Bitmap(src);
                var ed = new EditorForm(b, dlg.FileName, Icon);
                ed.NewSnip += () => StartSnip();
                ed.Show();
            }
        }
    }

    // ---------------------------------------------------------------- fullscreen selection overlay
    class OverlayForm : Form
    {
        readonly Bitmap shot, dim;
        readonly Rectangle vb;
        readonly SnipMode mode;
        readonly List<Rectangle> windows;
        Point start, cur;
        bool dragging;
        readonly List<Point> path = new List<Point>();
        Rectangle hoverWin = Rectangle.Empty;
        public Bitmap Result;

        public OverlayForm(Bitmap shot, Rectangle vb, SnipMode mode)
        {
            this.shot = shot; this.vb = vb; this.mode = mode;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = vb;
            TopMost = true;
            ShowInTaskbar = false;
            Cursor = mode == SnipMode.Window ? Cursors.Hand : Cursors.Cross;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            dim = new Bitmap(shot);
            using (var g = Graphics.FromImage(dim))
            using (var br = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                g.FillRectangle(br, 0, 0, dim.Width, dim.Height);

            if (mode == SnipMode.Window)
            {
                windows = new List<Rectangle>();
                foreach (var r in Native.TopLevelWindows(Handle))
                {
                    var c = Rectangle.Intersect(new Rectangle(r.X - vb.X, r.Y - vb.Y, r.Width, r.Height), new Rectangle(Point.Empty, vb.Size));
                    if (!c.IsEmpty) windows.Add(c);
                }
            }
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Bounds = vb; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.Enter) { Result = new Bitmap(shot); Close(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            if (mode == SnipMode.Window)
            {
                if (!hoverWin.IsEmpty) { Result = Crop(hoverWin); Close(); }
                return;
            }
            dragging = true; start = cur = e.Location;
            path.Clear(); path.Add(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (mode == SnipMode.Window)
            {
                var hit = Rectangle.Empty;
                foreach (var w in windows) if (w.Contains(e.Location)) { hit = w; break; }
                if (hit != hoverWin) { hoverWin = hit; Invalidate(); }
                return;
            }
            cur = e.Location;
            if (dragging && mode == SnipMode.Freeform) path.Add(e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            if (mode == SnipMode.Rectangle)
            {
                var r = Sel();
                if (r.Width < 3 || r.Height < 3) { Invalidate(); return; }
                Result = Crop(r);
            }
            else
            {
                if (path.Count < 3) { Invalidate(); return; }
                Result = CropFreeform();
                if (Result == null) { Invalidate(); return; }
            }
            Close();
        }

        Rectangle Sel()
        {
            return Rectangle.FromLTRB(Math.Min(start.X, cur.X), Math.Min(start.Y, cur.Y), Math.Max(start.X, cur.X), Math.Max(start.Y, cur.Y));
        }

        Bitmap Crop(Rectangle r)
        {
            r.Intersect(new Rectangle(Point.Empty, shot.Size));
            return shot.Clone(r, PixelFormat.Format32bppArgb);
        }

        Bitmap CropFreeform()
        {
            using (var gp = new GraphicsPath())
            {
                gp.AddPolygon(path.ToArray());
                var b = Rectangle.Round(gp.GetBounds());
                b.Intersect(new Rectangle(Point.Empty, shot.Size));
                if (b.Width < 3 || b.Height < 3) return null;
                var bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.TranslateTransform(-b.X, -b.Y);
                    g.SetClip(gp);
                    g.DrawImage(shot, 0, 0);
                }
                return bmp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(dim, 0, 0);
            using (var pen = new Pen(Color.FromArgb(0, 120, 212), 2))
            {
                if (mode == SnipMode.Rectangle && dragging)
                {
                    var r = Sel();
                    g.DrawImage(shot, r, r, GraphicsUnit.Pixel);
                    g.DrawRectangle(pen, r);
                    var label = r.Width + " × " + r.Height;
                    using (var f = new Font("Segoe UI", 9f))
                    {
                        var sz = g.MeasureString(label, f);
                        var ly = r.Y - sz.Height - 4 < 0 ? r.Bottom + 4 : r.Y - sz.Height - 4;
                        g.FillRectangle(Brushes.Black, r.X, ly, sz.Width + 6, sz.Height);
                        g.DrawString(label, f, Brushes.White, r.X + 3, ly);
                    }
                }
                else if (mode == SnipMode.Freeform && dragging && path.Count > 1)
                {
                    using (var gp = new GraphicsPath())
                    {
                        gp.AddLines(path.ToArray());
                        var st = g.Save();
                        g.SetClip(gp);
                        g.DrawImageUnscaled(shot, 0, 0);
                        g.Restore(st);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawLines(pen, path.ToArray());
                    }
                }
                else if (mode == SnipMode.Window && !hoverWin.IsEmpty)
                {
                    g.DrawImage(shot, hoverWin, hoverWin, GraphicsUnit.Pixel);
                    var r = hoverWin; r.Inflate(-1, -1);
                    pen.Width = 3;
                    g.DrawRectangle(pen, r);
                }
            }
            if (!dragging && hoverWin.IsEmpty)
            {
                string tip = mode == SnipMode.Window ? "캡처할 창을 클릭하세요 · Esc 취소" :
                             mode == SnipMode.Freeform ? "드래그하여 자유형으로 선택 · Esc 취소" :
                             "드래그하여 영역 선택 · Enter 전체 화면 · Esc 취소";
                using (var f = new Font("Segoe UI", 11f))
                {
                    var prim = Screen.PrimaryScreen.Bounds;
                    var sz = g.MeasureString(tip, f);
                    float x = prim.X - vb.X + (prim.Width - sz.Width) / 2, y = prim.Y - vb.Y + 24;
                    using (var br = new SolidBrush(Color.FromArgb(220, 32, 32, 32))) g.FillRectangle(br, x - 12, y - 6, sz.Width + 24, sz.Height + 12);
                    g.DrawString(tip, f, Brushes.White, x, y);
                }
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) dim.Dispose(); base.Dispose(disposing); }
    }

    // ---------------------------------------------------------------- annotation editor
    class Stroke
    {
        public List<Point> Points = new List<Point>();
        public Color Color;
        public float Width;
        public bool Highlighter;
    }

    class Canvas : Panel
    {
        public Bitmap Image;
        public readonly List<Stroke> Strokes = new List<Stroke>();
        public readonly Stack<Stroke> Redo = new Stack<Stroke>();
        public string Tool = "pen"; // pen | highlighter | eraser | none
        public Color PenColor = Color.Red;
        public float Zoom = 1f;
        Stroke live;

        public Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(243, 243, 243);
        }

        public void Relayout()
        {
            Size = new Size((int)(Image.Width * Zoom), (int)(Image.Height * Zoom));
            Invalidate();
        }

        Point ToImg(Point p) { return new Point((int)(p.X / Zoom), (int)(p.Y / Zoom)); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (Tool == "eraser") { EraseAt(ToImg(e.Location)); return; }
            if (Tool == "none") return;
            live = Tool == "highlighter"
                ? new Stroke { Color = Color.FromArgb(110, 255, 230, 0), Width = 18, Highlighter = true }
                : new Stroke { Color = PenColor, Width = 4 };
            live.Points.Add(ToImg(e.Location));
            Strokes.Add(live);
            Redo.Clear();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && Tool == "eraser") { EraseAt(ToImg(e.Location)); return; }
            if (live == null) return;
            live.Points.Add(ToImg(e.Location));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) { live = null; Invalidate(); }

        void EraseAt(Point p)
        {
            for (int i = Strokes.Count - 1; i >= 0; i--)
                foreach (var q in Strokes[i].Points)
                    if (Math.Abs(q.X - p.X) < 10 && Math.Abs(q.Y - p.Y) < 10)
                    { Redo.Push(Strokes[i]); Strokes.RemoveAt(i); Invalidate(); return; }
        }

        public static void DrawStrokes(Graphics g, IEnumerable<Stroke> strokes)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var s in strokes)
            {
                using (var p = new Pen(s.Color, s.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    if (s.Highlighter) { p.StartCap = p.EndCap = LineCap.Square; }
                    if (s.Points.Count == 1) { var pt = s.Points[0]; using (var b = new SolidBrush(s.Color)) g.FillEllipse(b, pt.X - s.Width / 2, pt.Y - s.Width / 2, s.Width, s.Width); }
                    else g.DrawLines(p, s.Points.ToArray());
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = Zoom < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.ScaleTransform(Zoom, Zoom);
            g.DrawImage(Image, 0, 0, Image.Width, Image.Height);
            DrawStrokes(g, Strokes);
        }

        public Bitmap Flatten()
        {
            var b = new Bitmap(Image.Width, Image.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) { g.DrawImageUnscaled(Image, 0, 0); DrawStrokes(g, Strokes); }
            return b;
        }
    }

    class EditorForm : Form
    {
        readonly Canvas canvas;
        readonly Panel scroller;
        readonly ToolStripButton penBtn, hlBtn, eraserBtn;
        readonly ToolStripLabel zoomLbl;
        string savedPath;
        public event Action NewSnip;

        public EditorForm(Bitmap img, string savedPath, Icon icon)
        {
            this.savedPath = savedPath;
            Text = "Tcapture" + (savedPath != null ? " - " + Path.GetFileName(savedPath) : "");
            Icon = icon;
            Font = new Font("Segoe UI", 10f);
            KeyPreview = true;

            canvas = new Canvas { Image = img, Location = new Point(16, 16) };
            scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(243, 243, 243) };
            scroller.Controls.Add(canvas);

            var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6), Dock = DockStyle.Top };
            var newBtn = new ToolStripButton("＋ 새 캡처");
            newBtn.Click += (s, e) => { if (NewSnip != null) NewSnip(); };
            penBtn = new ToolStripButton("🖊 펜") { CheckOnClick = false };
            hlBtn = new ToolStripButton("🖍 형광펜");
            eraserBtn = new ToolStripButton("⌫ 지우개");
            penBtn.Click += (s, e) => SetTool("pen");
            hlBtn.Click += (s, e) => SetTool("highlighter");
            eraserBtn.Click += (s, e) => SetTool("eraser");
            var colorBtn = new ToolStripDropDownButton("●") { ForeColor = canvas.PenColor };
            foreach (var c in new[] { Color.Red, Color.Black, Color.White, Color.FromArgb(0, 120, 212), Color.FromArgb(16, 137, 62), Color.Orange, Color.Magenta })
            {
                var cc = c;
                var it = new ToolStripMenuItem("●  " + c.Name) { ForeColor = c == Color.White ? Color.Gray : c };
                it.Click += (s, e) => { canvas.PenColor = cc; colorBtn.ForeColor = cc == Color.White ? Color.Gray : cc; SetTool("pen"); };
                colorBtn.DropDownItems.Add(it);
            }
            var undoBtn = new ToolStripButton("↶ 실행 취소");
            undoBtn.Click += (s, e) => Undo();
            var redoBtn = new ToolStripButton("↷ 다시 실행");
            redoBtn.Click += (s, e) => RedoAction();
            var zoomIn = new ToolStripButton("＋"); zoomIn.Click += (s, e) => SetZoom(canvas.Zoom * 1.25f);
            var zoomOut = new ToolStripButton("－"); zoomOut.Click += (s, e) => SetZoom(canvas.Zoom / 1.25f);
            zoomLbl = new ToolStripLabel("100%");
            var copyBtn = new ToolStripButton("⧉ 복사") { Alignment = ToolStripItemAlignment.Right };
            copyBtn.Click += (s, e) => CopyOut();
            var saveBtn = new ToolStripButton("💾 다른 이름으로 저장") { Alignment = ToolStripItemAlignment.Right };
            saveBtn.Click += (s, e) => SaveAs();
            ts.Items.AddRange(new ToolStripItem[] { newBtn, new ToolStripSeparator(), penBtn, colorBtn, hlBtn, eraserBtn, new ToolStripSeparator(), undoBtn, redoBtn, new ToolStripSeparator(), zoomOut, zoomLbl, zoomIn, saveBtn, copyBtn });

            var status = new StatusStrip();
            status.Items.Add(new ToolStripStatusLabel(img.Width + " × " + img.Height + " px · 클립보드에 복사됨" + (savedPath != null ? " · 저장됨" : "")));

            Controls.Add(scroller);
            Controls.Add(ts);
            Controls.Add(status);

            // fit window to image, capped at 85% of the working area
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            float fit = Math.Min(1f, Math.Min((wa.Width * 0.85f - 60) / img.Width, (wa.Height * 0.85f - 150) / img.Height));
            canvas.Zoom = Math.Max(0.1f, fit);
            ClientSize = new Size(Math.Max(720, (int)(img.Width * canvas.Zoom) + 50), Math.Max(420, (int)(img.Height * canvas.Zoom) + 120));
            StartPosition = FormStartPosition.CenterScreen;
            canvas.Relayout();
            zoomLbl.Text = (int)(canvas.Zoom * 100) + "%";
            SetTool("pen");
        }

        void SetTool(string t)
        {
            canvas.Tool = t;
            penBtn.Checked = t == "pen"; hlBtn.Checked = t == "highlighter"; eraserBtn.Checked = t == "eraser";
            canvas.Cursor = t == "eraser" ? Cursors.Hand : Cursors.Cross;
        }

        void SetZoom(float z)
        {
            canvas.Zoom = Math.Max(0.1f, Math.Min(8f, z));
            canvas.Relayout();
            zoomLbl.Text = (int)(canvas.Zoom * 100) + "%";
        }

        void Undo()
        {
            if (canvas.Strokes.Count == 0) return;
            canvas.Redo.Push(canvas.Strokes[canvas.Strokes.Count - 1]);
            canvas.Strokes.RemoveAt(canvas.Strokes.Count - 1);
            canvas.Invalidate();
        }

        void RedoAction()
        {
            if (canvas.Redo.Count == 0) return;
            canvas.Strokes.Add(canvas.Redo.Pop());
            canvas.Invalidate();
        }

        void CopyOut()
        {
            using (var b = canvas.Flatten()) Clipboard.SetImage(b);
        }

        void SaveAs()
        {
            using (var dlg = new SaveFileDialog { Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp|GIF|*.gif", FileName = savedPath != null ? Path.GetFileName(savedPath) : "tcapture " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var fmt = new[] { ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Bmp, ImageFormat.Gif }[dlg.FilterIndex - 1];
                using (var b = canvas.Flatten()) b.Save(dlg.FileName, fmt);
                savedPath = dlg.FileName;
                Text = "Tcapture - " + Path.GetFileName(savedPath);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z) Undo();
            else if (e.Control && e.KeyCode == Keys.Y) RedoAction();
            else if (e.Control && e.KeyCode == Keys.C) CopyOut();
            else if (e.Control && e.KeyCode == Keys.S) SaveAs();
            else if (e.Control && e.KeyCode == Keys.N) { if (NewSnip != null) NewSnip(); }
            else if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)) SetZoom(canvas.Zoom * 1.25f);
            else if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)) SetZoom(canvas.Zoom / 1.25f);
            else if (e.Control && e.KeyCode == Keys.D0) SetZoom(1f);
            else return;
            e.Handled = true;
        }

        protected override void Dispose(bool disposing) { if (disposing) canvas.Image.Dispose(); base.Dispose(disposing); }
    }
}
