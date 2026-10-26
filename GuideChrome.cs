using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

/// <summary>
/// Draws the compact Xbox 360 Guide silhouette in a fixed 830x1080 design space.
/// Every curve below was measured from the reference screenshot (1920x1080): the inner
/// dark rule, the cream highlight rule, the outer outline, the Player tab shoulder and the
/// header/footer bands. Colours go through <see cref="GuideTheme.Tint"/> so the classic gold
/// stays exact while the optional accent modes re-colour it.
/// </summary>
internal sealed class GuideChrome : FrameworkElement
{
    public const double DesignWidth = 830, DesignHeight = 1080;
    public const double HeaderBottom = 141, BodyBottom = 858;

    // x,y pairs: centre of the dark inner rule (body edge).
    static readonly Point[] InnerRule = Pts(
        785, 0, 779, 40, 773, 80, 766, 120, 761, 160, 756, 200, 753, 240, 749, 280, 747, 320, 744, 360,
        742, 400, 741, 440, 740, 480, 739, 520, 739, 560, 739, 600, 740, 640, 741, 680, 742, 720, 744, 760,
        746, 800, 750, 840, 753, 880, 759, 920, 765, 960, 771, 1000, 778, 1040, 785, 1080);

    // Outer outline above the Player tab shoulder.
    static readonly Point[] OuterTop = Pts(815, 0, 809, 40, 803, 80, 797, 120);

    // Outer outline from the tab shoulder down to the bottom edge.
    static readonly Point[] OuterTab = Pts(
        823, 144, 822, 200, 818, 300, 815, 400, 810, 500, 807, 600, 803, 700, 801, 720, 796, 740,
        789, 760, 779, 780, 774, 800, 771, 820, 772, 860, 777, 900, 787, 960, 796, 1000, 805, 1040, 814, 1080);

    // Cream outline of the header card (right side).
    static readonly Point[] HeaderEdge = Pts(778, 1, 777, 20, 773, 40, 769, 60, 765, 80, 760, 100);

    public GuideChrome()
    {
        Width = DesignWidth; Height = DesignHeight; IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    static Point[] Pts(params double[] v)
    {
        var p = new Point[v.Length / 2];
        for (int i = 0; i < p.Length; i++) p[i] = new Point(v[2 * i], v[2 * i + 1]);
        return p;
    }

    static Point[] Shift(Point[] src, double dx)
    {
        var p = new Point[src.Length];
        for (int i = 0; i < p.Length; i++) p[i] = new Point(src[i].X + dx, src[i].Y);
        return p;
    }

    // Catmull-Rom through the points, emitted as cubic Béziers. The figure must already be at p[0].
    static void Spline(StreamGeometryContext c, IReadOnlyList<Point> p, bool stroked)
    {
        for (int i = 0; i < p.Count - 1; i++)
        {
            Point p0 = p[Math.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Math.Min(i + 2, p.Count - 1)];
            c.BezierTo(new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6),
                       new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6), p2, stroked, true);
        }
    }

    static StreamGeometry Silhouette()
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, false);
            c.LineTo(OuterTop[0], false, false);
            Spline(c, OuterTop, true);
            // Square shoulder of the Player tab, rounded on its outer corner.
            c.LineTo(new Point(797, 129), true, false);
            c.LineTo(new Point(808, 131), true, false);
            c.QuadraticBezierTo(new Point(823, 131), new Point(823, 144), true, true);
            Spline(c, OuterTab, true);
            c.LineTo(new Point(0, DesignHeight), false, false);
        }
        g.Freeze();
        return g;
    }

    static StreamGeometry Body()
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 0), true, false);
            c.LineTo(InnerRule[0], false, false);
            Spline(c, InnerRule, false);
            c.LineTo(new Point(0, DesignHeight), false, false);
        }
        g.Freeze();
        return g;
    }

    static StreamGeometry Rule(double dx)
    {
        var pts = Shift(InnerRule, dx);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false, false);
            Spline(c, pts, true);
        }
        g.Freeze();
        return g;
    }

    static StreamGeometry Header()
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(0, 1), true, false);
            c.LineTo(HeaderEdge[0], true, false);
            Spline(c, HeaderEdge, true);
            c.BezierTo(new Point(756, 119), new Point(744, HeaderBottom), new Point(722, HeaderBottom), true, true);
            c.LineTo(new Point(0, HeaderBottom), true, false);
        }
        g.Freeze();
        return g;
    }

    static SolidColorBrush S(string hex) => GuideUi.Themed(hex);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var body = Body();

        // 1. Outer band with the dark outline.
        dc.DrawGeometry(S("#E1B423"), new Pen(S("#46432F"), 4) { LineJoin = PenLineJoin.Round }, Silhouette());

        // 2. Body: soft horizontal highlight like the reference (#F5C41F -> #FDD53A -> #E3B725).
        dc.DrawGeometry(GuideUi.Horizontal((0, "#F5C41F"), (0.52, "#FDD53A"), (1, "#E3B725")), null, body);

        // 3. Faint X-in-circle watermark, clipped to the body.
        dc.PushClip(body);
        var mark = new Pen(S("#EBBB1E"), 2);
        for (int r = 0; r < 14; r++)
            for (int k = 0; k < 9; k++)
            {
                double cx = 58 + k * 100 + (r % 2) * 50, cy = 190 + r * 86;
                dc.DrawEllipse(null, mark, new Point(cx, cy), 17, 17);
                dc.DrawLine(mark, new Point(cx - 9, cy - 9), new Point(cx + 9, cy + 9));
                dc.DrawLine(mark, new Point(cx + 9, cy - 9), new Point(cx - 9, cy + 9));
            }

        // 4. Footer band (dark olive fading into gold) and its cream hairline.
        dc.DrawRectangle(GuideUi.Vertical((0, "#3F3B1E"), (0.19, "#655819"), (0.35, "#867213"), (0.51, "#A78C0D"),
                                          (0.68, "#BC9C0E"), (0.84, "#D1AB0E"), (1, "#DDB510")),
                         null, new Rect(0, BodyBottom, DesignWidth, DesignHeight - BodyBottom));
        dc.DrawLine(new Pen(S("#E8E9CA"), 2), new Point(0, BodyBottom), new Point(DesignWidth, BodyBottom));
        dc.Pop();

        // 5. Header card: dark olive to gold, cream outline with a rounded lower-right corner.
        dc.DrawGeometry(GuideUi.Vertical((0, "#4B431A"), (0.26, "#756516"), (0.51, "#A78C0D"), (0.77, "#C8A40E"), (1, "#E9B818")),
                        new Pen(S("#E8E9CA"), 2) { LineJoin = PenLineJoin.Round }, Header());

        // 6. Inner dark rule and the cream highlight rule that follows it.
        dc.DrawGeometry(null, new Pen(S("#5B552E"), 4), Rule(0));
        dc.DrawGeometry(null, new Pen(S("#FAF2B9"), 4), Rule(8));
    }
}
