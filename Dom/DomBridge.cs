namespace FawkesWeb
{
    public class DomBridge : IDomBridge
    {
        private HtmlDocument _document;

        public void SetDocument(HtmlDocument document)
        {
            _document = document;
        }

        public HtmlNode GetElementById(string id)
        {
            return _document?.GetElementById(id);
        }

        public System.Collections.Generic.IEnumerable<HtmlNode> QuerySelectorAll(string selector)
        {
            return _document?.QuerySelectorAll(selector) ?? System.Linq.Enumerable.Empty<HtmlNode>();
        }
    }
}
