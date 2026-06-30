using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Color = System.Drawing.Color;

namespace FluentSvg;

/// <summary>
/// A minimal, dependency-free fluent builder for SVG documents.
/// Build up shapes, paths, text and SMIL animations, then render to a string or file.
/// </summary>
public class Svg : Svg.IAttributed
{
    // https://css-tricks.com/guide-svg-animations-smil/
    public Svg(
        string fileName,
        Vector2? size = null,
        string? title = null,
        [CallerMemberName] string? callerMemberName = null,
        [CallerFilePath] string? callerFilePath = null)
    {
        FileName = fileName;
        Size = size;

        // Set title: use provided title, or default to ClassName.MethodName
        if (title != null)
        {
            Title = title;
        }
        else if (!string.IsNullOrEmpty(callerMemberName))
        {
            var className = System.IO.Path.GetFileNameWithoutExtension(callerFilePath);
            Title = !string.IsNullOrEmpty(className)
                ? $"{className}.{callerMemberName}"
                : callerMemberName;
        }

        this.SetStroke("DarkGoldenRod");
        this.SetStrokeWidth("1");
        this.SetFill("transparent");
        this.SetAttribute("xmlns", "http://www.w3.org/2000/svg");
        this.SetAttribute("xmlns:xlink", "http://www.w3.org/1999/xlink");
    }

    public string FileName { get; set; }
    public Vector2? Size { get; set; }
    public string? Title { get; set; }
    public List<RenderItem> RenderItems { get; } = [];
    public List<ClipPath> ClipPaths { get; } = [];
    public IEnumerable<Vector2> Points => RenderItems.SelectMany(x => x.Points).Concat(ClipPaths.SelectMany(x => x.Points));
    public AttributeCollection Attributes { get; } = new();
    public string Id { get; set; }

    /// <summary>Convenience factory that targets a scratch file in the system temp directory.</summary>
    public static Svg Bob(
        Vector2? size = null,
        string? title = null,
        [CallerMemberName] string? callerMemberName = null,
        [CallerFilePath] string? callerFilePath = null)
        => new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bob.svg"), size, title, callerMemberName, callerFilePath);

    public Path AddPath(IEnumerable<IEnumerable<Vector2>> points) => new(this, points);
    public Path AddPath(IEnumerable<Vector2> points) => new(this, new List<IEnumerable<Vector2>> { points });
    public Path AddPath(IEnumerable<Vector2I> points) => new(this, new List<IEnumerable<Vector2>> { points.Select(p => p.ToVector2()) });
    public Path AddPath(params Vector2[] points) => new(this, new List<IEnumerable<Vector2>> { points });
    public Path AddPath(params Vector2I[] points) => new(this, new List<IEnumerable<Vector2>> { points.Select(p => p.ToVector2()) });
    public Circle AddCircle(Vector2 center, float radius) => new(this, center, radius);
    public ClipPath AddClipPath(string id, Vector2 position, Vector2 size) => new(this, id, position, size);
    public ClipPath AddClipPath(string id, Vector2 p1, Vector2 p2, Vector2 p3) => new(this, id, p1, p2, p3);
    /// <summary>Creates a clip path that EXCLUDES the triangle - clips out the triangle area.</summary>
    public ClipPath AddClipPathExcludeTriangle(string id, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 boundingMin, Vector2 boundingMax)
        => new(this, id, p1, p2, p3, boundingMin, boundingMax);
    public SubSvg AddSvg() => new(this);
    public Rectangle AddRectangleFromTo(Vector2 from, Vector2 to) => new(this, from, to);
    public Rectangle AddRectangleSized(Vector2 from, Vector2 size) => new(this, from, from + size);
    public Rectangle AddRectangleCenterSized(Vector2 center, Vector2 size) => new(this, center - size * 0.5f, center + size * 0.5f);

    public Animate AddAnimate(RenderItem target, string attributeName, string duration, IEnumerable<float> values)
        => new(this, target, attributeName, duration, values);

