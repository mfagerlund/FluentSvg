using System.Drawing;
using System.Numerics;
using FluentSvg;

// Generates the gallery of demo SVGs. Each file is a "combo" that exercises many published
// methods; between them they touch (nearly) the whole API. Usage:
//   dotnet run -- <outputDir>        (defaults to ../../gallery)
//
// Coverage map (headline method -> file):
//   AddSymbol/AddUse/AddAnimateTranslate/AddAnimateRotate/SetCornerRadius .. car.svg
//   AddPathBuilder (Cubic/Quad/Arc), DecodePath .......................... curves.svg
//   AddEllipse/AddPolygon/AddPolyline/AddSvg/PushGroup/PopGroup/
//     RotateAroundPoint/AddComment/Optional/System.Drawing.Color/text align  primitives.svg
//   AddClipPath (rect/triangle)/AddClipPathExcludeTriangle ............... clips.svg
//   AddAnimateXy/AddAnimate ............................................. orbits.svg, pulse.svg
//   AddAnimateLine/AddLine ............................................... wave.svg

var outDir = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "gallery"));
Directory.CreateDirectory(outDir);

const string Bg = "#0b1020";
const string Ink = "#e2e8f0";
const string Muted = "#94a3b8";

Car();
Curves();
Primitives();
Clips();
Orbits();
Pulse();
Wave();

Console.WriteLine($"Wrote gallery to {outDir}");

string File(string name) => Path.Combine(outDir, name);

void Background(Svg svg, float w, float h)
    => svg.AddRectangleFromTo(new Vector2(0, 0), new Vector2(w, h)).SetFill(Bg).ClearStroke();

Svg.Text Label(Svg svg, Vector2 at, string text, float size = 6f, string? color = null)
    => svg.AddText(at, text).Center().SetFill(color ?? Muted).SetFontSize(size).SetFontFamily("sans-serif");

// repeatCount='indefinite' makes the animations loop forever in a browser / README.
void Loop(params Svg.RenderItem[] anims)
{
    foreach (var a in anims) a.SetAttribute("repeatCount", "indefinite");
}

List<Vector2> EllipsePts(Vector2 center, float rx, float ry, int n, float phase = 0f)
{
    var pts = new List<Vector2>();
    for (var i = 0; i <= n; i++)
    {
        var t = phase + MathF.Tau * i / n;
        pts.Add(center + new Vector2(rx * MathF.Cos(t), ry * MathF.Sin(t)));
    }
    return pts;
}

// ---- 1. A real car driving (and steering) around a track ---------------------
void Car()
{
    const float w = 240, h = 150;
    var svg = new Svg(File("car.svg"), title: "FluentSvg — a car steering around a track");
    Background(svg, w, h);
    var center = new Vector2(w / 2, h / 2);

    // A wavy closed loop we can both draw and sample analytically.
    Vector2 Track(float t)
    {
        var a = MathF.Tau * t;
        var rx = 84 + 8 * MathF.Cos(3 * a);
        var ry = 44 + 6 * MathF.Sin(2 * a);
        return center + new Vector2(rx * MathF.Cos(a), ry * MathF.Sin(a));
    }

    List<Vector2> Sample(int n)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i <= n; i++) pts.Add(Track((float)i / n));
        return pts;
    }

    // Heading (degrees) at each sample, unwrapped so it winds once (+360) over the loop
    // -> the rotate animation returns to its start seamlessly.
    List<float> Headings(int n)
    {
        var raw = new List<float>();
        for (var i = 0; i <= n; i++)
        {
            var d = Track((float)(i + 1) / n) - Track((float)i / n);
            raw.Add(MathF.Atan2(d.Y, d.X) * 180f / MathF.PI);
        }
        var outp = new List<float> { raw[0] };
        for (var i = 1; i < raw.Count; i++)
        {
            var delta = raw[i] - raw[i - 1];
            while (delta > 180) delta -= 360;
            while (delta < -180) delta += 360;
            outp.Add(outp[i - 1] + delta);
        }
        return outp;
    }

    // Road: thick grey stroke + dashed centre line.
    var road = Sample(160);
    svg.AddPath(road).SetClosed().ClearStroke().SetStroke("#334155", 26).SetFill("transparent")
       .SetAttribute("stroke-linejoin", "round").SetAttribute("stroke-linecap", "round");
    svg.AddPath(road).SetClosed().SetStroke("#facc15", 1).SetFill("transparent")
       .SetStrokeDashArray("5 6").SetStrokeOpacity(0.65f);

    // The car, defined once as a <symbol> centred on the origin, nose toward +x.
    var sym = svg.AddSymbol("car");
    void Part(Svg.RenderItem r) => sym.AddChild(r);
    Part(svg.AddCircle(new Vector2(-6, 5), 3.2f).SetFill("#0f172a").SetStroke("#334155", 1)); // wheels
    Part(svg.AddCircle(new Vector2(6, 5), 3.2f).SetFill("#0f172a").SetStroke("#334155", 1));
    Part(svg.AddRectangleCenterSized(new Vector2(0, 0), new Vector2(24, 10)).SetCornerRadius(3)
            .SetFill("#38bdf8").SetStroke("#0ea5e9", 1));                                     // body
    Part(svg.AddRectangleCenterSized(new Vector2(-1, -5), new Vector2(12, 7)).SetCornerRadius(2)
            .SetFill("#bae6fd").SetStroke("#0ea5e9", 1));                                     // cabin
    Part(svg.AddRectangleCenterSized(new Vector2(-1, -5), new Vector2(9, 4)).SetCornerRadius(1)
            .SetFill("#0f172a").ClearStroke());                                              // window
    Part(svg.AddCircle(new Vector2(11, -1), 1.4f).SetFill("#fde68a").ClearStroke());         // headlight

    var car = svg.AddUse("car"); // placed at origin, then driven by the transform animations
    var pos = Sample(140);
    var head = Headings(140);
    const string dur = "9s";
    var move = svg.AddAnimateTranslate(car, dur, pos);
    var turn = svg.AddAnimateRotate(car, dur, head, additive: true); // additive: composes with translate
    Loop(move, turn);

    Label(svg, new Vector2(center.X, 13), "AddSymbol + AddUse, driven by AddAnimateTranslate + AddAnimateRotate", 5.4f, Ink);
    svg.SaveToFile();
}

