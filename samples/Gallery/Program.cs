using System.Numerics;
using FluentSvg;

// Generates the gallery of demo SVGs.
// Usage: dotnet run -- <outputDir>   (defaults to ../../gallery)

var outDir = args.Length > 0
    ? args[0]
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "gallery"));
Directory.CreateDirectory(outDir);

const string Bg = "#0b1020";
const string Ink = "#e2e8f0";

CarCorridor();
Orbits();
Pulse();
Wave();
Shapes();

Console.WriteLine($"Wrote gallery to {outDir}");

string File(string name) => Path.Combine(outDir, name);

void Background(Svg svg, float w, float h)
    => svg.AddRectangleFromTo(new Vector2(0, 0), new Vector2(w, h)).SetFill(Bg).ClearStroke();

// repeatCount='indefinite' so the animations loop forever in a browser / README.
void Loop(params Svg.Animate[] anims)
{
    foreach (var a in anims) a.SetAttribute("repeatCount", "indefinite");
}

// Points around an ellipse; the first point is repeated at the end so a loop is seamless.
List<Vector2> Ellipse(Vector2 center, float rx, float ry, int n, float phase = 0f)
{
    var pts = new List<Vector2>();
    for (var i = 0; i <= n; i++)
    {
        var t = phase + MathF.Tau * i / n;
        pts.Add(center + new Vector2(rx * MathF.Cos(t), ry * MathF.Sin(t)));
    }
    return pts;
}

// ---- 1. A car driving a corridor (the headline demo) -------------------------
void CarCorridor()
{
    const float w = 200, h = 120;
    var svg = new Svg(File("car-corridor.svg"), title: "FluentSvg — car following a corridor");
    Background(svg, w, h);

    var center = new Vector2(w / 2, h / 2);
    var track = Ellipse(center, 78, 44, 90);

    // Road: a wide grey stroke along the track, with a dashed centre line.
    svg.AddPath(track).SetClosed().ClearStroke().SetStroke("#334155", 22).SetFill("transparent")
       .SetAttribute("stroke-linejoin", "round").SetAttribute("stroke-linecap", "round");
    svg.AddPath(track).SetClosed().SetStroke("#facc15", 1).SetFill("transparent")
       .SetStrokeDashArray("4 5").SetStrokeOpacity(0.7f);

    // The car body + a small nose circle showing heading (leads the body along the track).
    var nosePts = new List<Vector2>();
    for (var i = 0; i < track.Count; i++) nosePts.Add(track[(i + 3) % track.Count]);

    var car = svg.AddCircle(track[0], 5.5f).SetFill("#38bdf8").SetStroke("#0ea5e9", 1);
    var nose = svg.AddCircle(nosePts[0], 2.2f).SetFill("#f8fafc").ClearStroke();

    const string dur = "7s";
    var (cx, cy) = svg.AddAnimateXy(car, dur, track);
    var (nx, ny) = svg.AddAnimateXy(nose, dur, nosePts);
    Loop(cx, cy, nx, ny);

    svg.AddText(new Vector2(center.X, 12), "AddAnimateXy along a closed path")
       .Center().SetFill(Ink).SetFontSize(6.0).SetFontFamily("sans-serif");

    svg.SaveToFile();
}

// ---- 2. Planets orbiting a sun ----------------------------------------------
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
        // faint orbit ring
        svg.AddCircle(c, r).ClearStroke().SetStroke("#1e293b", 1).SetFill("transparent");
        var orbit = Ellipse(c, r, r, 72);
        var planet = svg.AddCircle(orbit[0], size).SetFill(col).ClearStroke();
        var (ox, oy) = svg.AddAnimateXy(planet, dur, orbit);
        Loop(ox, oy);
    }

    // pulsing sun
    var sun = svg.AddCircle(c, 12).SetFill("#fbbf24").SetStroke("#f59e0b", 1).SetId("sun");
    var pulse = svg.AddAnimate(sun, "r", "2.5s", new[] { 11f, 14f, 11f });
    Loop(pulse);

    svg.SaveToFile();
}