    public (Animate x, Animate y) AddAnimateXy(
        RenderItem target,
        string duration,
        ICollection<Vector2> values,
        string attributePrefix = "c")
    {
        if (string.IsNullOrWhiteSpace(target.Id))
        {
            target.SetId(Guid.NewGuid().ToString().Substring(0, 6));
        }

        return (
            AddAnimate(target, $"{attributePrefix}x", duration, values.Select(state => state.X)),
            AddAnimate(target, $"{attributePrefix}y", duration, values.Select(state => state.Y)));
    }

    public Animate AddAnimateLine(
        Line line,
        string duration,
        IEnumerable<Vector2> positionsStart,
        IEnumerable<Vector2> positionsEnd)
    {
        if (string.IsNullOrWhiteSpace(line.Id))
        {
            line.SetId(Guid.NewGuid().ToString().Substring(0, 6));
        }

        var startPoints = positionsStart.ToList();
        var endPoints = positionsEnd.ToList();

        if (startPoints.Count != endPoints.Count)
        {
            throw new ArgumentException("The number of start and end positions must be the same.");
        }

        var pathValues = new List<string>();
        for (var i = 0; i < startPoints.Count; i++)
        {
            var start = startPoints[i];
            var end = endPoints[i];
            pathValues.Add($"M{Tos(start.X)},{Tos(start.Y)} L{Tos(end.X)},{Tos(end.Y)}");
        }

        var animate = new Animate(this, line, "d", duration, pathValues);
        return animate;
    }


    public Line AddLine(Vector2 from, Vector2 to) => new(this, from, to);
    public Text AddText(Vector2 position, string label) => new(this, position, label);
    public Text AddText(Vector2 position, object label) => new(this, position, label.ToString());
    public Comment AddComment(string label) => new(this, label);
    public Vector2 Margin { get; set; } = Vector2.One;
    public Stack<List<RenderItem>> RenderItemStack { get; } = new();
    public string Indent { get; private set; } = "  ";
    public static string RandomColor => $"#{Random.Shared.Next(256):X2}{Random.Shared.Next(256):X2}{Random.Shared.Next(256):X2}";
    public static string ToRgbString(Color color) => $"rgb({color.R}, {color.G}, {color.B})";

    public void PushGroup()
    {
        RenderItemStack.Push(RenderItems.ToList());
        RenderItems.Clear();
    }

    public Group PopGroup()
    {
        if (!RenderItemStack.Any())
        {
            throw new InvalidOperationException("RenderItemsStack is empty!");
        }

        var children = RenderItems.ToList();
        var group = new Group(this);
        group.Children.AddRange(children);
        RenderItems.Clear();
        RenderItems.AddRange(RenderItemStack.Pop());
        RenderItems.Add(group);
        return group;
    }

    public void SaveToFile(bool moveTextToTop = true)
    {
        if (moveTextToTop)
        {
            MoveTextToTop();
        }

        RenderToFile();
    }

    /// <summary>Renders the document to an SVG string.</summary>
    public string Render(bool moveTextToTop = true)
    {
        if (moveTextToTop)
        {
            MoveTextToTop();
        }

        while (RenderItemStack.Any())
        {
            PopGroup();
        }

        (var min, var max) = GetExtents();
        max = max + Margin;

        var size = Size ?? max - min;
        this.SetAttribute("width", $"{Tos(size.X)}mm");
        this.SetAttribute("height", $"{Tos(size.Y)}mm");

        var sb = new StringBuilder();
        sb.AppendLine(
            $"<svg " +
            $"viewBox='{Tos(min.X)} {Tos(min.Y)} {Tos(max.X - min.X)} {Tos(max.Y - min.Y)}' " +
            $"{Attributes} >");

        // Render title element if set
        if (!string.IsNullOrEmpty(Title))
        {
            sb.AppendLine($"{Indent}<title>{Title}</title>");
        }

        // Render defs section if we have clip paths
        if (ClipPaths.Count > 0)
        {
            sb.AppendLine($"{Indent}<defs>");
            var oldIndent = Indent;
            Indent += "  ";
            ClipPaths.ForEach(x => x.Render(sb));
            Indent = oldIndent;
            sb.AppendLine($"{Indent}</defs>");
        }

        RenderItems.ForEach(x => x.Render(sb));
        sb.AppendLine($"</svg>");
        return sb.ToString().Replace("'", "\"");
    }

