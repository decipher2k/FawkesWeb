using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
namespace FawkesWeb {
    public class HtmlEngine : IHtmlEngine
    {
        public CssEngine CssEngine { get; } = new CssEngine();
        public JsEngine JsEngine { get; } = new JsEngine();

        public HtmlDocument Parse(string url, string html)
        {
            var document = new HtmlDocument { Url = url, BaseUrl = url, Root = new HtmlNode { Tag = "html" } };

            if (string.IsNullOrWhiteSpace(html))
            {
                return document;
            }

            var stack = new Stack<HtmlNode>();
            stack.Push(document.Root);

            var tagRegex = new Regex("<(?<end>/)?(?<name>[a-zA-Z0-9#]+)(?<attrs>[^>]*)>", RegexOptions.Compiled);
            int lastIndex = 0;

            foreach (Match match in tagRegex.Matches(html))
            {
                if (match.Index > lastIndex)
                {
                    var textContent = html.Substring(lastIndex, match.Index - lastIndex);
                    AddTextNode(stack.Peek(), textContent);
                }

                var isEnd = match.Groups["end"].Success;
                var name = match.Groups["name"].Value.ToLowerInvariant();
                var attrs = match.Groups["attrs"].Value;

                if (isEnd)
                {
                    if (stack.Count > 1)
                    {
                        stack.Pop();
                    }
                }
                else
                {
                    var node = new HtmlNode { Tag = name };
                    ParseAttributes(attrs, node);
                    node.Parent = stack.Peek();
                    stack.Peek().Children.Add(node);
                    if (!IsSelfClosing(name, attrs))
                    {
                        stack.Push(node);
                    }

                    if (string.Equals(name, "base", StringComparison.OrdinalIgnoreCase) && node.Attributes.ContainsKey("href"))
                    {
                        var href = node.Attributes["href"];
                        Uri baseUri;
                        if (Uri.TryCreate(url, UriKind.Absolute, out baseUri))
                        {
                            Uri combined;
                            if (Uri.TryCreate(baseUri, href, out combined))
                            {
                                document.BaseUrl = combined.ToString();
                            }
                        }
                    }
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < html.Length)
            {
                AddTextNode(stack.Peek(), html.Substring(lastIndex));
            }

            return document;
        }

        public RenderTree Layout(HtmlDocument document, CssEngine cssEngine)
        {
            var cascade = cssEngine.Cascade(document);
            var tree = new RenderTree { Root = new RenderNode { Box = new Box { Tag = document.Root?.Tag ?? "root" } } };
            double y = 0;
            BuildRenderTree(document.Root, tree.Root, ref y, cascade, null);
            return tree;
        }

        public RenderResult Paint(RenderTree tree)
        {
            var sb = new StringBuilder();
            Describe(tree.Root, sb, 0);

            var bounds = GetBounds(tree.Root);
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height));

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 30)), null, new Rect(0, 0, pixelWidth, pixelHeight));
                PaintNode(tree.Root, dc);
            }

            var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            return new RenderResult { Description = sb.ToString(), Image = rtb };
        }

        private Rect GetBounds(RenderNode node)
        {
            if (node?.Box == null)
            {
                return Rect.Empty;
            }

            var rect = node.Box.Layout;
            foreach (var child in node.Children)
            {
                var c = GetBounds(child);
                if (!c.IsEmpty)
                {
                    rect = Rect.Union(rect, c);
                }
            }
            return rect;
        }

        private Brush ParseBrush(string color)
        {
            if (string.IsNullOrWhiteSpace(color) || string.Equals(color, "transparent", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string normalized;
            if (!CssEngine.TryParseColor(color, out normalized))
            {
                return null;
            }

            double r, g, b, a;
            CssEngine.ParseRgbaValues(normalized, out r, out g, out b, out a);
            return new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * 255), (byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b)));
        }

        private double ParseDouble(string raw, double fallback = 0)
        {
            double d;
            return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : fallback;
        }

        private double ParseThickness(Dictionary<string, string> styles, string key)
        {
            if (styles != null && styles.TryGetValue(key, out var val))
            {
                return ParseCssLengthValue(val, 0, styles);
            }
            return 0;
        }

        private void PaintNode(RenderNode node, DrawingContext dc)
        {
            if (node?.Box?.ComputedStyle == null)
            {
                return;
            }

            var styles = node.Box.ComputedStyle;
            var rect = node.Box.Layout;

            double opacity = 1.0;
            if (styles.TryGetValue("opacity", out var opStr))
            {
                double.TryParse(opStr, NumberStyles.Any, CultureInfo.InvariantCulture, out opacity);
            }

            dc.PushOpacity(Math.Max(0, Math.Min(1, opacity)));

            // transforms: only translate(x,y)
            if (styles.TryGetValue("transform", out var transform) && transform.IndexOf("translate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var m = Regex.Match(transform, @"translate\s*\(\s*(?<x>[-0-9\.]+)(px)?\s*(,\s*(?<y>[-0-9\.]+)(px)?)?\s*\)");
                if (m.Success)
                {
                    double tx = ParseDouble(m.Groups["x"].Value);
                    double ty = ParseDouble(m.Groups["y"].Success ? m.Groups["y"].Value : "0");
                    dc.PushTransform(new TranslateTransform(tx, ty));
                }
            }

            bool clip = styles.TryGetValue("_clip", out var clipFlag) && string.Equals(clipFlag, "true", StringComparison.OrdinalIgnoreCase);
            if (clip)
            {
                dc.PushClip(new RectangleGeometry(rect));
            }

            // background
            if (styles.TryGetValue("background-color", out var bgStr))
            {
                var bg = ParseBrush(bgStr);
                if (bg != null)
                {
                    double radiusX = 0, radiusY = 0;
                    styles.TryGetValue("border-top-left-radius", out var rStr);
                    if (!string.IsNullOrEmpty(rStr))
                    {
                        radiusX = radiusY = ParseCssLengthValue(rStr, 0, styles);
                    }
                    dc.DrawRoundedRectangle(bg, null, rect, radiusX, radiusY);
                }
            }

            // border
            var borderThickness = new Thickness(
                ParseThickness(styles, "border-left-width"),
                ParseThickness(styles, "border-top-width"),
                ParseThickness(styles, "border-right-width"),
                ParseThickness(styles, "border-bottom-width"));

            if (borderThickness.Left > 0 || borderThickness.Top > 0 || borderThickness.Right > 0 || borderThickness.Bottom > 0)
            {
                styles.TryGetValue("border-top-color", out var borderColor);
                var penBrush = ParseBrush(borderColor ?? "black");
                if (penBrush != null)
                {
                    var pen = new Pen(penBrush, borderThickness.Left);
                    dc.DrawRectangle(null, pen, rect);
                }
            }

            // box-shadow (first shadow only, no blur)
            if (styles.TryGetValue("box-shadow", out var shadowStr) && !string.Equals(shadowStr, "none", StringComparison.OrdinalIgnoreCase))
            {
                var parts = shadowStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    double sx = ParseDouble(parts[0]);
                    double sy = ParseDouble(parts[1]);
                    double blur = parts.Length >= 3 ? ParseDouble(parts[2]) : 0;
                    var colorToken = parts.FirstOrDefault(p => CssEngine.IsColorValue(p));
                    var shadowBrush = ParseBrush(colorToken ?? "rgba(0,0,0,0.3)");
                    if (shadowBrush != null)
                    {
                        var shadowRect = new Rect(rect.X + sx, rect.Y + sy, rect.Width, rect.Height);
                        dc.DrawRectangle(shadowBrush, null, shadowRect);
                    }
                }
            }

            // text content
            string text = null;
            if (node.Box.Tag == "#text")
            {
                text = node.Box.ComputedStyle.ContainsKey("content") ? node.Box.ComputedStyle["content"] : node.Box.Tag;
                text = node.Box.Tag == "#text" ? node.Box.ComputedStyle.ContainsKey("content") ? node.Box.ComputedStyle["content"] : node.Box.Tag : text;
            }
            if (string.IsNullOrEmpty(text) && styles.TryGetValue("content", out var contentVal))
            {
                text = contentVal;
            }
            if (string.IsNullOrEmpty(text) && node.Box.Tag == "::marker" && styles.TryGetValue("list-marker", out var markerVal))
            {
                text = markerVal;
            }
            if (!string.IsNullOrEmpty(text))
            {
                var color = styles.TryGetValue("color", out var cStr) ? ParseBrush(cStr) : Brushes.White;
                double fontSize = 14;
                if (styles.TryGetValue("font-size", out var fs))
                {
                    fontSize = ParseCssLengthValue(fs, fontSize, styles);
                }

                var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), fontSize, color ?? Brushes.White, 1.25);
                dc.DrawText(formatted, new Point(rect.X, rect.Y));
            }

            foreach (var child in node.Children)
            {
                PaintNode(child, dc);
            }

            if (clip)
            {
                dc.Pop();
            }

            if (styles.TryGetValue("transform", out transform) && transform.IndexOf("translate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                dc.Pop();
            }

            dc.Pop();
        }

        private void AddTextNode(HtmlNode parent, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            parent.Children.Add(new HtmlNode { Tag = "#text", Text = text.Trim() });
        }

        private void ParseAttributes(string raw, HtmlNode node)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var attrRegex = new Regex(@"(?<name>[a-zA-Z_:][a-zA-Z0-9_:-]*)\s*=\s*""?(?<value>[^""\s>]*)""?", RegexOptions.Compiled);
            foreach (Match m in attrRegex.Matches(raw))
            {
                var name = m.Groups["name"].Value;
                var value = m.Groups["value"].Value;
                if (!node.Attributes.ContainsKey(name))
                {
                    node.Attributes[name] = value;
                }
            }
        }

        private bool IsSelfClosing(string tag, string attrs)
        {
            return attrs.Contains("/") || string.Equals(tag, "br", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "img", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "meta", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "link", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "input", StringComparison.OrdinalIgnoreCase);
        }

        private double ParseCssLength(Dictionary<string, string> styles, string key, double fallback)
        {
            if (styles == null || !styles.ContainsKey(key))
            {
                return fallback;
        }

            return ParseCssLengthValue(styles[key], fallback);
        }

        private double ParseCssLengthValue(string raw, double fallback)
        {
            return ParseCssLengthValue(raw, fallback, null);
        }

        private struct IntrinsicSizes
        {
            public double Min;
            public double Max;
        }

        private double MeasureTextWidth(string text, Dictionary<string, string> styles)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            double fontSize = 16;
            if (styles != null && styles.TryGetValue("font-size", out var fs))
            {
                var parsed = ParseCssLengthValue(fs, 16, styles);
                if (parsed > 0)
                {
                    fontSize = parsed;
                }
            }

            double baseCharWidth = ComputeAverageCharWidth(styles, fontSize);

            double letterSpacing = 0;
            if (styles != null && styles.TryGetValue("letter-spacing", out var ls))
            {
                letterSpacing = ParseCssLengthValue(ls, 0, styles);
            }

            // crude shaping: account for small-caps/font-variant/features by scaling width slightly
            double shapingFactor = 1.0;
            if (styles != null)
            {
                string variant;
                if (styles.TryGetValue("font-variant", out variant) && variant.IndexOf("small-caps", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    shapingFactor *= 0.92;
                }

                string features;
                if (styles.TryGetValue("font-feature-settings", out features) && features.IndexOf("liga", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    shapingFactor *= 0.97;
                }
            }

            // handle multi-line text by taking widest line
            var lines = text.Split(new[] { '\n' });
            double max = 0;
            foreach (var line in lines)
            {
                int len = Math.Max(1, line.Length);
                double width = len * baseCharWidth * shapingFactor;
                if (len > 1)
                {
                    width += (len - 1) * letterSpacing;
                }
                max = Math.Max(max, width);
            }

            return Math.Max(1, max);
        }

        private Dictionary<string, string> ExtractPseudoStyles(Dictionary<string, string> styles, string pseudo)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (styles == null || string.IsNullOrWhiteSpace(pseudo))
            {
                return result;
            }

            var prefix = pseudo.Trim().ToLowerInvariant() + "::";
            foreach (var kv in styles)
            {
                if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var key = kv.Key.Substring(prefix.Length);
                    result[key] = kv.Value;
                }
            }

            return result;
        }

        private Dictionary<string, string> MergeStyles(Dictionary<string, string> baseStyles, Dictionary<string, string> overrides)
        {
            var merged = baseStyles != null
                ? new Dictionary<string, string>(baseStyles, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (overrides != null)
            {
                foreach (var kv in overrides)
                {
                    merged[kv.Key] = kv.Value;
                }
            }

            return merged;
        }

        private double ComputeAverageCharWidth(Dictionary<string, string> styles, double fontSize)
        {
            double factor = 0.52; // baseline sans-serif

            if (styles != null)
            {
                string family;
                if (styles.TryGetValue("font-family", out family) && !string.IsNullOrWhiteSpace(family))
                {
                    var primary = family.Split(',').Select(f => f.Trim().Trim('"', '\'')).FirstOrDefault();
                    if (!string.IsNullOrEmpty(primary))
                    {
                        var lower = primary.ToLowerInvariant();
                        if (lower.Contains("mono"))
                        {
                            factor = 0.60;
                        }
                        else if (lower.Contains("serif"))
                        {
                            factor = 0.55;
                        }
                        else if (lower.Contains("condensed"))
                        {
                            factor = 0.48;
                        }

                        string weightVal;
                        styles.TryGetValue("font-weight", out weightVal);
                        string styleVal;
                        styles.TryGetValue("font-style", out styleVal);
                        double faceFactor;
                        if (CssEngine != null && CssEngine.TryGetFontMetrics(primary, weightVal, styleVal, out faceFactor))
                        {
                            factor = faceFactor;
                        }
                    }
                }

                string weight;
                if (styles.TryGetValue("font-weight", out weight))
                {
                    if (string.Equals(weight, "bold", StringComparison.OrdinalIgnoreCase) || weight == "700")
                    {
                        factor *= 1.05;
                    }
                    else if (weight == "300" || string.Equals(weight, "light", StringComparison.OrdinalIgnoreCase))
                    {
                        factor *= 0.96;
                    }
                }

                string variant;
                if (styles.TryGetValue("font-variant", out variant) && variant.IndexOf("small-caps", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    factor *= 0.92;
                }
            }

            return Math.Max(4, fontSize * factor);
        }

        private void ResolveCurrentColor(Dictionary<string, string> styles, Dictionary<string, string> parent)
        {
            if (styles == null)
            {
                return;
            }

            string parentColor = "black";
            if (parent != null && parent.TryGetValue("color", out var pc) && !string.IsNullOrWhiteSpace(pc))
            {
                parentColor = pc;
            }

            string resolvedColor;
            if (!styles.TryGetValue("color", out resolvedColor) || string.IsNullOrWhiteSpace(resolvedColor) || string.Equals(resolvedColor, "currentcolor", StringComparison.OrdinalIgnoreCase))
            {
                resolvedColor = parentColor;
            }
            else
            {
                string normalized;
                if (!CssEngine.TryParseColor(resolvedColor, out normalized))
                {
                    resolvedColor = parentColor;
                }
                else
                {
                    resolvedColor = normalized;
                }
            }

            styles["color"] = resolvedColor;

            var keys = styles.Keys.ToList();
            foreach (var key in keys)
            {
                if (styles.TryGetValue(key, out var val) && string.Equals(val, "currentcolor", StringComparison.OrdinalIgnoreCase))
                {
                    styles[key] = resolvedColor;
                }
            }
        }

        private Dictionary<string, int> ExtractCountersFromComputed(Dictionary<string, string> computed)
        {
            var counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (computed == null)
            {
                return counters;
            }

            foreach (var kv in computed)
            {
                if (kv.Key.StartsWith("counter:", StringComparison.OrdinalIgnoreCase))
                {
                    var name = kv.Key.Substring("counter:".Length);
                    int val;
                    if (int.TryParse(kv.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                    {
                        counters[name] = val;
                    }
                }
            }

            return counters;
        }

        private string ResolveContentValue(HtmlNode node, string content, Dictionary<string, string> computed = null)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            content = content.Trim();

            if (string.Equals(content, "none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var attrMatch = Regex.Match(content, @"attr\s*\([^)]+\)", RegexOptions.IgnoreCase);
            if (attrMatch.Success)
            {
                var evaluated = CssEngine.EvaluateAttr(attrMatch.Value, node);
                return evaluated;
            }

            var countersMatch = Regex.Match(content, @"counters\s*\([^\)]*\)", RegexOptions.IgnoreCase);
            if (countersMatch.Success)
            {
                var countersExpr = countersMatch.Value;
                var counterDict = ExtractCountersFromComputed(computed);
                var cssEngine = new CssEngine();
                var evaluated = cssEngine.EvaluateCounters(countersExpr, null);
                if (counterDict.Count > 0)
                {
                    var nested = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in counterDict)
                    {
                        nested[kv.Key] = new List<int> { kv.Value };
                    }
                    evaluated = cssEngine.EvaluateCounters(countersExpr, nested);
                }
                return evaluated;
            }

            var counterMatch = Regex.Match(content, @"counter\s*\(\s*(?<name>[a-zA-Z_-][a-zA-Z0-9_-]*)\s*(,\s*(?<style>[^\)]+))?\)", RegexOptions.IgnoreCase);
            if (counterMatch.Success)
            {
                var name = counterMatch.Groups["name"].Value;
                var style = counterMatch.Groups["style"].Success ? counterMatch.Groups["style"].Value : null;
                if (string.Equals(name, "list-item", StringComparison.OrdinalIgnoreCase) && node != null && node.Attributes.TryGetValue("__list_index", out var idxVal))
                {
                    return idxVal;
                }

                var counterDict = ExtractCountersFromComputed(computed);
                var expr = style != null ? $"counter({name}, {style})" : $"counter({name})";
                var cssEngine = new CssEngine();
                var evaluated = cssEngine.EvaluateCounter(expr, counterDict);
                return evaluated;
            }

            if ((content.StartsWith("\"", StringComparison.Ordinal) && content.EndsWith("\"", StringComparison.Ordinal)) ||
                (content.StartsWith("'", StringComparison.Ordinal) && content.EndsWith("'", StringComparison.Ordinal)))
            {
                content = content.Substring(1, content.Length - 2);
            }

            return content;
        }

        private IntrinsicSizes ComputeIntrinsicSizes(HtmlNode node, CssCascadeResult cascade)
        {
            if (node == null)
            {
                return new IntrinsicSizes { Min = 0, Max = 0 };
            }

            var styles = cascade != null && cascade.Styles.ContainsKey(node) ? cascade.Styles[node] : null;

            // Replaced elements: respect explicit dimensions when present
            if (string.Equals(node.Tag, "img", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Tag, "video", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Tag, "canvas", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Tag, "iframe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Tag, "input", StringComparison.OrdinalIgnoreCase))
            {
                double w = 0;
                if (styles != null && styles.TryGetValue("width", out var sw))
                {
                    w = ParseCssLengthValue(sw, 0, styles);
                }
                else if (node.Attributes.ContainsKey("width"))
                {
                    w = ParseCssLengthValue(node.Attributes["width"], 0, styles);
                }

                if (w <= 0)
                {
                    w = 150;
                }

                return new IntrinsicSizes { Min = w, Max = w };
            }

            if (string.Equals(node.Tag, "#text", StringComparison.OrdinalIgnoreCase))
            {
                var raw = node.Text ?? string.Empty;
                var words = Regex.Split(raw, "\\s+").Where(x => !string.IsNullOrEmpty(x)).ToList();
                double maxWord = words.Count > 0 ? words.Max(w => MeasureTextWidth(w, styles)) : 0;
                double fullLine = MeasureTextWidth(raw.Replace("\n", " "), styles);
                return new IntrinsicSizes { Min = maxWord, Max = fullLine };
            }

            double min = 0;
            double max = 0;

            foreach (var child in node.Children)
            {
                var c = ComputeIntrinsicSizes(child, cascade);
                min = Math.Max(min, c.Min);
                max += c.Max;
            }

            return new IntrinsicSizes { Min = min, Max = Math.Max(min, max) };
        }

        private double ParseCssLengthValue(string raw, double fallback, Dictionary<string, string> customProperties)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            raw = raw.Trim();

            // Handle CSS variables: var(--name) or var(--name, fallback)
            if (raw.StartsWith("var(", StringComparison.OrdinalIgnoreCase))
            {
                raw = ResolveVar(raw, customProperties);
            }

            raw = raw.ToLowerInvariant();

            if (raw == "auto" || raw == "none")
            {
                return fallback;
            }

            // Handle calc() function
            if (raw.StartsWith("calc(", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateCalc(raw, fallback, customProperties);
            }

            // Handle min() function
            if (raw.StartsWith("min(", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateMinMaxClamp(raw, "min", fallback, customProperties);
            }

            // Handle max() function
            if (raw.StartsWith("max(", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateMinMaxClamp(raw, "max", fallback, customProperties);
            }

            // Handle clamp() function
            if (raw.StartsWith("clamp(", StringComparison.OrdinalIgnoreCase))
            {
                return EvaluateMinMaxClamp(raw, "clamp", fallback, customProperties);
            }

            // Handle abs() function
            if (raw.StartsWith("abs(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "abs");
                return Math.Abs(ParseCssLengthValue(inner, fallback, customProperties));
            }

            // Handle sign() function
            if (raw.StartsWith("sign(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "sign");
                return Math.Sign(ParseCssLengthValue(inner, fallback, customProperties));
            }

            // Handle round() function: round(strategy?, value, interval?)
            if (raw.StartsWith("round(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "round");
                var args = SplitFunctionArgs(inner);
                if (args.Count == 1)
                {
                    return Math.Round(ParseCssLengthValue(args[0].Trim(), fallback, customProperties));
                }
                else if (args.Count >= 2)
                {
                    var strategy = args[0].Trim().ToLowerInvariant();
                    double value, interval = 1;
                    if (strategy == "nearest" || strategy == "up" || strategy == "down" || strategy == "to-zero")
                    {
                        value = ParseCssLengthValue(args[1].Trim(), fallback, customProperties);
                        if (args.Count >= 3)
                            interval = ParseCssLengthValue(args[2].Trim(), 1, customProperties);
                    }
                    else
                    {
                        value = ParseCssLengthValue(args[0].Trim(), fallback, customProperties);
                        interval = ParseCssLengthValue(args[1].Trim(), 1, customProperties);
                        strategy = "nearest";
                    }
                    if (interval == 0) return value;
                    switch (strategy)
                    {
                        case "up": return Math.Ceiling(value / interval) * interval;
                        case "down": return Math.Floor(value / interval) * interval;
                        case "to-zero": return Math.Truncate(value / interval) * interval;
                        default: return Math.Round(value / interval) * interval;
                    }
                }
            }

            // Handle mod() function
            if (raw.StartsWith("mod(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "mod");
                var args = SplitFunctionArgs(inner);
                if (args.Count >= 2)
                {
                    var a = ParseCssLengthValue(args[0].Trim(), fallback, customProperties);
                    var b = ParseCssLengthValue(args[1].Trim(), 1, customProperties);
                    if (b != 0)
                    {
                        var r = a % b;
                        return (r + b) % b; // Euclidean remainder
                    }
                }
                return fallback;
            }

            // Handle rem() function (CSS remainder, different from mod)
            if (raw.StartsWith("rem(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "rem");
                var args = SplitFunctionArgs(inner);
                if (args.Count >= 2)
                {
                    var a = ParseCssLengthValue(args[0].Trim(), fallback, customProperties);
                    var b = ParseCssLengthValue(args[1].Trim(), 1, customProperties);
                    if (b != 0) return a - b * Math.Truncate(a / b);
                }
                return fallback;
            }

            // Handle sin() function
            if (raw.StartsWith("sin(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "sin");
                var angle = ParseAngleValue(inner, customProperties);
                return Math.Sin(angle);
            }

            // Handle cos() function
            if (raw.StartsWith("cos(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "cos");
                var angle = ParseAngleValue(inner, customProperties);
                return Math.Cos(angle);
            }

            // Handle tan() function
            if (raw.StartsWith("tan(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "tan");
                var angle = ParseAngleValue(inner, customProperties);
                return Math.Tan(angle);
            }

            // Handle asin() function
            if (raw.StartsWith("asin(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "asin");
                var value = ParseCssLengthValue(inner, 0, customProperties);
                return Math.Asin(Math.Max(-1, Math.Min(1, value)));
            }

            // Handle acos() function
            if (raw.StartsWith("acos(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "acos");
                var value = ParseCssLengthValue(inner, 0, customProperties);
                return Math.Acos(Math.Max(-1, Math.Min(1, value)));
            }

            // Handle atan() function
            if (raw.StartsWith("atan(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "atan");
                var value = ParseCssLengthValue(inner, 0, customProperties);
                return Math.Atan(value);
            }

            // Handle atan2() function
            if (raw.StartsWith("atan2(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "atan2");
                var args = SplitFunctionArgs(inner);
                if (args.Count >= 2)
                {
                    var y = ParseCssLengthValue(args[0].Trim(), 0, customProperties);
                    var x = ParseCssLengthValue(args[1].Trim(), 1, customProperties);
                    return Math.Atan2(y, x);
                }
                return 0;
            }

            // Handle pow() function
            if (raw.StartsWith("pow(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "pow");
                var args = SplitFunctionArgs(inner);
                if (args.Count >= 2)
                {
                    var baseVal = ParseCssLengthValue(args[0].Trim(), fallback, customProperties);
                    var exp = ParseCssLengthValue(args[1].Trim(), 1, customProperties);
                    return Math.Pow(baseVal, exp);
                }
                return fallback;
            }

            // Handle sqrt() function
            if (raw.StartsWith("sqrt(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "sqrt");
                var value = ParseCssLengthValue(inner, fallback, customProperties);
                return Math.Sqrt(Math.Abs(value));
            }

            // Handle hypot() function
            if (raw.StartsWith("hypot(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "hypot");
                var args = SplitFunctionArgs(inner);
                var sum = 0.0;
                foreach (var arg in args)
                {
                    var val = ParseCssLengthValue(arg.Trim(), 0, customProperties);
                    sum += val * val;
                }
                return Math.Sqrt(sum);
            }

            // Handle log() function
            if (raw.StartsWith("log(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "log");
                var args = SplitFunctionArgs(inner);
                var value = ParseCssLengthValue(args[0].Trim(), 1, customProperties);
                if (value <= 0) return fallback;
                if (args.Count >= 2)
                {
                    var baseVal = ParseCssLengthValue(args[1].Trim(), Math.E, customProperties);
                    if (baseVal <= 0 || Math.Abs(baseVal - 1) < 1e-9) return fallback;
                    return Math.Log(value) / Math.Log(baseVal);
                }
                return Math.Log(value);
            }

            // Handle exp() function
            if (raw.StartsWith("exp(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ExtractFunctionContent(raw, "exp");
                var value = ParseCssLengthValue(inner, 0, customProperties);
                return Math.Exp(value);
            }

            double numeric;
            if (raw.EndsWith("px"))
            {
                if (double.TryParse(raw.Replace("px", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return numeric;
                }
            }
            else if (raw.EndsWith("%"))
            {
                if (double.TryParse(raw.Replace("%", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return fallback * (numeric / 100.0);
                }
            }
            else if (raw.EndsWith("rem"))
            {
                if (double.TryParse(raw.Replace("rem", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return numeric * 16.0; // Root em = 16px
                }
            }
            else if (raw.EndsWith("em"))
            {
                if (double.TryParse(raw.Replace("em", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return numeric * 16.0;
                }
            }
            else if (raw.EndsWith("vw") || raw.EndsWith("vh") || raw.EndsWith("vmin") || raw.EndsWith("vmax") || raw.EndsWith("lvh") || raw.EndsWith("svh") || raw.EndsWith("dvh"))
            {
                var unit = Regex.Match(raw, "(vw|vh|vmin|vmax|lvh|svh|dvh)$", RegexOptions.IgnoreCase).Value;
                if (double.TryParse(raw.Replace(unit, string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return ConvertToPixels(numeric, unit, fallback);
                }
            }
            else if (raw.EndsWith("cap") || raw.EndsWith("ic") || raw.EndsWith("ch") || raw.EndsWith("ex") || raw.EndsWith("lh") || raw.EndsWith("cm") || raw.EndsWith("mm") || raw.EndsWith("q") || raw.EndsWith("in") || raw.EndsWith("pt") || raw.EndsWith("pc"))
            {
                var unitMatch = Regex.Match(raw, "(cap|ic|ch|ex|lh|cm|mm|q|in|pt|pc)$", RegexOptions.IgnoreCase);
                var unit = unitMatch.Success ? unitMatch.Value : string.Empty;
                if (double.TryParse(raw.Replace(unit, string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return ConvertToPixels(numeric, unit, fallback);
                }
            }
            else if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
            {
                return numeric;
            }

            return fallback;
        }

        private string ExtractFunctionContent(string raw, string funcName)
        {
            var start = raw.IndexOf('(');
            if (start < 0) return raw;
            var end = FindMatchingParenthesis(raw, start);
            if (end < 0) return raw;
            return raw.Substring(start + 1, end - start - 1).Trim();
        }

        private double ParseAngleValue(string value, Dictionary<string, string> customProperties)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Trim().ToLowerInvariant();

            // Handle nested functions
            if (value.Contains("("))
            {
                return ParseCssLengthValue(value, 0, customProperties);
            }

            double num;
            if (value.EndsWith("deg"))
            {
                if (double.TryParse(value.Replace("deg", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                    return num * Math.PI / 180.0;
            }
            else if (value.EndsWith("rad"))
            {
                if (double.TryParse(value.Replace("rad", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                    return num;
            }
            else if (value.EndsWith("grad"))
            {
                if (double.TryParse(value.Replace("grad", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                    return num * Math.PI / 200.0;
            }
            else if (value.EndsWith("turn"))
            {
                if (double.TryParse(value.Replace("turn", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                    return num * 2 * Math.PI;
            }
            else if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
            {
                // Assume radians if no unit
                return num;
            }

            return 0;
        }

        private string ResolveVar(string input, Dictionary<string, string> customProperties)
        {
            // var(--name) or var(--name, fallback)
            var match = Regex.Match(input, @"var\(\s*(?<name>--[a-zA-Z0-9_-]+)\s*(?:,\s*(?<fallback>[^)]+))?\)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return input;
            }

            var varName = match.Groups["name"].Value;
            var fallback = match.Groups["fallback"].Success ? match.Groups["fallback"].Value.Trim() : null;

            string resolved = null;
            if (customProperties != null && customProperties.TryGetValue(varName, out resolved))
            {
                return resolved;
            }

            return fallback ?? "0";
        }

        private double EvaluateCalc(string expression, double fallback, Dictionary<string, string> customProperties)
        {
            try
            {
                // Extract content inside calc(...)
                var start = expression.IndexOf('(');
                var end = FindMatchingParenthesis(expression, start);
                if (start < 0 || end < 0)
                {
                    return fallback;
                }

                var inner = expression.Substring(start + 1, end - start - 1).Trim();
                return EvaluateCalcExpression(inner, fallback, customProperties);
            }
            catch
            {
                return fallback;
            }
        }

        private double EvaluateCalcExpression(string expr, double fallback, Dictionary<string, string> customProperties)
        {
            expr = expr.Trim();

            // Replace var() references
            expr = Regex.Replace(expr, @"var\([^)]+\)", m => ResolveVar(m.Value, customProperties));

            // Tokenize: numbers with units, operators
            var tokens = new List<object>();
            var tokenRegex = new Regex(@"(?<num>-?\d+\.?\d*)\s*(?<unit>[a-z%]+)?|(?<op>[\+\-\*\/])|(?<paren>[\(\)])", RegexOptions.IgnoreCase);

            foreach (Match m in tokenRegex.Matches(expr))
            {
                if (m.Groups["num"].Success)
                {
                    var numStr = m.Groups["num"].Value;
                    var unit = m.Groups["unit"].Success ? m.Groups["unit"].Value : "";
                    double val;
                    if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                    {
                        // Convert to px
                        val = ConvertToPixels(val, unit, fallback);
                        tokens.Add(val);
                    }
                }
                else if (m.Groups["op"].Success)
                {
                    tokens.Add(m.Groups["op"].Value[0]);
                }
                else if (m.Groups["paren"].Success)
                {
                    tokens.Add(m.Groups["paren"].Value[0]);
                }
            }

            // Simple evaluation (left to right with operator precedence)
            return EvaluateTokens(tokens, fallback);
        }

        private double ConvertToPixels(double value, string unit, double percentBase)
        {
            unit = (unit ?? "").ToLowerInvariant();
            // Use actual screen size when available
            double screenW = SystemParameters.PrimaryScreenWidth > 0 ? SystemParameters.PrimaryScreenWidth : 1024.0;
            double screenH = SystemParameters.PrimaryScreenHeight > 0 ? SystemParameters.PrimaryScreenHeight : 768.0;
            switch (unit)
            {
                case "px": case "": return value;
                case "%": return percentBase * (value / 100.0);
                case "em": case "rem": return value * 16.0;
                case "cap": return value * 16.0 * 0.7; // approx cap-height
                case "ic": return value * 8.0; // approx ideographic character width
                case "vw": return screenW * (value / 100.0);
                case "vh": case "lvh": case "svh": case "dvh": return screenH * (value / 100.0);
                case "vmin": return Math.Min(screenW, screenH) * (value / 100.0);
                case "vmax": return Math.Max(screenW, screenH) * (value / 100.0);
                case "ch": return value * 8.0;
                case "ex": return value * 8.0;
                case "lh": return value * 19.2;
                case "cm": return value * (96.0 / 2.54);
                case "mm": return value * (96.0 / 25.4);
                case "q": return value * (96.0 / 101.6);
                case "in": return value * 96.0;
                case "pt": return value * (96.0 / 72.0);
                case "pc": return value * 16.0;
                default: return value;
            }
        }

        private double EvaluateTokens(List<object> tokens, double fallback)
        {
            if (tokens.Count == 0) return fallback;

            // Handle parentheses recursively
            while (tokens.Contains('('))
            {
                int openIdx = tokens.LastIndexOf('(');
                int closeIdx = tokens.IndexOf(')', openIdx);
                if (closeIdx < 0) break;

                var subTokens = tokens.Skip(openIdx + 1).Take(closeIdx - openIdx - 1).ToList();
                var subResult = EvaluateTokens(subTokens, fallback);
                tokens.RemoveRange(openIdx, closeIdx - openIdx + 1);
                tokens.Insert(openIdx, subResult);
            }

            // First pass: * and /
            for (int i = 1; i < tokens.Count - 1; i++)
            {
                if (tokens[i] is char op && (op == '*' || op == '/'))
                {
                    double left = Convert.ToDouble(tokens[i - 1]);
                    double right = Convert.ToDouble(tokens[i + 1]);
                    double result = op == '*' ? left * right : (right != 0 ? left / right : 0);
                    tokens.RemoveRange(i - 1, 3);
                    tokens.Insert(i - 1, result);
                    i--;
                }
            }

            // Second pass: + and -
            for (int i = 1; i < tokens.Count - 1; i++)
            {
                if (tokens[i] is char op && (op == '+' || op == '-'))
                {
                    double left = Convert.ToDouble(tokens[i - 1]);
                    double right = Convert.ToDouble(tokens[i + 1]);
                    double result = op == '+' ? left + right : left - right;
                    tokens.RemoveRange(i - 1, 3);
                    tokens.Insert(i - 1, result);
                    i--;
                }
            }

            return tokens.Count > 0 && tokens[0] is double d ? d : fallback;
        }

        private double EvaluateMinMaxClamp(string expression, string funcName, double fallback, Dictionary<string, string> customProperties)
        {
            try
            {
                var start = expression.IndexOf('(');
                var end = FindMatchingParenthesis(expression, start);
                if (start < 0 || end < 0) return fallback;

                var inner = expression.Substring(start + 1, end - start - 1);
                var args = SplitFunctionArgs(inner);

                var values = args.Select(a => ParseCssLengthValue(a.Trim(), fallback, customProperties)).ToList();

                switch (funcName.ToLowerInvariant())
                {
                    case "min":
                        return values.Count > 0 ? values.Min() : fallback;
                    case "max":
                        return values.Count > 0 ? values.Max() : fallback;
                    case "clamp":
                        if (values.Count >= 3)
                        {
                            return Math.Max(values[0], Math.Min(values[1], values[2]));
                        }
                        break;
                }
            }
            catch { }
            return fallback;
        }

        private double ParseAspectRatio(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Trim().ToLowerInvariant();
            if (value == "auto") return 0;

            // Handle "width / height" format
            if (value.Contains("/"))
            {
                var parts = value.Split('/');
                if (parts.Length == 2)
                {
                    double num, denom;
                    if (double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out num) &&
                        double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out denom) &&
                        denom > 0)
                    {
                        return num / denom;
                    }
                }
            }

            // Handle single number format (width / 1)
            double single;
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out single))
            {
                return single;
            }

            return 0;
        }

        private List<string> SplitFunctionArgs(string argsString)
        {
            var args = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < argsString.Length; i++)
            {
                char c = argsString[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    args.Add(argsString.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start < argsString.Length)
            {
                args.Add(argsString.Substring(start));
            }
            return args;
        }

        private int FindMatchingParenthesis(string text, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private void ApplyAttributeSizing(HtmlNode node, Dictionary<string, string> styles)
        {
            if (node == null || styles == null)
            {
                return;
            }

            if (!styles.ContainsKey("width") && node.Attributes.ContainsKey("width"))
            {
                styles["width"] = NormalizeCssLength(node.Attributes["width"]);
            }

            if (!styles.ContainsKey("height") && node.Attributes.ContainsKey("height"))
            {
                styles["height"] = NormalizeCssLength(node.Attributes["height"]);
            }
        }

        private string NormalizeCssLength(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }

            raw = raw.Trim();
            double numeric;
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
            {
                return numeric.ToString(CultureInfo.InvariantCulture) + "px";
            }

            return raw;
        }

        private void BuildRenderTree(HtmlNode node, RenderNode renderNode, ref double y, CssCascadeResult cascade, Dictionary<string, string> parentComputed)
        {
            if (node == null)
            {
                return;
            }

            var styles = cascade.Styles.ContainsKey(node) ? new Dictionary<string, string>(cascade.Styles[node]) : new Dictionary<string, string>();
            ApplyAttributeSizing(node, styles);
            MergeDefaults(node.Tag, styles);
            InheritStyles(styles, parentComputed);
            ResolveCurrentColor(styles, parentComputed);

            // determine available width from parent content box when present
            double availableFromParent = 800;
            if (parentComputed != null && parentComputed.TryGetValue("_content-width", out var parentContentWidth))
            {
                double parsed;
                if (double.TryParse(parentContentWidth, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                {
                    availableFromParent = parsed;
                }
            }

            var intrinsic = ComputeIntrinsicSizes(node, cascade);

            string displayOverride;
            DisplayType display;
            if (styles.TryGetValue("display", out displayOverride))
            {
                var d = displayOverride.ToLowerInvariant();
                if (d == "none")
                {
                    return;
                }

                switch (d)
                {
                    case "inline":
                        display = DisplayType.Inline;
                        break;
                    case "inline-block":
                        display = DisplayType.InlineBlock;
                        break;
                    case "inline-table":
                        display = DisplayType.InlineTable;
                        break;
                    default:
                        display = DisplayType.Block;
                        break;
                }
            }
            else
            {
                display = GetDisplayType(node);
            }

            if (IsFlexContainer(styles))
            {
                BuildFlexLayout(node, renderNode, ref y, styles, cascade, parentComputed);
                return;
            }

            if (IsGridContainer(styles))
            {
                BuildGridLayout(node, renderNode, ref y, styles, cascade, parentComputed);
                return;
            }

            if (IsTableContainer(styles))
            {
                BuildTableLayout(node, renderNode, ref y, styles, cascade, parentComputed);
                return;
            }

            if (IsHidden(styles))
            {
                return;
            }

            bool widthAuto = styles.TryGetValue("width", out var widthRaw) && string.Equals(widthRaw, "auto", StringComparison.OrdinalIgnoreCase);
            double width = ParseCssLength(styles, "width", double.IsNaN(availableFromParent) ? 800 : availableFromParent);
            double height = ParseCssLength(styles, "height", 20);
            double minWidth = ParseCssLength(styles, "min-width", 0);
            double minHeight = ParseCssLength(styles, "min-height", 0);
            double maxWidth = styles.ContainsKey("max-width") ? ParseCssLength(styles, "max-width", double.PositiveInfinity) : double.PositiveInfinity;
            double maxHeight = styles.ContainsKey("max-height") ? ParseCssLength(styles, "max-height", double.PositiveInfinity) : double.PositiveInfinity;
            double marginTop = ParseCssLength(styles, "margin-top", 0);
            double marginBottom = ParseCssLength(styles, "margin-bottom", 0);
            double marginLeft = ParseCssLength(styles, "margin-left", 0);
            double marginRight = ParseCssLength(styles, "margin-right", 0);
            double paddingTop = ParseCssLength(styles, "padding-top", 0);
            double paddingBottom = ParseCssLength(styles, "padding-bottom", 0);
            double paddingLeft = ParseCssLength(styles, "padding-left", 0);
            double paddingRight = ParseCssLength(styles, "padding-right", 0);
            double borderTop = ParseCssLength(styles, "border-top-width", 0);
            double borderBottom = ParseCssLength(styles, "border-bottom-width", 0);
            double borderLeft = ParseCssLength(styles, "border-left-width", 0);
            double borderRight = ParseCssLength(styles, "border-right-width", 0);
            string position = styles.ContainsKey("position") ? styles["position"].ToLowerInvariant() : "static";
            string overflow = styles.ContainsKey("overflow") ? styles["overflow"].ToLowerInvariant() : "visible";
            string overflowX = styles.ContainsKey("overflow-x") ? styles["overflow-x"].ToLowerInvariant() : overflow;
            string overflowY = styles.ContainsKey("overflow-y") ? styles["overflow-y"].ToLowerInvariant() : overflow;

            if (string.Equals(node.Tag, "#text", StringComparison.OrdinalIgnoreCase))
            {
                width = Math.Max(10, (node.Text ?? string.Empty).Length * 7);
                height = 16;
            }

            if (widthAuto || (!styles.ContainsKey("width") && !double.IsNaN(availableFromParent)))
            {
                // shrink-to-fit approximation: clamp between min/max content and available space
                double shrink = Math.Min(intrinsic.Max > 0 ? intrinsic.Max : availableFromParent, Math.Max(intrinsic.Min, availableFromParent));
                width = double.IsNaN(shrink) ? width : shrink;
            }

            // Handle aspect-ratio property
            string aspectRatio;
            if (styles.TryGetValue("aspect-ratio", out aspectRatio) && !string.Equals(aspectRatio, "auto", StringComparison.OrdinalIgnoreCase))
            {
                double ratio = ParseAspectRatio(aspectRatio);
                if (ratio > 0)
                {
                    bool hasExplicitWidth = styles.ContainsKey("width") && !string.Equals(styles["width"], "auto", StringComparison.OrdinalIgnoreCase);
                    bool hasExplicitHeight = styles.ContainsKey("height") && !string.Equals(styles["height"], "auto", StringComparison.OrdinalIgnoreCase);
                    
                    if (hasExplicitWidth && !hasExplicitHeight)
                    {
                        // Calculate height from width
                        height = width / ratio;
                    }
                    else if (hasExplicitHeight && !hasExplicitWidth)
                    {
                        // Calculate width from height
                        width = height * ratio;
                    }
                    else if (!hasExplicitWidth && !hasExplicitHeight)
                    {
                        // Both auto: use width and calculate height
                        height = width / ratio;
                    }
                    // If both explicit, aspect-ratio is ignored
                }
            }

            width = Math.Max(minWidth, width);
            if (!double.IsPositiveInfinity(maxWidth))
            {
                width = Math.Min(width, maxWidth);
            }

            height = Math.Max(minHeight, height);
            if (!double.IsPositiveInfinity(maxHeight))
            {
                height = Math.Min(height, maxHeight);
            }

            if (display == DisplayType.Inline)
            {
                BuildInlineLayout(node, renderNode, ref y, styles, paddingLeft, paddingRight, paddingTop, paddingBottom, borderLeft, borderRight, borderTop, borderBottom, marginLeft, marginRight, marginTop, marginBottom, width, height, position, overflow, cascade, parentComputed);
                return;
            }

            if (display == DisplayType.InlineBlock)
            {
                BuildBlockLayout(node, renderNode, ref y, styles, paddingLeft, paddingRight, paddingTop, paddingBottom, borderLeft, borderRight, borderTop, borderBottom, marginLeft, marginRight, marginTop, marginBottom, width, height, position, overflow, cascade, parentComputed);
                return;
            }

            if (display == DisplayType.InlineTable)
            {
                BuildTableLayout(node, renderNode, ref y, styles, cascade, parentComputed);
                return;
            }

            BuildBlockLayout(node, renderNode, ref y, styles, paddingLeft, paddingRight, paddingTop, paddingBottom, borderLeft, borderRight, borderTop, borderBottom, marginLeft, marginRight, marginTop, marginBottom, width, height, position, overflow, cascade, parentComputed);
        }

        private void BuildBlockLayout(HtmlNode node, RenderNode renderNode, ref double y, Dictionary<string, string> styles, double paddingLeft, double paddingRight, double paddingTop, double paddingBottom, double borderLeft, double borderRight, double borderTop, double borderBottom, double marginLeft, double marginRight, double marginTop, double marginBottom, double width, double height, string position, string overflow, CssCascadeResult cascade, Dictionary<string, string> parentComputed)
        {
            double x = marginLeft + borderLeft + paddingLeft;
            double listIndent = 0;
            string listMarker = null;
            string markerContent = null;
            Dictionary<string, string> markerStyles = null;
            string overflowX = styles.ContainsKey("overflow-x") ? styles["overflow-x"].ToLowerInvariant() : overflow;
            string overflowY = styles.ContainsKey("overflow-y") ? styles["overflow-y"].ToLowerInvariant() : overflow;

            // counters: inherit, reset, increment
            var counters = ExtractCountersFromComputed(parentComputed);
            string counterReset;
            if (styles.TryGetValue("counter-reset", out counterReset) && !string.IsNullOrWhiteSpace(counterReset))
            {
                var tokens = counterReset.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < tokens.Length; i++)
                {
                    var name = tokens[i];
                    int val = 0;
                    if (i + 1 < tokens.Length && int.TryParse(tokens[i + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                    {
                        i++;
                    }
                    counters[name] = val;
                }
            }

            string counterInc;
            if (styles.TryGetValue("counter-increment", out counterInc) && !string.IsNullOrWhiteSpace(counterInc))
            {
                var tokens = counterInc.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < tokens.Length; i++)
                {
                    var name = tokens[i];
                    int val = 1;
                    if (i + 1 < tokens.Length && int.TryParse(tokens[i + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                    {
                        i++;
                    }
                    counters[name] = counters.ContainsKey(name) ? counters[name] + val : val;
                }
            }

            foreach (var kv in counters)
            {
                styles["counter:" + kv.Key] = kv.Value.ToString(CultureInfo.InvariantCulture);
            }
            string listPos="";
            if (string.Equals(node.Tag, "li", StringComparison.OrdinalIgnoreCase))
            {
                
                styles.TryGetValue("list-style-position", out listPos);
                bool inside = string.Equals(listPos, "inside", StringComparison.OrdinalIgnoreCase);
                listIndent = inside ? 0 : 16;

                string listType;
                if (!styles.TryGetValue("list-style-type", out listType))
                {
                    listType = (node.Parent != null && string.Equals(node.Parent.Tag, "ol", StringComparison.OrdinalIgnoreCase)) ? "decimal" : "disc";
                }

                string listImage;
                styles.TryGetValue("list-style-image", out listImage);
                int idx = node.Parent != null ? node.Parent.Children.IndexOf(node) + 1 : 1;
                if (!string.IsNullOrEmpty(listImage) && !string.Equals(listImage, "none", StringComparison.OrdinalIgnoreCase))
                {
                    listMarker = "img:" + listImage;
                }
                else
                {
                    switch ((listType ?? string.Empty).ToLowerInvariant())
                    {
                        case "decimal":
                            listMarker = idx.ToString(CultureInfo.InvariantCulture) + ".";
                            break;
                        case "circle":
                            listMarker = "?";
                            break;
                        case "square":
                            listMarker = "¦";
                            break;
                        case "none":
                            listMarker = null;
                            listIndent = 0;
                            break;
                        default:
                            listMarker = "•";
                            break;
                    }
                }

                if (styles.TryGetValue("marker::content", out markerContent))
                {
                    markerContent = ResolveContentValue(node, markerContent, styles) ?? listMarker;
                }
                else
                {
                    markerContent = listMarker;
                }

                markerStyles = styles.Where(k => k.Key.StartsWith("marker::", StringComparison.OrdinalIgnoreCase))
                                     .ToDictionary(k => k.Key.Substring("marker::".Length), k => k.Value, StringComparer.OrdinalIgnoreCase);
                if (markerStyles != null)
                {
                    ResolveCurrentColor(markerStyles, styles);
                }

                // expose list index for counter(list-item)
                node.Attributes["__list_index"] = idx.ToString(CultureInfo.InvariantCulture);

                x += listIndent;
            }

            bool parentAllowsCollapse = parentComputed != null && parentComputed.TryGetValue("_can-collapse", out var collapseFlag) && bool.TryParse(collapseFlag, out var collapseAllowed) && collapseAllowed;

            if (string.Equals(position, "absolute", StringComparison.OrdinalIgnoreCase) || string.Equals(position, "fixed", StringComparison.OrdinalIgnoreCase))
            {
                double left = styles.ContainsKey("left") ? ParseCssLength(styles, "left", 0) : double.NaN;
                double top = styles.ContainsKey("top") ? ParseCssLength(styles, "top", 0) : double.NaN;
                if (!double.IsNaN(left))
                {
                    x = left + marginLeft + borderLeft + paddingLeft;
                }
                else if (string.Equals(position, "fixed", StringComparison.OrdinalIgnoreCase))
                {
                    x = marginLeft + borderLeft + paddingLeft;
                }

                if (!double.IsNaN(top))
                {
                    y = top + marginTop;
                }
                else if (string.Equals(position, "fixed", StringComparison.OrdinalIgnoreCase))
                {
                    y = marginTop;
                }
            }
            else
            {
                // parent/child margin collapsing (simplified): collapse first child margin into parent when no padding/border
                bool collapsedWithParent = parentAllowsCollapse && Math.Abs(y) < 0.001;
                if (!collapsedWithParent)
                {
                    y += marginTop;
                }
                else
                {
                    styles["_collapsed-with-parent"] = "true";
                }
            }

            height += paddingTop + paddingBottom + borderTop + borderBottom;

            var contentWidth = Math.Max(0, width - paddingLeft - paddingRight - borderLeft - borderRight - marginLeft - marginRight - listIndent);

            renderNode.Box = new Box
            {
                Tag = node.Tag,
                Layout = new Rect(x, y, contentWidth, height),
                ComputedStyle = new Dictionary<string, string>(styles)
            };

            renderNode.Box.ComputedStyle["_content-width"] = contentWidth.ToString(CultureInfo.InvariantCulture);
                renderNode.Box.ComputedStyle["_can-collapse"] = (paddingTop == 0 && paddingBottom == 0 && borderTop == 0 && borderBottom == 0).ToString();
                foreach (var kv in counters)
                {
                    renderNode.Box.ComputedStyle["counter:" + kv.Key] = kv.Value.ToString(CultureInfo.InvariantCulture);
                }

            string containerType;
            if (CssEngine != null && styles.TryGetValue("container-type", out containerType) &&
                !string.Equals(containerType, "normal", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(containerType, "none", StringComparison.OrdinalIgnoreCase))
            {
                string containerName;
                styles.TryGetValue("container-name", out containerName);
                CssEngine.RegisterContainer(string.IsNullOrWhiteSpace(containerName) ? "" : containerName.Trim(), renderNode.Box.Layout.Width, renderNode.Box.Layout.Height, containerType);
            }

                if (!string.IsNullOrEmpty(markerContent))
                {
                    renderNode.Box.ComputedStyle["list-marker"] = markerContent;
                    double markerWidth = Math.Max(7, markerContent.Length * 7);
                    bool inside = string.Equals(listPos, "inside", StringComparison.OrdinalIgnoreCase);
                    double markerX = inside ? x : x - listIndent;
                    var markerNode = new RenderNode
                    {
                        Box = new Box
                        {
                            Tag = "::marker",
                            Layout = new Rect(markerX, y + paddingTop + borderTop, markerWidth, 16),
                            ComputedStyle = markerStyles != null && markerStyles.Count > 0 ? new Dictionary<string, string>(markerStyles, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>()
                        }
                    };
                    markerNode.Box.ComputedStyle["content"] = markerContent;
                    ResolveCurrentColor(markerNode.Box.ComputedStyle, renderNode.Box.ComputedStyle);
                    renderNode.Children.Add(markerNode);
                }

            // relative positioning offsets (does not affect flow)
            if (string.Equals(position, "relative", StringComparison.OrdinalIgnoreCase))
            {
                double leftOffset = styles.ContainsKey("left") ? ParseCssLength(styles, "left", 0) : 0;
                double topOffset = styles.ContainsKey("top") ? ParseCssLength(styles, "top", 0) : 0;
                var l = renderNode.Box.Layout;
                renderNode.Box.Layout = new Rect(l.X + leftOffset, l.Y + topOffset, l.Width, l.Height);
            }

            // sticky positioning - like relative but with scroll-based constraints
            if (string.Equals(position, "sticky", StringComparison.OrdinalIgnoreCase))
            {
                // Mark as sticky for scroll handling
                renderNode.Box.ComputedStyle["_is-sticky"] = "true";
                
                // Store the static position
                renderNode.Box.ComputedStyle["_sticky-static-y"] = y.ToString(CultureInfo.InvariantCulture);
                
                // Store constraint values
                if (styles.ContainsKey("top"))
                    renderNode.Box.ComputedStyle["_sticky-top"] = styles["top"];
                if (styles.ContainsKey("bottom"))
                    renderNode.Box.ComputedStyle["_sticky-bottom"] = styles["bottom"];
                if (styles.ContainsKey("left"))
                    renderNode.Box.ComputedStyle["_sticky-left"] = styles["left"];
                if (styles.ContainsKey("right"))
                    renderNode.Box.ComputedStyle["_sticky-right"] = styles["right"];
            }

            double childY = y + paddingTop + borderTop;
            double maxChildY = childY;
            double floatLeftOffset = 0;
            double floatRightOffset = 0;
            double floatMaxHeight = 0;
            double prevMarginBottom = 0;
            bool prevFlow = false;

            // before pseudo
            string beforeContent;
            if (styles.TryGetValue("before::content", out beforeContent))
            {
                var resolved = ResolveContentValue(node, beforeContent);
                if (!string.IsNullOrEmpty(resolved))
                {
                    var pseudoNode = new RenderNode
                    {
                        Box = new Box
                        {
                            Tag = "::before",
                            Layout = new Rect(x, childY, Math.Max(10, resolved.Length * 7), 16),
                            ComputedStyle = styles.Where(k => k.Key.StartsWith("before::", StringComparison.OrdinalIgnoreCase)).ToDictionary(k => k.Key.Substring("before::".Length), k => k.Value, StringComparer.OrdinalIgnoreCase)
                        }
                    };
                    pseudoNode.Box.ComputedStyle["content"] = resolved;
                    renderNode.Children.Add(pseudoNode);
                    childY = pseudoNode.Box.Layout.Bottom;
                    maxChildY = Math.Max(maxChildY, childY);
                }
            }

            foreach (var child in node.Children)
            {
                var childRender = new RenderNode();

                // use tempY for float so normal flow is not consumed
                double tempY = childY;
                BuildRenderTree(child, childRender, ref tempY, cascade, renderNode.Box.ComputedStyle);

                var box = childRender.Box;
                if (box == null)
                {
                    continue;
                }

                string floatDir;
                box.ComputedStyle.TryGetValue("float", out floatDir);
                string clear;
                box.ComputedStyle.TryGetValue("clear", out clear);

                // apply clear: reset float offsets and move down past floats
                if (!string.IsNullOrEmpty(clear))
                {
                    var c = clear.ToLowerInvariant();
                    if (c.Contains("left") || c.Contains("both"))
                    {
                        floatLeftOffset = 0;
                    }
                    if (c.Contains("right") || c.Contains("both"))
                    {
                        floatRightOffset = 0;
                    }
                    childY = Math.Max(childY, y + paddingTop + borderTop + floatMaxHeight);
                }

                if (!string.IsNullOrEmpty(floatDir) && (floatDir.Equals("left", StringComparison.OrdinalIgnoreCase) || floatDir.Equals("right", StringComparison.OrdinalIgnoreCase)))
                {
                    double posX = floatDir.Equals("left", StringComparison.OrdinalIgnoreCase)
                        ? x + floatLeftOffset
                        : x + contentWidth - floatRightOffset - box.Layout.Width;
                    box.Layout = new Rect(posX, childY, box.Layout.Width, box.Layout.Height);

                    if (floatDir.Equals("left", StringComparison.OrdinalIgnoreCase))
                    {
                        floatLeftOffset += box.Layout.Width;
                    }
                    else
                    {
                        floatRightOffset += box.Layout.Width;
                    }

                    floatMaxHeight = Math.Max(floatMaxHeight, box.Layout.Height);
                    renderNode.Children.Add(childRender);
                    maxChildY = Math.Max(maxChildY, box.Layout.Bottom);
                    prevFlow = false;
                    continue;
                }

                // normal flow child
                double currTopMargin = ParseCssLength(box.ComputedStyle, "margin-top", 0);
                double currBottomMargin = ParseCssLength(box.ComputedStyle, "margin-bottom", 0);

                if (prevFlow)
                {
                    double collapse = Math.Min(prevMarginBottom, currTopMargin);
                    box.Layout = new Rect(box.Layout.X, box.Layout.Y - collapse, box.Layout.Width, box.Layout.Height);
                    childY -= collapse;
                }

                double availableWidth = Math.Max(0, contentWidth - floatLeftOffset - floatRightOffset);
                double posNormalX = x + floatLeftOffset;
                box.Layout = new Rect(posNormalX, childY, Math.Min(box.Layout.Width, availableWidth), box.Layout.Height);
                childY = box.Layout.Bottom;
                maxChildY = Math.Max(maxChildY, box.Layout.Bottom);
                renderNode.Children.Add(childRender);

                prevMarginBottom = currBottomMargin;
                prevFlow = true;
            }

            // overflow hidden/scroll: clamp height to box
            if (string.Equals(overflowX, "hidden", StringComparison.OrdinalIgnoreCase) || string.Equals(overflowY, "hidden", StringComparison.OrdinalIgnoreCase) || string.Equals(overflow, "clip", StringComparison.OrdinalIgnoreCase))
            {
                var layout = renderNode.Box.Layout;
                renderNode.Box.Layout = new Rect(layout.X, layout.Y, layout.Width, height);
                renderNode.Box.ComputedStyle["_clip"] = "true";
                renderNode.Box.ComputedStyle["scroll-height"] = (maxChildY - y).ToString(CultureInfo.InvariantCulture);
                renderNode.Box.ComputedStyle["scroll-width"] = layout.Width.ToString(CultureInfo.InvariantCulture);
            }
            else if (string.Equals(overflow, "scroll", StringComparison.OrdinalIgnoreCase) || string.Equals(overflowX, "scroll", StringComparison.OrdinalIgnoreCase) || string.Equals(overflowY, "scroll", StringComparison.OrdinalIgnoreCase) || string.Equals(overflow, "auto", StringComparison.OrdinalIgnoreCase))
            {
                var layout = renderNode.Box.Layout;
                // compute needed height; use current height but note scrollable content extent
                double contentExtent = maxChildY - y;
                renderNode.Box.Layout = new Rect(layout.X, layout.Y, layout.Width, height);
                renderNode.Box.ComputedStyle["scroll-height"] = contentExtent.ToString(CultureInfo.InvariantCulture);
                renderNode.Box.ComputedStyle["scroll-width"] = layout.Width.ToString(CultureInfo.InvariantCulture);
            }

            if (string.Equals(position, "absolute", StringComparison.OrdinalIgnoreCase) || string.Equals(position, "fixed", StringComparison.OrdinalIgnoreCase))
            {
                // out of flow: do not advance parent y
            }
            else
            {
                y = childY + paddingBottom + borderBottom + marginBottom;
            }
        }

        private void BuildInlineLayout(HtmlNode node, RenderNode renderNode, ref double y, Dictionary<string, string> styles, double paddingLeft, double paddingRight, double paddingTop, double paddingBottom, double borderLeft, double borderRight, double borderTop, double borderBottom, double marginLeft, double marginRight, double marginTop, double marginBottom, double width, double height, string position, string overflow, CssCascadeResult cascade, Dictionary<string, string> parentComputed)
        {
            double lineHeight = ParseCssLength(styles, "line-height", height);
            double available = width - paddingLeft - paddingRight - borderLeft - borderRight - marginLeft - marginRight;
            double cursorX = marginLeft + borderLeft + paddingLeft;
            double currentLineY = y + marginTop + paddingTop + borderTop;
            double maxLineHeight = lineHeight;
            var firstLineStyles = ExtractPseudoStyles(styles, "first-line");
            var firstLetterStyles = ExtractPseudoStyles(styles, "first-letter");
            var selectionStyles = ExtractPseudoStyles(styles, "selection");
            bool firstLineActive = true;
            bool firstLetterApplied = false;
            string whiteSpace;
            styles.TryGetValue("white-space", out whiteSpace);
            whiteSpace = (whiteSpace ?? string.Empty).ToLowerInvariant();
            string wordBreak;
            styles.TryGetValue("word-break", out wordBreak);
            wordBreak = (wordBreak ?? string.Empty).ToLowerInvariant();
            string overflowWrap;
            styles.TryGetValue("overflow-wrap", out overflowWrap);
            overflowWrap = (overflowWrap ?? string.Empty).ToLowerInvariant();
            bool preserve = whiteSpace == "pre";
            bool preserveNewlines = whiteSpace == "pre" || whiteSpace == "pre-wrap" || whiteSpace == "pre-line";
            bool collapseSpaces = whiteSpace == "normal" || whiteSpace == "nowrap" || whiteSpace == "pre-line";
            bool noWrap = whiteSpace == "nowrap" || whiteSpace == "pre";
            bool breakAll = wordBreak == "break-all";
            bool breakWord = !breakAll && (overflowWrap == "break-word" || wordBreak == "break-word");

            // Direct text node handling (node itself is #text)
            if (string.Equals(node.Tag, "#text", StringComparison.OrdinalIgnoreCase))
            {
                var textContent = CollapseWhitespace(node.Text ?? string.Empty);
                if (preserve)
                {
                    textContent = node.Text ?? string.Empty;
                }

                if (!string.IsNullOrEmpty(textContent))
                {
                    double textWidth = Math.Max(7, MeasureTextWidth(textContent, styles));
                    var textRender = new RenderNode
                    {
                        Box = new Box
                        {
                            Tag = "#text",
                            Layout = new Rect(cursorX, currentLineY, textWidth, lineHeight),
                            ComputedStyle = new Dictionary<string, string>(styles)
                        }
                    };
                    textRender.Box.ComputedStyle["content"] = textContent;
                    renderNode.Box = textRender.Box;
                    y = currentLineY + lineHeight + marginBottom;
                }
                return;
            }

            string direction;
            styles.TryGetValue("direction", out direction);
            direction = (direction ?? "ltr").ToLowerInvariant();

            var lineChildren = new List<RenderNode>();

            // before pseudo inline
            string inlineBefore;
            if (styles.TryGetValue("before::content", out inlineBefore))
            {
                var resolved = ResolveContentValue(node, inlineBefore);
                if (!string.IsNullOrEmpty(resolved))
                {
                    var textRender = new RenderNode
                    {
                        Box = new Box
                        {
                            Tag = "::before",
                            Layout = new Rect(cursorX, currentLineY, Math.Max(7, resolved.Length * 7), lineHeight),
                            ComputedStyle = styles.Where(k => k.Key.StartsWith("before::", StringComparison.OrdinalIgnoreCase)).ToDictionary(k => k.Key.Substring("before::".Length), k => k.Value, StringComparer.OrdinalIgnoreCase)
                        }
                    };
                    textRender.Box.ComputedStyle["content"] = resolved;
                    lineChildren.Add(textRender);
                    cursorX += textRender.Box.Layout.Width;
                }
            }

            foreach (var child in node.Children)
            {
                if (string.Equals(child.Tag, "br", StringComparison.OrdinalIgnoreCase))
                {
                    cursorX = marginLeft + borderLeft + paddingLeft;
                    currentLineY += maxLineHeight;
                    maxLineHeight = lineHeight;
                    firstLineActive = false;
                    continue;
                }

                if (string.Equals(child.Tag, "#text", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = child.Text ?? string.Empty;
                    string textContent;
                    if (preserve)
                    {
                        textContent = raw;
                    }
                    else if (collapseSpaces)
                    {
                        textContent = CollapseWhitespace(raw);
                    }
                    else
                    {
                        textContent = raw;
                    }
                    if (string.IsNullOrEmpty(textContent))
                    {
                        continue;
                    }

                    var lines = preserveNewlines ? textContent.Split(new[] { '\n' }) : new[] { textContent };
                    for (int li = 0; li < lines.Length; li++)
                    {
                        var line = lines[li];
                        var tokens = preserve ? new[] { line } : (collapseSpaces ? line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries) : Regex.Split(line, "(\\s+)").Where(t => t.Length > 0).ToArray());

                        if (string.Equals(direction, "rtl", StringComparison.Ordinal))
                        {
                            Array.Reverse(tokens);
                        }
                        foreach (var token in tokens)
                        {
                            string remainingToken = token;
                            while (!string.IsNullOrEmpty(remainingToken))
                            {
                                double tokenWidth = Math.Max(7, MeasureTextWidth(remainingToken, styles));
                                double availableSpace = marginLeft + borderLeft + paddingLeft + available - cursorX;

                                if (!noWrap && (breakAll || breakWord) && tokenWidth > availableSpace && availableSpace > 0)
                                {
                                    double charWidth = Math.Max(7, MeasureTextWidth(remainingToken.Substring(0, 1), styles));
                                    int charsFit = (int)Math.Max(1, Math.Floor(availableSpace / charWidth));
                                    var chunk = remainingToken.Substring(0, Math.Min(charsFit, remainingToken.Length));
                                    double chunkWidth = Math.Max(7, MeasureTextWidth(chunk, styles));

                                    var textRender = new RenderNode
                                    {
                                        Box = new Box
                                        {
                                            Tag = "#text",
                                            Layout = new Rect(cursorX, currentLineY, chunkWidth, lineHeight),
                                            ComputedStyle = new Dictionary<string, string>(styles)
                                        }
                                    };
                                    textRender.Box.ComputedStyle["content"] = chunk;
                                    lineChildren.Add(textRender);
                                    remainingToken = remainingToken.Substring(chunk.Length);
                                    cursorX += chunkWidth;

                                    if (!string.IsNullOrEmpty(remainingToken))
                                    {
                                        cursorX = marginLeft + borderLeft + paddingLeft;
                                        currentLineY += maxLineHeight;
                                        maxLineHeight = lineHeight;
                                    }
                                }
                                else
                                {
                                    if (!noWrap && cursorX + tokenWidth > marginLeft + borderLeft + paddingLeft + available)
                                    {
                                        cursorX = marginLeft + borderLeft + paddingLeft;
                                        currentLineY += maxLineHeight;
                                        maxLineHeight = lineHeight;
                                    }

                                    var textRender = new RenderNode
                                    {
                                        Box = new Box
                                        {
                                            Tag = "#text",
                                            Layout = new Rect(cursorX, currentLineY, tokenWidth, lineHeight),
                                            ComputedStyle = new Dictionary<string, string>(styles)
                                        }
                                    };
                                    textRender.Box.ComputedStyle["content"] = remainingToken;

                                    lineChildren.Add(textRender);
                                    cursorX += tokenWidth + (preserve ? 0 : 7);
                                    remainingToken = string.Empty;
                                }
                            }
                        }

                        if (preserve && li < lines.Length - 1)
                        {
                            cursorX = marginLeft + borderLeft + paddingLeft;
                            currentLineY += maxLineHeight;
                            maxLineHeight = lineHeight;
                        }
                    }

                    maxLineHeight = Math.Max(maxLineHeight, lineHeight);
                }
                else
                {
                    var childRender = new RenderNode();
                    var childY = currentLineY;
                    double dummyY = childY;
                    BuildRenderTree(child, childRender, ref dummyY, cascade, styles);
                    var childBox = childRender.Box;
                    if (childBox == null)
                    {
                        continue;
                    }

                    double childWidth = childBox.Layout.Width;
                    if (cursorX + childWidth > marginLeft + borderLeft + paddingLeft + available)
                    {
                        cursorX = marginLeft + borderLeft + paddingLeft;
                        currentLineY += maxLineHeight;
                        maxLineHeight = lineHeight;
                    }

                    // Apply vertical-align
                    double childHeight = Math.Max(childBox.Layout.Height, lineHeight);
                    double verticalOffset = 0;
                    string verticalAlign;
                    if (childBox.ComputedStyle.TryGetValue("vertical-align", out verticalAlign))
                    {
                        verticalAlign = verticalAlign.ToLowerInvariant();
                        switch (verticalAlign)
                        {
                            case "baseline":
                                // Default, no offset
                                break;
                            case "top":
                            case "text-top":
                                verticalOffset = 0;
                                break;
                            case "middle":
                                verticalOffset = (lineHeight - childHeight) / 2.0;
                                break;
                            case "bottom":
                            case "text-bottom":
                                verticalOffset = lineHeight - childHeight;
                                break;
                            case "sub":
                                verticalOffset = lineHeight * 0.3;
                                break;
                            case "super":
                                verticalOffset = -lineHeight * 0.3;
                                break;
                            default:
                                // Try to parse as length
                                if (verticalAlign.EndsWith("px") || verticalAlign.EndsWith("em") || verticalAlign.EndsWith("%"))
                                {
                                    verticalOffset = -ParseCssLength(childBox.ComputedStyle, "vertical-align", 0);
                                }
                                break;
                        }
                    }

                    childBox.Layout = new Rect(cursorX, currentLineY + verticalOffset, childWidth, childHeight);
                    cursorX += childWidth;
                    maxLineHeight = Math.Max(maxLineHeight, childBox.Layout.Height);
                    lineChildren.Add(childRender);
                }
            }

            height = maxLineHeight + paddingTop + paddingBottom + borderTop + borderBottom;
            var inlineLayout = new Rect(marginLeft + borderLeft + paddingLeft, y + marginTop, Math.Max(0, width - marginLeft - marginRight), height);
            // relative positioning
            if (string.Equals(position, "relative", StringComparison.OrdinalIgnoreCase))
            {
                double leftOffset = styles.ContainsKey("left") ? ParseCssLength(styles, "left", 0) : 0;
                double topOffset = styles.ContainsKey("top") ? ParseCssLength(styles, "top", 0) : 0;
                inlineLayout = new Rect(inlineLayout.X + leftOffset, inlineLayout.Y + topOffset, inlineLayout.Width, inlineLayout.Height);
            }

            renderNode.Box = new Box
            {
                Tag = node.Tag,
                Layout = inlineLayout,
                ComputedStyle = new Dictionary<string, string>(styles)
            };

            string inlineContainerType;
            if (CssEngine != null && styles.TryGetValue("container-type", out inlineContainerType) &&
                !string.Equals(inlineContainerType, "normal", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(inlineContainerType, "none", StringComparison.OrdinalIgnoreCase))
            {
                string inlineContainerName;
                styles.TryGetValue("container-name", out inlineContainerName);
                CssEngine.RegisterContainer(string.IsNullOrWhiteSpace(inlineContainerName) ? "" : inlineContainerName.Trim(), renderNode.Box.Layout.Width, renderNode.Box.Layout.Height, inlineContainerType);
            }

            renderNode.Children.AddRange(lineChildren);
            // inline ::after
            string inlineAfter;
            if (styles.TryGetValue("after::content", out inlineAfter))
            {
                var resolved = ResolveContentValue(node, inlineAfter);
                if (!string.IsNullOrEmpty(resolved))
                {
                    var afterRender = new RenderNode
                    {
                        Box = new Box
                        {
                            Tag = "::after",
                            Layout = new Rect(cursorX, currentLineY, Math.Max(7, resolved.Length * 7), lineHeight),
                            ComputedStyle = styles.Where(k => k.Key.StartsWith("after::", StringComparison.OrdinalIgnoreCase)).ToDictionary(k => k.Key.Substring("after::".Length), k => k.Value, StringComparer.OrdinalIgnoreCase)
                        }
                    };
                    afterRender.Box.ComputedStyle["content"] = resolved;
                    renderNode.Children.Add(afterRender);
                }
            }

            y += height + marginBottom + marginTop;
        }

        private void BuildTableLayout(HtmlNode node, RenderNode renderNode, ref double y, Dictionary<string, string> styles, CssCascadeResult cascade, Dictionary<string, string> parentComputed)
        {
            // Simple table: rows = children, cells = row children; no colspan/rowspan
            double width = ParseCssLength(styles, "width", 800);
            double marginTop = ParseCssLength(styles, "margin-top", 0);
            double marginBottom = ParseCssLength(styles, "margin-bottom", 0);
            double marginLeft = ParseCssLength(styles, "margin-left", 0);
            double marginRight = ParseCssLength(styles, "margin-right", 0);
            double paddingLeft = ParseCssLength(styles, "padding-left", 0);
            double paddingRight = ParseCssLength(styles, "padding-right", 0);
            double paddingTop = ParseCssLength(styles, "padding-top", 0);
            double paddingBottom = ParseCssLength(styles, "padding-bottom", 0);

            y += marginTop;
            double x = marginLeft + paddingLeft;
            double rowY = y + paddingTop;
            double availableWidth = Math.Max(0, width - marginLeft - marginRight - paddingLeft - paddingRight);

            // determine max columns considering colspan
            var rowNodes = node.Children;
            int totalCols = 1;
            foreach (var row in rowNodes)
            {
                int count = 0;
                foreach (var cell in row.Children)
                {
                    int cs;
                    count += (cell.Attributes.ContainsKey("colspan") && int.TryParse(cell.Attributes["colspan"], out cs) && cs > 0) ? cs : 1;
                }
                totalCols = Math.Max(totalCols, count);
            }

            var colMin = Enumerable.Repeat(0.0, totalCols).ToList();
            var colMax = Enumerable.Repeat(0.0, totalCols).ToList();

            // precompute intrinsic min/max per column
            foreach (var row in rowNodes)
            {
                int colIndex = 0;
                foreach (var cell in row.Children)
                {
                    int colspan = 1;
                    string csVal;
                    if (cell.Attributes.TryGetValue("colspan", out csVal))
                    {
                        int.TryParse(csVal, out colspan);
                    }
                    colspan = Math.Max(1, colspan);
                    var intrinsic = ComputeIntrinsicSizes(cell, cascade);
                    double perMin = intrinsic.Min / colspan;
                    double perMax = intrinsic.Max / colspan;
                    for (int c = 0; c < colspan && colIndex + c < totalCols; c++)
                    {
                        colMin[colIndex + c] = Math.Max(colMin[colIndex + c], perMin);
                        colMax[colIndex + c] = Math.Max(colMax[colIndex + c], perMax);
                    }
                    colIndex += colspan;
                }
            }

            // distribute available width respecting intrinsic constraints
            var colWidths = new List<double>(colMin);
            double minSum = colMin.Sum();
            double remaining = availableWidth - minSum;
            if (remaining > 0)
            {
                var flex = colMax.Select((max, i) => Math.Max(0, max - colMin[i])).ToList();
                double flexTotal = flex.Sum();
                if (flexTotal > 0)
                {
                    for (int i = 0; i < colWidths.Count; i++)
                    {
                        double share = flex[i] > 0 ? remaining * (flex[i] / flexTotal) : 0;
                        colWidths[i] = Math.Min(colMin[i] + flex[i], colMin[i] + share);
                    }
                }
                else
                {
                    for (int i = 0; i < colWidths.Count; i++)
                    {
                        colWidths[i] += remaining / Math.Max(1, colWidths.Count);
                    }
                }
            }

            var activeRowSpans = new List<(int col, int spanCols, int remainingRows, double height)>();

            foreach (var row in rowNodes)
            {
                var rowRender = new RenderNode();
                renderNode.Children.Add(rowRender);

                bool[] occupied = new bool[totalCols];
                for (int i = activeRowSpans.Count - 1; i >= 0; i--)
                {
                    var span = activeRowSpans[i];
                    for (int c = span.col; c < Math.Min(totalCols, span.col + span.spanCols); c++)
                    {
                        occupied[c] = true;
                    }
                    if (span.remainingRows <= 1)
                    {
                        activeRowSpans.RemoveAt(i);
                    }
                    else
                    {
                        activeRowSpans[i] = (span.col, span.spanCols, span.remainingRows - 1, span.height);
                    }
                }

                int colCursor = 0;
                double rowHeight = activeRowSpans.Count > 0 ? activeRowSpans.Max(s => s.height) : 0;

                foreach (var cell in row.Children)
                {
                    while (colCursor < totalCols && occupied[colCursor])
                    {
                        colCursor++;
                    }

                    int colspan;
                    colspan = (cell.Attributes.ContainsKey("colspan") && int.TryParse(cell.Attributes["colspan"], out colspan) && colspan > 0) ? colspan : 1;
                    int rowspan;
                    rowspan = (cell.Attributes.ContainsKey("rowspan") && int.TryParse(cell.Attributes["rowspan"], out rowspan) && rowspan > 0) ? rowspan : 1;

                    double cellX = x + colWidths.Take(colCursor).Sum();
                    double cellWidth = colWidths.Skip(colCursor).Take(Math.Max(1, colspan)).Sum();

                    var cellRender = new RenderNode();
                    double cellY = rowY;
                    var parentForCell = new Dictionary<string, string>(styles)
                    {
                        ["_content-width"] = cellWidth.ToString(CultureInfo.InvariantCulture)
                    };
                    BuildRenderTree(cell, cellRender, ref cellY, cascade, parentForCell);
                    var box = cellRender.Box;
                    if (box != null)
                    {
                        double cellHeight = box.Layout.Height * Math.Max(1, rowspan);
                        box.Layout = new Rect(cellX, rowY, cellWidth, cellHeight);
                        rowHeight = Math.Max(rowHeight, box.Layout.Height / Math.Max(1, rowspan));

                        if (rowspan > 1)
                        {
                            activeRowSpans.Add((colCursor, Math.Max(1, colspan), rowspan - 1, box.Layout.Height));
                        }
                    }

                    rowRender.Children.Add(cellRender);
                    for (int i = 0; i < colspan && colCursor + i < occupied.Length; i++)
                    {
                        occupied[colCursor + i] = true;
                    }
                    colCursor += Math.Max(1, colspan);
                }

                // row box
                double rowWidth = colWidths.Sum();
                rowRender.Box = new Box
                {
                    Tag = "tr",
                    Layout = new Rect(x, rowY, rowWidth, rowHeight),
                    ComputedStyle = new Dictionary<string, string>(styles)
                };

                rowY += rowHeight;
            }

            double tableHeight = rowY - y + paddingBottom;
            double tableWidth = colWidths.Sum();
            renderNode.Box = new Box
            {
                Tag = node.Tag,
                Layout = new Rect(x, y, tableWidth, tableHeight),
                ComputedStyle = new Dictionary<string, string>(styles)
            };

            string tableContainerType;
            if (CssEngine != null && styles.TryGetValue("container-type", out tableContainerType) &&
                !string.Equals(tableContainerType, "normal", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(tableContainerType, "none", StringComparison.OrdinalIgnoreCase))
            {
                string tableContainerName;
                styles.TryGetValue("container-name", out tableContainerName);
                CssEngine.RegisterContainer(string.IsNullOrWhiteSpace(tableContainerName) ? "" : tableContainerName.Trim(), renderNode.Box.Layout.Width, renderNode.Box.Layout.Height, tableContainerType);
            }

            y += tableHeight + marginBottom;
        }

        private string CollapseWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return Regex.Replace(text, "\\s+", " ").Trim();
        }

        private void BuildFlexLayout(HtmlNode node, RenderNode renderNode, ref double y, Dictionary<string, string> styles, CssCascadeResult cascade, Dictionary<string, string> parentComputed)
        {
            string direction = styles.ContainsKey("flex-direction") ? styles["flex-direction"].ToLowerInvariant() : "row";
            string justify = styles.ContainsKey("justify-content") ? styles["justify-content"].ToLowerInvariant() : "flex-start";
            string align = styles.ContainsKey("align-items") ? styles["align-items"].ToLowerInvariant() : "stretch";
            string alignContent = styles.ContainsKey("align-content") ? styles["align-content"].ToLowerInvariant() : "stretch";
            bool wrap = styles.ContainsKey("flex-wrap") && string.Equals(styles["flex-wrap"], "wrap", StringComparison.OrdinalIgnoreCase);
            double gap = ParseCssLength(styles, "gap", 0);
            double rowGap = styles.ContainsKey("row-gap") ? ParseCssLength(styles, "row-gap", gap) : gap;
            double colGap = styles.ContainsKey("column-gap") ? ParseCssLength(styles, "column-gap", gap) : gap;

            double width = ParseCssLength(styles, "width", 800);
            double height = ParseCssLength(styles, "height", 20);
            double paddingLeft = ParseCssLength(styles, "padding-left", 0);
            double paddingRight = ParseCssLength(styles, "padding-right", 0);
            double paddingTop = ParseCssLength(styles, "padding-top", 0);
            double paddingBottom = ParseCssLength(styles, "padding-bottom", 0);
            double marginTop = ParseCssLength(styles, "margin-top", 0);
            double marginBottom = ParseCssLength(styles, "margin-bottom", 0);
            double marginLeft = ParseCssLength(styles, "margin-left", 0);
            double marginRight = ParseCssLength(styles, "margin-right", 0);

            y += marginTop;

            double containerX = marginLeft + paddingLeft;
            double containerY = y + paddingTop;
            double innerWidth = Math.Max(0, width - paddingLeft - paddingRight);
            double innerHeight = Math.Max(0, height - paddingTop - paddingBottom);

            var children = new List<(RenderNode render, double mainSize, double crossSize, double grow, double shrink, double alignSelf)>();

            foreach (var child in node.Children)
            {
                var childRender = new RenderNode();
                double childY = 0;
                BuildRenderTree(child, childRender, ref childY, cascade, styles);
                var box = childRender.Box;
                if (box == null)
                {
                    continue;
                }

                double grow = ParseCssLength(box.ComputedStyle, "flex-grow", 0);
                double shrink = ParseCssLength(box.ComputedStyle, "flex-shrink", 1);
                double basis = ParseCssLength(box.ComputedStyle, "flex-basis", double.NaN);
                double alignSelf = double.NaN;
                string alignSelfText;
                if (box.ComputedStyle.TryGetValue("align-self", out alignSelfText))
                {
                    alignSelf = 1; // marker
                    box.ComputedStyle["_align-self"] = alignSelfText.ToLowerInvariant();
                }
                double main = direction == "column" ? box.Layout.Height : box.Layout.Width;
                double cross = direction == "column" ? box.Layout.Width : box.Layout.Height;

                double minMain = ParseCssLength(box.ComputedStyle, direction == "column" ? "min-height" : "min-width", 0);
                double maxMain = ParseCssLength(box.ComputedStyle, direction == "column" ? "max-height" : "max-width", double.PositiveInfinity);

                if (!double.IsNaN(basis))
                {
                    main = basis;
                }

                main = Math.Max(minMain, main);
                if (!double.IsPositiveInfinity(maxMain))
                {
                    main = Math.Min(main, maxMain);
                }

                children.Add((childRender, main, cross, grow, shrink, alignSelf));
            }

            // Build lines if wrapping, otherwise single line
            var lines = new List<List<int>>();
            if (wrap)
            {
                double lineUsed = 0;
                var current = new List<int>();
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    double next = lineUsed + c.mainSize;
                    double limit = direction == "row" ? innerWidth : innerHeight;
                    if (current.Count > 0 && next > limit)
                    {
                        lines.Add(current);
                        current = new List<int>();
                        lineUsed = 0;
                    }
                    current.Add(i);
                    lineUsed += c.mainSize;
                }
                if (current.Count > 0)
                {
                    lines.Add(current);
                }
            }
            else
            {
                lines.Add(Enumerable.Range(0, children.Count).ToList());
            }

            double lineCursor = 0;
            double maxCross = 0;
            var lineHeights = new List<double>();
            foreach (var line in lines)
            {
                double totalMain = line.Sum(idx => children[idx].mainSize) + Math.Max(0, (line.Count - 1) * colGap);
                double availableMain = direction == "column" ? innerHeight : innerWidth;
                double free = availableMain - totalMain;
                double totalGrow = line.Sum(idx => children[idx].grow);
                double totalShrink = line.Sum(idx => children[idx].shrink * children[idx].mainSize);

                if (free > 0 && totalGrow > 0)
                {
                    foreach (var idx in line)
                    {
                        var c = children[idx];
                        var extra = free * (c.grow / totalGrow);
                        children[idx] = (c.render, c.mainSize + extra, c.crossSize, c.grow, c.shrink, c.alignSelf);
                    }
                    totalMain = line.Sum(idx => children[idx].mainSize);
                    free = availableMain - totalMain;
                }
                else if (free < 0 && totalShrink > 0)
                {
                    foreach (var idx in line)
                    {
                        var c = children[idx];
                        var shrinkShare = (c.shrink * c.mainSize) / totalShrink;
                        var delta = free * shrinkShare; // free is negative
                        children[idx] = (c.render, Math.Max(0, c.mainSize + delta), c.crossSize, c.grow, c.shrink, c.alignSelf);
                    }
                    totalMain = line.Sum(idx => children[idx].mainSize);
                    free = availableMain - totalMain;
                }

                double startMain = 0;
                double spacing = 0;
                switch (justify)
                {
                    case "center":
                        startMain = free / 2.0;
                        break;
                    case "flex-end":
                        startMain = free;
                        break;
                    case "space-between":
                        spacing = line.Count > 1 ? free / (line.Count - 1) : 0;
                        break;
                    case "space-around":
                        spacing = line.Count > 0 ? free / line.Count : 0;
                        startMain = spacing / 2.0;
                        break;
                    default:
                        startMain = 0;
                        break;
                }

                double cursorMain = startMain;
                double lineCross = 0;
                foreach (var idx in line)
                {
                    var c = children[idx];
                    var box = c.render.Box;
                    if (box == null)
                    {
                        continue;
                    }

                    double crossAvailable = direction == "column" ? innerWidth : innerHeight;
                    string alignSelfText;
                    string stored;
                    if (box.ComputedStyle.TryGetValue("_align-self", out stored))
                    {
                        alignSelfText = stored;
                    }
                    else
                    {
                        alignSelfText = align;
                    }

                    double crossOffset = 0;
                    switch (alignSelfText)
                    {
                        case "center":
                            crossOffset = (crossAvailable - c.crossSize) / 2.0;
                            break;
                        case "flex-end":
                            crossOffset = crossAvailable - c.crossSize;
                            break;
                        case "stretch":
                            c.render.Box.Layout = direction == "row"
                                ? new Rect(box.Layout.X, box.Layout.Y, c.mainSize, crossAvailable)
                                : new Rect(box.Layout.X, box.Layout.Y, crossAvailable, c.mainSize);
                            break;
                    }

                    double x = containerX + (direction == "row" ? cursorMain : crossOffset);
                    double yy = containerY + (direction == "row" ? lineCursor + crossOffset : cursorMain + lineCursor);

                    if (alignSelfText != "stretch")
                    {
                        box.Layout = direction == "row"
                            ? new Rect(x, yy, c.mainSize, c.crossSize)
                            : new Rect(x, yy, c.crossSize, c.mainSize);
                    }
                    else
                    {
                        box.Layout = direction == "row"
                            ? new Rect(x, containerY + lineCursor, c.mainSize, crossAvailable)
                            : new Rect(containerX, yy, crossAvailable, c.mainSize);
                    }

                    renderNode.Children.Add(c.render);
                    cursorMain += c.mainSize + spacing + (colGap > 0 ? colGap : 0);
                    lineCross = Math.Max(lineCross, box.Layout.Height);
                }

                lineHeights.Add(lineCross);
                maxCross += lineCross;
                lineCursor += lineCross + (rowGap > 0 ? rowGap : 0);
            }

            // align-content for wrapped lines
            if (wrap && lines.Count > 1)
            {
                double totalCross = lineHeights.Sum() + Math.Max(0, (lineHeights.Count - 1) * rowGap);
                double freeCross = (direction == "row" ? innerHeight : innerWidth) - totalCross;
                double offset = 0;
                double contentGap = 0;
                switch (alignContent)
                {
                    case "center":
                        offset = freeCross / 2.0;
                        break;
                    case "flex-end":
                        offset = freeCross;
                        break;
                    case "space-between":
                        contentGap = lines.Count > 1 ? freeCross / (lines.Count - 1) : 0;
                        break;
                    case "space-around":
                        contentGap = lines.Count > 0 ? freeCross / lines.Count : 0;
                        offset = contentGap / 2.0;
                        break;
                    case "stretch":
                        contentGap = lines.Count > 0 ? freeCross / lines.Count : 0;
                        break;
                }

                double shift = 0;
                for (int l = 0; l < lines.Count; l++)
                {
                    foreach (var idx in lines[l])
                    {
                        var box = children[idx].render.Box;
                        if (box == null)
                        {
                            continue;
                        }

                        if (direction == "row")
                        {
                            box.Layout = new Rect(box.Layout.X, box.Layout.Y + offset + shift, box.Layout.Width, box.Layout.Height + (alignContent == "stretch" ? contentGap : 0));
                        }
                        else
                        {
                            box.Layout = new Rect(box.Layout.X + offset + shift, box.Layout.Y, box.Layout.Width + (alignContent == "stretch" ? contentGap : 0), box.Layout.Height);
                        }
                    }
                    shift += (direction == "row" ? lineHeights[l] : lines[l].Max(i => children[i].render.Box.Layout.Width)) + contentGap + (rowGap > 0 ? rowGap : 0);
                }
            }

            double finalMainExtent = direction == "row" ? innerWidth : innerHeight;
            if (!wrap)
            {
                double totalMain = children.Sum(c => c.mainSize) + Math.Max(0, (children.Count - 1) * colGap);
                double free = finalMainExtent - totalMain;
                finalMainExtent = totalMain + Math.Max(0, free);
            }

            if (direction == "row")
            {
                width = Math.Max(width, finalMainExtent + paddingLeft + paddingRight + marginLeft + marginRight);
                height = Math.Max(height, maxCross + paddingTop + paddingBottom + Math.Max(0, (lineHeights.Count - 1) * rowGap));
            }
            else
            {
                height = Math.Max(height, finalMainExtent + paddingTop + paddingBottom);
                width = Math.Max(width, maxCross + paddingLeft + paddingRight + Math.Max(0, (lineHeights.Count - 1) * rowGap));
            }

            renderNode.Box = new Box
            {
                Tag = node.Tag,
                Layout = new Rect(marginLeft + paddingLeft, y, Math.Max(0, width - marginLeft - marginRight), height),
                ComputedStyle = new Dictionary<string, string>(styles)
            };

            string flexContainerType;
            if (CssEngine != null && styles.TryGetValue("container-type", out flexContainerType) &&
                !string.Equals(flexContainerType, "normal", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(flexContainerType, "none", StringComparison.OrdinalIgnoreCase))
            {
                string flexContainerName;
                styles.TryGetValue("container-name", out flexContainerName);
                CssEngine.RegisterContainer(string.IsNullOrWhiteSpace(flexContainerName) ? "" : flexContainerName.Trim(), renderNode.Box.Layout.Width, renderNode.Box.Layout.Height, flexContainerType);
            }

            y += height + marginBottom + marginTop;
        }

        private void InheritStyles(Dictionary<string, string> styles, Dictionary<string, string> parent)
        {
            if (styles == null)
            {
                return;
            }

            if (parent == null)
            {
                return;
            }

            var inheritable = new[]
            {
                "color", "font-family", "font-size", "font-style", "font-weight", "font-variant", "font-stretch", "line-height",
                "letter-spacing", "word-spacing", "text-transform", "text-align", "text-indent", "text-decoration", "text-shadow",
                "white-space", "direction", "cursor", "visibility", "list-style-type", "list-style-position", "list-style-image"
            };

            foreach (var key in inheritable)
            {
                if (!styles.ContainsKey(key) && parent.ContainsKey(key))
                {
                    styles[key] = parent[key];
                }
            }
        }

        private bool IsHidden(Dictionary<string, string> styles)
        {
            if (styles == null)
            {
                return false;
            }

            if (styles.ContainsKey("display") && string.Equals(styles["display"], "none", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (styles.ContainsKey("visibility") && string.Equals(styles["visibility"], "hidden", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private bool IsFlexContainer(Dictionary<string, string> styles)
        {
            if (styles == null)
            {
                return false;
            }

            string displayValue;
            if (styles.TryGetValue("display", out displayValue))
            {
                return string.Equals(displayValue, "flex", StringComparison.OrdinalIgnoreCase) || string.Equals(displayValue, "inline-flex", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private bool IsGridContainer(Dictionary<string, string> styles)
        {
            if (styles == null)
            {
                return false;
            }

            string displayValue;
            if (styles.TryGetValue("display", out displayValue))
            {
                return string.Equals(displayValue, "grid", StringComparison.OrdinalIgnoreCase) || string.Equals(displayValue, "inline-grid", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private void BuildGridLayout(HtmlNode node, RenderNode renderNode, ref double y, Dictionary<string, string> styles, CssCascadeResult cascade, Dictionary<string, string> parentComputed)
        {
            double width = ParseCssLength(styles, "width", 800);
            double height = ParseCssLength(styles, "height", 0);
            double paddingLeft = ParseCssLength(styles, "padding-left", 0);
            double paddingRight = ParseCssLength(styles, "padding-right", 0);
            double paddingTop = ParseCssLength(styles, "padding-top", 0);
            double paddingBottom = ParseCssLength(styles, "padding-bottom", 0);
            double marginTop = ParseCssLength(styles, "margin-top", 0);
            double marginBottom = ParseCssLength(styles, "margin-bottom", 0);
            double marginLeft = ParseCssLength(styles, "margin-left", 0);
            double marginRight = ParseCssLength(styles, "margin-right", 0);

            y += marginTop;

            double innerWidth = Math.Max(0, width - paddingLeft - paddingRight);
            double innerHeight = height > 0 ? Math.Max(0, height - paddingTop - paddingBottom) : 0;

            // Parse grid-template-columns and grid-template-rows
            string templateCols = styles.ContainsKey("grid-template-columns") ? styles["grid-template-columns"] : "auto";
            string templateRows = styles.ContainsKey("grid-template-rows") ? styles["grid-template-rows"] : "auto";

            // Check for subgrid - inherit tracks from parent grid
            bool isSubgridCols = templateCols.Trim().ToLowerInvariant() == "subgrid";
            bool isSubgridRows = templateRows.Trim().ToLowerInvariant() == "subgrid";

            // If subgrid, inherit from parent (stored in parentComputed)
            if (isSubgridCols && parentComputed != null && parentComputed.ContainsKey("_grid-columns"))
            {
                templateCols = parentComputed["_grid-columns"];
            }
            if (isSubgridRows && parentComputed != null && parentComputed.ContainsKey("_grid-rows"))
            {
                templateRows = parentComputed["_grid-rows"];
            }

            // Store grid template for potential subgrids
            styles["_grid-columns"] = templateCols;
            styles["_grid-rows"] = templateRows;
            
            // Parse gap properties
            double gap = ParseCssLength(styles, "gap", 0);
            double rowGap = styles.ContainsKey("row-gap") ? ParseCssLength(styles, "row-gap", gap) : 
                           (styles.ContainsKey("grid-row-gap") ? ParseCssLength(styles, "grid-row-gap", gap) : gap);
            double colGap = styles.ContainsKey("column-gap") ? ParseCssLength(styles, "column-gap", gap) :
                           (styles.ContainsKey("grid-column-gap") ? ParseCssLength(styles, "grid-column-gap", gap) : gap);

            // Parse justify/align properties
            string justifyItems = styles.ContainsKey("justify-items") ? styles["justify-items"].ToLowerInvariant() : "stretch";
            string alignItems = styles.ContainsKey("align-items") ? styles["align-items"].ToLowerInvariant() : "stretch";
            string justifyContent = styles.ContainsKey("justify-content") ? styles["justify-content"].ToLowerInvariant() : "start";
            string alignContent = styles.ContainsKey("align-content") ? styles["align-content"].ToLowerInvariant() : "start";

            // Parse column and row sizes
            var colSizes = ParseGridTrackSizes(templateCols, innerWidth, node.Children.Count);
            var rowSizes = ParseGridTrackSizes(templateRows, innerHeight > 0 ? innerHeight : 500, node.Children.Count);

            int numCols = colSizes.Count;
            int numRows = rowSizes.Count;

            // Precompute intrinsic min/max for tracks
            var colMin = Enumerable.Repeat(0.0, numCols).ToList();
            var colMax = Enumerable.Repeat(0.0, numCols).ToList();
            var rowMin = Enumerable.Repeat(0.0, numRows).ToList();
            var rowMax = Enumerable.Repeat(0.0, numRows).ToList();
            
            // Prepare grid item placements
            var gridItems = new List<GridItem>();
            int autoRow = 1, autoCol = 1;

            foreach (var child in node.Children)
            {
                if (string.Equals(child.Tag, "#text", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(child.Text))
                    continue;

                var childStyles = cascade.Styles.ContainsKey(child) ? cascade.Styles[child] : new Dictionary<string, string>();
                
                // Parse grid-column and grid-row
                int colStart = autoCol, colEnd = autoCol + 1;
                int rowStart = autoRow, rowEnd = autoRow + 1;

                if (childStyles.ContainsKey("grid-column"))
                {
                    ParseGridLine(childStyles["grid-column"], out colStart, out colEnd, numCols);
                }
                else
                {
                    if (childStyles.ContainsKey("grid-column-start"))
                        int.TryParse(childStyles["grid-column-start"], out colStart);
                    if (childStyles.ContainsKey("grid-column-end"))
                        int.TryParse(childStyles["grid-column-end"], out colEnd);
                    else
                        colEnd = colStart + 1;
                }

                if (childStyles.ContainsKey("grid-row"))
                {
                    ParseGridLine(childStyles["grid-row"], out rowStart, out rowEnd, numRows);
                }
                else
                {
                    if (childStyles.ContainsKey("grid-row-start"))
                        int.TryParse(childStyles["grid-row-start"], out rowStart);
                    if (childStyles.ContainsKey("grid-row-end"))
                        int.TryParse(childStyles["grid-row-end"], out rowEnd);
                    else
                        rowEnd = rowStart + 1;
                }

                // Handle grid-area (row-start / col-start / row-end / col-end)
                if (childStyles.ContainsKey("grid-area"))
                {
                    var areaParts = childStyles["grid-area"].Split('/');
                    if (areaParts.Length >= 4)
                    {
                        int.TryParse(areaParts[0].Trim(), out rowStart);
                        int.TryParse(areaParts[1].Trim(), out colStart);
                        int.TryParse(areaParts[2].Trim(), out rowEnd);
                        int.TryParse(areaParts[3].Trim(), out colEnd);
                    }
                }

                // Clamp to valid range
                colStart = Math.Max(1, Math.Min(colStart, numCols));
                colEnd = Math.Max(colStart + 1, Math.Min(colEnd, numCols + 1));
                rowStart = Math.Max(1, rowStart);
                rowEnd = Math.Max(rowStart + 1, rowEnd);

                gridItems.Add(new GridItem
                {
                    Node = child,
                    ColStart = colStart,
                    ColEnd = colEnd,
                    RowStart = rowStart,
                    RowEnd = rowEnd
                });

                // track intrinsic contributions
                var intrinsic = ComputeIntrinsicSizes(child, cascade);
                double spanCols = Math.Max(1, colEnd - colStart);
                double perMin = intrinsic.Min / spanCols;
                double perMax = intrinsic.Max / spanCols;
                for (int c = colStart; c < colEnd && c - 1 < colMin.Count; c++)
                {
                    colMin[c - 1] = Math.Max(colMin[c - 1], perMin);
                    colMax[c - 1] = Math.Max(colMax[c - 1], perMax);
                }

                double spanRows = Math.Max(1, rowEnd - rowStart);
                double perRowMin = intrinsic.Min / spanRows;
                double perRowMax = intrinsic.Max / spanRows;
                for (int r = rowStart; r < rowEnd && r - 1 < rowMin.Count; r++)
                {
                    rowMin[r - 1] = Math.Max(rowMin[r - 1], perRowMin);
                    rowMax[r - 1] = Math.Max(rowMax[r - 1], perRowMax);
                }

                // Auto-placement for next item
                autoCol = colEnd;
                if (autoCol > numCols)
                {
                    autoCol = 1;
                    autoRow++;
                }
            }

            // Determine actual number of rows needed
            int maxRow = gridItems.Count > 0 ? gridItems.Max(i => i.RowEnd) : 1;
            while (rowSizes.Count < maxRow)
            {
                rowSizes.Add(new GridTrackSize { Type = "auto", Value = 0 });
                rowMin.Add(0);
                rowMax.Add(0);
            }

            // Calculate actual column widths using intrinsic constraints
            var colWidths = CalculateGridTrackSizes(colSizes, innerWidth, colGap, colMin, colMax);

            // Build children first to get their sizes for auto rows
            var childRenders = new Dictionary<GridItem, RenderNode>();
            foreach (var item in gridItems)
            {
                var childRender = new RenderNode();
                double childY = 0;
                BuildRenderTree(item.Node, childRender, ref childY, cascade, styles);
                childRenders[item] = childRender;
            }

            // Calculate row heights (including auto-sizing based on content)
            var rowHeights = new List<double>();
            for (int r = 0; r < maxRow; r++)
            {
                if (r < rowSizes.Count && rowSizes[r].Type != "auto")
                {
                    rowHeights.Add(rowSizes[r].Value);
                }
                else
                {
                    // Auto-size based on content in this row with intrinsic minima
                    double maxHeight = Math.Max(20, rowMin[r]);
                    foreach (var item in gridItems.Where(i => i.RowStart == r + 1))
                    {
                        if (childRenders.ContainsKey(item) && childRenders[item].Box != null)
                        {
                            maxHeight = Math.Max(maxHeight, childRenders[item].Box.Layout.Height);
                        }
                    }
                    rowHeights.Add(maxHeight);
                }
            }

            // Position each grid item
            double containerX = marginLeft + paddingLeft;
            double containerY = y + paddingTop;

            foreach (var item in gridItems)
            {
                var childRender = childRenders[item];
                if (childRender.Box == null) continue;

                // Calculate position
                double itemX = containerX + colWidths.Take(item.ColStart - 1).Sum() + (item.ColStart - 1) * colGap;
                double itemY = containerY + rowHeights.Take(item.RowStart - 1).Sum() + (item.RowStart - 1) * rowGap;

                // Calculate size (spanning multiple tracks if needed)
                double itemWidth = colWidths.Skip(item.ColStart - 1).Take(item.ColEnd - item.ColStart).Sum() + 
                                   Math.Max(0, item.ColEnd - item.ColStart - 1) * colGap;
                double itemHeight = rowHeights.Skip(item.RowStart - 1).Take(item.RowEnd - item.RowStart).Sum() +
                                    Math.Max(0, item.RowEnd - item.RowStart - 1) * rowGap;

                // Provide available width to child auto sizing
                var childParentComputed = new Dictionary<string, string>(styles);
                childParentComputed["_content-width"] = itemWidth.ToString(CultureInfo.InvariantCulture);
                double dummyY = 0;
                var rerender = new RenderNode();
                BuildRenderTree(item.Node, rerender, ref dummyY, cascade, childParentComputed);
                if (rerender.Box != null)
                {
                    childRender = rerender;
                    childRenders[item] = rerender;
                }

                // Apply justify-self / align-self
                var childStyles = cascade.Styles.ContainsKey(item.Node) ? cascade.Styles[item.Node] : new Dictionary<string, string>();
                string justifySelf = childStyles.ContainsKey("justify-self") ? childStyles["justify-self"].ToLowerInvariant() : justifyItems;
                string alignSelf = childStyles.ContainsKey("align-self") ? childStyles["align-self"].ToLowerInvariant() : alignItems;

                double contentWidth = childRender.Box.Layout.Width;
                double contentHeight = childRender.Box.Layout.Height;

                if (justifySelf != "stretch")
                {
                    switch (justifySelf)
                    {
                        case "center":
                            itemX += (itemWidth - contentWidth) / 2;
                            itemWidth = contentWidth;
                            break;
                        case "end":
                            itemX += itemWidth - contentWidth;
                            itemWidth = contentWidth;
                            break;
                        case "start":
                            itemWidth = contentWidth;
                            break;
                    }
                }

                if (alignSelf != "stretch")
                {
                    switch (alignSelf)
                    {
                        case "center":
                            itemY += (itemHeight - contentHeight) / 2;
                            itemHeight = contentHeight;
                            break;
                        case "end":
                            itemY += itemHeight - contentHeight;
                            itemHeight = contentHeight;
                            break;
                        case "start":
                            itemHeight = contentHeight;
                            break;
                    }
                }

                childRender.Box.Layout = new Rect(itemX, itemY, itemWidth, itemHeight);
                renderNode.Children.Add(childRender);
            }

            // Calculate total grid size
            double totalWidth = colWidths.Sum() + Math.Max(0, colWidths.Count - 1) * colGap;
            double totalHeight = rowHeights.Sum() + Math.Max(0, rowHeights.Count - 1) * rowGap;

            height = Math.Max(height, totalHeight + paddingTop + paddingBottom);
            width = Math.Max(width, totalWidth + paddingLeft + paddingRight);

            renderNode.Box = new Box
            {
                Tag = node.Tag,
                Layout = new Rect(marginLeft, y, width, height),
                ComputedStyle = new Dictionary<string, string>(styles)
            };

            string gridContainerType;
            if (CssEngine != null && styles.TryGetValue("container-type", out gridContainerType) &&
                !string.Equals(gridContainerType, "normal", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(gridContainerType, "none", StringComparison.OrdinalIgnoreCase))
            {
                string gridContainerName;
                styles.TryGetValue("container-name", out gridContainerName);
                CssEngine.RegisterContainer(string.IsNullOrWhiteSpace(gridContainerName) ? "" : gridContainerName.Trim(), renderNode.Box.Layout.Width, renderNode.Box.Layout.Height, gridContainerType);
            }

            y += height + marginBottom;
        }

        private class GridItem
        {
            public HtmlNode Node { get; set; }
            public int ColStart { get; set; }
            public int ColEnd { get; set; }
            public int RowStart { get; set; }
            public int RowEnd { get; set; }
        }

        private class GridTrackSize
        {
            public string Type { get; set; } // "px", "fr", "%", "auto", "min-content", "max-content"
            public double Value { get; set; }
        }

        private List<GridTrackSize> ParseGridTrackSizes(string template, double available, int itemCount)
        {
            var sizes = new List<GridTrackSize>();
            if (string.IsNullOrWhiteSpace(template) || template == "none")
            {
                // Default: single auto column/row
                sizes.Add(new GridTrackSize { Type = "auto", Value = 0 });
                return sizes;
            }

            // Handle repeat() function
            template = Regex.Replace(template, @"repeat\(\s*(\d+)\s*,\s*([^)]+)\s*\)", m =>
            {
                int count = int.Parse(m.Groups[1].Value);
                string pattern = m.Groups[2].Value.Trim();
                return string.Join(" ", Enumerable.Repeat(pattern, count));
            }, RegexOptions.IgnoreCase);

            // Handle auto-fill and auto-fit
            template = Regex.Replace(template, @"repeat\(\s*(auto-fill|auto-fit)\s*,\s*minmax\(\s*([^,]+)\s*,\s*([^)]+)\s*\)\s*\)", m =>
            {
                double minSize = ParseCssLengthValue(m.Groups[2].Value.Trim(), 100, null);
                int autoCount = Math.Max(1, (int)(available / (minSize + 10)));
                return string.Join(" ", Enumerable.Repeat("1fr", autoCount));
            }, RegexOptions.IgnoreCase);

            // Parse individual track sizes
            var tokens = template.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var trimmed = token.Trim().ToLowerInvariant();
                
                if (trimmed == "auto")
                {
                    sizes.Add(new GridTrackSize { Type = "auto", Value = 0 });
                }
                else if (trimmed == "min-content")
                {
                    sizes.Add(new GridTrackSize { Type = "min-content", Value = 0 });
                }
                else if (trimmed == "max-content")
                {
                    sizes.Add(new GridTrackSize { Type = "max-content", Value = 0 });
                }
                else if (trimmed.EndsWith("fr"))
                {
                    double fr;
                    if (double.TryParse(trimmed.Replace("fr", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out fr))
                    {
                        sizes.Add(new GridTrackSize { Type = "fr", Value = fr });
                    }
                }
                else if (trimmed.StartsWith("minmax("))
                {
                    // minmax(min, max) - use max for now
                    var match = Regex.Match(trimmed, @"minmax\(\s*([^,]+)\s*,\s*([^)]+)\s*\)");
                    if (match.Success)
                    {
                        var maxVal = match.Groups[2].Value.Trim();
                        if (maxVal.EndsWith("fr"))
                        {
                            double fr;
                            if (double.TryParse(maxVal.Replace("fr", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out fr))
                            {
                                sizes.Add(new GridTrackSize { Type = "fr", Value = fr });
                            }
                        }
                        else
                        {
                            sizes.Add(new GridTrackSize { Type = "px", Value = ParseCssLengthValue(maxVal, 100, null) });
                        }
                    }
                }
                else
                {
                    // Fixed size (px, %, em, etc.)
                    sizes.Add(new GridTrackSize { Type = "px", Value = ParseCssLengthValue(trimmed, 100, null) });
                }
            }

            return sizes.Count > 0 ? sizes : new List<GridTrackSize> { new GridTrackSize { Type = "auto", Value = 0 } };
        }

        private List<double> CalculateGridTrackSizes(List<GridTrackSize> sizes, double available, double gap, List<double> minConstraints = null, List<double> maxConstraints = null)
        {
            var result = new List<double>();
            double totalFixed = 0;
            double totalFr = 0;
            int autoCount = 0;

            // First pass: calculate fixed sizes and count fr/auto
            foreach (var size in sizes)
            {
                switch (size.Type)
                {
                    case "px":
                        totalFixed += size.Value;
                        result.Add(size.Value);
                        break;
                    case "fr":
                        totalFr += size.Value;
                        result.Add(0); // placeholder
                        break;
                    case "auto":
                    case "min-content":
                    case "max-content":
                        autoCount++;
                        result.Add(0); // placeholder
                        break;
                    default:
                        result.Add(100);
                        totalFixed += 100;
                        break;
                }
            }

            // Calculate remaining space after gaps
            double gapTotal = Math.Max(0, sizes.Count - 1) * gap;
            double remaining = available - totalFixed - gapTotal;

            // Distribute remaining space to fr and auto tracks
            if (remaining > 0)
            {
                if (totalFr > 0)
                {
                    double perFr = remaining / totalFr;
                    for (int i = 0; i < sizes.Count; i++)
                    {
                        if (sizes[i].Type == "fr")
                        {
                            result[i] = sizes[i].Value * perFr;
                        }
                    }
                    remaining = 0;
                }
                
                if (autoCount > 0 && remaining > 0)
                {
                    double perAuto = remaining / autoCount;
                    for (int i = 0; i < sizes.Count; i++)
                    {
                        if (sizes[i].Type == "auto" || sizes[i].Type == "min-content" || sizes[i].Type == "max-content")
                        {
                            result[i] = perAuto;
                        }
                    }
                }
            }

            // Apply min/max constraints
            for (int i = 0; i < result.Count; i++)
            {
                if (minConstraints != null && i < minConstraints.Count)
                {
                    result[i] = Math.Max(result[i], minConstraints[i]);
                }

                if (maxConstraints != null && i < maxConstraints.Count && maxConstraints[i] > 0)
                {
                    result[i] = Math.Min(result[i], maxConstraints[i]);
                }

                if (result[i] <= 0)
                {
                    result[i] = Math.Max(20, available / Math.Max(1, result.Count));
                }
            }

            return result;
        }

        private void ParseGridLine(string value, out int start, out int end, int maxLines)
        {
            start = 1;
            end = 2;

            if (string.IsNullOrWhiteSpace(value)) return;

            var parts = value.Split('/');
            if (parts.Length >= 1)
            {
                var startStr = parts[0].Trim();
                if (startStr.StartsWith("span", StringComparison.OrdinalIgnoreCase))
                {
                    int span;
                    if (int.TryParse(startStr.Substring(4).Trim(), out span))
                    {
                        end = start + span;
                    }
                }
                else
                {
                    int.TryParse(startStr, out start);
                    end = start + 1;
                }
            }

            if (parts.Length >= 2)
            {
                var endStr = parts[1].Trim();
                if (endStr.StartsWith("span", StringComparison.OrdinalIgnoreCase))
                {
                    int span;
                    if (int.TryParse(endStr.Substring(4).Trim(), out span))
                    {
                        end = start + span;
                    }
                }
                else
                {
                    int.TryParse(endStr, out end);
                }
            }
        }

        private bool IsTableContainer(Dictionary<string, string> styles)
        {
            if (styles == null)
            {
                return false;
            }

            string displayValue;
            if (styles.TryGetValue("display", out displayValue))
            {
                return string.Equals(displayValue, "table", StringComparison.OrdinalIgnoreCase) || string.Equals(displayValue, "table-row", StringComparison.OrdinalIgnoreCase) || string.Equals(displayValue, "table-cell", StringComparison.OrdinalIgnoreCase) || string.Equals(displayValue, "inline-table", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private DisplayType GetDisplayType(HtmlNode node)
        {
            if (node == null)
            {
                return DisplayType.Block;
            }

            if (string.Equals(node.Tag, "#text", StringComparison.OrdinalIgnoreCase))
            {
                return DisplayType.Inline;
            }

            switch (node.Tag)
            {
                case "span":
                case "a":
                case "b":
                case "i":
                case "em":
                case "strong":
                case "img":
                    return DisplayType.Inline;
                case "div":
                case "section":
                case "article":
                case "main":
                case "header":
                case "footer":
                case "nav":
                case "ul":
                case "ol":
                case "li":
                case "p":
                case "h1":
                case "h2":
                case "h3":
                    return DisplayType.Block;
                default:
                    return DisplayType.Block;
            }
        }

        private void MergeDefaults(string tag, Dictionary<string, string> styles)
        {
            if (styles == null)
            {
                return;
            }

            Dictionary<string, string> defaults = null;

            switch (tag)
            {
                case "body":
                    defaults = new Dictionary<string, string> { { "margin-top", "8px" }, { "margin-bottom", "8px" } };
                    break;
                case "p":
                    defaults = new Dictionary<string, string> { { "margin-top", "4px" }, { "margin-bottom", "8px" } };
                    break;
                case "h1":
                    defaults = new Dictionary<string, string> { { "height", "32px" }, { "margin-bottom", "12px" } };
                    break;
                case "h2":
                    defaults = new Dictionary<string, string> { { "height", "26px" }, { "margin-bottom", "10px" } };
                    break;
                case "h3":
                    defaults = new Dictionary<string, string> { { "height", "22px" }, { "margin-bottom", "8px" } };
                    break;
                case "li":
                    defaults = new Dictionary<string, string> { { "margin-bottom", "4px" } };
                    break;
                case "img":
                    defaults = new Dictionary<string, string> { { "width", "150px" }, { "height", "100px" }, { "object-fit", "fill" }, { "object-position", "50% 50%" } };
                    break;
                case "video":
                    defaults = new Dictionary<string, string> { { "width", "300px" }, { "height", "150px" }, { "object-fit", "contain" }, { "object-position", "50% 50%" } };
                    break;
                case "iframe":
                    defaults = new Dictionary<string, string> { { "width", "300px" }, { "height", "150px" }, { "object-fit", "fill" }, { "object-position", "50% 50%" } };
                    break;
                case "object":
                case "embed":
                    defaults = new Dictionary<string, string> { { "width", "300px" }, { "height", "150px" }, { "object-fit", "contain" }, { "object-position", "50% 50%" } };
                    break;
                case "input":
                    defaults = new Dictionary<string, string> { { "height", "24px" } };
                    break;
                case "button":
                    defaults = new Dictionary<string, string> { { "height", "28px" }, { "padding-top", "4px" }, { "padding-bottom", "4px" } };
                    break;
            }

            if (defaults == null)
            {
                return;
            }

            foreach (var kv in defaults)
            {
                if (!styles.ContainsKey(kv.Key))
                {
                    styles[kv.Key] = kv.Value;
                }
            }
        }

        private void Describe(RenderNode node, StringBuilder sb, int depth)
        {
            if (node == null || node.Box == null)
            {
                return;
            }

            sb.Append(new string(' ', depth * 2));
            sb.Append(node.Box.Tag + " @" + node.Box.Layout.ToString());

            // z-index / stacking context summary
            int z = GetZIndex(node);
            if (z != 0)
            {
                sb.Append(" z=" + z.ToString(CultureInfo.InvariantCulture));
            }

            // paint summary: background, border, text color if present
            var styles = node.Box.ComputedStyle;
            var paintParts = new List<string>();
            if (styles != null)
            {
                string bg;
                if (styles.TryGetValue("background-color", out bg))
                {
                    paintParts.Add("bg=" + bg);
                }

                var bgLayers = styles.Keys.Where(k => k.StartsWith("background-image", StringComparison.OrdinalIgnoreCase)).ToList();
                if (bgLayers.Count > 0)
                {
                    foreach (var layerKey in bgLayers.OrderBy(k => k))
                    {
                        var suffix = layerKey.Length > "background-image".Length ? layerKey.Substring("background-image".Length) : string.Empty;
                        string bgImg;
                        if (!styles.TryGetValue(layerKey, out bgImg))
                        {
                            continue;
                        }

                        string pos;
                        styles.TryGetValue("background-position" + suffix, out pos);
                        string rep;
                        styles.TryGetValue("background-repeat" + suffix, out rep);
                        string size;
                        styles.TryGetValue("background-size" + suffix, out size);
                        paintParts.Add("bgimg" + suffix + "=" + bgImg + (string.IsNullOrEmpty(pos) ? "" : "@" + pos) + (string.IsNullOrEmpty(size) ? "" : "/" + size) + (string.IsNullOrEmpty(rep) ? "" : "(" + rep + ")"));
                    }
                }

                string borderColor;
                string borderWidth;
                if (styles.TryGetValue("border-top-color", out borderColor) || styles.TryGetValue("border-color", out borderColor))
                {
                    styles.TryGetValue("border-top-width", out borderWidth);
                    paintParts.Add("border=" + (borderWidth ?? "0") + " " + borderColor);
                }

                string radiusTopLeft, radiusTopRight, radiusBottomRight, radiusBottomLeft;
                styles.TryGetValue("border-top-left-radius", out radiusTopLeft);
                styles.TryGetValue("border-top-right-radius", out radiusTopRight);
                styles.TryGetValue("border-bottom-right-radius", out radiusBottomRight);
                styles.TryGetValue("border-bottom-left-radius", out radiusBottomLeft);
                if (!string.IsNullOrEmpty(radiusTopLeft) || !string.IsNullOrEmpty(radiusTopRight) || !string.IsNullOrEmpty(radiusBottomRight) || !string.IsNullOrEmpty(radiusBottomLeft))
                {
                    paintParts.Add("radius=" + string.Join(" ", new[] { radiusTopLeft, radiusTopRight, radiusBottomRight, radiusBottomLeft }.Select(r => string.IsNullOrEmpty(r) ? "0" : r)));
                }

                string color;
                if (styles.TryGetValue("color", out color))
                {
                    paintParts.Add("text=" + color);
                }

                string opacity;
                if (styles.TryGetValue("opacity", out opacity))
                {
                    paintParts.Add("opacity=" + opacity);
                }

                string mixBlend;
                if (styles.TryGetValue("mix-blend-mode", out mixBlend))
                {
                    paintParts.Add("blend=" + mixBlend);
                }

                // box shadow
                var boxShadows = styles.Keys.Where(k => k.StartsWith("box-shadow", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k).ToList();
                if (boxShadows.Count > 0)
                {
                    foreach (var key in boxShadows)
                    {
                        string shadow;
                        if (styles.TryGetValue(key, out shadow) && !string.IsNullOrWhiteSpace(shadow) && !string.Equals(shadow, "none", StringComparison.OrdinalIgnoreCase))
                        {
                            paintParts.Add("box-shadow=" + shadow);
                        }
                    }
                }

                // text shadow
                string textShadow;
                if (styles.TryGetValue("text-shadow", out textShadow) && !string.IsNullOrWhiteSpace(textShadow) && !string.Equals(textShadow, "none", StringComparison.OrdinalIgnoreCase))
                {
                    paintParts.Add("text-shadow=" + textShadow);
                }

                string transform;
                if (styles.TryGetValue("transform", out transform) && !string.IsNullOrWhiteSpace(transform) && !string.Equals(transform, "none", StringComparison.OrdinalIgnoreCase))
                {
                    paintParts.Add("transform=" + transform);
                }

                string filter;
                if (styles.TryGetValue("filter", out filter) && !string.IsNullOrWhiteSpace(filter) && !string.Equals(filter, "none", StringComparison.OrdinalIgnoreCase))
                {
                    paintParts.Add("filter=" + filter);
                }

                string backdrop;
                if (styles.TryGetValue("backdrop-filter", out backdrop) && !string.IsNullOrWhiteSpace(backdrop) && !string.Equals(backdrop, "none", StringComparison.OrdinalIgnoreCase))
                {
                    paintParts.Add("backdrop=" + backdrop);
                }
            }

            if (paintParts.Count > 0)
            {
                sb.Append(" paint[");
                sb.Append(string.Join(",", paintParts));
                sb.Append("]");
            }

            string marker;
            if (styles != null && styles.TryGetValue("list-marker", out marker))
            {
                sb.Append(" marker=");
                sb.Append(marker);
            }

            sb.AppendLine();

            // z-index aware traversal
            var ordered = node.Children
                .Select((c, i) => new { Node = c, Order = i, Z = GetZIndex(c) })
                .OrderBy(x => x.Z)
                .ThenBy(x => x.Order)
                .Select(x => x.Node);

            foreach (var child in ordered)
            {
                Describe(child, sb, depth + 1);
            }
        }

        private int GetZIndex(RenderNode node)
        {
            if (node?.Box?.ComputedStyle == null)
            {
                return 0;
            }

            string z;
            if (node.Box.ComputedStyle.TryGetValue("z-index", out z))
            {
                int parsed;
                if (int.TryParse(z, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }

            return 0;
        }
    }

}