// ---- 2. PathBuilder: cubic, quadratic and arc segments -----------------------
void Curves()
{
    const float w = 240, h = 140;
    var svg = new Svg(File("curves.svg"), title: "FluentSvg — curves (PathBuilder)");
    Background(svg, w, h);

    // Heart from two cubic beziers.
    const float hx = 45, hy = 48;
    svg.AddPathBuilder()
       .MoveTo(new Vector2(hx, hy + 20))
       .CubicTo(new Vector2(hx - 26, hy), new Vector2(hx - 16, hy - 26), new Vector2(hx, hy - 8))
       .CubicTo(new Vector2(hx + 16, hy - 26), new Vector2(hx + 26, hy), new Vector2(hx, hy + 20))
       .Close()
       .SetFill("#f472b6").SetStroke("#be185d", 1);
    Label(svg, new Vector2(hx, hy + 34), "CubicTo");

    // Quadratic wave.
    var wave = svg.AddPathBuilder().SetFill("transparent").SetStroke("#22d3ee", 2)
                  .SetAttribute("stroke-linecap", "round");
    wave.MoveTo(new Vector2(95, 45));
    for (var i = 0; i < 4; i++)
    {
        var x = 95 + i * 24;
        var up = i % 2 == 0;
        wave.QuadTo(new Vector2(x + 12, up ? 20 : 70), new Vector2(x + 24, 45));
    }
    Label(svg, new Vector2(131, 84), "QuadTo");

    // Arc (a pie slice) via ArcTo.
    var cx = 200f;
    var cy = 52f;
    svg.AddPathBuilder()
       .MoveTo(new Vector2(cx, cy))
       .LineTo(new Vector2(cx + 26, cy))
       .ArcTo(new Vector2(26, 26), 0, largeArc: false, sweep: true, new Vector2(cx, cy + 26))
       .Close()
       .SetFill("#a78bfa").SetStroke("#7c3aed", 1);
    Label(svg, new Vector2(cx, cy + 40), "ArcTo");

    // DecodePath: parse an SVG 'd' string back into points, then draw it as a polyline.
    var decoded = Svg.DecodePath("M14,118 L54,98 L94,118 L134,98 L174,118 L214,98");
    svg.AddPath(decoded).ClearStroke().SetStroke("#34d399", 2).SetFill("transparent");
    Label(svg, new Vector2(w / 2, 132), "Svg.DecodePath -> AddPath");

    svg.SaveToFile();
}