    public void RenderToFile()
    {
        File.WriteAllText(FileName, Render(moveTextToTop: false));
    }

    public (Vector2, Vector2) GetExtents()
    {
        var points = Points.ToList();
        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        if (points.Any())
        {
            min = new Vector2(points.Min(p => p.X), points.Min(p => p.Y));
            max = new Vector2(points.Max(p => p.X), points.Max(p => p.Y));
        }

        if (Math.Abs(min.X - max.X) < float.Epsilon)
        {
            min = new Vector2(min.X - 0.1f, min.Y);
            max = new Vector2(max.X + 0.1f, max.Y);
        }

        if (Math.Abs(min.Y - max.Y) < float.Epsilon)
        {
            min = new Vector2(min.X, min.Y - 0.1f);
            max = new Vector2(max.X, max.Y + 0.1f);
        }

        return (min, max);
    }

    public static string Tos(float value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool RoughlyEquals(Vector2 a, Vector2 b, float epsilon = 1e-4f)
        => Math.Abs(a.X - b.X) < epsilon && Math.Abs(a.Y - b.Y) < epsilon;

    public static List<List<Vector2>> DecodePath(string spath, float scale = 1)
    {
        var pathSteps = new Queue<string>();
        var regexObj = new Regex(@"[MLHVZ, ]|-?\d+(\.\d+)?", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.CultureInvariant);
        var matchResult = regexObj.Match(spath);
        while (matchResult.Success)
        {
            if (matchResult.Value != " " && matchResult.Value != ",")
            {
                pathSteps.Enqueue(matchResult.Value);
            }

            matchResult = matchResult.NextMatch();
        }

        var paths = new List<List<Vector2>>();
        List<Vector2> path = null;
        var currentPos = new Vector2(float.NaN, float.NaN);

        void MoveTo(Vector2 newPos)
        {
            if (path == null)
            {
                throw new InvalidOperationException($"No path is currently active!");
            }

            newPos *= scale;
            if (!RoughlyEquals(currentPos, newPos))
            {
                currentPos = newPos;
                path.Add(currentPos);
            }
        }

        while (pathSteps.Any())
        {
            var command = pathSteps.Dequeue();
            switch (command.ToUpperInvariant())
            {
                case "M":
                    path = new List<Vector2>();
                    paths.Add(path);
                    MoveTo(new Vector2(float.Parse(pathSteps.Dequeue(), CultureInfo.InvariantCulture), float.Parse(pathSteps.Dequeue(), CultureInfo.InvariantCulture)));
                    break;
                case "L":
                    MoveTo(new Vector2(float.Parse(pathSteps.Dequeue(), CultureInfo.InvariantCulture), float.Parse(pathSteps.Dequeue(), CultureInfo.InvariantCulture)));
                    break;
                case "H":
                    MoveTo(new Vector2(currentPos.X + float.Parse(pathSteps.Dequeue(), CultureInfo.InvariantCulture), currentPos.Y));
                    break;
                case "V":
                    MoveTo(new Vector2(currentPos.X, currentPos.Y + float.Parse(pathSteps.Dequeue(), CultureInfo.InvariantCulture)));
                    break;
                case "Z":
                    MoveTo(path![0]);
                    currentPos = new Vector2(float.NaN, float.NaN);
                    path = null;
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled path command: {command}");
            }
        }

        return paths;
    }

    public class AttributeCollection
    {
        public List<Attribute> Attributes { get; } = [];

        public override string ToString()
        {
            return string.Join(" ", Attributes.Select(x => $"{x.Name}='{x.Value}'"));
        }

        public void Set(string attributeName, string attributeValue) => Set(new Attribute(attributeName, attributeValue));

        public void Set(Attribute attribute)
        {
            Attributes.RemoveAll(x => x.Name == attribute.Name);
            Attributes.Add(attribute);
        }
    }

    public interface IAttributed
    {
        string Id { get; set; }
        AttributeCollection Attributes { get; }
    }

    public class Attribute(string name, string value)
    {
        public Attribute(string name, float value) : this(name, Tos(value))
        {
        }

        public string Name { get; } = name;
        public string Value { get; } = value;
    }

    public abstract class RenderItem : IAttributed
    {
        public Svg Svg { get; }
        public string Id { get; set; }

        protected RenderItem(Svg svg)
        {
            Svg = svg;
            svg.RenderItems.Add(this);
        }

        public abstract void Render(StringBuilder sb);
        public abstract IEnumerable<Vector2> Points { get; }
        public AttributeCollection Attributes { get; } = new();

        public void Remove() => Svg.RenderItems.Remove(this);

        protected string PointToSvgText(Vector2 p)
        {
            return
                $"{Tos(p.X)} {Tos(p.Y)}";
        }
    }

    public class Group(Svg svg) : RenderItem(svg)
    {
        public override void Render(StringBuilder sb)
        {
            sb.AppendLine($"{Svg.Indent}<g {Attributes}>");
            var p = Svg.Indent;
            Svg.Indent += "  ";
            Children.ForEach(c => c.Render(sb));
            Svg.Indent = p;
            sb.AppendLine($"{Svg.Indent}</g>");
        }

        public List<RenderItem> Children { get; } = [];
        public override IEnumerable<Vector2> Points => Children.SelectMany(c => c.Points);
    }

    public class SubSvg(Svg svg) : RenderItem(svg)
    {
        public override void Render(StringBuilder sb)
        {
            sb.AppendLine($"{Svg.Indent}<svg {Attributes}>");
            var p = Svg.Indent;
            Svg.Indent += "  ";
            Children.ForEach(c => c.Render(sb));
            Svg.Indent = p;
            sb.AppendLine($"{Svg.Indent}</svg>");
        }

        public List<RenderItem> Children { get; } = [];

        public void AddChild(RenderItem child)
        {
            Svg.RenderItems.Remove(child);
            Children.Add(child);
        }

        public override IEnumerable<Vector2> Points => Children.SelectMany(c => c.Points);
    }

    public class Comment(Svg svg, string label) : RenderItem(svg)
    {
        public string Label { get; } = label;

        public override void Render(StringBuilder sb)
        {
            sb.AppendLine($"{Svg.Indent}<!-- {Label} -->");
        }

        public override IEnumerable<Vector2> Points => [];
    }

    /// <summary>
    /// A clip path definition that can be referenced by other elements.
    /// </summary>
    public class ClipPath : RenderItem
    {
        private readonly string _id;
        private readonly Vector2[]? _trianglePoints;
        private readonly Vector2 _position;
        private readonly Vector2 _size;
        private readonly bool _isTriangle;
        private readonly bool _excludeTriangle;
        private readonly Vector2 _boundingMin;
        private readonly Vector2 _boundingMax;

        /// <summary>Rectangle clip path</summary>
        public ClipPath(Svg svg, string id, Vector2 position, Vector2 size) : base(svg)
        {
            _id = id;
            _position = position;
            _size = size;
            _isTriangle = false;
            _excludeTriangle = false;
            svg.ClipPaths.Add(this);
            svg.RenderItems.Remove(this);
        }

        /// <summary>Triangle clip path (3 points)</summary>
        public ClipPath(Svg svg, string id, Vector2 p1, Vector2 p2, Vector2 p3) : base(svg)
        {
            _id = id;
            _trianglePoints = [p1, p2, p3];
            _isTriangle = true;
            _excludeTriangle = false;
            svg.ClipPaths.Add(this);
            svg.RenderItems.Remove(this);
        }

        /// <summary>
        /// Clip path that EXCLUDES a triangle - shows everything except the triangle area.
        /// Uses a large bounding box with the triangle as a hole (evenodd).
        /// </summary>
        public ClipPath(Svg svg, string id, Vector2 p1, Vector2 p2, Vector2 p3, Vector2 boundingMin, Vector2 boundingMax) : base(svg)
        {
            _id = id;
            _trianglePoints = [p1, p2, p3];
            _isTriangle = true;
            _excludeTriangle = true;
            _boundingMin = boundingMin;
            _boundingMax = boundingMax;
            svg.ClipPaths.Add(this);
            svg.RenderItems.Remove(this);
        }

        public new string Id => _id;

        public override void Render(StringBuilder sb)
        {
            sb.AppendLine($"{Svg.Indent}<clipPath id='{_id}'>");
            if (_isTriangle && _trianglePoints != null)
            {
                var p1 = _trianglePoints[0];
                var p2 = _trianglePoints[1];
                var p3 = _trianglePoints[2];

                if (_excludeTriangle)
                {
                    // Large bounding rect + triangle hole using evenodd
                    sb.AppendLine($"{Svg.Indent}  <path clip-rule='evenodd' d='" +
                        $"M{Tos(_boundingMin.X)},{Tos(_boundingMin.Y)} " +
                        $"L{Tos(_boundingMax.X)},{Tos(_boundingMin.Y)} " +
                        $"L{Tos(_boundingMax.X)},{Tos(_boundingMax.Y)} " +
                        $"L{Tos(_boundingMin.X)},{Tos(_boundingMax.Y)} Z " +
                        $"M{Tos(p1.X)},{Tos(p1.Y)} L{Tos(p2.X)},{Tos(p2.Y)} L{Tos(p3.X)},{Tos(p3.Y)} Z'/>");
                }
                else
                {
                    sb.AppendLine($"{Svg.Indent}  <path d='M{Tos(p1.X)},{Tos(p1.Y)} L{Tos(p2.X)},{Tos(p2.Y)} L{Tos(p3.X)},{Tos(p3.Y)} Z'/>");
                }
            }
            else
            {
                sb.AppendLine($"{Svg.Indent}  <rect x='{Tos(_position.X)}' y='{Tos(_position.Y)}' width='{Tos(_size.X)}' height='{Tos(_size.Y)}'/>");
            }
            sb.AppendLine($"{Svg.Indent}</clipPath>");
        }

        public override IEnumerable<Vector2> Points => _isTriangle && _trianglePoints != null
            ? _trianglePoints
            : [_position, _position + _size];
    }

    public class Path : RenderItem
    {
        private readonly List<List<Vector2>> _points;

        public Path(Svg svg, IEnumerable<IEnumerable<Vector2>> points) : base(svg)
        {
            _points = points.Select(x => x.ToList()).ToList();
        }

        public bool Closed { get; set; }
        public override IEnumerable<Vector2> Points => _points.SelectMany(x => x);

        public Path SetClosed(bool closed = true)
        {
            Closed = closed;
            return this;
        }

        public override void Render(StringBuilder sb)
        {
            sb.Append($"{Svg.Indent}<path {Attributes} d='");
            foreach (var subPath in _points)
            {
                sb.Append($"M{PointToSvgText(subPath[0])}");
                for (var index = 1; index < subPath.Count; index++)
                {
                    sb.Append($" L{PointToSvgText(subPath[index])}");
                }

                if (Closed)
                {
                    sb.Append(" Z ");
                }
            }

            sb.AppendLine("'/>");
        }
    }

    public class Line(Svg svg, Vector2 from, Vector2 to) : Path(svg, new List<List<Vector2>> { new() { from, to } });

    public class Rectangle(Svg svg, Vector2 from, Vector2 to) : RenderItem(svg)
    {
        public Vector2 From { get; } = from;
        public Vector2 To { get; } = to;
        public Vector2 Size => To - From;

        public override IEnumerable<Vector2> Points => [From, To];

        public override void Render(StringBuilder sb)
        {
            sb.AppendLine($"{Svg.Indent}<rect {Attributes} x='{Tos(From.X)}' y='{Tos(From.Y)}' width='{Tos(Size.X)}' height='{Tos(Size.Y)}'/>");
        }
    }

    public class Circle(Svg svg, Vector2 center, float radius) : RenderItem(svg)
    {
        public Vector2 Center { get; } = center;
        public float Radius { get; set; } = radius;

        public Circle SetRadius(float radius)
        {
            Radius = radius;
            return this;
        }

        public override IEnumerable<Vector2> Points =>
        [
            Center,
            Center + new Vector2(Radius, Radius),
            Center + new Vector2(Radius, -Radius),
            Center + new Vector2(-Radius, -Radius),
            Center + new Vector2(-Radius, Radius)
        ];

        public override void Render(StringBuilder sb)
        {
            sb.AppendLine($"{Svg.Indent}<circle  {Attributes} cx='{Tos(Center.X)}' cy='{Tos(Center.Y)}' r='{Tos(Radius)}' />");
        }
    }

    public class Text : RenderItem
    {
        public Text(Svg svg, Vector2 position, string label) : base(svg)
        {
            Position = position;
            Label = label;
            this.SetStroke("transparent");
            this.SetFill("black");
            this.SetAttribute("paint-order", "stroke");
        }

        public Vector2 Position { get; set; }
        public string Label { get; set; }

        public Text SetTextAnchor(string value) => this.SetAttribute("text-anchor", value);

        public Text Move(Vector2 delta)
        {
            Position += delta;
            return this;
        }

        public Text SetDefaults()
        {
            this.SetFontSize("0.75pt");
            this.SetAttribute("dy", "0.25pt");
            SetTextAnchor("middle");
            this.SetStroke("transparent");
            this.SetFill("black");
            return this;
        }

        public override void Render(StringBuilder sb)
        {
            sb.Append(
                $"{Svg.Indent}<text " +
                $"x='{Tos(Position.X)}' " +
                $"y='{Tos(Position.Y)}' " +
                Attributes);

            sb.Append(">");
            sb.Append(Label);
            sb.AppendLine("</text>");
        }

        public override IEnumerable<Vector2> Points =>
        [
            Position + new Vector2(-15, -15),
            Position + new Vector2(15, 15)
        ];

        // Current methods for reference
        public Text CenterVertically() => this.SetAttribute("dominant-baseline", "middle");
        public Text CenterHorizontally() => this.SetAttribute("text-anchor", "middle");
        public Text Center() => CenterHorizontally().CenterVertically();

        // Horizontal alignment methods
        public Text AlignLeft() => this.SetAttribute("text-anchor", "start");
        public Text AlignRight() => this.SetAttribute("text-anchor", "end");

        // Vertical alignment methods
        public Text AlignTop() => this.SetAttribute("dominant-baseline", "text-before-edge");
        public Text AlignBottom() => this.SetAttribute("dominant-baseline", "text-after-edge");

        // Combined alignment methods
        public Text AlignTopLeft() => AlignTop().AlignLeft();
        public Text AlignTopRight() => AlignTop().AlignRight();
        public Text AlignBottomLeft() => AlignBottom().AlignLeft();
        public Text AlignBottomRight() => AlignBottom().AlignRight();

        // Additional combined alignment methods with center
        public Text AlignCenterLeft() => CenterVertically().AlignLeft();
        public Text AlignCenterRight() => CenterVertically().AlignRight();
        public Text AlignTopCenter() => AlignTop().CenterHorizontally();
        public Text AlignBottomCenter() => AlignBottom().CenterHorizontally();
    }

    public class Animate(
        Svg svg,
        RenderItem target,
        string attributeName,
        string duration,
        IEnumerable<string> values)
        : RenderItem(svg)
    {
        public Animate(Svg svg,
            RenderItem target,
            string attributeName,
            string duration,
            IEnumerable<float> values) : this(svg, target, attributeName, duration, values.Select(Tos))
        {
        }

        public RenderItem Target { get; } = target;
        public string AttributeName { get; } = attributeName;
        public string Duration { get; } = duration;

        public string Values { get; } = string.Join(";", values);

        public override void Render(StringBuilder sb)
        {
            if (Target.Id == null)
            {
                throw new InvalidOperationException("Target must have id set!");
            }

            this.SetAttribute("dur", Duration);
            this.SetAttribute("xlink:href", "#" + Target.Id);
            this.SetAttribute("fill", "freeze");
            this.SetAttribute("attributeName", AttributeName);

            sb.AppendLine($"{Svg.Indent}<animate {Attributes}");
            sb.AppendLine($"{Svg.Indent}  values='{Values}' ");
            sb.AppendLine(" />");
        }

        public override IEnumerable<Vector2> Points => new List<Vector2>();
    }

    public void MoveTextToTop() => MoveToTop(x => x is Text);

    public void MoveToTop(Predicate<RenderItem> func)
    {
        var texts = RenderItems.Where(x => func(x)).ToList();
        RenderItems.RemoveAll(func);
        RenderItems.AddRange(texts);
    }
}

/// <summary>A minimal integer 2D point used by the integer <c>AddPath</c> overloads.</summary>
public readonly struct Vector2I(int x, int y)
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public Vector2 ToVector2() => new(X, Y);
}

