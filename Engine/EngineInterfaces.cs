using System.Collections.Generic;

namespace FawkesWeb
{
    public interface IHtmlEngine
    {
        HtmlDocument Parse(string url, string html);
        RenderTree Layout(HtmlDocument document, CssEngine cssEngine);
        RenderResult Paint(RenderTree tree);
    }

    public interface ICssEngine
    {
        CssStyleSheet Parse(string cssText);
        CssCascadeResult Cascade(HtmlDocument document);
    }

    public interface IJsEngine
    {
        void Execute(string script, JsContext context);
        void RegisterDomBridge(IDomBridge bridge);
        IEventLoop EventLoop { get; }
    }

    public interface IDomBridge
    {
        void SetDocument(HtmlDocument document);
        HtmlNode GetElementById(string id);
        IEnumerable<HtmlNode> QuerySelectorAll(string selector);
    }

    public interface IEventLoop
    {
        void Enqueue(System.Action work);
        void Tick();
    }
}
