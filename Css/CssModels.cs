using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FawkesWeb
{
    public class CssStyleSheet
    {
        public string Raw { get; set; }
        public List<CssRule> Rules { get; set; } = new List<CssRule>();
        public List<string> Imports { get; set; } = new List<string>();
        public List<CssFontFace> FontFaces { get; set; } = new List<CssFontFace>();
        public Dictionary<string, CssKeyframesRule> Keyframes { get; set; } = new Dictionary<string, CssKeyframesRule>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> CustomProperties { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public class CssFontFace
    {
        public string FontFamily { get; set; }
        public string Src { get; set; }
        public string FontWeight { get; set; } = "normal";
        public string FontStyle { get; set; } = "normal";
        public string FontDisplay { get; set; } = "auto";
    }

    public class CssKeyframesRule
    {
        public string Name { get; set; }
        public List<CssKeyframe> Keyframes { get; set; } = new List<CssKeyframe>();
    }

    public class CssKeyframe
    {
        public double Percentage { get; set; }
        public Dictionary<string, string> Declarations { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public class CssTransform
    {
        public string Function { get; set; }
        public List<double> Args { get; set; } = new List<double>();
    }

    public class CssTransition
    {
        public string Property { get; set; } = "all";
        public double Duration { get; set; } = 0;
        public string TimingFunction { get; set; } = "ease";
        public double Delay { get; set; } = 0;
    }

    public class CssAnimation
    {
        public string Name { get; set; }
        public double Duration { get; set; } = 0;
        public string TimingFunction { get; set; } = "ease";
        public double Delay { get; set; } = 0;
        public int IterationCount { get; set; } = 1;
        public string Direction { get; set; } = "normal";
        public string FillMode { get; set; } = "none";
        public string PlayState { get; set; } = "running";
    }

    public class CssBoxShadow
    {
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double BlurRadius { get; set; }
        public double SpreadRadius { get; set; }
        public string Color { get; set; } = "rgba(0,0,0,1)";
        public bool Inset { get; set; }
    }

    public class CssTextShadow
    {
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double BlurRadius { get; set; }
        public string Color { get; set; } = "rgba(0,0,0,1)";
    }

    public class CssFilter
    {
        public string Function { get; set; }
        public double Value { get; set; }
        public string Unit { get; set; }
    }

    public class CssGradient
    {
        public string Type { get; set; }
        public double Angle { get; set; } = 180;
        public string Direction { get; set; }
        public string Shape { get; set; }
        public List<CssColorStop> ColorStops { get; set; } = new List<CssColorStop>();
    }

    public class CssColorStop
    {
        public string Color { get; set; }
        public double Position { get; set; }
        public double PositionPx { get; set; }
        public bool HasPosition { get; set; }
    }

    public class CssCascadeResult
    {
        public int AppliedCount { get; set; }
        public Dictionary<HtmlNode, Dictionary<string, string>> Styles { get; set; } = new Dictionary<HtmlNode, Dictionary<string, string>>();
        internal Dictionary<HtmlNode, Dictionary<string, StyleEntry>> Weights { get; set; } = new Dictionary<HtmlNode, Dictionary<string, StyleEntry>>();
    }

    internal class StyleEntry
    {
        public string Value { get; set; }
        public int Specificity { get; set; }
        public bool Important { get; set; }
        public int Order { get; set; }
        public int LayerPriority { get; set; } = int.MaxValue;
    }

    public class CssRule
    {
        public string SelectorText { get; set; }
        public Dictionary<string, string> Declarations { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int Specificity { get; set; }
        public int SourceOrder { get; set; }
        public string MediaCondition { get; set; }
        public string PseudoElement { get; set; }
        public string LayerName { get; set; }
        public int LayerPriority { get; set; } = int.MaxValue;
    }

    public class CssClipPath
    {
        public string Type { get; set; } = "none";
        public string Url { get; set; }
        public List<string> Insets { get; set; } = new List<string>();
        public string BorderRadius { get; set; }
        public string Radius { get; set; }
        public string RadiusX { get; set; }
        public string RadiusY { get; set; }
        public string Position { get; set; }
        public List<(string X, string Y)> Points { get; set; } = new List<(string, string)>();
        public string FillRule { get; set; } = "nonzero";
        public string PathData { get; set; }
        public string Box { get; set; }
    }

    public class ContainerContext
    {
        public string Name { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string ContainerType { get; set; } = "normal";
    }

    public class ImageSetOption
    {
        public string Url { get; set; }
        public double Resolution { get; set; } = 1.0;
        public string Type { get; set; }
    }

    public class CssTextDecoration
    {
        public string Line { get; set; } = "none";
        public string Style { get; set; } = "solid";
        public string Color { get; set; } = "currentcolor";
        public string Thickness { get; set; } = "auto";
    }

    public class CssScrollSnap
    {
        public string Type { get; set; } = "none";
        public string Strictness { get; set; } = "proximity";
        public string Align { get; set; } = "none";
        public string Stop { get; set; } = "normal";
    }

    public class GridItem
    {
        public HtmlNode Node { get; set; }
        public int ColStart { get; set; }
        public int ColEnd { get; set; }
        public int RowStart { get; set; }
        public int RowEnd { get; set; }
    }

    public class GridTrackSize
    {
        public string Type { get; set; }
        public double Value { get; set; }
    }
}