public static class SvgRenderItemMethods
{
    public static T SetAttribute<T>(this T renderItem, string name, string value) where T : Svg.IAttributed
    {
        renderItem.Attributes.Set(new Svg.Attribute(name, value));
        return renderItem;
    }

    public static T SetAttribute<T>(this T renderItem, string name, float value) where T : Svg.IAttributed
    {
        renderItem.Attributes.Set(new Svg.Attribute(name, value));
        return renderItem;
    }

    public static T SetStrokeWidth<T>(this T ri, double value) where T : Svg.IAttributed => ri.SetAttribute("stroke-width", value.ToString(CultureInfo.InvariantCulture));
    public static T SetStrokeWidth<T>(this T ri, string value) where T : Svg.IAttributed => ri.SetAttribute("stroke-width", value);
    public static T SetFill<T>(this T ri, string value) where T : Svg.IAttributed => ri.SetAttribute("fill", value);
    public static T SetFill<T>(this T ri, Color value) where T : Svg.IAttributed => ri.SetAttribute("fill", Svg.ToRgbString(value));
    public static T SetFillOpacity<T>(this T ri, float value) where T : Svg.IAttributed => ri.SetAttribute("fill-opacity", value);

    public static T ClearStroke<T>(this T ri) where T : Svg.IAttributed => ri.SetStrokeWidth(0);