// ---- 3. Pulsing concentric rings (animating r / opacity) --------------------
void Pulse()
{
    const float w = 200, h = 120;
    var svg = new Svg(File("pulse.svg"), title: "FluentSvg — pulse");
    Background(svg, w, h);
    var c = new Vector2(w / 2, h / 2);

    string[] cols = ["#f472b6", "#a78bfa", "#38bdf8", "#34d399"];
    for (var i = 0; i < cols.Length; i++)
    {
        var ring = svg.AddCircle(c, 6).ClearStroke().SetStroke(cols[i], 2).SetFill("transparent")
                      .SetId($"ring{i}");
        // staggered radii so the rings ripple outward
        var lo = 6f + i * 2;
        var hi = 46f + i * 2;
        var rAnim = svg.AddAnimate(ring, "r", "3s", new[] { lo, hi, lo });
        var oAnim = svg.AddAnimate(ring, "stroke-opacity", "3s", new[] { 0.9f, 0.0f, 0.9f });
        Loop(rAnim, oAnim);
    }

    var dot = svg.AddCircle(c, 4).SetFill("#fef08a").ClearStroke();
    svg.SaveToFile();
}

// ---- 4. A travelling sine wave (AddAnimateLine) -----------------------------
void Wave()
{
    const float w = 220, h = 120;
    var svg = new Svg(File("wave.svg"), title: "FluentSvg — wave");
    Background(svg, w, h);

    const int bars = 34;
    const int frames = 24;
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
            var phase = MathF.Tau * f / frames;            // one full loop
            var k = MathF.Sin(phase + b * 0.45f) * 0.5f + 0.5f; // 0..1 height
            starts.Add(bottom);
            ends.Add(new Vector2(x, baseY - 6 - amp * k));
        }

        var line = svg.AddLine(bottom, new Vector2(x, baseY - 6))
                      .SetStroke(cols[b % cols.Length], 3).SetStrokeOpacity(0.85f)
                      .SetAttribute("stroke-linecap", "round");
        var anim = svg.AddAnimateLine(line, "2.4s", starts, ends);
        Loop(anim);
    }

    svg.SaveToFile();
}

// ---- 5. Static shapes showcase ----------------------------------------------
void Shapes()
{
    const float w = 220, h = 130;
    var svg = new Svg(File("shapes.svg"), title: "FluentSvg — shapes");
    Background(svg, w, h);

    svg.AddRectangleSized(new Vector2(14, 22), new Vector2(40, 28))
       .SetFill("#1e293b").SetStroke("#38bdf8", 2);

    svg.AddRectangleSized(new Vector2(66, 22), new Vector2(40, 28))
       .SetFill("transparent").SetStroke("#f472b6", 2).SetStrokeDashArray("6 4");

    svg.AddCircle(new Vector2(138, 36), 16).SetFill("#34d399").SetFillOpacity(0.5f).SetStroke("#10b981", 2);

    // closed triangle path
    svg.AddPath(new Vector2(176, 50), new Vector2(206, 50), new Vector2(191, 22))
       .SetClosed().SetFill("#fbbf24").SetStroke("#f59e0b", 2);

    svg.AddLine(new Vector2(14, 64), new Vector2(206, 64)).SetStroke("#475569", 1).SetStrokeDashArray("2 3");

    // text alignment row
    svg.AddText(new Vector2(20, 86), "AlignLeft").AlignLeft().SetFill(Ink).SetFontSize(7.0).SetFontFamily("sans-serif");
    svg.AddText(new Vector2(110, 86), "Center").Center().SetFill(Ink).SetFontSize(7.0).SetFontFamily("sans-serif");
    svg.AddText(new Vector2(206, 86), "AlignRight").AlignRight().SetFill(Ink).SetFontSize(7.0).SetFontFamily("sans-serif");

    svg.AddText(new Vector2(110, 110), "shapes · paths · text · strokes · fills")
       .Center().SetFill("#94a3b8").SetFontSize(6.0).SetFontFamily("sans-serif");

    svg.SaveToFile();
}