// ---- 3. Static primitive showcase -------------------------------------------
void Primitives()
{
    const float w = 260, h = 170;
    var svg = new Svg(File("primitives.svg"), title: "FluentSvg — primitives");
    Background(svg, w, h);
    svg.AddComment("primitive showcase generated by FluentSvg");

    // rounded rect via System.Drawing.Color overloads
    svg.AddRectangleSized(new Vector2(14, 18), new Vector2(46, 30)).SetCornerRadius(7)
       .SetFill(Color.SteelBlue).SetStroke(Color.LightSkyBlue, 2);
    // dashed rect
    svg.AddRectangleSized(new Vector2(72, 18), new Vector2(46, 30))
       .SetFill("transparent").SetStroke("#f472b6", 2).SetStrokeDashArray("6 4");
    // circle with fill opacity
    svg.AddCircle(new Vector2(150, 33), 15).SetFill("#34d399").SetFillOpacity(0.5f).SetStroke("#10b981", 2);
    // ellipse
    svg.AddEllipse(new Vector2(214, 33), 22, 13).SetFill("#fbbf24").SetStroke("#f59e0b", 2);

    // polygon (star) + polyline (zigzag) + dashed line
    svg.AddPolygon(Star(new Vector2(40, 86), 18, 8, 5)).SetFill("#a78bfa").SetStroke("#7c3aed", 1);
    svg.AddPolyline(new Vector2(74, 96), new Vector2(90, 76), new Vector2(106, 96), new Vector2(122, 76))
       .SetFill("transparent").SetStroke("#22d3ee", 2).SetAttribute("stroke-linejoin", "round");
    svg.AddLine(new Vector2(140, 86), new Vector2(246, 86)).SetStroke("#475569", 1).SetStrokeDashArray("2 3");

    // a group we rotate as a unit (PushGroup / PopGroup + RotateAroundPoint)
    svg.PushGroup();
    svg.AddRectangleCenterSized(new Vector2(196, 96), new Vector2(30, 14)).SetFill("#1e293b").SetStroke("#38bdf8", 1);
    svg.AddCircle(new Vector2(196, 96), 3).SetFill("#38bdf8").ClearStroke();
    var group = svg.PopGroup();
    group.RotateAroundPoint(new Vector2(196, 96), -12).SetStrokeOpacity(0.9f);

    // a nested <svg> (SubSvg) holding its own content
    var sub = svg.AddSvg();
    sub.SetAttribute("x", "228").SetAttribute("y", "78");
    sub.AddChild(svg.AddCircle(new Vector2(8, 8), 6).SetFill("#f97316").ClearStroke());

    // text: alignment helpers + weight/family; object overload; Optional
    Label(svg, new Vector2(w / 2, 128), "shapes · paths · text · groups · clips", 6.5f, Ink);
    svg.AddText(new Vector2(14, 146), "AlignLeft").AlignLeft().SetFill(Muted).SetFontSize(7.0).SetFontFamily("sans-serif");
    svg.AddText(new Vector2(w / 2, 146), "Center").Center().SetFill(Muted).SetFontSize(7.0).SetFontFamily("sans-serif");
    svg.AddText(new Vector2(246, 146), "AlignRight").AlignRight().SetFill(Muted).SetFontSize(7.0).SetFontFamily("sans-serif");
    svg.AddText(new Vector2(w / 2, 160), 2026).Center().SetFill(Muted).SetFontSize(6.0)
       .SetFontFamily("serif").SetFontWeight("bold")
       .Optional(true, t => t.SetStrokeOpacity(0f));

    svg.SaveToFile();
}

// ---- 4. Clip paths -----------------------------------------------------------
void Clips()
{
    const float w = 240, h = 110;
    var svg = new Svg(File("clips.svg"), title: "FluentSvg — clip paths");
    Background(svg, w, h);

    // A reusable busy backdrop (diagonal bands) that we clip three different ways.
    void Backdrop(Vector2 origin, string clipId)
    {
        svg.PushGroup();
        for (var i = -6; i < 12; i++)
        {
            var x = origin.X + i * 7;
            svg.AddLine(new Vector2(x, origin.Y), new Vector2(x - 40, origin.Y + 60))
               .SetStroke(i % 2 == 0 ? "#38bdf8" : "#f472b6", 5);
        }
        var g = svg.PopGroup();
        g.SetAttribute("clip-path", $"url(#{clipId})");
    }

    // rectangle clip
    svg.AddClipPath("clipRect", new Vector2(14, 26), new Vector2(56, 56));
    Backdrop(new Vector2(30, 26), "clipRect");
    Label(svg, new Vector2(42, 96), "AddClipPath (rect)", 5.4f);

    // triangle clip
    svg.AddClipPath("clipTri", new Vector2(120, 26), new Vector2(92, 82), new Vector2(148, 82));
    Backdrop(new Vector2(126, 26), "clipTri");
    Label(svg, new Vector2(120, 96), "AddClipPath (triangle)", 5.4f);

    // everything-except-triangle clip
    svg.AddClipPathExcludeTriangle("clipHole", new Vector2(196, 30), new Vector2(178, 74), new Vector2(214, 74),
        new Vector2(170, 24), new Vector2(224, 84));
    Backdrop(new Vector2(196, 26), "clipHole");
    Label(svg, new Vector2(197, 96), "AddClipPathExcludeTriangle", 5.0f);

    svg.SaveToFile();
}