    public static T SetStroke<T>(this T ri, string value) where T : Svg.IAttributed => ri.SetAttribute("stroke", value);
    public static T SetStroke<T>(this T ri, string value, float strokeWidth) where T : Svg.IAttributed => ri.SetAttribute("stroke", value).SetStrokeWidth(strokeWidth);
    public static T SetStrokeDashArray<T>(this T ri, string value = "4 2") where T : Svg.IAttributed => ri.SetAttribute("stroke-dasharray", value);
    public static T SetStroke<T>(this T ri, Color value) where T : Svg.IAttributed => ri.SetAttribute("stroke", Svg.ToRgbString(value));
    public static T SetStrokeOpacity<T>(this T ri, float value) where T : Svg.IAttributed => ri.SetAttribute("stroke-opacity", value);

    public static T SetFontFamily<T>(this T ri, string value) where T : Svg.IAttributed => ri.SetAttribute("font-family", value);
    public static T SetFontSize<T>(this T ri, string value) where T : Svg.IAttributed => ri.SetAttribute("font-size", value);
    public static T SetFontSize<T>(this T ri, double value) where T : Svg.IAttributed => ri.SetAttribute("font-size", value.ToString(CultureInfo.InvariantCulture));
    public static T SetFontWeight<T>(this T ri, string value) where T : Svg.IAttributed => ri.SetAttribute("font-weight", value);

    public static T Optional<T>(this T ri, bool predicate, Action<T> action) where T : Svg.IAttributed
    {
        if (predicate) action(ri);
        return ri;
    }

    public static T SetId<T>(this T ri, string id) where T : Svg.IAttributed
    {
        ri.Id = id;
        return ri.SetAttribute("id", id);
    }


    public static T MoveFirst<T>(this T ri) where T : Svg.RenderItem
    {
        ri.Svg.RenderItems.Remove(ri);
        ri.Svg.RenderItems.Insert(0, ri);
        return ri;
    }

    public static T MoveLast<T>(this T ri) where T : Svg.RenderItem
    {
        ri.Svg.RenderItems.Remove(ri);
        ri.Svg.RenderItems.Add(ri);
        return ri;
    }


    public static T RotateAroundPoint<T>(this T ri, Vector2 pivot, float rotationDeg) where T : Svg.RenderItem
    {
        ri.SetAttribute("transform", $"rotate({Tos(rotationDeg)} {Tos(pivot.X)} {Tos(pivot.Y)})");
        return ri;
    }

    public static string Tos(float value) => value.ToString(CultureInfo.InvariantCulture);
}
