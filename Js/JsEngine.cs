using System;
using System.Collections.Generic;
using System.Net;

namespace FawkesWeb
{
    public class JsEngine : IJsEngine
    {
        private IDomBridge _bridge;
        public IEventLoop EventLoop { get; } = new EventLoop();

        public void Execute(string script, JsContext context)
        {
            if (context == null)
            {
                return;
            }

            context.Document = context.Document ?? new HtmlDocument();
            context.DomBridge = context.DomBridge ?? _bridge;

            if (string.IsNullOrWhiteSpace(script))
            {
                return;
            }

            try
            {
                EvaluateScript(script, context);
            }
            catch (Exception ex)
            {
                context.Errors.Add(ex.Message);
            }
        }

        public void RegisterDomBridge(IDomBridge bridge)
        {
            _bridge = bridge;
        }

        private void EvaluateScript(string script, JsContext context)
        {
            // console.log / alert
            var logRegex = new System.Text.RegularExpressions.Regex(@"(console\.log|alert)\s*\(\s*['""](?<msg>[^'""]*)['""]\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in logRegex.Matches(script))
            {
                var msg = m.Groups["msg"].Value;
                context.Logs.Add(msg);
            }

            // document.write('text')
            var writeRegex = new System.Text.RegularExpressions.Regex(@"document\.write\s*\(\s*['""](?<content>[^'""]*)['""]\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in writeRegex.Matches(script))
            {
                var content = m.Groups["content"].Value;
                AppendToBody(context.Document, content);
                context.Logs.Add("document.write: " + content);
            }

            // setTimeout(function(){ ... }, delay) -> enqueue immediately (ignoring delay)
            var timeoutRegex = new System.Text.RegularExpressions.Regex(@"setTimeout\s*\(\s*function\s*\(\)\s*{(?<code>[^}]*)}\s*,\s*(?<ms>\d+)\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in timeoutRegex.Matches(script))
            {
                var code = m.Groups["code"].Value;
                EventLoop.Enqueue(() => EvaluateScript(code, context));
                context.Logs.Add("setTimeout scheduled: " + code.Trim());
            }
        }

        private void AppendToBody(HtmlDocument doc, string text)
        {
            if (doc == null)
            {
                return;
            }

            var sanitized = StripTags(text);
            if (string.IsNullOrEmpty(sanitized))
            {
                return;
            }

            var body = FindNode(doc.Root, "body") ?? doc.Root;
            if (body == null)
            {
                return;
            }

            var textNode = new HtmlNode { Tag = "#text", Text = sanitized, Parent = body };
            body.Children.Add(textNode);
        }

        private string StripTags(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            // remove HTML tags
            var withoutTags = System.Text.RegularExpressions.Regex.Replace(input, "<[^>]+>", string.Empty);
            // decode entities
            return WebUtility.HtmlDecode(withoutTags);
        }

        private HtmlNode FindNode(HtmlNode node, string tag)
        {
            if (node == null)
            {
                return null;
            }
            if (string.Equals(node.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
            foreach (var child in node.Children)
            {
                var found = FindNode(child, tag);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }

    public class JsContext
    {
        public HtmlDocument Document { get; set; }
        public List<string> Logs { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        public IDomBridge DomBridge { get; set; }
    }

    public class EventLoop : IEventLoop
    {
        private readonly Queue<Action> _queue = new Queue<Action>();

        public void Enqueue(Action work)
        {
            if (work != null)
            {
                _queue.Enqueue(work);
            }
        }

        public void Tick()
        {
            var count = _queue.Count;
            for (int i = 0; i < count; i++)
            {
                var work = _queue.Dequeue();
                work();
            }
        }
    }
}