// ---- 5. Planets orbiting a sun ----------------------------------------------
void Orbits()
{
    const float w = 200, h = 200;
    var svg = new Svg(File("orbits.svg"), title: "FluentSvg — orbits");
    Background(svg, w, h);
    var c = new Vector2(w / 2, h / 2);

    (float r, string col, string dur, float size)[] planets =
    [
        (34, "#60a5fa", "4s", 4f),
        (58, "#34d399", "7s", 5.5f),
        (82, "#f472b6", "11s", 7f),
    ];

    foreach (var (r, col, dur, size) in planets)
    {
        svg.AddEllipse(c, r, r).ClearStroke().SetStroke("#1e293b", 1).SetFill("transparent");
        var orbit = EllipsePts(c, r, r, 72);
        var planet = svg.AddCircle(orbit[0], size).SetFill(col).ClearStroke();
        var (ox, oy) = svg.AddAnimateXy(planet, dur, orbit);
        Loop(ox, oy);
    }

    var sun = svg.AddCircle(c, 12).SetFill("#fbbf24").SetStroke("#f59e0b", 1).SetId("sun");
    var pulse = svg.AddAnimate(sun, "r", "2.5s", new[] { 11f, 14f, 11f });
    Loop(pulse);

    svg.SaveToFile();
}

// ---- 6. Pulsing concentric rings (animating r / stroke-opacity) -------------
void Pulse()
{
    const float w = 200, h = 120;
    var svg = new Svg(File("pulse.svg"), title: "FluentSvg — pulse");
    Background(svg, w, h);
    var c = new Vector2(w / 2, h / 2);

    string[] cols = ["#f472b6", "#a78bfa", "#38bdf8", "#34d399"];
    for (var i = 0; i < cols.Length; i++)
    {
        var ring = svg.AddCircle(c, 6).ClearStroke().SetStroke(cols[i], 2).SetFill("transparent").SetId($"ring{i}");
        var lo = 6f + i * 2;
        var hi = 46f + i * 2;
        var rAnim = svg.AddAnimate(ring, "r", "3s", new[] { lo, hi, lo });
        var oAnim = svg.AddAnimate(ring, "stroke-opacity", "3s", new[] { 0.9f, 0.0f, 0.9f });
        Loop(rAnim, oAnim);
    }

    svg.AddCircle(c, 4).SetFill("#fef08a").ClearStroke();
    svg.SaveToFile();
}

// ---- 7. A travelling sine wave (AddAnimateLine) -----------------------------
void Wave()
{
    const float w = 220, h = 120;
    var svg = new Svg(File("wave.svg"), title: "FluentSvg — wave");
    Background(svg, w, h);

    const int bars = 34, frames = 24;
    const float baseY = h - 20, amp = 34f, margin = 12f;
    var step = (w - 2 * margin) / (bars - 1);
    string[] cols = ["#38bdf8", "#22d3ee", "#34d399"];

    for (var b = 0; b < bars; b++)
    {
        var x = margin + b * step;
        var bottom = new Vector2(x, baseY);
        var starts = new List<Vector2>();
        var ends = new List<Vector2>();
        for (var f = 0; f <= frames; f++)
        {
            var phase = MathF.Tau * f / frames;
            var k = MathF.Sin(phase + b * 0.45f) * 0.5f + 0.5f;
            starts.Add(bottom);
            ends.Add(new Vector2(x, baseY - 6 - amp * k));
        }

        var line = svg.AddLine(bottom, new Vector2(x, baseY - 6))
                      .SetStroke(cols[b % cols.Length], 3).SetStrokeOpacity(0.85f)
                      .SetAttribute("stroke-linecap", "round");
        Loop(svg.AddAnimateLine(line, "2.4s", starts, ends));
    }

    svg.SaveToFile();
}

// Star polygon points (outer/inner radius), used by the primitives demo.
Vector2[] Star(Vector2 center, float outer, float inner, int points)
{
    var pts = new Vector2[points * 2];
    for (var i = 0; i < points * 2; i++)
    {
        var r = i % 2 == 0 ? outer : inner;
        var a = -MathF.PI / 2 + MathF.PI * i / points;
        pts[i] = center + new Vector2(r * MathF.Cos(a), r * MathF.Sin(a));
    }
    return pts;
}
