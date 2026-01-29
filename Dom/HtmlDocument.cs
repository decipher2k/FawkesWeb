using System;
using System.Collections.Generic;
using System.Linq;

namespace FawkesWeb
{
    public class HtmlDocument
    {
        public string Url { get; set; }
        public string BaseUrl { get; set; }
        public HtmlNode Root { get; set; }
        public CssStyleSheet StyleSheet { get; set; } = new CssStyleSheet();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public HtmlNode GetElementById(string id)
        {
            return FindByPredicate(Root, n => n.Attributes.ContainsKey("id") && string.Equals(n.Attributes["id"], id, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<HtmlNode> QuerySelectorAll(string selector)
        {
            var list = new List<HtmlNode>();
            Collect(Root, selector, list);
            return list;
        }

        private HtmlNode FindByPredicate(HtmlNode node, Func<HtmlNode, bool> predicate)
        {
            if (node == null)
            {
                return null;
            }

            if (predicate(node))
            {
                return node;
            }

            foreach (var child in node.Children)
            {
                var found = FindByPredicate(child, predicate);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void Collect(HtmlNode node, string selector, List<HtmlNode> list)
        {
            if (node == null || string.IsNullOrWhiteSpace(selector))
            {
                return;
            }

            if (CssEngineMatchesSelector(node, selector))
            {
                list.Add(node);
            }

            foreach (var child in node.Children)
            {
                Collect(child, selector, list);
            }
        }

        private bool CssEngineMatchesSelector(HtmlNode node, string selector)
        {
            selector = selector.Trim();

            if (selector.StartsWith("#", StringComparison.Ordinal))
            {
                var id = selector.Substring(1);
                return node.Attributes.ContainsKey("id") && string.Equals(node.Attributes["id"], id, StringComparison.OrdinalIgnoreCase);
            }

            if (selector.StartsWith(".", StringComparison.Ordinal))
            {
                var cls = selector.Substring(1);
                if (node.Attributes.ContainsKey("class"))
                {
                    var classes = node.Attributes["class"].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var c in classes)
                    {
                        if (string.Equals(c, cls, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            return string.Equals(node.Tag, selector, StringComparison.OrdinalIgnoreCase);
        }
    }
}
