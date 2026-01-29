using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace FawkesWeb
{
    public class RenderTree
    {
        public RenderNode Root { get; set; }
    }

    public class RenderNode
    {
        public Box Box { get; set; }
        public List<RenderNode> Children { get; set; } = new List<RenderNode>();
    }

    public class Box
    {
        public string Tag { get; set; }
        public Rect Layout { get; set; }
        public Dictionary<string, string> ComputedStyle { get; set; } = new Dictionary<string, string>();
    }

    public enum DisplayType
    {
        Block,
        Inline,
        InlineBlock,
        InlineTable
    }

    public class RenderResult
    {
        public string Description { get; set; }
        public ImageSource Image { get; set; }
    }
}
