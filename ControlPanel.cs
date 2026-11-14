// ControlPanel — всплывающая панель управления SnowDesk (открывается кликом по значку в трее).
// Вся панель рисуется вручную: тёмная тема, карточки, переключатели, ползунки,
// "ночное небо" со снегопадом и маленьким снеговиком в шапке.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SnowDesk
{
    class ControlPanel : Form
    {
        readonly SnowForm app;
        readonly float s;

        static readonly Color Bg = Color.FromArgb(255, 16, 20, 30);
        static readonly Color Card = Color.FromArgb(255, 26, 32, 47);
        static readonly Color CardHover = Color.FromArgb(255, 33, 41, 60);
        static readonly Color Border = Color.FromArgb(255, 40, 49, 70);
        static readonly Color TextC = Color.FromArgb(255, 236, 241, 250);
        static readonly Color SubC = Color.FromArgb(255, 138, 151, 176);
        static readonly Color Accent = Color.FromArgb(255, 96, 186, 255);
        static readonly Color Danger = Color.FromArgb(255, 255, 120, 130);

        Font fTitle, fSub, fBody, fBodyBold, fSmall, fSection, fSeg;

        RectangleF rHeader, rClose, rPlay, rPlayToggle, rSecSnow, rCardSnow, rSegAll, rRowSnowing, rRowDrifts,
                   rCardSliders, rRowAccum, rTrackAccum, rRowWind, rTrackWind, rSecOpts, rCardOpts,
                   rRowFs, rRowAuto, rBtnShake, rBtnClear, rExit, rStats;
        readonly RectangleF[] rSeg = new RectangleF[3];

        class Hot { public string Id; public RectangleF R; public Hot(string id, RectangleF r) { Id = id; R = r; } }
        readonly List<Hot> hots = new List<Hot>();
        string hover, pressed, dragSlider;
        readonly Dictionary<string, float> knob = new Dictionary<string, float>();
        bool autostart;

        readonly Timer anim = new Timer();
        readonly Stopwatch sw = new Stopwatch();
        double lastT;
        float slide; // анимация появления 0..1
        int targetTop;

        // снежинки в шапке
        float[] fx, fy, fsz, fv, fph;
        readonly BallLook lookBase = SnowPainter.MakeLook(11), lookMid = SnowPainter.MakeLook(23), lookHead = SnowPainter.MakeLook(37);
        public DateTime LastHidden = DateTime.MinValue;

        public ControlPanel(SnowForm app, float scale)
        {
            this.app = app;
            s = scale;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            Text = "SnowDesk";
            BackColor = Bg;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            string family = PickFont("Segoe UI Variable Text", "Segoe UI");
            string display = PickFont("Segoe UI Variable Display", family);
            fTitle = new Font(display, 21f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            fSub = new Font(family, 12.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            fBody = new Font(family, 14f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            fBodyBold = new Font(family, 14.5f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            fSmall = new Font(family, 12f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            fSection = new Font(family, 11f * s, FontStyle.Bold, GraphicsUnit.Pixel);
            fSeg = new Font(family, 13f * s, FontStyle.Bold, GraphicsUnit.Pixel);

            DoLayout();
            InitFlakes();

            anim.Interval = 16;
            anim.Tick += OnAnim;
        }

        static string PickFont(string a, string b)
        {
            foreach (string n in new string[] { a, b })
            {
                try { using (FontFamily f = new FontFamily(n)) return n; }
                catch (ArgumentException) { }
            }
            return "Microsoft Sans Serif";
        }

        float S(float v) { return v * s; }

        void DoLayout()
        {
            float W = S(360), M = S(16);
            float cw = W - 2 * M;
            float y = 0;
            rHeader = new RectangleF(0, 0, W, S(116));
            rClose = new RectangleF(W - S(40), S(10), S(28), S(28));
            y = rHeader.Bottom + S(14);

            rPlay = new RectangleF(M, y, cw, S(70));
            rPlayToggle = new RectangleF(rPlay.Right - S(58), rPlay.Y + rPlay.Height / 2 - S(11), S(44), S(22));
            y = rPlay.Bottom + S(18);

            rSecSnow = new RectangleF(M + S(4), y, cw, S(18));
            y += S(24);
            rCardSnow = new RectangleF(M, y, cw, S(148));
            rSegAll = new RectangleF(M + S(10), y + S(10), cw - S(20), S(38));
            float sw3 = (rSegAll.Width - S(8)) / 3f;
            for (int i = 0; i < 3; i++)
                rSeg[i] = new RectangleF(rSegAll.X + S(4) + i * sw3, rSegAll.Y + S(4), sw3, rSegAll.Height - S(8));
            rRowSnowing = new RectangleF(M + S(4), y + S(56), cw - S(8), S(44));
            rRowDrifts = new RectangleF(M + S(4), y + S(100), cw - S(8), S(44));
            y = rCardSnow.Bottom + S(10);

            rCardSliders = new RectangleF(M, y, cw, S(112));
            rRowAccum = new RectangleF(M + S(14), y + S(10), cw - S(28), S(46));
            rTrackAccum = new RectangleF(rRowAccum.X + S(2), rRowAccum.Y + S(34), rRowAccum.Width - S(4), S(4));
            rRowWind = new RectangleF(M + S(14), y + S(58), cw - S(28), S(46));
            rTrackWind = new RectangleF(rRowWind.X + S(2), rRowWind.Y + S(34), rRowWind.Width - S(4), S(4));
            y = rCardSliders.Bottom + S(18);

            rSecOpts = new RectangleF(M + S(4), y, cw, S(18));
            y += S(24);
            rCardOpts = new RectangleF(M, y, cw, S(96));
            rRowFs = new RectangleF(M + S(4), y + S(4), cw - S(8), S(44));
            rRowAuto = new RectangleF(M + S(4), y + S(48), cw - S(8), S(44));
            y = rCardOpts.Bottom + S(12);

            float bw = (cw - S(10)) / 2f;
            rBtnShake = new RectangleF(M, y, bw, S(40));
            rBtnClear = new RectangleF(M + bw + S(10), y, bw, S(40));
            y = rBtnShake.Bottom + S(12);

            rExit = new RectangleF(M, y, S(92), S(32));
            rStats = new RectangleF(M + S(96), y, cw - S(96), S(32));
            y = rExit.Bottom + S(12);

            ClientSize = new Size((int)Math.Ceiling(W), (int)Math.Ceiling(y));

            hots.Clear();
            hots.Add(new Hot("close", rClose));
            hots.Add(new Hot("play", rPlay));
            for (int i = 0; i < 3; i++) hots.Add(new Hot("int" + i, rSeg[i]));
            hots.Add(new Hot("snowing", rRowSnowing));
            hots.Add(new Hot("drifts", rRowDrifts));
            hots.Add(new Hot("accum", RectangleF.Inflate(rTrackAccum, S(8), S(12))));
            hots.Add(new Hot("wind", RectangleF.Inflate(rTrackWind, S(8), S(12))));
            hots.Add(new Hot("hidefs", rRowFs));
            hots.Add(new Hot("autostart", rRowAuto));
            hots.Add(new Hot("shake", rBtnShake));
            hots.Add(new Hot("clear", rBtnClear));
            hots.Add(new Hot("exit", rExit));
        }

        void InitFlakes()
        {
            Random r = new Random(7);
            int n = 46;
            fx = new float[n]; fy = new float[n]; fsz = new float[n]; fv = new float[n]; fph = new float[n];
            for (int i = 0; i < n; i++)
            {
                fx[i] = (float)r.NextDouble() * rHeader.Width;
                fy[i] = (float)r.NextDouble() * rHeader.Height;
                float d = (float)r.NextDouble();
                fsz[i] = S(1f + 2f * d);
                fv[i] = S(10f + 26f * d);
                fph[i] = (float)(r.NextDouble() * Math.PI * 2);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int round = 2; // DWMWCP_ROUND
                NativeMethods.DwmSetWindowAttribute(Handle, 33, ref round, 4);
                int dark = 1;
                NativeMethods.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            }
            catch { }
            if (!SnowForm.IsWindows11())
                using (GraphicsPath p = RR(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), S(10)))
                    Region = new Region(p);
        }

        // ------------------------------------------------------------ показ

        public void TogglePanel()
        {
            if (Visible) { HidePanel(); return; }
            if ((DateTime.Now - LastHidden).TotalMilliseconds < 300) return; // клик по значку, который и закрыл панель
            ShowPanel();
        }

        public void ShowPanel()
        {
            autostart = app.Autostart;
            knob["play"] = app.PlayMode ? 1f : 0f;
            knob["snowing"] = app.Snowing ? 1f : 0f;
            knob["drifts"] = app.Drifts ? 1f : 0f;
            knob["hidefs"] = app.HideFullscreen ? 1f : 0f;
            knob["autostart"] = autostart ? 1f : 0f;

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Left = wa.Right - Width - (int)S(12);
            targetTop = wa.Bottom - Height - (int)S(12);
            Top = targetTop + (int)S(24);
            slide = 0f;
            Show();
            Activate();
            NativeMethods.SetForegroundWindow(Handle);
            lastT = 0;
            sw.Restart();
            anim.Start();
        }

        public void HidePanel()
        {
            if (!Visible) return;
            anim.Stop();
            hover = pressed = dragSlider = null;
            Hide();
            LastHidden = DateTime.Now;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HidePanel();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) HidePanel();
        }

        void OnAnim(object sender, EventArgs e)
        {
            double now = sw.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(0.05, now - lastT);
            lastT = now;

            if (slide < 1f)
            {
                slide = Math.Min(1f, slide + dt * 7f);
                float ease = 1f - (1f - slide) * (1f - slide) * (1f - slide);
                Top = targetTop + (int)(S(24) * (1f - ease));
            }

            for (int i = 0; i < fx.Length; i++)
            {
                fy[i] += fv[i] * dt;
                fx[i] += (float)Math.Sin(now * 0.9 + fph[i]) * S(8f) * dt;
                if (fy[i] > rHeader.Height) { fy[i] = -S(3); fx[i] = (float)(new Random(i + (int)(now * 1000)).NextDouble()) * rHeader.Width; }
            }

            SetKnob("play", app.PlayMode, dt);
            SetKnob("snowing", app.Snowing, dt);
            SetKnob("drifts", app.Drifts, dt);
            SetKnob("hidefs", app.HideFullscreen, dt);
            SetKnob("autostart", autostart, dt);
            Invalidate();
        }

        void SetKnob(string id, bool on, float dt)
        {
            float v;
            knob.TryGetValue(id, out v);
            float target = on ? 1f : 0f;
            v += (target - v) * Math.Min(1f, dt * 16f);
            if (Math.Abs(v - target) < 0.002f) v = target;
            knob[id] = v;
        }

        // ------------------------------------------------------------ мышь

        string HitTest(PointF p)
        {
            for (int i = 0; i < hots.Count; i++)
                if (hots[i].R.Contains(p)) return hots[i].Id;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragSlider != null) { UpdateSlider(dragSlider, e.X); return; }
            string h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                Cursor = h != null ? Cursors.Hand : Cursors.Default;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (dragSlider == null) hover = null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            pressed = HitTest(e.Location);
            if (pressed == "accum" || pressed == "wind")
            {
                dragSlider = pressed;
                UpdateSlider(dragSlider, e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragSlider != null) { dragSlider = null; pressed = null; return; }
            string h = HitTest(e.Location);
            if (h != null && h == pressed) DoClick(h);
            pressed = null;
        }

        void UpdateSlider(string id, float mx)
        {
            RectangleF tr = id == "accum" ? rTrackAccum : rTrackWind;
            float t = Math.Max(0f, Math.Min(1f, (mx - tr.X) / tr.Width));
            if (id == "accum") app.AccumRate = 0.5f + t * 5.5f;
            else app.WindRate = t * 3f;
        }

        void DoClick(string id)
        {
            switch (id)
            {
                case "close": HidePanel(); break;
                case "play": app.SetPlayMode(!app.PlayMode, false); break;
                case "int0": app.Intensity = 0; break;
                case "int1": app.Intensity = 1; break;
                case "int2": app.Intensity = 2; break;
                case "snowing": app.Snowing = !app.Snowing; break;
                case "drifts": app.Drifts = !app.Drifts; break;
                case "hidefs": app.HideFullscreen = !app.HideFullscreen; break;
                case "autostart": app.Autostart = !autostart; autostart = app.Autostart; break;
                case "shake": app.ShakeWindows(); break;
                case "clear": app.ClearAll(); break;
                case "exit": HidePanel(); app.ExitApp(); break;
            }
        }

        // ------------------------------------------------------------ отрисовка

        static GraphicsPath RR(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(rad * 2f, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        void FillRR(Graphics g, RectangleF r, float rad, Color c)
        {
            using (GraphicsPath p = RR(r, rad))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        void StrokeRR(Graphics g, RectangleF r, float rad, Color c, float w)
        {
            using (GraphicsPath p = RR(r, rad))
            using (Pen pen = new Pen(c, w))
                g.DrawPath(pen, p);
        }

        void Txt(Graphics g, string t, Font f, Color c, RectangleF r, StringAlignment h, StringAlignment v)
        {
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            using (SolidBrush b = new SolidBrush(c))
            {
                sf.Alignment = h;
                sf.LineAlignment = v;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(t, f, b, r, sf);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Bg);

            DrawHeader(g);
            DrawPlayCard(g);

            Txt(g, "СНЕГОПАД", fSection, SubC, rSecSnow, StringAlignment.Near, StringAlignment.Center);
            FillRR(g, rCardSnow, S(12), Card);
            DrawSegments(g);
            DrawToggleRow(g, rRowSnowing, "snowing", "Идёт снег", app.Snowing ? "Снежинки падают" : "Снегопад на паузе");
            DrawToggleRow(g, rRowDrifts, "drifts", "Сугробы",
                app.Drifts ? "Снег копится на окнах и панели задач" : "Снег пролетает насквозь");

            FillRR(g, rCardSliders, S(12), Card);
            DrawSlider(g, rRowAccum, rTrackAccum, "Накопление сугробов", (app.AccumRate - 0.5f) / 5.5f,
                "×" + app.AccumRate.ToString("0.0"), "accum");
            DrawSlider(g, rRowWind, rTrackWind, "Ветер", app.WindRate / 3f,
                app.WindRate < 0.05f ? "штиль" : "×" + app.WindRate.ToString("0.0"), "wind");

            Txt(g, "ПАРАМЕТРЫ", fSection, SubC, rSecOpts, StringAlignment.Near, StringAlignment.Center);
            FillRR(g, rCardOpts, S(12), Card);
            DrawToggleRow(g, rRowFs, "hidefs", "Прятать в играх и видео", "В полноэкранных приложениях");
            DrawToggleRow(g, rRowAuto, "autostart", "Запуск вместе с Windows", "Снег сразу после входа");

            DrawButton(g, rBtnShake, "shake", "Стряхнуть с окон");
            DrawButton(g, rBtnClear, "clear", "Убрать весь снег");

            if (hover == "exit") FillRR(g, rExit, S(8), Color.FromArgb(255, 58, 30, 38));
            DrawPower(g, new PointF(rExit.X + S(18), rExit.Y + rExit.Height / 2), S(6.5f), Danger);
            Txt(g, "Выход", fBody, Danger, new RectangleF(rExit.X + S(30), rExit.Y, rExit.Width - S(30), rExit.Height),
                StringAlignment.Near, StringAlignment.Center);

            int men = app.SnowmanCount, ballsN = app.BallCount;
            string stats = men > 0 ? "Снеговиков: " + men + "  ·  Снежков: " + ballsN
                         : ballsN > 0 ? "Снежков: " + ballsN : "Слепите первого снеговика!";
            Txt(g, stats, fSmall, SubC, new RectangleF(rStats.X, rStats.Y, rStats.Width - S(4), rStats.Height),
                StringAlignment.Far, StringAlignment.Center);
        }

        void DrawHeader(Graphics g)
        {
            RectangleF h = rHeader;
            using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(h.X, h.Y - 1, h.Width, h.Height + 2),
                Color.FromArgb(255, 10, 20, 46), Color.FromArgb(255, 38, 70, 128), 90f))
                g.FillRectangle(lb, h);

            // мягкое "северное" свечение
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(h.Width * 0.35f, -h.Height * 0.6f, h.Width * 0.9f, h.Height * 1.2f);
                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterColor = Color.FromArgb(70, 110, 200, 255);
                    pb.SurroundColors = new Color[] { Color.FromArgb(0, 110, 200, 255) };
                    g.FillPath(pb, gp);
                }
            }

            // звёзды и снежинки
            using (SolidBrush b = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
                for (int i = 0; i < fx.Length; i++)
                    g.FillEllipse(b, fx[i], fy[i], fsz[i], fsz[i]);

            // сугробы
            float by = h.Bottom - S(18);
            PointF[] hill =
            {
                new PointF(-S(10), by + S(4)), new PointF(S(60), by - S(4)), new PointF(S(130), by + S(3)),
                new PointF(S(200), by - S(2)), new PointF(S(270), by - S(9)), new PointF(S(330), by - S(3)),
                new PointF(h.Width + S(10), by + S(2))
            };
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddCurve(hill, 0.5f);
                gp.AddLine(h.Width + S(10), h.Bottom + 1, -S(10), h.Bottom + 1);
                gp.CloseFigure();
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(0, by - S(12), h.Width, S(32)),
                    Color.FromArgb(255, 250, 252, 255), Color.FromArgb(255, 196, 214, 242), 90f))
                    g.FillPath(lb, gp);
            }

            // снеговик
            float sx = h.Width - S(100);
            float r1 = S(19), r2 = S(14), r3 = S(10.5f);
            float y1 = by - S(6) - r1 * 0.55f;
            float y2 = y1 - r1 - r2 * 0.78f;
            float y3 = y2 - r2 - r3 * 0.78f;
            SnowPainter.DrawBall(g, lookBase, sx, y1, r1, 0f, true, 0, 1);
            SnowPainter.DrawBall(g, lookMid, sx, y2, r2, 0f, true, 2, 2);
            SnowPainter.DrawBall(g, lookHead, sx, y3, r3, 0f, true, 3, 2);

            // логотип и заголовок
            DrawSnowflake(g, new PointF(S(34), S(40)), S(13), Color.FromArgb(255, 200, 232, 255));
            Txt(g, "SnowDesk", fTitle, Color.White, new RectangleF(S(56), S(22), S(220), S(32)),
                StringAlignment.Near, StringAlignment.Center);
            Txt(g, "Снег на вашем рабочем столе", fSub, Color.FromArgb(255, 170, 200, 240),
                new RectangleF(S(57), S(50), S(220), S(20)), StringAlignment.Near, StringAlignment.Center);

            // закрыть
            if (hover == "close")
                using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                    g.FillEllipse(b, rClose);
            using (Pen p = new Pen(Color.FromArgb(220, 255, 255, 255), S(1.6f)))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                float c = S(5);
                PointF m = new PointF(rClose.X + rClose.Width / 2, rClose.Y + rClose.Height / 2);
                g.DrawLine(p, m.X - c, m.Y - c, m.X + c, m.Y + c);
                g.DrawLine(p, m.X - c, m.Y + c, m.X + c, m.Y - c);
            }
        }

        void DrawPlayCard(Graphics g)
        {
            bool on = app.PlayMode;
            float k;
            knob.TryGetValue("play", out k);
            using (GraphicsPath p = RR(rPlay, S(14)))
            {
                Color c1 = Lerp(hover == "play" ? CardHover : Card, Color.FromArgb(255, 30, 64, 108), k);
                Color c2 = Lerp(hover == "play" ? CardHover : Card, Color.FromArgb(255, 26, 40, 70), k);
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(rPlay.X - 1, rPlay.Y, rPlay.Width + 2, rPlay.Height), c1, c2, 0f))
                    g.FillPath(lb, p);
                using (Pen pen = new Pen(Lerp(Border, Accent, k), S(1.2f)))
                    g.DrawPath(pen, p);
            }

            RectangleF tile = new RectangleF(rPlay.X + S(14), rPlay.Y + rPlay.Height / 2 - S(21), S(42), S(42));
            FillRR(g, tile, S(11), Lerp(Color.FromArgb(255, 38, 48, 70), Color.FromArgb(255, 60, 130, 210), k));
            // иконка: маленький снеговик
            float cx = tile.X + tile.Width / 2, cy = tile.Y + tile.Height / 2;
            using (SolidBrush w = new SolidBrush(Color.FromArgb(255, 245, 250, 255)))
            {
                g.FillEllipse(w, cx - S(9), cy - S(1), S(18), S(16));
                g.FillEllipse(w, cx - S(6.5f), cy - S(13), S(13), S(12.5f));
            }
            using (SolidBrush cb = new SolidBrush(Color.FromArgb(255, 40, 44, 56)))
            {
                g.FillEllipse(cb, cx - S(3.2f), cy - S(9), S(2.2f), S(2.2f));
                g.FillEllipse(cb, cx + S(1.2f), cy - S(9), S(2.2f), S(2.2f));
            }
            using (SolidBrush ob = new SolidBrush(Color.FromArgb(255, 245, 135, 40)))
                g.FillPolygon(ob, new PointF[] { new PointF(cx - S(0.5f), cy - S(7.2f)), new PointF(cx + S(5), cy - S(5.8f)), new PointF(cx - S(0.5f), cy - S(5.2f)) });

            float tx = tile.Right + S(12);
            float tw = rPlayToggle.X - tx - S(8);
            Txt(g, "Играть со снегом", fBodyBold, TextC, new RectangleF(tx, rPlay.Y + S(13), tw, S(22)),
                StringAlignment.Near, StringAlignment.Center);
            Txt(g, on ? "Включено · лепите снежки!" : "Снежки и снеговики · Ctrl+Alt+S", fSmall,
                on ? Color.FromArgb(255, 150, 210, 255) : SubC,
                new RectangleF(tx, rPlay.Y + S(36), tw, S(20)), StringAlignment.Near, StringAlignment.Center);
            DrawToggle(g, rPlayToggle, k);
        }

        void DrawSegments(Graphics g)
        {
            FillRR(g, rSegAll, S(10), Color.FromArgb(255, 17, 22, 33));
            string[] names = { "Лёгкий", "Обычный", "Метель" };
            for (int i = 0; i < 3; i++)
            {
                bool sel = app.Intensity == i;
                RectangleF r = rSeg[i];
                if (sel)
                {
                    using (GraphicsPath p = RR(r, S(7)))
                    using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2),
                        Color.FromArgb(255, 120, 200, 255), Color.FromArgb(255, 72, 160, 240), 90f))
                        g.FillPath(lb, p);
                }
                else if (hover == "int" + i) FillRR(g, r, S(7), Color.FromArgb(255, 34, 42, 60));
                Txt(g, names[i], fSeg, sel ? Color.FromArgb(255, 8, 24, 44) : SubC, r,
                    StringAlignment.Center, StringAlignment.Center);
            }
        }

        void DrawToggleRow(Graphics g, RectangleF r, string id, string title, string sub)
        {
            if (hover == id) FillRR(g, r, S(9), CardHover);
            float tx = r.X + S(10);
            float tw = r.Width - S(74);
            Txt(g, title, fBody, TextC, new RectangleF(tx, r.Y + S(4), tw, S(20)), StringAlignment.Near, StringAlignment.Center);
            Txt(g, sub, fSmall, SubC, new RectangleF(tx, r.Y + S(22), tw, S(18)), StringAlignment.Near, StringAlignment.Center);
            float k;
            knob.TryGetValue(id, out k);
            DrawToggle(g, new RectangleF(r.Right - S(54), r.Y + r.Height / 2 - S(11), S(44), S(22)), k);
        }

        void DrawToggle(Graphics g, RectangleF r, float t)
        {
            using (GraphicsPath p = RR(r, r.Height / 2))
            {
                using (SolidBrush b = new SolidBrush(Lerp(Color.FromArgb(255, 40, 48, 66), Accent, t)))
                    g.FillPath(b, p);
                if (t < 0.99f)
                    using (Pen pen = new Pen(Color.FromArgb((int)(255 * (1 - t)), 86, 98, 125), S(1.2f)))
                        g.DrawPath(pen, p);
            }
            float kr = r.Height / 2 - S(4f) + S(1f) * t;
            float kx = r.X + r.Height / 2 + (r.Width - r.Height) * t;
            float ky = r.Y + r.Height / 2;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillEllipse(b, kx - kr, ky - kr + S(1), kr * 2, kr * 2);
            using (SolidBrush b = new SolidBrush(Lerp(Color.FromArgb(255, 165, 176, 198), Color.White, t)))
                g.FillEllipse(b, kx - kr, ky - kr, kr * 2, kr * 2);
        }

        void DrawSlider(Graphics g, RectangleF row, RectangleF track, string title, float t, string value, string id)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            Txt(g, title, fBody, TextC, new RectangleF(row.X, row.Y + S(2), row.Width - S(70), S(22)),
                StringAlignment.Near, StringAlignment.Center);
            Txt(g, value, fSmall, Accent, new RectangleF(row.Right - S(80), row.Y + S(2), S(80), S(22)),
                StringAlignment.Far, StringAlignment.Center);

            FillRR(g, track, track.Height / 2, Color.FromArgb(255, 44, 53, 74));
            RectangleF fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * t), track.Height);
            using (GraphicsPath p = RR(fill, track.Height / 2))
            using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(track.X - 1, track.Y, track.Width + 2, track.Height),
                Color.FromArgb(255, 70, 150, 235), Color.FromArgb(255, 150, 215, 255), 0f))
                g.FillPath(lb, p);

            bool active = hover == id || dragSlider == id;
            float kr = active ? S(9) : S(8);
            float kx = track.X + track.Width * t, ky = track.Y + track.Height / 2;
            if (active)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(50, 96, 186, 255)))
                    g.FillEllipse(b, kx - kr - S(5), ky - kr - S(5), (kr + S(5)) * 2, (kr + S(5)) * 2);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                g.FillEllipse(b, kx - kr, ky - kr + S(1.5f), kr * 2, kr * 2);
            using (SolidBrush b = new SolidBrush(Color.White))
                g.FillEllipse(b, kx - kr, ky - kr, kr * 2, kr * 2);
            using (SolidBrush b = new SolidBrush(Accent))
                g.FillEllipse(b, kx - S(3), ky - S(3), S(6), S(6));
        }

        void DrawButton(Graphics g, RectangleF r, string id, string text)
        {
            bool hov = hover == id, prs = pressed == id && hov;
            Color c = prs ? Color.FromArgb(255, 30, 38, 56) : hov ? Color.FromArgb(255, 44, 56, 82) : Card;
            FillRR(g, r, S(10), c);
            StrokeRR(g, r, S(10), hov ? Color.FromArgb(255, 70, 90, 128) : Border, S(1));
            Txt(g, text, fBody, TextC, r, StringAlignment.Center, StringAlignment.Center);
        }

        static void DrawSnowflake(Graphics g, PointF c, float r, Color col)
        {
            using (Pen p = new Pen(col, Math.Max(1.5f, r * 0.16f)))
            using (Pen t = new Pen(col, Math.Max(1f, r * 0.11f)))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                t.StartCap = t.EndCap = LineCap.Round;
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3 - Math.PI / 2;
                    float ex = c.X + (float)Math.Cos(a) * r, ey = c.Y + (float)Math.Sin(a) * r;
                    g.DrawLine(p, c.X, c.Y, ex, ey);
                    float bx = c.X + (float)Math.Cos(a) * r * 0.6f, by = c.Y + (float)Math.Sin(a) * r * 0.6f;
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        double a2 = a + s2 * Math.PI / 4;
                        g.DrawLine(t, bx, by, bx + (float)Math.Cos(a2) * r * 0.32f, by + (float)Math.Sin(a2) * r * 0.32f);
                    }
                }
            }
        }

        static void DrawPower(Graphics g, PointF c, float r, Color col)
        {
            using (Pen p = new Pen(col, Math.Max(1.4f, r * 0.26f)))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawArc(p, c.X - r, c.Y - r, r * 2, r * 2, -60, 300);
                g.DrawLine(p, c.X, c.Y - r * 1.15f, c.X, c.Y - r * 0.2f);
            }
        }

        // Для проверки: отрисовать панель в картинку
        public Bitmap RenderToBitmap()
        {
            knob["play"] = app.PlayMode ? 1f : 0f;
            knob["snowing"] = app.Snowing ? 1f : 0f;
            knob["drifts"] = app.Drifts ? 1f : 0f;
            knob["hidefs"] = app.HideFullscreen ? 1f : 0f;
            knob["autostart"] = app.Autostart ? 1f : 0f;
            Bitmap b = new Bitmap(ClientSize.Width, ClientSize.Height);
            using (Graphics g = Graphics.FromImage(b))
                OnPaint(new PaintEventArgs(g, new Rectangle(Point.Empty, ClientSize)));
            return b;
        }
    }
}
