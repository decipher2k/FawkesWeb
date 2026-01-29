using System.Collections.Generic;

namespace FawkesWeb
{
    public class HtmlNode
    {
        public string Tag { get; set; }
        public string Text { get; set; }
        public List<HtmlNode> Children { get; set; } = new List<HtmlNode>();
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
        public HtmlNode Parent { get; set; }
    }
}
