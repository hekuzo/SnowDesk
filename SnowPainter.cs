// SnowPainter — реалистичная отрисовка снежных шаров и снеговика.
// Каждый шар получает свою "лепку" (неровный контур, зернистость, кривые веточки),
// чтобы снеговики не выглядели идеальными кругами.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SnowDesk
{
    // Неповторимый вид одного шара
    class BallLook
    {
        public int Seed;
        public int[] K;
        public float[] Amp, Ph;      // гармоники неровного контура
        public float SqX, SqY;       // лёгкая сплюснутость
        public float[] SpA, SpD, SpS; // крапинки: угол, расстояние, размер
        public bool[] SpBright;
        public int Side;             // куда смотрит нос и свисает шарф
        public float HatTilt;
        public float[] Arm;          // изгибы веточек
    }

    static class SnowPainter
    {
        static readonly SolidBrush coalBrush = new SolidBrush(Color.FromArgb(255, 34, 34, 40));
        static readonly SolidBrush coalHl = new SolidBrush(Color.FromArgb(150, 125, 125, 140));
        static readonly Pen branchPen = MakeRoundPen(Color.FromArgb(255, 92, 60, 34));
        static readonly Pen carrotRidge = new Pen(Color.FromArgb(150, 175, 75, 18), 1f);
        static readonly Pen stripePen = new Pen(Color.FromArgb(120, 245, 232, 232), 1f);

        static Pen MakeRoundPen(Color c)
        {
            Pen p = new Pen(c, 2f);
            p.StartCap = p.EndCap = LineCap.Round;
            p.LineJoin = LineJoin.Round;
            return p;
        }

        public static BallLook MakeLook(int seed)
        {
            Random r = new Random(seed);
            BallLook L = new BallLook();
            L.Seed = seed;
            int[] ks = { 2, 3, 4, 5, 7, 9, 13 };
            float[] amps = { 0.032f, 0.024f, 0.017f, 0.012f, 0.008f, 0.006f, 0.004f };
            L.K = ks;
            L.Amp = new float[ks.Length];
            L.Ph = new float[ks.Length];
            for (int i = 0; i < ks.Length; i++)
            {
                L.Amp[i] = amps[i] * (0.5f + (float)r.NextDouble());
                L.Ph[i] = (float)(r.NextDouble() * Math.PI * 2);
            }
            L.SqX = 1.02f + (float)r.NextDouble() * 0.05f;
            L.SqY = 0.94f + (float)r.NextDouble() * 0.04f;

            int ns = 30;
            L.SpA = new float[ns];
            L.SpD = new float[ns];
            L.SpS = new float[ns];
            L.SpBright = new bool[ns];
            for (int i = 0; i < ns; i++)
            {
                L.SpA[i] = (float)(r.NextDouble() * Math.PI * 2);
                L.SpD[i] = (float)Math.Sqrt(r.NextDouble());
                L.SpS[i] = 0.02f + (float)r.NextDouble() * 0.05f;
                L.SpBright[i] = r.NextDouble() < 0.35;
            }
            L.Side = r.NextDouble() < 0.5 ? -1 : 1;
            L.HatTilt = ((float)r.NextDouble() - 0.5f) * 16f;
            L.Arm = new float[8];
            for (int i = 0; i < 8; i++) L.Arm[i] = (float)r.NextDouble() - 0.5f;
            return L;
        }

        static PointF[] Outline(BallLook L, float x, float y, float r, float angle, bool flat)
        {
            int n = Math.Max(28, Math.Min(80, (int)(r * 1.3f)));
            PointF[] pts = new PointF[n];
            float flatY = r * 0.84f;
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n;
                double rr = 1.0;
                for (int k = 0; k < L.K.Length; k++)
                    rr += L.Amp[k] * Math.Sin(L.K[k] * (a - angle) + L.Ph[k]);
                float px = (float)(Math.Cos(a) * rr) * r * L.SqX;
                float py = (float)(Math.Sin(a) * rr) * r * L.SqY;
                if (flat && py > flatY) py = flatY + (py - flatY) * 0.2f; // примят снизу
                pts[i] = new PointF(x + px, y + py);
            }
            return pts;
        }

        // shadow: 0 — нет, 1 — тень на снегу, 2 — тень на нижнем шаре
        // role:   0 — шар, 1 — голова, 2 — туловище (руки, пуговицы), 3 — голова со шляпой и шарфом
        public static void DrawBall(Graphics g, BallLook L, float x, float y, float r, float angle, bool flat, int role, int shadow)
        {
            if (r < 2f) r = 2f;

            if (shadow == 1)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(38, 60, 85, 135)))
                    g.FillEllipse(b, x - r * 0.9f, y + r * 0.74f, r * 1.8f, r * 0.32f);
            else if (shadow == 2)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(26, 90, 115, 165)))
                    g.FillEllipse(b, x - r * 0.75f, y + r * 0.62f, r * 1.5f, r * 0.42f);

            if (role == 2) DrawArms(g, L, x, y, r);

            PointF[] pts = Outline(L, x, y, r, angle, flat);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddClosedCurve(pts, 0.45f);

                using (PathGradientBrush pb = new PathGradientBrush(gp))
                {
                    pb.CenterPoint = new PointF(x - r * 0.32f, y - r * 0.38f);
                    pb.CenterColor = Color.FromArgb(255, 255, 255, 255);
                    // почти белый: края лишь чуть холоднее центра
                    pb.SurroundColors = new Color[] { Color.FromArgb(255, 236, 243, 252) };
                    pb.FocusScales = new PointF(0.62f, 0.62f);
                    g.FillPath(pb, gp);
                }

                GraphicsState st = g.Save();
                g.SetClip(gp, CombineMode.Intersect);

                // лёгкая холодная тень только у самого низа
                RectangleF ar = new RectangleF(x - r * 1.2f, y + r * 0.45f, r * 2.4f, r * 0.7f);
                using (LinearGradientBrush lb = new LinearGradientBrush(ar,
                    Color.FromArgb(0, 170, 192, 225), Color.FromArgb(48, 150, 175, 215), 90f))
                    g.FillRectangle(lb, ar);

                // зернистость снега
                if (r > 5f)
                {
                    for (int i = 0; i < L.SpA.Length; i++)
                    {
                        float a = L.SpA[i] + angle;
                        float d = L.SpD[i] * r * 0.92f;
                        float px = x + (float)Math.Cos(a) * d * L.SqX;
                        float py = y + (float)Math.Sin(a) * d * L.SqY;
                        float sz = Math.Max(0.7f, L.SpS[i] * r);
                        Color c = L.SpBright[i] ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(26, 140, 165, 205);
                        using (SolidBrush sb = new SolidBrush(c))
                            g.FillEllipse(sb, px - sz / 2f, py - sz / 2f, sz, sz * 0.8f);
                    }
                }
                g.Restore(st);

                // еле заметный контур — чтобы шар не пропадал на белом фоне
                using (Pen op = new Pen(Color.FromArgb(55, 150, 172, 210), Math.Max(1f, r * 0.018f)))
                    g.DrawPath(op, gp);
            }

            if (role == 2)
            {
                for (int k = -1; k <= 1; k++)
                    Coal(g, x + L.Side * r * 0.04f * k, y + k * r * 0.33f - r * 0.05f, r * 0.085f, L.Seed + 31 * (k + 2));
            }
            if (role == 1 || role == 3) DrawFace(g, L, x, y, r);
            if (role == 3)
            {
                DrawScarf(g, L, x, y, r);
                DrawHat(g, L, x, y, r);
            }
        }

        public static void Coal(Graphics g, float cx, float cy, float rad, int seed)
        {
            if (rad < 0.9f) rad = 0.9f;
            uint h = unchecked((uint)seed * 2654435761u);
            PointF[] p = new PointF[6];
            for (int i = 0; i < 6; i++)
            {
                h = unchecked(h * 1103515245u + 12345u);
                float k = 0.72f + ((h >> 16) & 255) / 255f * 0.5f;
                double a = i * Math.PI / 3 + 0.4;
                p[i] = new PointF(cx + (float)Math.Cos(a) * rad * k, cy + (float)Math.Sin(a) * rad * k);
            }
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddClosedCurve(p, 0.3f);
                g.FillPath(coalBrush, gp);
            }
            if (rad > 1.6f)
                g.FillEllipse(coalHl, cx - rad * 0.5f, cy - rad * 0.55f, rad * 0.45f, rad * 0.35f);
        }

        static void DrawFace(Graphics g, BallLook L, float x, float y, float r)
        {
            int s = L.Side;
            float fx = x + s * r * 0.07f; // лицо чуть повёрнуто
            Coal(g, fx - r * 0.3f, y - r * 0.2f, r * 0.11f, L.Seed + 1);
            Coal(g, fx + r * 0.3f, y - r * 0.23f, r * 0.1f, L.Seed + 2);

            for (int k = 0; k < 6; k++)
            {
                double a = Math.PI * (0.22 + 0.112 * k);
                float px = fx + (float)Math.Cos(a) * r * 0.46f;
                float py = y + r * 0.08f + (float)Math.Sin(a) * r * 0.33f;
                float sz = r * (0.045f + 0.02f * (((L.Seed >> k) & 3) / 3f));
                Coal(g, px, py, sz, L.Seed + 10 + k);
            }
            Carrot(g, fx, y + r * 0.02f, r, s);
        }

        static void Carrot(Graphics g, float bx, float by, float r, int s)
        {
            float len = r * 0.78f, w = r * 0.1f;
            PointF[] p =
            {
                new PointF(bx - s * w * 0.3f, by - w),
                new PointF(bx + s * len * 0.45f, by - w * 0.5f + len * 0.04f),
                new PointF(bx + s * len, by + len * 0.13f),
                new PointF(bx + s * len * 0.45f, by + w * 0.55f + len * 0.08f),
                new PointF(bx - s * w * 0.3f, by + w)
            };
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddClosedCurve(p, 0.25f);
                RectangleF rr = new RectangleF(bx - len, by - w - 1f, len * 2f, w * 2f + len * 0.2f + 2f);
                using (LinearGradientBrush lb = new LinearGradientBrush(rr,
                    Color.FromArgb(255, 255, 168, 70), Color.FromArgb(255, 205, 85, 18), 90f))
                    g.FillPath(lb, gp);
            }
            carrotRidge.Width = Math.Max(0.8f, r * 0.018f);
            for (int k = 1; k <= 3; k++)
            {
                float t = k * 0.22f;
                float px = bx + s * len * t;
                float py = by + len * 0.13f * t;
                float hw = w * (1f - t) * 0.8f;
                g.DrawLine(carrotRidge, px, py - hw, px + s * w * 0.25f, py + hw * 0.9f);
            }
        }

        static void DrawArms(Graphics g, BallLook L, float x, float y, float r)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                int ai = side < 0 ? 0 : 4;
                float a = (float)((-32f + L.Arm[ai] * 22f) * Math.PI / 180.0);
                float len = r * (1.2f + L.Arm[ai + 1] * 0.35f);
                float px = x + side * r * 0.72f, py = y - r * 0.12f;
                float seg = len / 3f;
                PointF fork = PointF.Empty;
                for (int k = 0; k < 3; k++)
                {
                    a += L.Arm[ai + 2] * 0.45f * (k == 1 ? 1f : -0.7f);
                    float nx = px + side * (float)Math.Cos(a) * seg;
                    float ny = py + (float)Math.Sin(a) * seg;
                    branchPen.Width = Math.Max(1.2f, r * (0.075f - k * 0.017f));
                    g.DrawLine(branchPen, px, py, nx, ny);
                    px = nx;
                    py = ny;
                    if (k == 1) fork = new PointF(px, py);
                }
                branchPen.Width = Math.Max(1f, r * 0.03f);
                // сучок посередине
                float ta = a - 0.75f + L.Arm[ai + 3] * 0.3f;
                g.DrawLine(branchPen, fork.X, fork.Y,
                    fork.X + side * (float)Math.Cos(ta) * len * 0.28f, fork.Y + (float)Math.Sin(ta) * len * 0.28f);
                // "пальцы"
                for (int f = -1; f <= 1; f += 2)
                {
                    float fa = a + f * 0.5f;
                    g.DrawLine(branchPen, px, py,
                        px + side * (float)Math.Cos(fa) * len * 0.17f, py + (float)Math.Sin(fa) * len * 0.17f);
                }
            }
        }

        static void DrawScarf(Graphics g, BallLook L, float x, float y, float r)
        {
            int s = L.Side;
            float w = r * 1.8f, h = Math.Max(3f, r * 0.34f), cy = y + r * 0.8f;
            RectangleF band = new RectangleF(x - w / 2f, cy - h / 2f, w, h);
            Color red = Color.FromArgb(255, 222, 55, 66), dark = Color.FromArgb(255, 158, 28, 42);

            // свисающий конец (под лентой)
            PointF[] tail =
            {
                new PointF(x + s * r * 0.18f, cy),
                new PointF(x + s * r * 0.5f, cy),
                new PointF(x + s * r * 0.6f, cy + r * 0.95f),
                new PointF(x + s * r * 0.27f, cy + r * 1.0f)
            };
            using (GraphicsPath tp = new GraphicsPath())
            {
                tp.AddPolygon(tail);
                using (LinearGradientBrush lb = new LinearGradientBrush(
                    new RectangleF(x - r, cy, r * 2f, r * 1.05f), dark, red, 90f))
                    g.FillPath(lb, tp);
                GraphicsState st = g.Save();
                g.SetClip(tp, CombineMode.Intersect);
                stripePen.Width = Math.Max(1f, r * 0.06f);
                for (int k = 1; k <= 2; k++)
                {
                    float sy = cy + r * (0.3f + 0.25f * k);
                    g.DrawLine(stripePen, x - r, sy, x + r, sy + r * 0.04f);
                }
                g.Restore(st);
            }
            // бахрома
            stripePen.Width = Math.Max(1f, r * 0.03f);
            using (Pen fr = new Pen(dark, Math.Max(1f, r * 0.035f)))
                for (int k = 0; k < 4; k++)
                {
                    float fx = x + s * r * (0.29f + 0.09f * k);
                    float fy = cy + r * (0.995f - 0.012f * k);
                    g.DrawLine(fr, fx, fy, fx + s * r * 0.01f, fy + r * 0.12f);
                }

            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(band);
                using (LinearGradientBrush lb = new LinearGradientBrush(
                    new RectangleF(band.X, band.Y - 1f, band.Width, band.Height + 2f), red, dark, 90f))
                    g.FillPath(lb, gp);
                GraphicsState st = g.Save();
                g.SetClip(gp, CombineMode.Intersect);
                stripePen.Width = Math.Max(1f, r * 0.05f);
                for (float sx = band.X; sx < band.Right; sx += r * 0.24f)
                    g.DrawLine(stripePen, sx, band.Bottom, sx + h * 0.6f, band.Y);
                g.Restore(st);
            }
        }

        static void DrawHat(Graphics g, BallLook L, float x, float y, float r)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(x, y - r * 0.7f);
            g.RotateTransform(L.HatTilt);
            float bw = r * 1.75f, bh = Math.Max(2f, r * 0.22f);
            float cw = r * 1.06f, ch = r * 0.95f;
            RectangleF crown = new RectangleF(-cw / 2f, -ch, cw, ch);
            using (GraphicsPath cp = new GraphicsPath())
            {
                float rad = r * 0.09f;
                cp.AddArc(crown.X, crown.Y, rad * 2, rad * 2, 180, 90);
                cp.AddArc(crown.Right - rad * 2, crown.Y, rad * 2, rad * 2, 270, 90);
                cp.AddLine(crown.Right, crown.Bottom, crown.X, crown.Bottom);
                cp.CloseFigure();
                using (LinearGradientBrush lb = new LinearGradientBrush(
                    new RectangleF(crown.X - 1f, crown.Y, crown.Width + 2f, crown.Height),
                    Color.FromArgb(255, 62, 62, 74), Color.FromArgb(255, 16, 16, 22), 0f))
                    g.FillPath(lb, cp);
            }
            using (SolidBrush band = new SolidBrush(Color.FromArgb(255, 185, 32, 48)))
                g.FillRectangle(band, crown.X, -ch * 0.3f, cw, ch * 0.19f);
            using (SolidBrush brim = new SolidBrush(Color.FromArgb(255, 28, 28, 36)))
                g.FillEllipse(brim, -bw / 2f, -bh / 2f, bw, bh);
            using (Pen hl = new Pen(Color.FromArgb(90, 150, 150, 170), Math.Max(1f, r * 0.02f)))
                g.DrawArc(hl, -bw / 2f, -bh / 2f, bw, bh, 200, 140);
            // немного снега на шляпе
            using (SolidBrush sn = new SolidBrush(Color.FromArgb(240, 250, 252, 255)))
                g.FillEllipse(sn, crown.X + cw * 0.12f, -ch - r * 0.06f, cw * 0.7f, r * 0.13f);
            g.Restore(st);
        }
    }
}
