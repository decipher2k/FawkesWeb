using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Net;
using System.Globalization;

namespace FawkesWeb
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _torConnected;
        private DispatcherTimer _refreshTimer;
        private readonly List<string> _rssCache = new List<string>();
        private readonly List<string> _calendarCache = new List<string>();
        private readonly List<string> _mailCache = new List<string>();
        private ListBox _rssList;
        private ListBox _calendarList;
        private ListBox _mailList;

        public MainWindow()
        {
            InitializeComponent();
            InitializeShell();
            InitializeDataCaches();
        }

        private void InitializeShell()
        {
            if (BrowserTabs != null)
            {
                BrowserTabs.Items.Clear();
                BrowserTabs.Items.Add(CreateStartTab());
            }

            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Interval = TimeSpan.FromMinutes(5);
            _refreshTimer.Tick += RefreshCaches;
            _refreshTimer.Start();
        }

        private void InitializeDataCaches()
        {
            RefreshCaches(this, EventArgs.Empty);
        }

        private TabItem CreateStartTab()
        {
            var root = new Grid { Margin = new Thickness(16) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _rssList = CreateListSection(root, 0, "RSS Reader (cached)");
            _calendarList = CreateListSection(root, 1, "Calendar (iCal preview)");
            _mailList = CreateListSection(root, 2, "IMAP Inbox (preview)");

            UpdateStartPageViews();

            var tab = new TabItem { Header = "Start" };
            tab.Content = new ScrollViewer { Content = root };
            return tab;
        }

        private ListBox CreateListSection(Grid root, int columnIndex, string header)
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };
            Grid.SetColumn(stack, columnIndex);

            stack.Children.Add(new TextBlock
            {
                Text = header,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var list = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
                Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                BorderThickness = new Thickness(1),
                Height = 400
            };

            stack.Children.Add(list);
            root.Children.Add(stack);
            return list;
        }

        private void RefreshCaches(object sender, EventArgs e)
        {
            _rssCache.Clear();
            _calendarCache.Clear();
            _mailCache.Clear();

            var timestamp = DateTime.Now.ToString("T");

            var settings = AppSettings.Current;
            if (settings.RssFeeds.Any())
            {
                foreach (var feed in settings.RssFeeds.Take(3))
                {
                    _rssCache.Add("Feed from " + feed + " refreshed at " + timestamp);
                }
            }
            else
            {
                for (int i = 1; i <= 5; i++)
                {
                    _rssCache.Add("Feed item " + i + " updated at " + timestamp);
                }
            }

            if (!string.IsNullOrWhiteSpace(settings.ICalUrl))
            {
                _calendarCache.Add("Calendar refreshed from " + settings.ICalUrl + " at " + timestamp);
            }
            else
            {
                for (int i = 1; i <= 5; i++)
                {
                    _calendarCache.Add("Event " + i + " refreshed at " + timestamp);
                }
            }

            if (!string.IsNullOrWhiteSpace(settings.ImapServer))
            {
                _mailCache.Add("Mail synced from " + settings.ImapServer + " at " + timestamp);
            }
            else
            {
                for (int i = 1; i <= 5; i++)
                {
                    _mailCache.Add("Mail preview " + i + " fetched at " + timestamp);
                }
            }

            UpdateStartPageViews();
        }

        private void UpdateStartPageViews()
        {
            if (_rssList != null)
            {
                _rssList.ItemsSource = null;
                _rssList.ItemsSource = _rssCache.Take(20).ToList();
            }

            if (_calendarList != null)
            {
                _calendarList.ItemsSource = null;
                _calendarList.ItemsSource = _calendarCache;
            }

            if (_mailList != null)
            {
                _mailList.ItemsSource = null;
                _mailList.ItemsSource = _mailCache;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleWindowState();
                return;
            }

            DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
        {
            ToggleWindowState();
        }

        private void ToggleWindowState()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                if (MaxButton != null)
                {
                    MaxButton.Content = "☐";
                }
            }
            else
            {
                WindowState = WindowState.Maximized;
                if (MaxButton != null)
                {
                    MaxButton.Content = "❐";
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            AddressBar.Focus();
        }

        private void Navigate_Click(object sender, RoutedEventArgs e)
        {
            OpenPage(AddressBar.Text, (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control);
        }

        private void OpenPage(string address, bool background)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            var url = NormalizeUrl(address);
            var host = ExtractHost(url);

            if (AppSettings.Current.TorEnforce && !_torConnected)
            {
                ConnectTor(enforce: true);
                if (!_torConnected)
                {
                    MessageBox.Show("Navigation blocked until TOR connection is active.", "FakewsWeb", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (AppSettings.Current.IsDomainBlocked(host))
            {
                MessageBox.Show("This domain is blocked by policy.", "FakewsWeb", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            string html = string.Empty;
            try
            {
                using (var client = new WebClient())
                {
                    if (!string.IsNullOrWhiteSpace(AppSettings.Current.UserAgent))
                    {
                        client.Headers[HttpRequestHeader.UserAgent] = AppSettings.Current.UserAgent;
                    }

                    if (AppSettings.Current.NoReferrer)
                    {
                        client.Headers.Remove("Referer");
                    }

                    html = client.DownloadString(url);
                }
            }
            catch (Exception ex)
            {
                html = "<!-- failed to fetch -->" + ex.Message;
            }

            var engine = new HtmlEngine();
            var document = engine.Parse(url, html);

            var cssBuilder = new StringBuilder();
            var inlineCss = ExtractInlineCss(html);
            if (!AppSettings.Current.IsCssBlocked(inlineCss))
            {
                cssBuilder.AppendLine(inlineCss);
            }

            var baseUrl = document.BaseUrl ?? url;

            foreach (var cssLink in ExtractExternalStyleLinks(html))
            {
                var resolved = ResolveUrl(baseUrl, cssLink);
                if (IsResourceHostBlocked(resolved))
                {
                    continue;
                }

                var cssContent = FetchResource(resolved);
                if (!AppSettings.Current.IsCssBlocked(cssContent))
                {
                    cssBuilder.AppendLine(cssContent);
                }
            }

            document.StyleSheet = engine.CssEngine.Parse(cssBuilder.ToString());

            var domBridge = new DomBridge();
            domBridge.SetDocument(document);
            engine.JsEngine.RegisterDomBridge(domBridge);

            var scripts = new List<string>();
            scripts.AddRange(ExtractInlineScripts(html));

            foreach (var scriptLink in ExtractExternalScriptLinks(html))
            {
                var resolved = ResolveUrl(baseUrl, scriptLink);
                if (IsResourceHostBlocked(resolved))
                {
                    continue;
                }

                var js = FetchResource(resolved);
                scripts.Add(js);
            }

            var jsContext = new JsContext { Document = document };
            foreach (var script in scripts)
            {
                if (!AppSettings.Current.IsJsBlocked(script))
                {
                    engine.JsEngine.Execute(script, jsContext);
                }
            }

            var renderTree = engine.Layout(document, engine.CssEngine);
            var render = engine.Paint(renderTree);

            var tab = new TabItem { Header = address };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var summary = new TextBlock
            {
                Text = "Rendered description:\n" + render.Description,
                Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var rawView = new TextBox
            {
                Text = html,
                Background = new SolidColorBrush(Color.FromRgb(12, 18, 31)),
                Foreground = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
                AcceptsReturn = true,
                AcceptsTab = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            Grid.SetRow(summary, 0);
            Grid.SetRow(rawView, 1);
            grid.Children.Add(summary);
            grid.Children.Add(rawView);

            tab.Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                Padding = new Thickness(16),
                Child = grid
            };

            BrowserTabs.Items.Add(tab);

            if (!background)
            {
                BrowserTabs.SelectedItem = tab;
            }
        }

        private void AllowCookies_Click(object sender, RoutedEventArgs e)
        {
            var host = ExtractHost(AddressBar.Text);
            AppSettings.Current.AllowCookiesForHost(host);
            MessageBox.Show("Cookies allowed for " + host + " (in-memory).", "FakewsWeb", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void TorConnect_Click(object sender, RoutedEventArgs e)
        {
            ConnectTor(enforce: false);
        }

        private void ConnectTor(bool enforce)
        {
            _torConnected = !_torConnected;
            if (TorButton != null)
            {
                TorButton.Content = _torConnected ? "Connected to TOR" : "Connect to TOR network";
            }

            if (!_torConnected && enforce && AppSettings.Current.TorReconnect)
            {
                _torConnected = true;
                TorButton.Content = "Connected to TOR";
            }

            MessageBox.Show(_torConnected ? "TOR connection established (stub)." : "TOR disconnected (stub).", "FakewsWeb", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SummarizeCurrent_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("AI summarize current tab (placeholder).", "FakewsWeb", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SummarizeAll_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("AI summarize all tabs (placeholder).", "FakewsWeb", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Speak_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Text-to-Speech playback started (stub).", "FakewsWeb", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void NewTab_Click(object sender, RoutedEventArgs e)
        {
            var tab = CreateStartTab();
            BrowserTabs.Items.Add(tab);
            BrowserTabs.SelectedItem = tab;
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsWindow();
            settings.Owner = this;
            settings.ShowDialog();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (AppSettings.Current.PauseOnBlur)
            {
                PauseActiveScripts();
                ReleasePrefetchedMedia();
            }
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            if (AppSettings.Current.PauseOnBlur)
            {
                ResumeActiveScripts();
                ReloadPrefetchedMedia();
            }
        }

        private void PauseActiveScripts()
        {
            // Placeholder: pause JavaScript execution when losing focus.
        }

        private void ResumeActiveScripts()
        {
            // Placeholder: resume JavaScript when focus returns.
        }

        private void ReleasePrefetchedMedia()
        {
            // Placeholder: clear prefetched images/pages when leaving tab or window focus.
        }

        private void ReloadPrefetchedMedia()
        {
            // Placeholder: reload prefetched images/pages on focus.
        }

        private string ExtractInlineCss(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            var styleRegex = new Regex("<style[^>]*>(?<css>[\\s\\S]*?)</style>", RegexOptions.IgnoreCase);
            foreach (Match m in styleRegex.Matches(html))
            {
                sb.AppendLine(m.Groups["css"].Value);
            }

            return sb.ToString();
        }

        private List<string> ExtractInlineScripts(string html)
        {
            var scripts = new List<string>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return scripts;
            }

            var scriptRegex = new Regex("<script[^>]*>(?<js>[\\s\\S]*?)</script>", RegexOptions.IgnoreCase);
            foreach (Match m in scriptRegex.Matches(html))
            {
                scripts.Add(m.Groups["js"].Value);
            }

            return scripts;
        }

        private List<string> ExtractExternalStyleLinks(string html)
        {
            var links = new List<string>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return links;
            }

            var linkRegex = new Regex("<link[^>]*rel=\"?stylesheet\"?[^>]*href=\"(?<href>[^\"]+)\"[^>]*>", RegexOptions.IgnoreCase);
            foreach (Match m in linkRegex.Matches(html))
            {
                links.Add(m.Groups["href"].Value);
            }

            return links;
        }

        private List<string> ExtractExternalScriptLinks(string html)
        {
            var links = new List<string>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return links;
            }

            var scriptRegex = new Regex("<script[^>]*src=\"(?<src>[^\"]+)\"[^>]*>", RegexOptions.IgnoreCase);
            foreach (Match m in scriptRegex.Matches(html))
            {
                links.Add(m.Groups["src"].Value);
            }

            return links;
        }

        private string FetchResource(string resourceUrl)
        {
            if (string.IsNullOrWhiteSpace(resourceUrl))
            {
                return string.Empty;
            }

            try
            {
                using (var client = new WebClient())
                {
                    if (!string.IsNullOrWhiteSpace(AppSettings.Current.UserAgent))
                    {
                        client.Headers[HttpRequestHeader.UserAgent] = AppSettings.Current.UserAgent;
                    }

                    if (AppSettings.Current.NoReferrer)
                    {
                        client.Headers.Remove("Referer");
                    }

                    return client.DownloadString(resourceUrl);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ResolveUrl(string baseUrl, string resourceUrl)
        {
            if (string.IsNullOrWhiteSpace(resourceUrl))
            {
                return string.Empty;
            }

            if (resourceUrl.StartsWith("//", StringComparison.Ordinal))
            {
                var baseUri = new Uri(baseUrl);
                return baseUri.Scheme + ":" + resourceUrl;
            }

            Uri absolute;
            if (Uri.TryCreate(resourceUrl, UriKind.Absolute, out absolute))
            {
                return absolute.ToString();
            }

            Uri resolved;
            if (Uri.TryCreate(new Uri(baseUrl), resourceUrl, out resolved))
            {
                return resolved.ToString();
            }

            return resourceUrl;
        }

        private bool IsResourceHostBlocked(string resourceUrl)
        {
            var host = ExtractHost(resourceUrl);
            return AppSettings.Current.IsDomainBlocked(host);
        }

        private string NormalizeUrl(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return address;
            }

            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri))
            {
                Uri.TryCreate("http://" + address, UriKind.Absolute, out uri);
            }

            return uri != null ? uri.ToString() : address;
        }

        private string ExtractHost(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return string.Empty;
            }

            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri))
            {
                Uri.TryCreate("http://" + address, UriKind.Absolute, out uri);
            }

            return uri != null ? uri.Host : string.Empty;
        }
    }

    public class AppSettings
    {
        public static AppSettings Current { get; } = new AppSettings();

        public bool TorEnforce { get; set; }
        public bool TorReconnect { get; set; }
        public bool NoReferrer { get; set; } = true;
        public string UserAgent { get; set; } = "FakewsWeb/0.1";
        public bool FingerprintProtection { get; set; }
        public bool PauseOnBlur { get; set; }
        public bool AutoRejectCookies { get; set; }
        public bool AutoRead { get; set; }
        public bool EnableDrm { get; set; }
        public bool DisableWebGl { get; set; } = true;
        public string AiEndpoint { get; set; } = "https://api.example.com/v1/chat/completions";
        public string AiToken { get; set; }
        public string AiModel { get; set; } = "gpt-4";
        public List<string> BlockedDomains { get; set; } = new List<string>();
        public List<string> BlockedJs { get; set; } = new List<string>();
        public List<string> BlockedCss { get; set; } = new List<string>();
        public List<string> CookieAllowList { get; set; } = new List<string>();
        public List<string> RssFeeds { get; set; } = new List<string>();
        public string ICalUrl { get; set; }
        public string ImapServer { get; set; }
        public string ImapUser { get; set; }
        public string ImapPassword { get; set; }

        public bool IsJsBlocked(string content)
        {
            return MatchesPatterns(BlockedJs, content);
        }

        public bool IsCssBlocked(string content)
        {
            return MatchesPatterns(BlockedCss, content);
        }

        public bool IsDomainBlocked(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            return MatchesPatterns(BlockedDomains, host);
        }

        public bool IsCookieAllowed(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            foreach (var pattern in CookieAllowList)
            {
                var trimmed = pattern.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                if (trimmed.StartsWith("*.", StringComparison.Ordinal))
                {
                    var suffix = trimmed.Substring(2);
                    if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                else if (string.Equals(host, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public void AllowCookiesForHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return;
            }

            if (!CookieAllowList.Any(x => string.Equals(x.Trim(), host, StringComparison.OrdinalIgnoreCase)))
            {
                CookieAllowList.Add(host);
            }
        }

        private static bool MatchesPatterns(IEnumerable<string> patterns, string text)
        {
            foreach (var pattern in patterns)
            {
                var p = pattern?.Trim();
                if (string.IsNullOrEmpty(p))
                {
                    continue;
                }

                if (text.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                try
                {
                    if (Regex.IsMatch(text, p, RegexOptions.IgnoreCase))
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                    // ignore invalid regex entries
                }
            }

            return false;
        }
    }

    // Placeholder engine scaffolding (non-functional)
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
        void Enqueue(Action work);
        void Tick();
    }

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
            return new RenderResult { Description = sb.ToString() };
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
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            raw = raw.Trim().ToLowerInvariant();

            if (raw == "auto")
            {
                return fallback;
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
            else if (raw.EndsWith("em") || raw.EndsWith("rem"))
            {
                if (double.TryParse(raw.Replace("em", string.Empty).Replace("rem", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return numeric * 16.0;
                }
            }
            else if (raw.EndsWith("vw"))
            {
                if (double.TryParse(raw.Replace("vw", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return 1024.0 * (numeric / 100.0);
                }
            }
            else if (raw.EndsWith("vh"))
            {
                if (double.TryParse(raw.Replace("vh", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return 768.0 * (numeric / 100.0);
                }
            }
            else if (raw.EndsWith("vmin"))
            {
                if (double.TryParse(raw.Replace("vmin", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    var minSide = Math.Min(1024.0, 768.0);
                    return minSide * (numeric / 100.0);
                }
            }
            else if (raw.EndsWith("vmax"))
            {
                if (double.TryParse(raw.Replace("vmax", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    var maxSide = Math.Max(1024.0, 768.0);
                    return maxSide * (numeric / 100.0);
                }
            }
            else if (raw.EndsWith("cm"))
            {
                if (double.TryParse(raw.Replace("cm", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return numeric * (96.0 / 2.54);
                }
            }
            else if (raw.EndsWith("mm"))
            {
                if (double.TryParse(raw.Replace("mm", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return numeric * (96.0 / 25.4);
                }
            }
            else if (raw.EndsWith("in"))
            {
                if (double.TryParse(raw.Replace("in", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return numeric * 96.0;
                }
            }
            else if (raw.EndsWith("pt"))
            {
                if (double.TryParse(raw.Replace("pt", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return numeric * (96.0 / 72.0);
                }
            }
            else if (raw.EndsWith("pc"))
            {
                if (double.TryParse(raw.Replace("pc", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return numeric * 16.0; // 1pc = 12pt = 16px
                }
            }
            else if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
            {
                return numeric;
            }

            return fallback;
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

            if (IsTableContainer(styles))
            {
                BuildTableLayout(node, renderNode, ref y, styles, cascade, parentComputed);
                return;
            }

            if (IsHidden(styles))
            {
                return;
            }

            double width = ParseCssLength(styles, "width", 800);
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
            string overflowX = styles.ContainsKey("overflow-x") ? styles["overflow-x"].ToLowerInvariant() : overflow;
            string overflowY = styles.ContainsKey("overflow-y") ? styles["overflow-y"].ToLowerInvariant() : overflow;
            if (string.Equals(node.Tag, "li", StringComparison.OrdinalIgnoreCase))
            {
                string listPos;
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
                if (!string.IsNullOrEmpty(listImage) && !string.Equals(listImage, "none", StringComparison.OrdinalIgnoreCase))
                {
                    listMarker = "img:" + listImage;
                }
                else
                {
                    int idx = node.Parent != null ? node.Parent.Children.IndexOf(node) + 1 : 1;
                    switch ((listType ?? string.Empty).ToLowerInvariant())
                    {
                        case "decimal":
                            listMarker = idx.ToString(CultureInfo.InvariantCulture) + ".";
                            break;
                        case "circle":
                            listMarker = "○";
                            break;
                        case "square":
                            listMarker = "■";
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

                x += listIndent;
            }

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
                y += marginTop;
            }

            height += paddingTop + paddingBottom + borderTop + borderBottom;

            var contentWidth = Math.Max(0, width - paddingLeft - paddingRight - borderLeft - borderRight - marginLeft - marginRight - listIndent);

            renderNode.Box = new Box
            {
                Tag = node.Tag,
                Layout = new Rect(x, y, contentWidth, height),
                ComputedStyle = new Dictionary<string, string>(styles)
            };

            if (!string.IsNullOrEmpty(listMarker))
            {
                renderNode.Box.ComputedStyle["list-marker"] = listMarker;
            }

            // relative positioning offsets (does not affect flow)
            if (string.Equals(position, "relative", StringComparison.OrdinalIgnoreCase))
            {
                double leftOffset = styles.ContainsKey("left") ? ParseCssLength(styles, "left", 0) : 0;
                double topOffset = styles.ContainsKey("top") ? ParseCssLength(styles, "top", 0) : 0;
                var l = renderNode.Box.Layout;
                renderNode.Box.Layout = new Rect(l.X + leftOffset, l.Y + topOffset, l.Width, l.Height);
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
                var pseudoNode = new RenderNode
                {
                    Box = new Box
                    {
                        Tag = "::before",
                        Layout = new Rect(x, childY, Math.Max(10, beforeContent.Length * 7), 16),
                        ComputedStyle = styles.Where(k => k.Key.StartsWith("before::", StringComparison.OrdinalIgnoreCase)).ToDictionary(k => k.Key.Substring("before::".Length), k => k.Value, StringComparer.OrdinalIgnoreCase)
                    }
                };
                renderNode.Children.Add(pseudoNode);
                childY = pseudoNode.Box.Layout.Bottom;
                maxChildY = Math.Max(maxChildY, childY);
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
                // hide children outside bounds by adjusting height reporting; no actual clipping drawing
            }
            else if (string.Equals(overflow, "scroll", StringComparison.OrdinalIgnoreCase) || string.Equals(overflowX, "scroll", StringComparison.OrdinalIgnoreCase) || string.Equals(overflowY, "scroll", StringComparison.OrdinalIgnoreCase) || string.Equals(overflow, "auto", StringComparison.OrdinalIgnoreCase))
            {
                var layout = renderNode.Box.Layout;
                // compute needed height; use current height but note scrollable content extent
                double contentExtent = maxChildY - y;
                renderNode.Box.Layout = new Rect(layout.X, layout.Y, layout.Width, height);
                renderNode.Box.ComputedStyle["scroll-height"] = contentExtent.ToString(CultureInfo.InvariantCulture);
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
            string whiteSpace;
            styles.TryGetValue("white-space", out whiteSpace);
            whiteSpace = (whiteSpace ?? string.Empty).ToLowerInvariant();
            bool preserve = whiteSpace == "pre";
            bool noWrap = whiteSpace == "nowrap";

            var lineChildren = new List<RenderNode>();

            // before pseudo inline
            string inlineBefore;
            if (styles.TryGetValue("before::content", out inlineBefore))
            {
                var textRender = new RenderNode
                {
                    Box = new Box
                    {
                        Tag = "::before",
                        Layout = new Rect(cursorX, currentLineY, Math.Max(7, inlineBefore.Length * 7), lineHeight),
                        ComputedStyle = styles.Where(k => k.Key.StartsWith("before::", StringComparison.OrdinalIgnoreCase)).ToDictionary(k => k.Key.Substring("before::".Length), k => k.Value, StringComparer.OrdinalIgnoreCase)
                    }
                };
                lineChildren.Add(textRender);
                cursorX += textRender.Box.Layout.Width;
            }

            foreach (var child in node.Children)
            {
                if (string.Equals(child.Tag, "br", StringComparison.OrdinalIgnoreCase))
                {
                    cursorX = marginLeft + borderLeft + paddingLeft;
                    currentLineY += maxLineHeight;
                    maxLineHeight = lineHeight;
                    continue;
                }

                if (string.Equals(child.Tag, "#text", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = child.Text ?? string.Empty;
                    string textContent = preserve ? raw : CollapseWhitespace(raw);
                    if (string.IsNullOrEmpty(textContent))
                    {
                        continue;
                    }

                    var lines = preserve ? textContent.Split(new[] { '\n' }) : new[] { textContent };
                    for (int li = 0; li < lines.Length; li++)
                    {
                        var line = lines[li];
                        var tokens = preserve ? new[] { line } : line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var token in tokens)
                        {
                            double tokenWidth = Math.Max(7, token.Length * 7);
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

                            lineChildren.Add(textRender);
                            cursorX += tokenWidth + (preserve ? 0 : 7); // simple space width when collapsing
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

                    childBox.Layout = new Rect(cursorX, currentLineY, childWidth, Math.Max(childBox.Layout.Height, lineHeight));
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

            renderNode.Children.AddRange(lineChildren);
            // inline ::after
            string inlineAfter;
            if (styles.TryGetValue("after::content", out inlineAfter))
            {
                var afterRender = new RenderNode
                {
                    Box = new Box
                    {
                        Tag = "::after",
                        Layout = new Rect(cursorX, currentLineY, Math.Max(7, inlineAfter.Length * 7), lineHeight),
                        ComputedStyle = styles.Where(k => k.Key.StartsWith("after::", StringComparison.OrdinalIgnoreCase)).ToDictionary(k => k.Key.Substring("after::".Length), k => k.Value, StringComparer.OrdinalIgnoreCase)
                    }
                };
                renderNode.Children.Add(afterRender);
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

            double colWidth = totalCols > 0 ? availableWidth / totalCols : availableWidth;
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

                    double cellX = x + colCursor * colWidth;
                    double cellWidth = colWidth * Math.Max(1, colspan);

                    var cellRender = new RenderNode();
                    double cellY = rowY;
                    BuildRenderTree(cell, cellRender, ref cellY, cascade, styles);
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
                rowRender.Box = new Box
                {
                    Tag = "tr",
                    Layout = new Rect(x, rowY, availableWidth, rowHeight),
                    ComputedStyle = new Dictionary<string, string>(styles)
                };

                rowY += rowHeight;
            }

            double tableHeight = rowY - y + paddingBottom;
            renderNode.Box = new Box
            {
                Tag = node.Tag,
                Layout = new Rect(x, y, availableWidth, tableHeight),
                ComputedStyle = new Dictionary<string, string>(styles)
            };

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
                    defaults = new Dictionary<string, string> { { "width", "150px" }, { "height", "100px" } };
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

    public class CssEngine : ICssEngine
    {
        private static readonly HashSet<string> NamedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "black","silver","gray","white","maroon","red","purple","fuchsia","green","lime","olive","yellow","navy","blue","teal","aqua",
            "orange","aliceblue","antiquewhite","aquamarine","azure","beige","bisque","blanchedalmond","blueviolet","brown","burlywood","cadetblue","chartreuse","chocolate",
            "coral","cornflowerblue","cornsilk","crimson","cyan","darkblue","darkcyan","darkgoldenrod","darkgray","darkgreen","darkgrey","darkkhaki","darkmagenta","darkolivegreen",
            "darkorange","darkorchid","darkred","darksalmon","darkseagreen","darkslateblue","darkslategray","darkslategrey","darkturquoise","darkviolet","deeppink","deepskyblue",
            "dimgray","dimgrey","dodgerblue","firebrick","floralwhite","forestgreen","gainsboro","ghostwhite","gold","goldenrod","greenyellow","grey","honeydew","hotpink",
            "indianred","indigo","ivory","khaki","lavender","lavenderblush","lawngreen","lemonchiffon","lightblue","lightcoral","lightcyan","lightgoldenrodyellow","lightgray",
            "lightgreen","lightgrey","lightpink","lightsalmon","lightseagreen","lightskyblue","lightslategray","lightslategrey","lightsteelblue","lightyellow","limegreen","linen",
            "magenta","mediumaquamarine","mediumblue","mediumorchid","mediumpurple","mediumseagreen","mediumslateblue","mediumspringgreen","mediumturquoise","mediumvioletred",
            "midnightblue","mintcream","mistyrose","moccasin","navajowhite","oldlace","olivedrab","orangered","orchid","palegoldenrod","palegreen","paleturquoise","palevioletred",
            "papayawhip","peachpuff","peru","pink","plum","powderblue","rosybrown","royalblue","saddlebrown","salmon","sandybrown","seagreen","seashell","sienna","skyblue",
            "slateblue","slategray","slategrey","snow","springgreen","steelblue","tan","thistle","tomato","turquoise","violet","wheat","whitesmoke","yellowgreen"
        };

        public CssStyleSheet Parse(string cssText)
        {
            var sheet = new CssStyleSheet { Raw = cssText };
            if (string.IsNullOrWhiteSpace(cssText))
            {
                return sheet;
            }

            cssText = Regex.Replace(cssText, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

            int order = 0;

            // handle simple @media blocks
            var mediaRegex = new Regex(@"@media\s*(?<cond>[^\{]+)\{(?<body>.*?)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in mediaRegex.Matches(cssText))
            {
                var cond = m.Groups["cond"].Value.Trim();
                var body = m.Groups["body"].Value;
                ParseRuleBlocks(sheet, body, cond, ref order);
            }

            // remove media blocks processed
            cssText = mediaRegex.Replace(cssText, string.Empty);

            // top-level rules
            ParseRuleBlocks(sheet, cssText, null, ref order);

            return sheet;
        }

        private void ParseRuleBlocks(CssStyleSheet sheet, string css, string mediaCondition, ref int order)
        {
            var blocks = css.Split(new[] { '}' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                var parts = block.Split(new[] { '{' }, 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var selectorText = parts[0].Trim();
                var decls = parts[1];

                var selectors = selectorText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var sel in selectors)
                {
                    var trimmedSel = sel.Trim();
                    if (string.IsNullOrEmpty(trimmedSel))
                    {
                        continue;
                    }

                    string pseudoElement = null;
                    if (trimmedSel.EndsWith("::before", StringComparison.OrdinalIgnoreCase))
                    {
                        pseudoElement = "before";
                        trimmedSel = trimmedSel.Substring(0, trimmedSel.Length - "::before".Length).Trim();
                    }
                    else if (trimmedSel.EndsWith("::after", StringComparison.OrdinalIgnoreCase))
                    {
                        pseudoElement = "after";
                        trimmedSel = trimmedSel.Substring(0, trimmedSel.Length - "::after".Length).Trim();
                    }

                    var rule = new CssRule
                    {
                        SelectorText = trimmedSel,
                        SourceOrder = order++,
                        Specificity = CalculateSpecificity(trimmedSel),
                        MediaCondition = mediaCondition,
                        PseudoElement = pseudoElement
                    };
                    MergeDeclarations(rule.Declarations, decls);
                    sheet.Rules.Add(rule);
                }
            }
        }

        private const double MediaViewportWidth = 1024.0;

        public CssCascadeResult Cascade(HtmlDocument document)
        {
            var result = new CssCascadeResult();
            if (document?.Root == null)
            {
                return result;
            }

            ApplyRecursive(document.Root, result, document.StyleSheet);
            return result;
        }

        private void ApplyRecursive(HtmlNode node, CssCascadeResult result, CssStyleSheet sheet)
        {
            if (node == null)
            {
                return;
            }

            EnsureStyleBucket(node, result);

            foreach (var rule in sheet?.Rules ?? new List<CssRule>())
            {
                if (!string.IsNullOrEmpty(rule.MediaCondition) && !EvaluateMedia(rule.MediaCondition))
                {
                    continue;
                }

                if (MatchesSelector(node, rule.SelectorText))
                {
                    foreach (var kv in rule.Declarations)
                    {
                        if (!string.IsNullOrEmpty(rule.PseudoElement))
                        {
                            ApplyProperty(result, node, rule.PseudoElement + "::" + kv.Key, kv.Value, rule.Specificity, rule.SourceOrder);
                        }
                        else
                        {
                            ApplyProperty(result, node, kv.Key, kv.Value, rule.Specificity, rule.SourceOrder);
                        }
                    }
                }
            }

            if (node.Attributes.ContainsKey("style"))
            {
                MergeDeclarations(result.Styles[node], node.Attributes["style"], result.Weights[node], inlineSpecificity: 1000, inlineOrder: int.MaxValue);
            }

            foreach (var child in node.Children)
            {
                ApplyRecursive(child, result, sheet);
            }
        }

        private void EnsureStyleBucket(HtmlNode node, CssCascadeResult result)
        {
            if (!result.Styles.ContainsKey(node))
            {
                result.Styles[node] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            if (!result.Weights.ContainsKey(node))
            {
                result.Weights[node] = new Dictionary<string, StyleEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private bool EvaluateMedia(string condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
            {
                return true;
            }

            var parts = condition.Split(new[] { "and" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var trimmed = part.Trim().Trim('(', ')');
                if (trimmed.StartsWith("min-width", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = trimmed.IndexOf(':');
                    if (idx > 0)
                    {
                        var val = trimmed.Substring(idx + 1).Trim();
                        var px = ParseMediaLength(val, MediaViewportWidth);
                        if (MediaViewportWidth < px)
                        {
                            return false;
                        }
                    }
                }
                else if (trimmed.StartsWith("max-width", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = trimmed.IndexOf(':');
                    if (idx > 0)
                    {
                        var val = trimmed.Substring(idx + 1).Trim();
                        var px = ParseMediaLength(val, MediaViewportWidth);
                        if (MediaViewportWidth > px)
                        {
                            return false;
                        }
                    }
                }
                // other conditions ignored -> pass
            }

            return true;
        }

        private double ParseMediaLength(string raw, double fallback)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            raw = raw.Trim().ToLowerInvariant();

            double numeric;
            if (raw.EndsWith("px") && double.TryParse(raw.Replace("px", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
            {
                return numeric;
            }

            if (raw.EndsWith("em") || raw.EndsWith("rem"))
            {
                if (double.TryParse(raw.Replace("em", string.Empty).Replace("rem", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
                {
                    return numeric * 16.0;
                }
            }

            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
            {
                return numeric;
            }

            return fallback;
        }

        private void MergeDeclarations(Dictionary<string, string> target, string decls, Dictionary<string, StyleEntry> weights = null, int inlineSpecificity = 0, int inlineOrder = 0)
        {
            if (target == null || string.IsNullOrWhiteSpace(decls))
            {
                return;
            }

            var declarations = decls.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var decl in declarations)
            {
                var kv = decl.Split(new[] { ':' }, 2);
                if (kv.Length != 2)
                {
                    continue;
                }

                var name = kv[0].Trim();
                var value = kv[1].Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (string.Equals(name, "margin", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBoxShorthand(target, "margin", value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "padding", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBoxShorthand(target, "padding", value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "border", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "list-style", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandListStyle(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "border-top", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "border-right", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "border-bottom", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "border-left", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderSideShorthand(target, name, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "border-style", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderBoxProperty(target, "border-", "-style", value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "border-color", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderBoxProperty(target, "border-", "-color", value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "border-width", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderBoxProperty(target, "border-", "-width", value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "background", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBackgroundShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "overflow", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandOverflowShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "background-size", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, name, value, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "border-radius", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderRadius(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (string.Equals(name, "font", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandFontShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                if (weights != null)
                {
                    ApplyProperty(target, weights, name, value, inlineSpecificity, inlineOrder, IsImportant(value));
                }
                else
                {
                    target[name] = StripImportant(value);
                }
            }

        }

        private void ExpandBorderShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string width, style, color;
            ParseBorderComponents(value, out width, out style, out color);

            ApplyBorderAttributes(target, "border-top", width, style, color, weights, specificity, order);
            ApplyBorderAttributes(target, "border-right", width, style, color, weights, specificity, order);
            ApplyBorderAttributes(target, "border-bottom", width, style, color, weights, specificity, order);
            ApplyBorderAttributes(target, "border-left", width, style, color, weights, specificity, order);
        }

        private void ExpandOverflowShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var tokens = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return;
            }

            string x = tokens[0];
            string y = tokens.Length > 1 ? tokens[1] : x;

            ApplyPropertyMaybeWeighted(target, weights, "overflow-x", x, specificity, order);
            ApplyPropertyMaybeWeighted(target, weights, "overflow-y", y, specificity, order);
        }

        private void ExpandListStyle(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string type = null;
            string position = null;
            string image = null;

            var tokens = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var tokenRaw in tokens)
            {
                var token = tokenRaw.Trim();
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                if (string.Equals(token, "inside", StringComparison.OrdinalIgnoreCase) || string.Equals(token, "outside", StringComparison.OrdinalIgnoreCase))
                {
                    position = token;
                    continue;
                }

                if (token.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
                {
                    image = token;
                    continue;
                }

                // assume list-style-type
                type = token;
            }

            if (!string.IsNullOrEmpty(type))
            {
                ApplyPropertyMaybeWeighted(target, weights, "list-style-type", type, specificity, order);
            }

            if (!string.IsNullOrEmpty(position))
            {
                ApplyPropertyMaybeWeighted(target, weights, "list-style-position", position, specificity, order);
            }

            if (!string.IsNullOrEmpty(image))
            {
                ApplyPropertyMaybeWeighted(target, weights, "list-style-image", image, specificity, order);
            }
        }

        private void ExpandBorderSideShorthand(Dictionary<string, string> target, string name, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            string width, style, color;
            ParseBorderComponents(value, out width, out style, out color);
            ApplyBorderAttributes(target, name, width, style, color, weights, specificity, order);
        }

        private void ExpandBorderBoxProperty(Dictionary<string, string> target, string prefix, string suffix, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            string top, right, bottom, left;
            if (parts.Length == 1)
            {
                top = right = bottom = left = parts[0];
            }
            else if (parts.Length == 2)
            {
                top = bottom = parts[0];
                right = left = parts[1];
            }
            else if (parts.Length == 3)
            {
                top = parts[0];
                right = left = parts[1];
                bottom = parts[2];
            }
            else
            {
                top = parts[0];
                right = parts[1];
                bottom = parts[2];
                left = parts[3];
            }

            ApplyPropertyMaybeWeighted(target, weights, prefix + "top" + suffix, top, specificity, order);
            ApplyPropertyMaybeWeighted(target, weights, prefix + "right" + suffix, right, specificity, order);
            ApplyPropertyMaybeWeighted(target, weights, prefix + "bottom" + suffix, bottom, specificity, order);
            ApplyPropertyMaybeWeighted(target, weights, prefix + "left" + suffix, left, specificity, order);
        }

        private void ApplyBorderAttributes(Dictionary<string, string> target, string prefix, string width, string style, string color, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (!string.IsNullOrWhiteSpace(width))
            {
                ApplyPropertyMaybeWeighted(target, weights, prefix + "-width", width, specificity, order);
            }

            if (!string.IsNullOrWhiteSpace(style))
            {
                ApplyPropertyMaybeWeighted(target, weights, prefix + "-style", style, specificity, order);
            }

            if (!string.IsNullOrWhiteSpace(color))
            {
                ApplyPropertyMaybeWeighted(target, weights, prefix + "-color", color, specificity, order);
            }
        }

        private void ParseBorderComponents(string value, out string width, out string style, out string color)
        {
            width = null;
            style = null;
            color = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var tokens = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (style == null && IsBorderStyleToken(token))
                {
                    style = token;
                    continue;
                }

                if (color == null && IsColorToken(token))
                {
                    color = token;
                    continue;
                }

                if (width == null)
                {
                    width = token;
                }
            }
        }

        private bool IsBorderStyleToken(string token)
        {
            var styles = new[] { "none", "solid", "dashed", "dotted", "double", "groove", "ridge", "inset", "outset", "hidden" };
            return styles.Any(s => string.Equals(s, token, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsColorToken(string token)
        {
            string normalized;
            return TryParseColor(token, out normalized);
        }

        private void ApplyPropertyMaybeWeighted(Dictionary<string, string> target, Dictionary<string, StyleEntry> weights, string name, string value, int specificity, int order)
        {
            var cleaned = StripImportant(value);

            if (IsColorProperty(name))
            {
                string normalizedColor;
                if (TryParseColor(cleaned, out normalizedColor))
                {
                    cleaned = normalizedColor;
                }
            }

            if (weights != null)
            {
                ApplyProperty(target, weights, name, cleaned, specificity, order, IsImportant(value));
            }
            else
            {
                target[name] = cleaned;
            }
        }

        private bool IsColorProperty(string name)
        {
            return !string.IsNullOrEmpty(name) && name.IndexOf("color", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryParseColor(string input, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            input = input.Trim();

            if (NamedColors.Contains(input))
            {
                normalized = input.ToLowerInvariant();
                return true;
            }

            if (input.StartsWith("#", StringComparison.Ordinal))
            {
                var hex = input.Substring(1);
                if (hex.Length == 3)
                {
                    hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
                }
                else if (hex.Length == 4)
                {
                    hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2], hex[3], hex[3]);
                }

                if (hex.Length == 6 || hex.Length == 8)
                {
                    int r, g, b, a = 255;
                    if (int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
                        int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
                        int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                    {
                        if (hex.Length == 8)
                        {
                            int.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a);
                        }

                        normalized = ToRgba(r, g, b, a / 255.0);
                        return true;
                    }
                }
            }

            if (input.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                var start = input.IndexOf('(');
                var end = input.IndexOf(')');
                if (start > 0 && end > start)
                {
                    var args = input.Substring(start + 1, end - start - 1).Split(',');
                    if (args.Length >= 3)
                    {
                        double r, g, b, a = 1.0;
                        if (TryParseColorComponent(args[0], out r) && TryParseColorComponent(args[1], out g) && TryParseColorComponent(args[2], out b))
                        {
                            if (args.Length >= 4)
                            {
                                double.TryParse(args[3].Trim().TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out a);
                                if (args[3].Trim().EndsWith("%", StringComparison.Ordinal))
                                {
                                    a = Math.Max(0, Math.Min(1, a / 100.0));
                                }
                            }

                            normalized = ToRgba(r, g, b, a);
                            return true;
                        }
                    }
                }
            }

            if (input.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
            {
                var start = input.IndexOf('(');
                var end = input.IndexOf(')');
                if (start > 0 && end > start)
                {
                    var args = input.Substring(start + 1, end - start - 1).Split(',');
                    if (args.Length >= 3)
                    {
                        double h, s, l, a = 1.0;
                        if (double.TryParse(args[0].Trim().TrimEnd('°'), NumberStyles.Any, CultureInfo.InvariantCulture, out h) &&
                            TryParsePercentage(args[1], out s) &&
                            TryParsePercentage(args[2], out l))
                        {
                            if (args.Length >= 4)
                            {
                                a = args[3].Trim().EndsWith("%", StringComparison.Ordinal) ? Math.Max(0, Math.Min(1, double.Parse(args[3].Trim().TrimEnd('%'), CultureInfo.InvariantCulture) / 100.0)) : Math.Max(0, Math.Min(1, double.Parse(args[3], CultureInfo.InvariantCulture)));
                            }

                            int r, g, b;
                            HslToRgb(h, s, l, out r, out g, out b);
                            normalized = ToRgba(r, g, b, a);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool TryParseColorComponent(string input, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            input = input.Trim();
            if (input.EndsWith("%", StringComparison.Ordinal))
            {
                double percent;
                if (double.TryParse(input.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out percent))
                {
                    value = Math.Max(0, Math.Min(255, (percent / 100.0) * 255.0));
                    return true;
                }
                return false;
            }

            double numeric;
            if (double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
            {
                value = Math.Max(0, Math.Min(255, numeric));
                return true;
            }

            return false;
        }

        private bool TryParsePercentage(string input, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            input = input.Trim();
            if (!input.EndsWith("%", StringComparison.Ordinal))
            {
                return false;
            }

            double numeric;
            if (double.TryParse(input.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out numeric))
            {
                value = Math.Max(0, Math.Min(1, numeric / 100.0));
                return true;
            }

            return false;
        }

        private void HslToRgb(double h, double s, double l, out int r, out int g, out int b)
        {
            h = h % 360.0;
            if (h < 0) h += 360.0;

            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2;

            double r1 = 0, g1 = 0, b1 = 0;

            if (h < 60)
            {
                r1 = c; g1 = x; b1 = 0;
            }
            else if (h < 120)
            {
                r1 = x; g1 = c; b1 = 0;
            }
            else if (h < 180)
            {
                r1 = 0; g1 = c; b1 = x;
            }
            else if (h < 240)
            {
                r1 = 0; g1 = x; b1 = c;
            }
            else if (h < 300)
            {
                r1 = x; g1 = 0; b1 = c;
            }
            else
            {
                r1 = c; g1 = 0; b1 = x;
            }

            r = (int)Math.Round((r1 + m) * 255);
            g = (int)Math.Round((g1 + m) * 255);
            b = (int)Math.Round((b1 + m) * 255);

            r = Math.Max(0, Math.Min(255, r));
            g = Math.Max(0, Math.Min(255, g));
            b = Math.Max(0, Math.Min(255, b));
        }

        private string ToRgba(double r, double g, double b, double a)
        {
            r = Math.Max(0, Math.Min(255, r));
            g = Math.Max(0, Math.Min(255, g));
            b = Math.Max(0, Math.Min(255, b));
            a = Math.Max(0, Math.Min(1, a));
            return string.Format(CultureInfo.InvariantCulture, "rgba({0},{1},{2},{3})", Math.Round(r), Math.Round(g), Math.Round(b), a);
        }

        private void ExpandBackgroundShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var layers = SplitBackgroundLayers(value);
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                string image = null, color = null, repeat = null, position = null, size = null;

                var urlMatch = Regex.Match(layer, "url\\([^\\)]*\\)", RegexOptions.IgnoreCase);
                if (urlMatch.Success)
                {
                    image = urlMatch.Value.Trim();
                    layer = layer.Replace(urlMatch.Value, " ");
                }

                // split position/size syntax: "pos / size"
                string positionPart = layer;
                var slashIndex = layer.IndexOf('/');
                if (slashIndex >= 0)
                {
                    positionPart = layer.Substring(0, slashIndex);
                    size = layer.Substring(slashIndex + 1).Trim();
                }

                var tokens = positionPart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokens)
                {
                    if (repeat == null && (string.Equals(token, "repeat", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(token, "no-repeat", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(token, "repeat-x", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(token, "repeat-y", StringComparison.OrdinalIgnoreCase)))
                    {
                        repeat = token;
                        continue;
                    }

                    if (color == null && IsColorToken(token))
                    {
                        color = token;
                        continue;
                    }

                    if (position == null)
                    {
                        position = token;
                    }
                    else
                    {
                        position += " " + token;
                    }
                }

                string suffix = layers.Count > 1 ? "-" + i.ToString(CultureInfo.InvariantCulture) : string.Empty;

                if (!string.IsNullOrEmpty(image))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "background-image" + suffix, image, specificity, order);
                }

                if (!string.IsNullOrEmpty(color) && i == layers.Count - 1)
                {
                    // only last layer color applies
                    ApplyPropertyMaybeWeighted(target, weights, "background-color", color, specificity, order);
                }

                if (!string.IsNullOrEmpty(repeat))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "background-repeat" + suffix, repeat, specificity, order);
                }

                if (!string.IsNullOrEmpty(position))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "background-position" + suffix, position, specificity, order);
                }

                if (!string.IsNullOrEmpty(size))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "background-size" + suffix, size, specificity, order);
                }
            }
        }

        private List<string> SplitBackgroundLayers(string value)
        {
            var layers = new List<string>();
            var sb = new StringBuilder();
            int depth = 0;
            foreach (var ch in value)
            {
                if (ch == '(')
                {
                    depth++;
                }
                else if (ch == ')')
                {
                    depth = Math.Max(0, depth - 1);
                }

                if (ch == ',' && depth == 0)
                {
                    layers.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }

            if (sb.Length > 0)
            {
                layers.Add(sb.ToString());
            }

            return layers.Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();
        }

        private void ExpandBorderRadius(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            string tl, tr, br, bl;
            if (parts.Length == 1)
            {
                tl = tr = br = bl = parts[0];
            }
            else if (parts.Length == 2)
            {
                tl = br = parts[0];
                tr = bl = parts[1];
            }
            else if (parts.Length == 3)
            {
                tl = parts[0];
                tr = bl = parts[1];
                br = parts[2];
            }
            else
            {
                tl = parts[0];
                tr = parts[1];
                br = parts[2];
                bl = parts[3];
            }

            ApplyPropertyMaybeWeighted(target, weights, "border-top-left-radius", tl, specificity, order);
            ApplyPropertyMaybeWeighted(target, weights, "border-top-right-radius", tr, specificity, order);
            ApplyPropertyMaybeWeighted(target, weights, "border-bottom-right-radius", br, specificity, order);
            ApplyPropertyMaybeWeighted(target, weights, "border-bottom-left-radius", bl, specificity, order);
        }

        private void ExpandFontShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string fontStyle = null;
            string fontWeight = null;
            string fontSize = null;
            string lineHeight = null;
            var familyTokens = new List<string>();

            var tokens = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool collectingFamily = false;

            foreach (var token in tokens)
            {
                if (!collectingFamily)
                {
                    if (token.IndexOf('/') > 0)
                    {
                        var sizeParts = token.Split('/');
                        fontSize = sizeParts[0];
                        if (sizeParts.Length > 1)
                        {
                            lineHeight = sizeParts[1];
                        }
                        collectingFamily = true;
                        continue;
                    }

                    if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase) || token.EndsWith("em", StringComparison.OrdinalIgnoreCase) || token.EndsWith("rem", StringComparison.OrdinalIgnoreCase) || token.EndsWith("%", StringComparison.Ordinal) || token.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
                    {
                        fontSize = token;
                        collectingFamily = true;
                        continue;
                    }

                    if (string.Equals(token, "italic", StringComparison.OrdinalIgnoreCase) || string.Equals(token, "oblique", StringComparison.OrdinalIgnoreCase) || string.Equals(token, "normal", StringComparison.OrdinalIgnoreCase))
                    {
                        fontStyle = token;
                        continue;
                    }

                    if (string.Equals(token, "bold", StringComparison.OrdinalIgnoreCase) || string.Equals(token, "bolder", StringComparison.OrdinalIgnoreCase) || string.Equals(token, "lighter", StringComparison.OrdinalIgnoreCase) || string.Equals(token, "normal", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(token, "^\\d{3}$"))
                    {
                        fontWeight = token;
                        continue;
                    }

                    // ignore other tokens before size
                }
                else
                {
                    familyTokens.Add(token);
                }
            }

            if (fontSize == null)
            {
                return;
            }

            if (familyTokens.Count > 0)
            {
                var family = string.Join(" ", familyTokens);
                ApplyPropertyMaybeWeighted(target, weights, "font-family", family, specificity, order);
            }

            ApplyPropertyMaybeWeighted(target, weights, "font-size", fontSize, specificity, order);

            if (!string.IsNullOrWhiteSpace(lineHeight))
            {
                ApplyPropertyMaybeWeighted(target, weights, "line-height", lineHeight, specificity, order);
            }

            if (!string.IsNullOrWhiteSpace(fontStyle))
            {
                ApplyPropertyMaybeWeighted(target, weights, "font-style", fontStyle, specificity, order);
            }

            if (!string.IsNullOrWhiteSpace(fontWeight))
            {
                ApplyPropertyMaybeWeighted(target, weights, "font-weight", fontWeight, specificity, order);
            }
        }

        private void ExpandBoxShorthand(Dictionary<string, string> target, string prefix, string value, Dictionary<string, StyleEntry> weights = null, int specificity = 0, int order = 0)
        {
            if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            string top, right, bottom, left;
            if (parts.Length == 1)
            {
                top = right = bottom = left = parts[0];
            }
            else if (parts.Length == 2)
            {
                top = bottom = parts[0];
                right = left = parts[1];
            }
            else if (parts.Length == 3)
            {
                top = parts[0];
                right = left = parts[1];
                bottom = parts[2];
            }
            else
            {
                top = parts[0];
                right = parts[1];
                bottom = parts[2];
                left = parts[3];
            }

            if (weights != null)
            {
                ApplyProperty(target, weights, prefix + "-top", top, specificity, order, IsImportant(top));
                ApplyProperty(target, weights, prefix + "-right", right, specificity, order, IsImportant(right));
                ApplyProperty(target, weights, prefix + "-bottom", bottom, specificity, order, IsImportant(bottom));
                ApplyProperty(target, weights, prefix + "-left", left, specificity, order, IsImportant(left));
            }
            else
            {
                target[prefix + "-top"] = StripImportant(top);
                target[prefix + "-right"] = StripImportant(right);
                target[prefix + "-bottom"] = StripImportant(bottom);
                target[prefix + "-left"] = StripImportant(left);
            }
        }

        private void ApplyProperty(CssCascadeResult result, HtmlNode node, string name, string value, int specificity, int order)
        {
            ApplyProperty(result.Styles[node], result.Weights[node], name, value, specificity, order, IsImportant(value));
            result.AppliedCount++;
        }

        private void ApplyProperty(Dictionary<string, string> styles, Dictionary<string, StyleEntry> weights, string name, string value, int specificity, int order, bool important)
        {
            if (styles == null || weights == null || string.IsNullOrEmpty(name))
            {
                return;
            }

            var cleanedValue = StripImportant(value);
            StyleEntry current;
            if (!weights.TryGetValue(name, out current))
            {
                weights[name] = new StyleEntry { Value = cleanedValue, Specificity = specificity, Important = important, Order = order };
                styles[name] = cleanedValue;
                return;
            }

            if (important != current.Important)
            {
                if (important && !current.Important)
                {
                    weights[name] = new StyleEntry { Value = cleanedValue, Specificity = specificity, Important = important, Order = order };
                    styles[name] = cleanedValue;
                }
                return;
            }

            if (specificity > current.Specificity || (specificity == current.Specificity && order >= current.Order))
            {
                weights[name] = new StyleEntry { Value = cleanedValue, Specificity = specificity, Important = important, Order = order };
                styles[name] = cleanedValue;
            }
        }

        private bool IsImportant(string value)
        {
            return value != null && value.IndexOf("!important", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string StripImportant(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var idx = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                return value.Substring(0, idx).Trim();
            }

            return value.Trim();
        }

        private bool MatchesSelector(HtmlNode node, string selector)
        {
            if (string.IsNullOrWhiteSpace(selector) || node == null)
            {
                return false;
            }

            selector = selector.Trim();

            if (selector.Contains(","))
            {
                var list = selector.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var sel in list)
                {
                    if (MatchesSelector(node, sel.Trim()))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (selector.Contains("+")) 
            {
                var partsPlus = selector.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(s => s.Trim())
                                         .ToArray();
                if (partsPlus.Length == 2)
                {
                    return MatchesAdjacentSelector(node, partsPlus[0], partsPlus[1]);
                }
            }

            if (selector.Contains("~"))
            {
                var partsSibling = selector.Split(new[] { '~' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Select(s => s.Trim())
                                           .ToArray();
                if (partsSibling.Length == 2)
                {
                    return MatchesGeneralSiblingSelector(node, partsSibling[0], partsSibling[1]);
                }
            }

            if (selector.Contains(">"))
            {
                var chain = selector.Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(s => s.Trim())
                                    .ToArray();
                if (chain.Length > 1)
                {
                    return MatchesChildSelectorChain(node, chain, chain.Length - 1);
                }
            }

            var parts = selector.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                return MatchesCompoundSelector(node, parts, parts.Length - 1);
            }

            return MatchesSimpleSelector(node, selector);
        }

        private bool MatchesCompoundSelector(HtmlNode node, string[] parts, int index)
        {
            if (!MatchesSimpleSelector(node, parts[index]))
            {
                return false;
            }

            if (index == 0)
            {
                return true;
            }

            var parent = node.Parent;
            while (parent != null)
            {
                if (MatchesCompoundSelector(parent, parts, index - 1))
                {
                    return true;
                }

                parent = parent.Parent;
            }

            return false;
        }

        private bool MatchesChildSelectorChain(HtmlNode node, string[] chain, int index)
        {
            if (!MatchesSimpleSelector(node, chain[index]))
            {
                return false;
            }

            if (index == 0)
            {
                return true;
            }

            var parent = node.Parent;
            if (parent == null)
            {
                return false;
            }

            return MatchesChildSelectorChain(parent, chain, index - 1);
        }

        private bool MatchesAdjacentSelector(HtmlNode node, string left, string right)
        {
            if (!MatchesSimpleSelector(node, right))
            {
                return false;
            }

            var parent = node.Parent;
            if (parent == null)
            {
                return false;
            }

            var siblings = parent.Children;
            var index = siblings.IndexOf(node);
            if (index <= 0)
            {
                return false;
            }

            return MatchesSimpleSelector(siblings[index - 1], left);
        }

        private bool MatchesGeneralSiblingSelector(HtmlNode node, string left, string right)
        {
            if (!MatchesSimpleSelector(node, right))
            {
                return false;
            }

            var parent = node.Parent;
            if (parent == null)
            {
                return false;
            }

            var siblings = parent.Children;
            var index = siblings.IndexOf(node);
            if (index <= 0)
            {
                return false;
            }

            for (int i = index - 1; i >= 0; i--)
            {
                if (MatchesSimpleSelector(siblings[i], left))
                {
                    return true;
                }
            }

            return false;
        }

        private bool MatchesSimpleSelector(HtmlNode node, string selector)
        {
            selector = selector.Trim();

            bool requireFirstChild = false;
            bool requireLastChild = false;
            int? requireNthChild = null;
            string notSelector = null;
            bool requireEmpty = false;
            bool requireHover = false;
            bool requireFocus = false;

            int pseudoIndex = selector.IndexOf(':');
            while (pseudoIndex >= 0)
            {
                var pseudo = selector.Substring(pseudoIndex);
                selector = selector.Substring(0, pseudoIndex).Trim();

                if (pseudo.StartsWith(":first-child", StringComparison.OrdinalIgnoreCase))
                {
                    requireFirstChild = true;
                    pseudo = pseudo.Substring(":first-child".Length);
                }
                else if (pseudo.StartsWith(":last-child", StringComparison.OrdinalIgnoreCase))
                {
                    requireLastChild = true;
                    pseudo = pseudo.Substring(":last-child".Length);
                }
                else if (pseudo.StartsWith(":not(", StringComparison.OrdinalIgnoreCase) && pseudo.EndsWith(")", StringComparison.Ordinal))
                {
                    var inner = pseudo.Substring(5, pseudo.Length - 6).Trim();
                    notSelector = inner;
                    pseudo = string.Empty;
                }
                else if (pseudo.StartsWith(":nth-child(", StringComparison.OrdinalIgnoreCase) && pseudo.EndsWith(")", StringComparison.Ordinal))
                {
                    var inner = pseudo.Substring(":nth-child(".Length, pseudo.Length - ":nth-child(".Length - 1);
                    int n;
                    if (int.TryParse(inner, out n) && n > 0)
                    {
                        requireNthChild = n;
                    }
                    else
                    {
                        return false;
                    }
                    pseudo = string.Empty;
                }
                else if (pseudo.StartsWith(":empty", StringComparison.OrdinalIgnoreCase))
                {
                    requireEmpty = true;
                    pseudo = pseudo.Substring(":empty".Length);
                }
                else if (pseudo.StartsWith(":hover", StringComparison.OrdinalIgnoreCase))
                {
                    requireHover = true;
                    pseudo = pseudo.Substring(":hover".Length);
                }
                else if (pseudo.StartsWith(":focus", StringComparison.OrdinalIgnoreCase))
                {
                    requireFocus = true;
                    pseudo = pseudo.Substring(":focus".Length);
                }
                else
                {
                    // unsupported pseudo
                    return false;
                }

                pseudoIndex = pseudo.IndexOf(':');
                if (pseudoIndex >= 0)
                {
                    selector = selector + pseudo.Substring(0, pseudoIndex);
                    pseudo = pseudo.Substring(pseudoIndex);
                }
                else
                {
                    selector = selector + pseudo;
                }

                pseudoIndex = selector.IndexOf(':');
            }

            bool baseMatch;

            if (string.Equals(selector, "*", StringComparison.Ordinal))
            {
                baseMatch = true;
            }
            else if (selector.StartsWith("#", StringComparison.Ordinal))
            {
                var id = selector.Substring(1);
                baseMatch = node.Attributes.ContainsKey("id") && string.Equals(node.Attributes["id"], id, StringComparison.OrdinalIgnoreCase);
            }
            else if (selector.StartsWith(".", StringComparison.Ordinal))
            {
                var cls = selector.Substring(1);
                baseMatch = false;
                if (node.Attributes.ContainsKey("class"))
                {
                    var classes = node.Attributes["class"].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var c in classes)
                    {
                        if (string.Equals(c, cls, StringComparison.OrdinalIgnoreCase))
                        {
                            baseMatch = true;
                            break;
                        }
                    }
                }
            }
            else if (selector.StartsWith("[", StringComparison.Ordinal) && selector.EndsWith("]", StringComparison.Ordinal))
            {
                baseMatch = MatchesAttributeSelector(node, selector);
            }
            else
            {
                baseMatch = string.IsNullOrEmpty(selector) || string.Equals(node.Tag, selector, StringComparison.OrdinalIgnoreCase);
            }

            if (!baseMatch)
            {
                return false;
            }

            if (requireFirstChild && !IsFirstChild(node))
            {
                return false;
            }

            if (requireLastChild && !IsLastChild(node))
            {
                return false;
            }

            if (requireNthChild.HasValue)
            {
                if (node.Parent == null)
                {
                    return false;
                }

                var idx = node.Parent.Children.IndexOf(node) + 1;
                if (idx != requireNthChild.Value)
                {
                    return false;
                }
            }

            if (requireEmpty)
            {
                bool hasChildren = node.Children != null && node.Children.Count > 0;
                bool hasText = !string.IsNullOrWhiteSpace(node.Text);
                if (hasChildren || hasText)
                {
                    return false;
                }
            }

            if (requireHover && !(node.Attributes.ContainsKey("data-hover") || node.Attributes.ContainsKey("hover")))
            {
                return false;
            }

            if (requireFocus && !(node.Attributes.ContainsKey("data-focus") || node.Attributes.ContainsKey("focus")))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(notSelector) && MatchesSimpleSelector(node, notSelector))
            {
                return false;
            }

            return true;
        }

        private bool MatchesAttributeSelector(HtmlNode node, string selector)
        {
            var content = selector.Substring(1, selector.Length - 2);
            string[] ops = new[] { "~=", "|=", "^=", "$=", "*=", "=" };

            foreach (var op in ops)
            {
                var idx = content.IndexOf(op, StringComparison.Ordinal);
                if (idx > 0)
                {
                    var attrName = content.Substring(0, idx).Trim();
                    var expected = content.Substring(idx + op.Length).Trim('"', '\'', ' ');
                    if (!node.Attributes.ContainsKey(attrName))
                    {
                        return false;
                    }

                    var actual = node.Attributes[attrName];
                    switch (op)
                    {
                        case "=":
                            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                        case "~=":
                            return actual.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Any(x => string.Equals(x, expected, StringComparison.OrdinalIgnoreCase));
                        case "|=":
                            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) || actual.StartsWith(expected + "-", StringComparison.OrdinalIgnoreCase);
                        case "^=":
                            return actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase);
                        case "$=":
                            return actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase);
                        case "*=":
                            return actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
            }

            // simple [attr]
            var simpleName = content.Trim();
            return node.Attributes.ContainsKey(simpleName);
        }

        private bool IsFirstChild(HtmlNode node)
        {
            if (node?.Parent == null)
            {
                return false;
            }

            return node.Parent.Children.FirstOrDefault() == node;
        }

        private bool IsLastChild(HtmlNode node)
        {
            if (node?.Parent == null)
            {
                return false;
            }

            return node.Parent.Children.LastOrDefault() == node;
        }

        private int CalculateSpecificity(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                return 0;
            }

            int ids = 0, classes = 0, elements = 0;

            for (int i = 0; i < selector.Length; i++)
            {
                var c = selector[i];

                if (c == '#')
                {
                    ids++;
                    i = ConsumeIdent(selector, i + 1);
                }
                else if (c == '.')
                {
                    classes++;
                    i = ConsumeIdent(selector, i + 1);
                }
                else if (c == '[')
                {
                    classes++;
                    i = selector.IndexOf(']', i);
                    if (i < 0)
                    {
                        break;
                    }
                }
                else if (c == ':')
                {
                    var isDouble = (i + 1 < selector.Length && selector[i + 1] == ':');
                    if (isDouble)
                    {
                        elements++;
                        i = ConsumeIdent(selector, i + 2);
                        continue;
                    }

                    // pseudo-class
                    if (selector.IndexOf(":not(", i, StringComparison.OrdinalIgnoreCase) == i)
                    {
                        var open = i + 5;
                        var close = FindMatchingParen(selector, open - 1);
                        if (close > open)
                        {
                            var inner = selector.Substring(open, close - open);
                            var innerSpec = CalculateSpecificity(inner);
                            ids += innerSpec / 100;
                            classes += (innerSpec / 10) % 10;
                            elements += innerSpec % 10;
                            i = close;
                            continue;
                        }
                    }

                    classes++;
                    i = ConsumeIdent(selector, i + 1);
                }
                else if (c == '*' || c == ' ' || c == '>' || c == '+' || c == '~' || c == ',')
                {
                    // combinators/universal selector do not add specificity
                }
                else if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    elements++;
                    i = ConsumeIdent(selector, i + 1);
                }
            }

            return ids * 100 + classes * 10 + elements;
        }

        private int ConsumeIdent(string selector, int index)
        {
            while (index < selector.Length)
            {
                var ch = selector[index];
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '\\')
                {
                    index++;
                    continue;
                }

                break;
            }

            return index - 1;
        }

        private int FindMatchingParen(string text, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (text[i] == '(')
                {
                    depth++;
                }
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }
    }

    public class JsEngine : IJsEngine
    {
        private IDomBridge _bridge;
        public IEventLoop EventLoop { get; } = new EventLoop();

        public void Execute(string script, JsContext context)
        {
            // TODO: integrate real JS VM
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

    // Simple data holders for the placeholders
    public class HtmlDocument
    {
        public string Url { get; set; }
        public string BaseUrl { get; set; }
        public HtmlNode Root { get; set; }
        public CssStyleSheet StyleSheet { get; set; } = new CssStyleSheet();

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

    public class HtmlNode
    {
        public string Tag { get; set; }
        public string Text { get; set; }
        public List<HtmlNode> Children { get; set; } = new List<HtmlNode>();
        public Dictionary<string, string> Attributes { get; set; } = new Dictionary<string, string>();
        public HtmlNode Parent { get; set; }
    }

    public class CssStyleSheet
    {
        public string Raw { get; set; }
        public List<CssRule> Rules { get; set; } = new List<CssRule>();
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
    }

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
    }

    public class JsContext
    {
        public HtmlDocument Document { get; set; }
        public List<string> Logs { get; set; } = new List<string>();
        public IDomBridge DomBridge { get; set; }
    }

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

        public IEnumerable<HtmlNode> QuerySelectorAll(string selector)
        {
            return _document?.QuerySelectorAll(selector) ?? Enumerable.Empty<HtmlNode>();
        }
    }

    public class CssRule
    {
        public string SelectorText { get; set; }
        public Dictionary<string, string> Declarations { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int Specificity { get; set; }
        public int SourceOrder { get; set; }
        public string MediaCondition { get; set; }
        public string PseudoElement { get; set; }
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
