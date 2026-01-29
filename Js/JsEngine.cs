using System;
using System.Collections.Generic;

namespace FawkesWeb
{
    public class JsEngine : IJsEngine
    {
        private IDomBridge _bridge;
        public IEventLoop EventLoop { get; } = new EventLoop();

        public void Execute(string script, JsContext context)
        {
            context.Logs.Add("Executed script length=" + (script ?? string.Empty).Length);
            if (_bridge != null && context != null)
            {
                context.Document = context.Document ?? new HtmlDocument();
                context.DomBridge = _bridge;
            }
        }

        public void RegisterDomBridge(IDomBridge bridge)
        {
            _bridge = bridge;
        }
    }

    public class JsContext
    {
        public HtmlDocument Document { get; set; }
        public List<string> Logs { get; set; } = new List<string>();
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
