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

            double viewportWidth = BrowserTabs != null && !double.IsNaN(BrowserTabs.ActualWidth) && BrowserTabs.ActualWidth > 0
                ? BrowserTabs.ActualWidth
                : (!double.IsNaN(ActualWidth) && ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth);

            double viewportHeight = BrowserTabs != null && !double.IsNaN(BrowserTabs.ActualHeight) && BrowserTabs.ActualHeight > 0
                ? BrowserTabs.ActualHeight
                : (!double.IsNaN(ActualHeight) && ActualHeight > 0 ? ActualHeight : SystemParameters.PrimaryScreenHeight);

            engine.CssEngine.UpdateMediaContext(viewportWidth, viewportHeight, colorScheme: null, reducedMotion: null, contrast: null, reducedTransparency: null);

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

            var fullCss = InlineCssImports(cssBuilder.ToString(), baseUrl);
            document.StyleSheet = engine.CssEngine.Parse(fullCss);

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
            engine.CssEngine.ApplyTransitions(renderTree.Root, DateTime.UtcNow, document);
            engine.CssEngine.ApplyAnimations(renderTree.Root, DateTime.UtcNow, document);
            var render = engine.Paint(renderTree);

            var tab = new TabItem { Header = address };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var image = new Image
            {
                Source = render.Image,
                Stretch = Stretch.None,
                Margin = new Thickness(0, 0, 0, 12)
            };

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

            Grid.SetRow(image, 0);
            Grid.SetRow(summary, 1);
            Grid.SetRow(rawView, 2);
            grid.Children.Add(image);
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

        private string InlineCssImports(string css, string baseUrl, int depth = 0)
        {
            if (string.IsNullOrWhiteSpace(css))
            {
                return string.Empty;
            }

            if (depth > 3)
            {
                // prevent deep or circular imports
                return css;
            }

            var importRegex = new Regex(@"@import\s+(?:url\(['""]?(?<url>[^'"")\s]+)['""]?\)|['""](?<url2>[^'""]+)['""])", RegexOptions.IgnoreCase);
            var sb = new StringBuilder();
            int lastIndex = 0;

            foreach (Match m in importRegex.Matches(css))
            {
                // append content before the import
                sb.Append(css.Substring(lastIndex, m.Index - lastIndex));

                var importUrl = m.Groups["url"].Success ? m.Groups["url"].Value : m.Groups["url2"].Value;
                var resolved = ResolveUrl(baseUrl, importUrl);

                if (!IsResourceHostBlocked(resolved))
                {
                    var importedCss = FetchResource(resolved);
                    if (!string.IsNullOrWhiteSpace(importedCss))
                    {
                        // recursively inline nested imports
                        importedCss = InlineCssImports(importedCss, resolved, depth + 1);
                        sb.AppendLine(importedCss);
                    }
                }

                lastIndex = m.Index + m.Length;
            }

            if (lastIndex < css.Length)
            {
                sb.Append(css.Substring(lastIndex));
            }

            return sb.ToString();
        }

        private bool IsResourceHostBlocked(string resourceUrl)
        {
            var host = ExtractHost(resourceUrl);
            return AppSettings.Current.IsDomainBlocked(host);
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

            // counters(): allow multiple levels joined by separator
            var countersMatch = Regex.Match(content, @"counters\s*\([^\)]*\)", RegexOptions.IgnoreCase);
            if (countersMatch.Success)
            {
                var countersExpr = countersMatch.Value;
                var counterDict = ExtractCountersFromComputed(computed);
                var cssEngine = new CssEngine();
                var evaluated = cssEngine.EvaluateCounters(countersExpr, null);
                // EvaluateCounters expects parsed counters; create nested list wrapper
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

        private void ResolveCurrentColor(Dictionary<string, string> styles, Dictionary<string, string> parent)
        {
            if (styles == null)
            {
                return;
            }

            // Determine the inherited color (default to black when nothing is provided)
            string parentColor = "black";
            if (parent != null && parent.TryGetValue("color", out var pc) && !string.IsNullOrWhiteSpace(pc))
            {
                parentColor = pc;
            }

            // Resolve the element's own color value
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

            // Replace any remaining currentcolor usages with the resolved color
            var keys = styles.Keys.ToList();
            foreach (var key in keys)
            {
                if (styles.TryGetValue(key, out var val) && string.Equals(val, "currentcolor", StringComparison.OrdinalIgnoreCase))
                {
                    styles[key] = resolvedColor;
                }
            }
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

    public class CssEngine : ICssEngine
    {
        // CSS Variables storage
        private Dictionary<string, string> _customProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);


        // Keyframes storage
        private Dictionary<string, CssKeyframesRule> _keyframes = new Dictionary<string, CssKeyframesRule>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Exposes coarse average width metrics for a registered @font-face.
        /// </summary>
        public bool TryGetFontMetrics(string family, string weight, string style, out double averageWidthFactor)
        {
            averageWidthFactor = 0;
            if (string.IsNullOrWhiteSpace(family))
            {
                return false;
            }

            var match = _fontFaces.FirstOrDefault(ff => string.Equals(ff.FontFamily, family, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                return false;
            }

            double factor = 0.52; // base
            if (!string.IsNullOrWhiteSpace(weight ?? match.FontWeight))
            {
                var w = (weight ?? match.FontWeight).ToLowerInvariant();
                if (w.Contains("bold") || w.Contains("700"))
                {
                    factor *= 1.05;
                }
                else if (w.Contains("300") || w.Contains("light"))
                {
                    factor *= 0.96;
                }
            }

            if (!string.IsNullOrWhiteSpace(style ?? match.FontStyle))
            {
                var s = (style ?? match.FontStyle).ToLowerInvariant();
                if (s.Contains("italic") || s.Contains("oblique"))
                {
                    factor *= 1.02;
                }
            }

            averageWidthFactor = factor;
            return true;
        }

        // Font-face storage
        private List<CssFontFace> _fontFaces = new List<CssFontFace>();

        // Imported stylesheets
        private List<string> _imports = new List<string>();

        // Layer order tracking for @layer cascade
        private List<string> _layerOrder = new List<string>();
        private Dictionary<string, int> _layerPriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Container query contexts
        private Dictionary<string, ContainerContext> _containerContexts = new Dictionary<string, ContainerContext>(StringComparer.OrdinalIgnoreCase);

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

        // Default property values for 'initial' keyword
        private static readonly Dictionary<string, string> InitialValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"display", "inline"}, {"position", "static"}, {"float", "none"}, {"clear", "none"},
            {"visibility", "visible"}, {"opacity", "1"}, {"z-index", "auto"},
            {"width", "auto"}, {"height", "auto"}, {"min-width", "0"}, {"min-height", "0"},
            {"max-width", "none"}, {"max-height", "none"},
            {"margin-top", "0"}, {"margin-right", "0"}, {"margin-bottom", "0"}, {"margin-left", "0"},
            {"padding-top", "0"}, {"padding-right", "0"}, {"padding-bottom", "0"}, {"padding-left", "0"},
            {"border-top-width", "medium"}, {"border-right-width", "medium"}, {"border-bottom-width", "medium"}, {"border-left-width", "medium"},
            {"border-top-style", "none"}, {"border-right-style", "none"}, {"border-bottom-style", "none"}, {"border-left-style", "none"},
            {"border-top-color", "currentcolor"}, {"border-right-color", "currentcolor"}, {"border-bottom-color", "currentcolor"}, {"border-left-color", "currentcolor"},
            {"background-color", "transparent"}, {"background-image", "none"}, {"background-repeat", "repeat"}, {"background-position", "0% 0%"},
            {"color", "black"}, {"font-family", "serif"}, {"font-size", "medium"}, {"font-style", "normal"}, {"font-weight", "normal"},
            {"text-align", "left"}, {"text-decoration", "none"}, {"text-transform", "none"}, {"line-height", "normal"},
            {"text-decoration-line", "none"}, {"text-decoration-style", "solid"}, {"text-decoration-color", "currentcolor"}, {"text-decoration-thickness", "auto"},
            {"text-underline-offset", "auto"}, {"text-underline-position", "auto"},
            {"text-overflow", "clip"}, {"word-break", "normal"}, {"overflow-wrap", "normal"}, {"hyphens", "manual"},
            {"overflow", "visible"}, {"overflow-x", "visible"}, {"overflow-y", "visible"},
            {"box-sizing", "content-box"}, {"cursor", "auto"},
            {"object-fit", "fill"}, {"object-position", "50% 50%"},
            {"aspect-ratio", "auto"}, {"contain", "none"}, {"content-visibility", "visible"}, {"container-type", "normal"}, {"container-name", "none"},
            {"flex-direction", "row"}, {"flex-wrap", "nowrap"}, {"justify-content", "flex-start"}, {"align-items", "stretch"}, {"align-content", "stretch"},
            {"flex-grow", "0"}, {"flex-shrink", "1"}, {"flex-basis", "auto"}, {"order", "0"},
            {"grid-template-columns", "none"}, {"grid-template-rows", "none"}, {"grid-gap", "0"}, {"grid-column-gap", "0"}, {"grid-row-gap", "0"},
            {"transform", "none"}, {"transform-origin", "50% 50%"},
            {"transition", "none"}, {"animation", "none"},
            {"outline-width", "medium"}, {"outline-style", "none"}, {"outline-color", "invert"}, {"outline-offset", "0"},
            {"clip-path", "none"}, {"mask", "none"}, {"filter", "none"}, {"backdrop-filter", "none"},
            {"mix-blend-mode", "normal"}, {"isolation", "auto"},
            {"writing-mode", "horizontal-tb"}, {"text-orientation", "mixed"}, {"direction", "ltr"}, {"unicode-bidi", "normal"},
            {"vertical-align", "baseline"},
            {"scroll-behavior", "auto"}, {"scroll-snap-type", "none"}, {"scroll-snap-align", "none"}, {"scroll-snap-stop", "normal"},
            {"scroll-margin", "0"}, {"scroll-padding", "0"},
            {"resize", "none"}, {"user-select", "auto"}, {"pointer-events", "auto"},
            {"accent-color", "auto"}, {"caret-color", "auto"},
            {"will-change", "auto"}, {"contain-intrinsic-size", "none"},
            {"font-optical-sizing", "auto"}, {"font-variation-settings", "normal"}, {"font-feature-settings", "normal"}
        };

        // Inheritable properties
        private static readonly HashSet<string> InheritableProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "color", "font-family", "font-size", "font-style", "font-weight", "font-variant", "font",
            "line-height", "letter-spacing", "word-spacing", "text-align", "text-indent", "text-transform",
            "white-space", "direction", "visibility", "cursor", "list-style", "list-style-type", "list-style-position", "list-style-image",
            "quotes", "caption-side", "empty-cells", "border-collapse", "border-spacing",
            "writing-mode", "text-orientation", "unicode-bidi"
        };

        // Counter storage for CSS counters
        private Dictionary<string, int> _counters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers a container for container queries
        /// </summary>
        public void RegisterContainer(string name, double width, double height, string containerType)
        {
            _containerContexts[name ?? ""] = new ContainerContext
            {
                Name = name,
                Width = width,
                Height = height,
                ContainerType = containerType
            };
        }

        /// <summary>
        /// Evaluates a container query condition
        /// </summary>
        private bool EvaluateContainerQuery(string condition, ContainerContext container)
        {
            if (container == null || string.IsNullOrWhiteSpace(condition))
                return false;

            condition = condition.Trim().Trim('(', ')');

            // Parse container query features
            var features = condition.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var feature in features)
            {
                var trimmed = feature.Trim().Trim('(', ')');
                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0) continue;

                var name = trimmed.Substring(0, colonIdx).Trim().ToLowerInvariant();
                var value = trimmed.Substring(colonIdx + 1).Trim();

                double targetValue = ParseMediaLength(value, 0);

                switch (name)
                {
                    case "min-width":
                        if (container.Width < targetValue) return false;
                        break;
                    case "max-width":
                        if (container.Width > targetValue) return false;
                        break;
                    case "width":
                        if (Math.Abs(container.Width - targetValue) > 1) return false;
                        break;
                    case "min-height":
                        if (container.Height < targetValue) return false;
                        break;
                    case "max-height":
                        if (container.Height > targetValue) return false;
                        break;
                    case "height":
                        if (Math.Abs(container.Height - targetValue) > 1) return false;
                        break;
                    case "min-inline-size":
                        if (container.Width < targetValue) return false;
                        break;
                    case "max-inline-size":
                        if (container.Width > targetValue) return false;
                        break;
                    case "min-block-size":
                        if (container.Height < targetValue) return false;
                        break;
                    case "max-block-size":
                        if (container.Height > targetValue) return false;
                        break;
                    case "orientation":
                        if (value == "portrait" && container.Width >= container.Height) return false;
                        if (value == "landscape" && container.Width < container.Height) return false;
                        break;
                    case "aspect-ratio":
                        // Parse and compare aspect ratio
                        break;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets the layer priority for cascade ordering
        /// </summary>
        private int GetLayerPriority(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return int.MaxValue; // Unlayered styles have highest priority
            
            int priority;
            if (_layerPriority.TryGetValue(layerName, out priority))
                return priority;
            
            return 0; // Unknown layers have lowest priority
        }

        /// <summary>
        /// Registers a layer in the cascade order
        /// </summary>
        public void RegisterLayer(string layerName)
        {
            if (!string.IsNullOrEmpty(layerName) && !_layerOrder.Contains(layerName))
            {
                _layerOrder.Add(layerName);
                _layerPriority[layerName] = _layerOrder.Count;
            }
        }

        /// <summary>
        /// Applies CSS transitions to interpolatable properties based on transition-* settings.
        /// Uses document creation time as start reference (no dynamic change tracking yet).
        /// </summary>
        public void ApplyTransitions(RenderNode root, DateTime now, HtmlDocument document)
        {
            if (root == null || document == null)
            {
                return;
            }

            ApplyTransitionsRecursive(root, now, document.CreatedAt);
        }

        private void ApplyTransitionsRecursive(RenderNode node, DateTime now, DateTime startTime)
        {
            if (node?.Box?.ComputedStyle != null)
            {
                var styles = node.Box.ComputedStyle;
                string propList;
                if (styles.TryGetValue("transition-property", out propList) && !string.IsNullOrWhiteSpace(propList) && !string.Equals(propList, "none", StringComparison.OrdinalIgnoreCase))
                {
                    string durationStr;
                    styles.TryGetValue("transition-duration", out durationStr);
                    double duration = ParseTimeValue(durationStr ?? "0s");
                    if (duration > 0)
                    {
                        string delayStr;
                        styles.TryGetValue("transition-delay", out delayStr);
                        double delay = ParseTimeValue(delayStr ?? "0s");

                        var props = propList.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
                        if (props.Count == 0)
                        {
                            props.Add("all");
                        }

                        var eligible = props.Contains("all", StringComparer.OrdinalIgnoreCase)
                            ? styles.Keys.Where(k => !k.StartsWith("_", StringComparison.Ordinal)).ToList()
                            : props;

                        var elapsed = (now - startTime).TotalSeconds - delay;
                        double tNorm = elapsed <= 0 ? 0 : Math.Min(1.0, elapsed / duration);

                        foreach (var prop in eligible)
                        {
                            if (prop.StartsWith("transition", StringComparison.OrdinalIgnoreCase) || prop.StartsWith("animation", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string targetVal;
                            if (!styles.TryGetValue(prop, out targetVal))
                            {
                                continue;
                            }

                            string fromKey = "_transition-from:" + prop;
                            string fromVal;
                            if (!styles.TryGetValue(fromKey, out fromVal))
                            {
                                if (!InitialValues.TryGetValue(prop, out fromVal))
                                {
                                    fromVal = targetVal;
                                }
                                styles[fromKey] = fromVal;
                            }

                            var interpolated = InterpolateValue(prop, fromVal, targetVal, tNorm);
                            styles[prop] = interpolated;
                        }
                    }
                }
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    ApplyTransitionsRecursive(child, now, startTime);
                }
            }
        }

        /// <summary>
        /// Applies CSS animations to the render tree at the given timestamp.
        /// Interpolates numeric/length/color properties between keyframes and merges into computed styles.
        /// </summary>
        public void ApplyAnimations(RenderNode root, DateTime now, HtmlDocument document)
        {
            if (root == null || document == null)
            {
                return;
            }

            ApplyAnimationsRecursive(root, now, document.CreatedAt);
        }

        private void ApplyAnimationsRecursive(RenderNode node, DateTime now, DateTime startTime)
        {
            if (node?.Box?.ComputedStyle != null)
            {
                var styles = node.Box.ComputedStyle;
                string animationName;
                if (styles.TryGetValue("animation-name", out animationName) && !string.IsNullOrWhiteSpace(animationName) && !string.Equals(animationName, "none", StringComparison.OrdinalIgnoreCase))
                {
                    var names = animationName.Split(',').Select(n => n.Trim()).Where(n => !string.IsNullOrEmpty(n)).ToList();
                    string durationStr;
                    styles.TryGetValue("animation-duration", out durationStr);
                    double duration = ParseTimeValue(durationStr ?? "1s");
                    if (duration <= 0) duration = 1; // prevent div by zero

                    string delayStr;
                    styles.TryGetValue("animation-delay", out delayStr);
                    double delay = ParseTimeValue(delayStr ?? "0s");

                    string iterStr;
                    styles.TryGetValue("animation-iteration-count", out iterStr);
                    double iterations = 1;
                    if (!string.IsNullOrWhiteSpace(iterStr))
                    {
                        if (string.Equals(iterStr.Trim(), "infinite", StringComparison.OrdinalIgnoreCase))
                        {
                            iterations = double.PositiveInfinity;
                        }
                        else
                        {
                            double.TryParse(iterStr, NumberStyles.Any, CultureInfo.InvariantCulture, out iterations);
                            if (iterations <= 0) iterations = 1;
                        }
                    }

                    string direction;
                    styles.TryGetValue("animation-direction", out direction);
                    direction = (direction ?? "normal").ToLowerInvariant();

                    string fillMode;
                    styles.TryGetValue("animation-fill-mode", out fillMode);
                    fillMode = (fillMode ?? "none").ToLowerInvariant();

                    foreach (var name in names)
                    {
                        CssKeyframesRule keyframes;
                        if (!_keyframes.TryGetValue(name, out keyframes) || keyframes.Keyframes.Count == 0)
                        {
                            continue;
                        }

                        var elapsed = (now - startTime).TotalSeconds - delay;
                        if (elapsed < 0 && fillMode != "backwards")
                        {
                            continue;
                        }

                        double cycle = duration;
                        if (cycle <= 0) cycle = 1;
                        double total = iterations * cycle;
                        bool finished = !double.IsInfinity(iterations) && elapsed >= total;

                        double tNorm;
                        if (finished)
                        {
                            tNorm = 1.0;
                        }
                        else if (elapsed <= 0)
                        {
                            tNorm = 0.0;
                        }
                        else
                        {
                            var posInCycle = elapsed % cycle;
                            tNorm = posInCycle / cycle;
                            if (direction == "reverse" || (direction == "alternate" && Math.Floor(elapsed / cycle) % 2 == 1))
                            {
                                tNorm = 1.0 - tNorm;
                            }
                        }

                        var interpolated = EvaluateKeyframes(keyframes, tNorm, styles);
                        foreach (var kv in interpolated)
                        {
                            styles[kv.Key] = kv.Value;
                        }

                        styles["_animation-progress"] = tNorm.ToString("0.###", CultureInfo.InvariantCulture);
                    }
                }
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    ApplyAnimationsRecursive(child, now, startTime);
                }
            }
        }

        private Dictionary<string, string> EvaluateKeyframes(CssKeyframesRule rule, double progress, Dictionary<string, string> baseStyles)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (rule == null || rule.Keyframes.Count == 0)
            {
                return result;
            }

            var frames = rule.Keyframes.OrderBy(k => k.Percentage).ToList();
            var prev = frames.First();
            var next = frames.Last();
            foreach (var f in frames)
            {
                if (f.Percentage / 100.0 <= progress)
                {
                    prev = f;
                }
                if (f.Percentage / 100.0 >= progress)
                {
                    next = f;
                    break;
                }
            }

            double span = (next.Percentage - prev.Percentage) / 100.0;
            double localT = span > 0 ? (progress - prev.Percentage / 100.0) / span : 0;
            localT = Math.Max(0, Math.Min(1, localT));

            var keys = prev.Declarations.Keys.Union(next.Declarations.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                string fromVal = prev.Declarations.ContainsKey(key) ? prev.Declarations[key] : (baseStyles != null && baseStyles.ContainsKey(key) ? baseStyles[key] : null);
                string toVal = next.Declarations.ContainsKey(key) ? next.Declarations[key] : fromVal;
                if (toVal == null)
                {
                    continue;
                }

                var interp = InterpolateValue(key, fromVal, toVal, localT);
                result[key] = interp;
            }

            return result;
        }

        private string InterpolateValue(string property, string fromVal, string toVal, double t)
        {
            if (string.IsNullOrWhiteSpace(fromVal)) return toVal;
            if (string.IsNullOrWhiteSpace(toVal)) return fromVal;

            double fromNum, toNum;
            // numeric or px lengths
            if (double.TryParse(fromVal.Replace("px", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out fromNum) &&
                double.TryParse(toVal.Replace("px", string.Empty), NumberStyles.Any, CultureInfo.InvariantCulture, out toNum))
            {
                var v = fromNum + (toNum - fromNum) * t;
                return toVal.Contains("px") ? v.ToString("0.###", CultureInfo.InvariantCulture) + "px" : v.ToString("0.###", CultureInfo.InvariantCulture);
            }

            // colors
            string fromColor, toColor;
            if (TryParseColor(fromVal, out fromColor) && TryParseColor(toVal, out toColor))
            {
                double fr, fg, fb, fa, tr, tg, tb, ta;
                ParseRgbaValues(fromColor, out fr, out fg, out fb, out fa);
                ParseRgbaValues(toColor, out tr, out tg, out tb, out ta);
                var r = fr + (tr - fr) * t;
                var g = fg + (tg - fg) * t;
                var b = fb + (tb - fb) * t;
                var a = fa + (ta - fa) * t;
                return ToRgba(r, g, b, a);
            }

            // fallback: pick destination halfway after 50%
            return t < 0.5 ? fromVal : toVal;
        }

        /// <summary>
        /// Parses image-set() CSS function
        /// </summary>
        public static List<ImageSetOption> ParseImageSet(string imageSetValue)
        {
            var options = new List<ImageSetOption>();
            if (string.IsNullOrWhiteSpace(imageSetValue)) return options;

            var match = Regex.Match(imageSetValue, @"image-set\s*\((?<content>.+)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return options;

            var content = match.Groups["content"].Value;
            var parts = SplitGradientParts(content);

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                var option = new ImageSetOption();

                // Parse url or image
                var urlMatch = Regex.Match(trimmed, @"url\(['""]?(?<url>[^'"")\s]+)['""]?\)", RegexOptions.IgnoreCase);
                if (urlMatch.Success)
                {
                    option.Url = urlMatch.Groups["url"].Value;
                    trimmed = trimmed.Replace(urlMatch.Value, "").Trim();
                }
                else
                {
                    // Try string format "image.png"
                    var strMatch = Regex.Match(trimmed, @"['""](?<url>[^'""]+)['""]");
                    if (strMatch.Success)
                    {
                        option.Url = strMatch.Groups["url"].Value;
                        trimmed = trimmed.Replace(strMatch.Value, "").Trim();
                    }
                }

                // Parse resolution
                var resMatch = Regex.Match(trimmed, @"(?<res>[\d.]+)(?<unit>x|dppx|dpi|dpcm)", RegexOptions.IgnoreCase);
                if (resMatch.Success)
                {
                    double res;
                    if (double.TryParse(resMatch.Groups["res"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out res))
                    {
                        var unit = resMatch.Groups["unit"].Value.ToLowerInvariant();
                        if (unit == "dpi") res = res / 96.0;
                        else if (unit == "dpcm") res = res / 37.8;
                        option.Resolution = res;
                    }
                }
                else
                {
                    option.Resolution = 1.0; // Default 1x
                }

                // Parse type hint
                var typeMatch = Regex.Match(trimmed, @"type\(['""]?(?<type>[^'"")\s]+)['""]?\)", RegexOptions.IgnoreCase);
                if (typeMatch.Success)
                {
                    option.Type = typeMatch.Groups["type"].Value;
                }

                if (!string.IsNullOrEmpty(option.Url))
                {
                    options.Add(option);
                }
            }

            return options;
        }

        /// <summary>
        /// Evaluates counter() CSS function
        /// Format: counter(name) or counter(name, style)
        /// </summary>
        public  string EvaluateCounter(string counterExpr, Dictionary<string, int> counters)
        {
            if (string.IsNullOrWhiteSpace(counterExpr)) return "0";

            var match = Regex.Match(counterExpr, @"counter\s*\(\s*(?<name>[a-zA-Z_-][a-zA-Z0-9_-]*)\s*(?:,\s*(?<style>[^)]+))?\s*\)", RegexOptions.IgnoreCase);
            if (!match.Success) return "0";

            var name = match.Groups["name"].Value;
            var style = match.Groups["style"].Success ? match.Groups["style"].Value.Trim() : "decimal";

            int value = 0;
            if (counters != null) counters.TryGetValue(name, out value);

            return FormatCounterValue(value, style);
        }

        /// <summary>
        /// Evaluates counters() CSS function (nested counters)
        /// Format: counters(name, string) or counters(name, string, style)
        /// </summary>
        public  string EvaluateCounters(string countersExpr, Dictionary<string, List<int>> nestedCounters)
        {
            if (string.IsNullOrWhiteSpace(countersExpr)) return "";

            var match = Regex.Match(countersExpr, @"counters\s*\(\s*(?<name>[a-zA-Z_-][a-zA-Z0-9_-]*)\s*,\s*['""](?<sep>[^'""]*)['""](?:\s*,\s*(?<style>[^)]+))?\s*\)", RegexOptions.IgnoreCase);
            if (!match.Success) return "";

            var name = match.Groups["name"].Value;
            var separator = match.Groups["sep"].Value;
            var style = match.Groups["style"].Success ? match.Groups["style"].Value.Trim() : "decimal";

            if (nestedCounters == null || !nestedCounters.ContainsKey(name))
                return "0";

            var values = nestedCounters[name];
            return string.Join(separator, values.Select(v => FormatCounterValue(v, style)));
        }

        /// <summary>
        /// Formats a counter value according to list-style-type
        /// </summary>
        private static string FormatCounterValue(int value, string style)
        {
            style = (style ?? "decimal").ToLowerInvariant().Trim();

            switch (style)
            {
                case "decimal":
                    return value.ToString(CultureInfo.InvariantCulture);
                case "decimal-leading-zero":
                    return value.ToString("D2", CultureInfo.InvariantCulture);
                case "lower-roman":
                    return ToRoman(value).ToLowerInvariant();
                case "upper-roman":
                    return ToRoman(value);
                case "lower-alpha":
                case "lower-latin":
                    return value > 0 && value <= 26 ? ((char)('a' + value - 1)).ToString() : value.ToString();
                case "upper-alpha":
                case "upper-latin":
                    return value > 0 && value <= 26 ? ((char)('A' + value - 1)).ToString() : value.ToString();
                case "lower-greek":
                    return value > 0 && value <= 24 ? ((char)('α' + value - 1)).ToString() : value.ToString();
                case "disc":
                    return "•";
                case "circle":
                    return "○";
                case "square":
                    return "■";
                case "none":
                    return "";
                default:
                    return value.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string ToRoman(int number)
        {
            if (number <= 0 || number > 3999) return number.ToString();
            
            var result = new StringBuilder();
            var romanNumerals = new[] {
                (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
                (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
                (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
            };

            foreach (var (val, numeral) in romanNumerals)
            {
                while (number >= val)
                {
                    result.Append(numeral);
                    number -= val;
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Evaluates attr() CSS function
        /// Format: attr(name) or attr(name type, fallback)
        /// </summary>
        public static string EvaluateAttr(string attrExpr, HtmlNode node)
        {
            if (string.IsNullOrWhiteSpace(attrExpr) || node == null) return "";

            // Match attr(name) or attr(name type, fallback) or attr(name, fallback)
            var match = Regex.Match(attrExpr, @"attr\s*\(\s*(?<name>[a-zA-Z_-][a-zA-Z0-9_-]*)(?:\s+(?<type>[a-z%]+))?(?:\s*,\s*(?<fallback>[^)]*))?\s*\)", RegexOptions.IgnoreCase);
            if (!match.Success) return "";

            var attrName = match.Groups["name"].Value;
            var attrType = match.Groups["type"].Success ? match.Groups["type"].Value.ToLowerInvariant() : "string";
            var fallback = match.Groups["fallback"].Success ? match.Groups["fallback"].Value.Trim() : "";

            string value;
            if (!node.Attributes.TryGetValue(attrName, out value))
            {
                return fallback;
            }

            // Type conversion
            switch (attrType)
            {
                case "string":
                    return value;
                case "url":
                    return "url(\"" + value + "\")";
                case "integer":
                case "number":
                    double num;
                    if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                        return num.ToString(CultureInfo.InvariantCulture);
                    return fallback;
                case "length":
                case "px":
                    return value.EndsWith("px") ? value : value + "px";
                case "em":
                    return value.EndsWith("em") ? value : value + "em";
                case "%":
                case "percentage":
                    return value.EndsWith("%") ? value : value + "%";
                case "color":
                    return value; // Return as-is, assume valid color
                case "angle":
                case "deg":
                    return value.EndsWith("deg") ? value : value + "deg";
                case "time":
                case "s":
                    return value.EndsWith("s") ? value : value + "s";
                default:
                    return value;
            }
        }

        /// <summary>
        /// Evaluates env() CSS function for environment variables
        /// Format: env(name) or env(name, fallback)
        /// </summary>
        public static string EvaluateEnv(string envExpr)
        {
            if (string.IsNullOrWhiteSpace(envExpr)) return "0px";

            var match = Regex.Match(envExpr, @"env\s*\(\s*(?<name>[a-zA-Z_-][a-zA-Z0-9_-]*)(?:\s*,\s*(?<fallback>[^)]*))?\s*\)", RegexOptions.IgnoreCase);
            if (!match.Success) return "0px";

            var envName = match.Groups["name"].Value.ToLowerInvariant();
            var fallback = match.Groups["fallback"].Success ? match.Groups["fallback"].Value.Trim() : "0px";

            // Standard environment variables (safe-area-inset-*)
            switch (envName)
            {
                case "safe-area-inset-top":
                    return "0px"; // No notch in desktop browser
                case "safe-area-inset-right":
                    return "0px";
                case "safe-area-inset-bottom":
                    return "0px";
                case "safe-area-inset-left":
                    return "0px";
                case "titlebar-area-x":
                    return "0px";
                case "titlebar-area-y":
                    return "0px";
                case "titlebar-area-width":
                    return "100%";
                case "titlebar-area-height":
                    return "0px";
                case "keyboard-inset-top":
                case "keyboard-inset-right":
                case "keyboard-inset-bottom":
                case "keyboard-inset-left":
                case "keyboard-inset-width":
                case "keyboard-inset-height":
                    return "0px";
                default:
                    return fallback;
            }
        }

        /// <summary>
        /// Parses CSS transform property value into a list of transform functions
        /// </summary>
        public static List<CssTransform> ParseTransform(string transformValue)
        {
            var transforms = new List<CssTransform>();
            if (string.IsNullOrWhiteSpace(transformValue) || transformValue.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return transforms;
            }

            var funcRegex = new Regex(@"(?<func>[a-zA-Z3]+)\((?<args>[^)]+)\)", RegexOptions.IgnoreCase);
            foreach (Match match in funcRegex.Matches(transformValue))
            {
                var funcName = match.Groups["func"].Value.ToLowerInvariant();
                var argsStr = match.Groups["args"].Value;

                var transform = new CssTransform { Function = funcName };

                var argParts = argsStr.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var arg in argParts)
                {
                    var trimmed = arg.Trim();
                    double value = 0;

                    if (trimmed.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
                    {
                        double.TryParse(trimmed.Replace("deg", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
                    }
                    else if (trimmed.EndsWith("rad", StringComparison.OrdinalIgnoreCase))
                    {
                        double.TryParse(trimmed.Replace("rad", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
                        value = value * (180.0 / Math.PI); // Convert to degrees
                    }
                    else if (trimmed.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
                    {
                        double.TryParse(trimmed.Replace("turn", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
                        value = value * 360.0; // Convert to degrees
                    }
                    else if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                    {
                        double.TryParse(trimmed.Replace("px", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
                    }
                    else if (trimmed.EndsWith("%", StringComparison.Ordinal))
                    {
                        double.TryParse(trimmed.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
                    }
                    else
                    {
                        double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
                    }

                    transform.Args.Add(value);
                }

                transforms.Add(transform);
            }

            return transforms;
        }

        /// <summary>
        /// Parses CSS transition property value into a list of transitions
        /// </summary>
        public static List<CssTransition> ParseTransition(string transitionValue)
        {
            var transitions = new List<CssTransition>();
            if (string.IsNullOrWhiteSpace(transitionValue) || transitionValue.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return transitions;
            }

            var parts = transitionValue.Split(',');
            foreach (var part in parts)
            {
                var tokens = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var transition = new CssTransition();

                int timeIndex = 0;
                foreach (var token in tokens)
                {
                    var t = token.Trim().ToLowerInvariant();

                    if (t.EndsWith("s") || t.EndsWith("ms"))
                    {
                        double duration = ParseTimeValue(t);
                        if (timeIndex == 0)
                        {
                            transition.Duration = duration;
                            timeIndex++;
                        }
                        else
                        {
                            transition.Delay = duration;
                        }
                    }
                    else if (IsTimingFunction(t))
                    {
                        transition.TimingFunction = t;
                    }
                    else
                    {
                        transition.Property = t;
                    }
                }

                transitions.Add(transition);
            }

            return transitions;
        }

        /// <summary>
        /// Parses CSS animation property value into an animation descriptor
        /// </summary>
        public static CssAnimation ParseAnimation(string animationValue)
        {
            var animation = new CssAnimation();
            if (string.IsNullOrWhiteSpace(animationValue) || animationValue.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return animation;
            }

            var tokens = animationValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int timeIndex = 0;

            foreach (var token in tokens)
            {
                var t = token.Trim().ToLowerInvariant();

                if (t.EndsWith("s") || t.EndsWith("ms"))
                {
                    double time = ParseTimeValue(t);
                    if (timeIndex == 0)
                    {
                        animation.Duration = time;
                        timeIndex++;
                    }
                    else
                    {
                        animation.Delay = time;
                    }
                }
                else if (IsTimingFunction(t))
                {
                    animation.TimingFunction = t;
                }
                else if (t == "infinite")
                {
                    animation.IterationCount = -1; // Use -1 to represent infinite
                }
                else if (t == "normal" || t == "reverse" || t == "alternate" || t == "alternate-reverse")
                {
                    animation.Direction = t;
                }
                else if (t == "none" || t == "forwards" || t == "backwards" || t == "both")
                {
                    animation.FillMode = t;
                }
                else if (t == "running" || t == "paused")
                {
                    animation.PlayState = t;
                }
                else
                {
                    int iterations;
                    if (int.TryParse(t, out iterations))
                    {
                        animation.IterationCount = iterations;
                    }
                    else
                    {
                        animation.Name = t;
                    }
                }
            }

            return animation;
        }

        private static double ParseTimeValue(string value)
        {
            value = value.Trim().ToLowerInvariant();
            double result = 0;

            if (value.EndsWith("ms"))
            {
                double.TryParse(value.Replace("ms", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
                result = result / 1000.0; // Convert to seconds
            }
            else if (value.EndsWith("s"))
            {
                double.TryParse(value.Replace("s", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
            }

            return result;
        }

        private static bool IsTimingFunction(string value)
        {
            var functions = new[] { "ease", "ease-in", "ease-out", "ease-in-out", "linear", "step-start", "step-end" };
            return functions.Contains(value) || value.StartsWith("cubic-bezier") || value.StartsWith("steps");
        }

        /// <summary>
        /// Parses CSS box-shadow property value into a list of shadows
        /// Format: [inset] offset-x offset-y [blur-radius] [spread-radius] [color]
        /// </summary>
        public static List<CssBoxShadow> ParseBoxShadow(string shadowValue)
        {
            var shadows = new List<CssBoxShadow>();
            if (string.IsNullOrWhiteSpace(shadowValue) || shadowValue.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return shadows;
            }

            // Split multiple shadows by comma (but not inside functions)
            var shadowParts = SplitShadowValues(shadowValue);

            foreach (var part in shadowParts)
            {
                var shadow = new CssBoxShadow();
                var tokens = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                // Check for inset
                if (tokens.Any(t => t.Equals("inset", StringComparison.OrdinalIgnoreCase)))
                {
                    shadow.Inset = true;
                    tokens.RemoveAll(t => t.Equals("inset", StringComparison.OrdinalIgnoreCase));
                }

                // Extract color (could be at start or end)
                string colorToken = null;
                for (int i = tokens.Count - 1; i >= 0; i--)
                {
                    if (IsColorValue(tokens[i]))
                    {
                        colorToken = tokens[i];
                        tokens.RemoveAt(i);
                        break;
                    }
                }
                shadow.Color = colorToken ?? "rgba(0,0,0,1)";

                // Remaining should be: offset-x, offset-y, [blur], [spread]
                if (tokens.Count >= 2)
                {
                    shadow.OffsetX = ParseShadowLength(tokens[0]);
                    shadow.OffsetY = ParseShadowLength(tokens[1]);
                }
                if (tokens.Count >= 3)
                {
                    shadow.BlurRadius = ParseShadowLength(tokens[2]);
                }
                if (tokens.Count >= 4)
                {
                    shadow.SpreadRadius = ParseShadowLength(tokens[3]);
                }

                shadows.Add(shadow);
            }

            return shadows;
        }

        /// <summary>
        /// Parses CSS text-shadow property value into a list of shadows
        /// Format: offset-x offset-y [blur-radius] [color]
        /// </summary>
        public static List<CssTextShadow> ParseTextShadow(string shadowValue)
        {
            var shadows = new List<CssTextShadow>();
            if (string.IsNullOrWhiteSpace(shadowValue) || shadowValue.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return shadows;
            }

            var shadowParts = SplitShadowValues(shadowValue);

            foreach (var part in shadowParts)
            {
                var shadow = new CssTextShadow();
                var tokens = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                // Extract color
                string colorToken = null;
                for (int i = tokens.Count - 1; i >= 0; i--)
                {
                    if (IsColorValue(tokens[i]))
                    {
                        colorToken = tokens[i];
                        tokens.RemoveAt(i);
                        break;
                    }
                }
                shadow.Color = colorToken ?? "rgba(0,0,0,1)";

                // Remaining: offset-x, offset-y, [blur]
                if (tokens.Count >= 2)
                {
                    shadow.OffsetX = ParseShadowLength(tokens[0]);
                    shadow.OffsetY = ParseShadowLength(tokens[1]);
                }
                if (tokens.Count >= 3)
                {
                    shadow.BlurRadius = ParseShadowLength(tokens[2]);
                }

                shadows.Add(shadow);
            }

            return shadows;
        }

        /// <summary>
        /// Parses CSS filter property value into a list of filter functions
        /// </summary>
        public static List<CssFilter> ParseFilter(string filterValue)
        {
            var filters = new List<CssFilter>();
            if (string.IsNullOrWhiteSpace(filterValue) || filterValue.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return filters;
            }

            var funcRegex = new Regex(@"(?<func>[a-z-]+)\((?<args>[^)]+)\)", RegexOptions.IgnoreCase);
            foreach (Match match in funcRegex.Matches(filterValue))
            {
                var funcName = match.Groups["func"].Value.ToLowerInvariant();
                var argsStr = match.Groups["args"].Value.Trim();

                var filter = new CssFilter { Function = funcName };

                // Parse the argument
                if (argsStr.EndsWith("%"))
                {
                    double val;
                    if (double.TryParse(argsStr.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                    {
                        filter.Value = val / 100.0;
                        filter.Unit = "%";
                    }
                }
                else if (argsStr.EndsWith("px"))
                {
                    double val;
                    if (double.TryParse(argsStr.Replace("px", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                    {
                        filter.Value = val;
                        filter.Unit = "px";
                    }
                }
                else if (argsStr.EndsWith("deg"))
                {
                    double val;
                    if (double.TryParse(argsStr.Replace("deg", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                    {
                        filter.Value = val;
                        filter.Unit = "deg";
                    }
                }
                else
                {
                    double val;
                    if (double.TryParse(argsStr, NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                    {
                        filter.Value = val;
                    }
                }

                filters.Add(filter);
            }

            return filters;
        }

        /// <summary>
        /// Parses CSS backdrop-filter property value into filter descriptors
        /// </summary>
        public static List<CssFilter> ParseBackdropFilter(string filterValue)
        {
            return ParseFilter(filterValue);
        }

        /// <summary>
        /// Parses CSS linear-gradient() into a gradient descriptor
        /// </summary>
        public static CssGradient ParseLinearGradient(string gradientValue)
        {
            var gradient = new CssGradient { Type = "linear" };
            if (string.IsNullOrWhiteSpace(gradientValue))
            {
                return gradient;
            }

            // Extract content inside linear-gradient(...)
            var match = Regex.Match(gradientValue, @"linear-gradient\s*\((?<content>.+)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success)
            {
                return gradient;
            }

            var content = match.Groups["content"].Value.Trim();
            var parts = SplitGradientParts(content);

            int startIndex = 0;

            // Check for direction
            if (parts.Count > 0)
            {
                var first = parts[0].Trim().ToLowerInvariant();
                if (first.EndsWith("deg"))
                {
                    double angle;
                    if (double.TryParse(first.Replace("deg", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out angle))
                    {
                        gradient.Angle = angle;
                        startIndex = 1;
                    }
                }
                else if (first.StartsWith("to "))
                {
                    gradient.Direction = first;
                    gradient.Angle = DirectionToAngle(first);
                    startIndex = 1;
                }
            }

            // Parse color stops
            for (int i = startIndex; i < parts.Count; i++)
            {
                var stop = ParseColorStop(parts[i].Trim());
                if (stop != null)
                {
                    gradient.ColorStops.Add(stop);
                }
            }

            // Auto-calculate positions if not specified
            AutoCalculateStopPositions(gradient.ColorStops);

            return gradient;
        }

        /// <summary>
        /// Parses CSS radial-gradient() into a gradient descriptor
        /// </summary>
        public static CssGradient ParseRadialGradient(string gradientValue)
        {
            var gradient = new CssGradient { Type = "radial" };
            if (string.IsNullOrWhiteSpace(gradientValue))
            {
                return gradient;
            }

            var match = Regex.Match(gradientValue, @"radial-gradient\s*\((?<content>.+)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success)
            {
                return gradient;
            }

            var content = match.Groups["content"].Value.Trim();
            var parts = SplitGradientParts(content);

            int startIndex = 0;

            // Check for shape/size/position
            if (parts.Count > 0)
            {
                var first = parts[0].Trim().ToLowerInvariant();
                if (first.Contains("circle") || first.Contains("ellipse") || first.Contains("at ") ||
                    first.Contains("closest") || first.Contains("farthest"))
                {
                    gradient.Shape = first;
                    startIndex = 1;
                }
            }

            // Parse color stops
            for (int i = startIndex; i < parts.Count; i++)
            {
                var stop = ParseColorStop(parts[i].Trim());
                if (stop != null)
                {
                    gradient.ColorStops.Add(stop);
                }
            }

            AutoCalculateStopPositions(gradient.ColorStops);

            return gradient;
        }

        /// <summary>
        /// Parses CSS conic-gradient() into a gradient descriptor
        /// </summary>
        public static CssGradient ParseConicGradient(string gradientValue)
        {
            var gradient = new CssGradient { Type = "conic" };
            if (string.IsNullOrWhiteSpace(gradientValue))
            {
                return gradient;
            }

            var match = Regex.Match(gradientValue, @"conic-gradient\s*\((?<content>.+)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success)
            {
                return gradient;
            }

            var content = match.Groups["content"].Value.Trim();
            var parts = SplitGradientParts(content);

            int startIndex = 0;

            // Check for "from Xdeg at X Y"
            if (parts.Count > 0)
            {
                var first = parts[0].Trim().ToLowerInvariant();
                if (first.StartsWith("from ") || first.Contains("at "))
                {
                    var fromMatch = Regex.Match(first, @"from\s+(?<angle>[\d.]+)deg", RegexOptions.IgnoreCase);
                    if (fromMatch.Success)
                    {
                        double angle;
                        if (double.TryParse(fromMatch.Groups["angle"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out angle))
                        {
                            gradient.Angle = angle;
                        }
                    }
                    startIndex = 1;
                }
            }

            // Parse color stops
            for (int i = startIndex; i < parts.Count; i++)
            {
                var stop = ParseColorStop(parts[i].Trim());
                if (stop != null)
                {
                    gradient.ColorStops.Add(stop);
                }
            }

            AutoCalculateStopPositions(gradient.ColorStops);

            return gradient;
        }

        /// <summary>
        /// Parses CSS clip-path property into a clip path descriptor
        /// Supports: inset(), circle(), ellipse(), polygon(), path()
        /// </summary>
        public static CssClipPath ParseClipPath(string clipPathValue)
        {
            var clipPath = new CssClipPath();
            if (string.IsNullOrWhiteSpace(clipPathValue) || clipPathValue.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                clipPath.Type = "none";
                return clipPath;
            }

            clipPathValue = clipPathValue.Trim().ToLowerInvariant();

            // Check for url() reference
            if (clipPathValue.StartsWith("url("))
            {
                clipPath.Type = "url";
                var urlMatch = Regex.Match(clipPathValue, @"url\(['""]?(?<url>[^'"")\s]+)['""]?\)", RegexOptions.IgnoreCase);
                if (urlMatch.Success)
                {
                    clipPath.Url = urlMatch.Groups["url"].Value;
                }
                return clipPath;
            }

            // Parse basic shapes
            if (clipPathValue.StartsWith("inset("))
            {
                clipPath.Type = "inset";
                var match = Regex.Match(clipPathValue, @"inset\((?<args>[^)]+)\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var args = match.Groups["args"].Value.Trim();
                    // Format: inset(top right bottom left round border-radius)
                    var roundIdx = args.IndexOf("round", StringComparison.OrdinalIgnoreCase);
                    if (roundIdx >= 0)
                    {
                        clipPath.BorderRadius = args.Substring(roundIdx + 5).Trim();
                        args = args.Substring(0, roundIdx).Trim();
                    }
                    clipPath.Insets = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
                return clipPath;
            }

            if (clipPathValue.StartsWith("circle("))
            {
                clipPath.Type = "circle";
                var match = Regex.Match(clipPathValue, @"circle\((?<args>[^)]*)\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var args = match.Groups["args"].Value.Trim();
                    // Format: circle(radius at x y) or circle(radius)
                    var atIdx = args.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
                    if (atIdx >= 0)
                    {
                        clipPath.Radius = args.Substring(0, atIdx).Trim();
                        clipPath.Position = args.Substring(atIdx + 4).Trim();
                    }
                    else if (!string.IsNullOrEmpty(args))
                    {
                        clipPath.Radius = args;
                    }
                }
                return clipPath;
            }

            if (clipPathValue.StartsWith("ellipse("))
            {
                clipPath.Type = "ellipse";
                var match = Regex.Match(clipPathValue, @"ellipse\((?<args>[^)]*)\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var args = match.Groups["args"].Value.Trim();
                    // Format: ellipse(rx ry at x y)
                    var atIdx = args.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
                    if (atIdx >= 0)
                    {
                        var radii = args.Substring(0, atIdx).Trim().Split(' ');
                        if (radii.Length >= 2)
                        {
                            clipPath.RadiusX = radii[0];
                            clipPath.RadiusY = radii[1];
                        }
                        clipPath.Position = args.Substring(atIdx + 4).Trim();
                    }
                    else
                    {
                        var radii = args.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (radii.Length >= 2)
                        {
                            clipPath.RadiusX = radii[0];
                            clipPath.RadiusY = radii[1];
                        }
                    }
                }
                return clipPath;
            }

            if (clipPathValue.StartsWith("polygon("))
            {
                clipPath.Type = "polygon";
                var match = Regex.Match(clipPathValue, @"polygon\((?<args>[^)]+)\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var args = match.Groups["args"].Value.Trim();
                    // Format: polygon(fill-rule?, x1 y1, x2 y2, ...)
                    var points = args.Split(',');
                    foreach (var point in points)
                    {
                        var trimmed = point.Trim();
                        if (trimmed == "nonzero" || trimmed == "evenodd")
                        {
                            clipPath.FillRule = trimmed;
                            continue;
                        }
                        var coords = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (coords.Length >= 2)
                        {
                            clipPath.Points.Add((coords[0], coords[1]));
                        }
                    }
                }
                return clipPath;
            }

            if (clipPathValue.StartsWith("path("))
            {
                clipPath.Type = "path";
                var match = Regex.Match(clipPathValue, @"path\(['""](?<path>[^'""]+)['""]\)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    clipPath.PathData = match.Groups["path"].Value;
                }
                return clipPath;
            }

            // Keywords
            if (clipPathValue == "margin-box" || clipPathValue == "border-box" ||
                clipPathValue == "padding-box" || clipPathValue == "content-box" ||
                clipPathValue == "fill-box" || clipPathValue == "stroke-box" ||
                clipPathValue == "view-box")
            {
                clipPath.Type = "box";
                clipPath.Box = clipPathValue;
                return clipPath;
            }

            clipPath.Type = "none";
            return clipPath;
        }

        private static List<string> SplitShadowValues(string value)
        {
            var results = new List<string>();
            var sb = new StringBuilder();
            int depth = 0;

            foreach (char c in value)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;

                if (c == ',' && depth == 0)
                {
                    results.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (sb.Length > 0)
            {
                results.Add(sb.ToString());
            }

            return results;
        }

        private static List<string> SplitGradientParts(string content)
        {
            var parts = new List<string>();
            var sb = new StringBuilder();
            int depth = 0;

            foreach (char c in content)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;

                if (c == ',' && depth == 0)
                {
                    parts.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (sb.Length > 0)
            {
                parts.Add(sb.ToString());
            }

            return parts;
        }

        private static double ParseShadowLength(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            value = value.Trim().ToLowerInvariant();

            double num;
            if (value.EndsWith("px"))
            {
                if (double.TryParse(value.Replace("px", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                    return num;
            }
            else if (value.EndsWith("em") || value.EndsWith("rem"))
            {
                if (double.TryParse(value.Replace("em", "").Replace("rem", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                    return num * 16.0;
            }
            else if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out num))
            {
                return num;
            }

            return 0;
        }

        public static bool IsColorValue(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            token = token.Trim().ToLowerInvariant();

            // Named colors
            if (NamedColors.Contains(token)) return true;

            // Hex colors
            if (token.StartsWith("#")) return true;

            // rgb/rgba/hsl/hsla
            if (token.StartsWith("rgb") || token.StartsWith("hsl")) return true;

            // transparent
            if (token == "transparent" || token == "currentcolor") return true;

            return false;
        }

        private static CssColorStop ParseColorStop(string stopStr)
        {
            if (string.IsNullOrWhiteSpace(stopStr)) return null;

            var stop = new CssColorStop();
            var tokens = stopStr.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                if (IsColorValue(token))
                {
                    stop.Color = token;
                }
                else if (token.EndsWith("%"))
                {
                    double pos;
                    if (double.TryParse(token.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out pos))
                    {
                        stop.Position = pos / 100.0;
                        stop.HasPosition = true;
                    }
                }
                else if (token.EndsWith("px"))
                {
                    double pos;
                    if (double.TryParse(token.Replace("px", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out pos))
                    {
                        stop.PositionPx = pos;
                        stop.HasPosition = true;
                    }
                }
            }

            return string.IsNullOrEmpty(stop.Color) ? null : stop;
        }

        private static double DirectionToAngle(string direction)
        {
            switch (direction.ToLowerInvariant())
            {
                case "to top": return 0;
                case "to top right": return 45;
                case "to right": return 90;
                case "to bottom right": return 135;
                case "to bottom": return 180;
                case "to bottom left": return 225;
                case "to left": return 270;
                case "to top left": return 315;
                default: return 180; // default is "to bottom"
            }
        }

        private static void AutoCalculateStopPositions(List<CssColorStop> stops)
        {
            if (stops.Count == 0) return;

            // Assign positions to stops without explicit positions
            var positioned = new List<int>();
            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i].HasPosition)
                {
                    positioned.Add(i);
                }
            }

            // First and last default to 0% and 100%
            if (!stops[0].HasPosition)
            {
                stops[0].Position = 0;
                stops[0].HasPosition = true;
            }
            if (!stops[stops.Count - 1].HasPosition)
            {
                stops[stops.Count - 1].Position = 1;
                stops[stops.Count - 1].HasPosition = true;
            }

            // Interpolate middle stops
            for (int i = 1; i < stops.Count - 1; i++)
            {
                if (!stops[i].HasPosition)
                {
                    // Find previous and next positioned stops
                    int prevIdx = i - 1;
                    int nextIdx = i + 1;
                    while (nextIdx < stops.Count && !stops[nextIdx].HasPosition)
                    {
                        nextIdx++;
                    }

                    double prevPos = stops[prevIdx].Position;
                    double nextPos = nextIdx < stops.Count ? stops[nextIdx].Position : 1;
                    int count = nextIdx - prevIdx;
                    stops[i].Position = prevPos + (nextPos - prevPos) * (i - prevIdx) / count;
                    stops[i].HasPosition = true;
                }
            }
        }

        public CssStyleSheet Parse(string cssText)
        {
            var sheet = new CssStyleSheet { Raw = cssText };
            if (string.IsNullOrWhiteSpace(cssText))
            {
                return sheet;
            }

            cssText = Regex.Replace(cssText, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

            int order = 0;

            // Parse and remove @charset (must be first rule if present)
            var charsetRegex = new Regex(@"^[\s]*@charset\s+['""](?<charset>[^'""]+)['""]\s*;", RegexOptions.IgnoreCase);
            var charsetMatch = charsetRegex.Match(cssText);
            if (charsetMatch.Success)
            {
                sheet.CustomProperties["@charset"] = charsetMatch.Groups["charset"].Value;
                cssText = charsetRegex.Replace(cssText, string.Empty);
            }

            // Parse @namespace rules
            var namespaceRegex = new Regex(@"@namespace\s+(?:(?<prefix>[a-zA-Z_][a-zA-Z0-9_-]*)\s+)?(?:url\(['""]?(?<url>[^'"")\s]+)['""]?\)|['""](?<url2>[^'""]+)['""])\s*;", RegexOptions.IgnoreCase);
            foreach (Match m in namespaceRegex.Matches(cssText))
            {
                var prefix = m.Groups["prefix"].Success ? m.Groups["prefix"].Value : "";
                var url = m.Groups["url"].Success ? m.Groups["url"].Value : m.Groups["url2"].Value;
                sheet.CustomProperties["@namespace:" + prefix] = url;
            }
            cssText = namespaceRegex.Replace(cssText, string.Empty);

            // Parse @import rules
            var importRegex = new Regex(@"@import\s+(?:url\(['""]?(?<url>[^'"")\s]+)['""]?\)|['""](?<url2>[^'""]+)['""])\s*;", RegexOptions.IgnoreCase);
            foreach (Match m in importRegex.Matches(cssText))
            {
                var url = m.Groups["url"].Success ? m.Groups["url"].Value : m.Groups["url2"].Value;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    _imports.Add(url.Trim());
                    sheet.Imports.Add(url.Trim());
                }
            }
            cssText = importRegex.Replace(cssText, string.Empty);

            // Parse @font-face rules
            var fontFaceRegex = new Regex(@"@font-face\s*\{(?<body>[^\}]*)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in fontFaceRegex.Matches(cssText))
            {
                var body = m.Groups["body"].Value;
                var fontFace = ParseFontFace(body);
                if (fontFace != null)
                {
                    _fontFaces.Add(fontFace);
                    sheet.FontFaces.Add(fontFace);
                }
            }
            cssText = fontFaceRegex.Replace(cssText, string.Empty);

            // Parse @keyframes rules
            var keyframesRegex = new Regex(@"@(?:-webkit-|-moz-|-o-)?keyframes\s+(?<name>[^\s\{]+)\s*\{(?<body>(?:[^\{\}]|\{[^\}]*\})*)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in keyframesRegex.Matches(cssText))
            {
                var name = m.Groups["name"].Value.Trim();
                var body = m.Groups["body"].Value;
                var keyframes = ParseKeyframes(name, body);
                if (keyframes != null)
                {
                    _keyframes[name] = keyframes;
                    sheet.Keyframes[name] = keyframes;
                }
            }
            cssText = keyframesRegex.Replace(cssText, string.Empty);

            // Parse @page rules (for print stylesheets)
            var pageRegex = new Regex(@"@page\s*(?<selector>[^\{]*)\{(?<body>[^\}]*)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in pageRegex.Matches(cssText))
            {
                var selector = m.Groups["selector"].Value.Trim();
                var body = m.Groups["body"].Value;
                // Store page rules with @page prefix
                var pageSelector = "@page" + (string.IsNullOrEmpty(selector) ? "" : " " + selector);
                ParseRuleBlocks(sheet, body, pageSelector, ref order);
            }
            cssText = pageRegex.Replace(cssText, string.Empty);

            // Parse @property rules (CSS Houdini custom property registration)
            var propertyRegex = new Regex(@"@property\s+(?<name>--[a-zA-Z0-9_-]+)\s*\{(?<body>[^\}]*)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in propertyRegex.Matches(cssText))
            {
                var propName = m.Groups["name"].Value.Trim();
                var body = m.Groups["body"].Value;
                
                // Parse property definition
                var syntaxMatch = Regex.Match(body, @"syntax\s*:\s*['""](?<syntax>[^'""]+)['""]", RegexOptions.IgnoreCase);
                var inheritsMatch = Regex.Match(body, @"inherits\s*:\s*(?<val>true|false)", RegexOptions.IgnoreCase);
                var initialMatch = Regex.Match(body, @"initial-value\s*:\s*(?<val>[^;]+)", RegexOptions.IgnoreCase);
                
                // Store registered property metadata
                if (syntaxMatch.Success)
                    sheet.CustomProperties[propName + ":syntax"] = syntaxMatch.Groups["syntax"].Value;
                if (inheritsMatch.Success)
                    sheet.CustomProperties[propName + ":inherits"] = inheritsMatch.Groups["val"].Value;
                if (initialMatch.Success)
                    sheet.CustomProperties[propName + ":initial"] = initialMatch.Groups["val"].Value.Trim();
            }
            cssText = propertyRegex.Replace(cssText, string.Empty);

            // handle simple @media blocks
            var mediaRegex = new Regex(@"@media\s*(?<cond>[^\{]+)\{(?<body>(?:[^\{\}]|\{[^\}]*\})*)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in mediaRegex.Matches(cssText))
            {
                var cond = m.Groups["cond"].Value.Trim();
                var body = m.Groups["body"].Value;
                ParseRuleBlocks(sheet, body, cond, ref order);
            }

            // remove media blocks processed
            cssText = mediaRegex.Replace(cssText, string.Empty);

            // Parse @container blocks (Container Queries)
            var containerRegex = new Regex(@"@container\s*(?<name>[a-zA-Z_-][a-zA-Z0-9_-]*)?\s*(?<cond>\([^\{]+\))?\s*\{(?<body>(?:[^\{\}]|\{[^\}]*\})*)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in containerRegex.Matches(cssText))
            {
                var containerName = m.Groups["name"].Success ? m.Groups["name"].Value.Trim() : null;
                var cond = m.Groups["cond"].Success ? m.Groups["cond"].Value.Trim() : "";
                var body = m.Groups["body"].Value;
                // Container queries are stored with a special marker, evaluated at layout time
                var containerCondition = "@container" + (containerName != null ? " " + containerName : "") + " " + cond;
                ParseRuleBlocks(sheet, body, containerCondition, ref order);
            }

            // remove @container blocks processed
            cssText = containerRegex.Replace(cssText, string.Empty);

            // Parse @layer blocks (Cascade Layers)
            var layerRegex = new Regex(@"@layer\s+(?<names>[^{;]+?)(?:\s*;|\s*\{(?<body>(?:[^\{\}]|\{[^\}]*\})*)\})", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in layerRegex.Matches(cssText))
            {
                var layerNames = m.Groups["names"].Value.Trim();
                // Register each declared layer in order
                foreach (var ln in layerNames.Split(',').Select(n => n.Trim()).Where(n => !string.IsNullOrEmpty(n)))
                {
                    RegisterLayer(ln);
                }

                if (m.Groups["body"].Success)
                {
                    var body = m.Groups["body"].Value;
                    var firstLayer = layerNames.Split(',').Select(n => n.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n));
                    ParseRuleBlocks(sheet, body, mediaCondition: null, ref order, layerName: firstLayer);
                }
                // else: just layer declaration, no rules
            }

            // remove @layer blocks processed
            cssText = layerRegex.Replace(cssText, string.Empty);

            // Parse @scope blocks
            var scopeRegex = new Regex(@"@scope\s*(?:\(\s*(?<root>[^)]+)\s*\))?\s*(?:to\s*\(\s*(?<limit>[^)]+)\s*\))?\s*\{(?<body>(?:[^\{\}]|\{[^\}]*\})*)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in scopeRegex.Matches(cssText))
            {
                var scopeRoot = m.Groups["root"].Success ? m.Groups["root"].Value.Trim() : null;
                var scopeLimit = m.Groups["limit"].Success ? m.Groups["limit"].Value.Trim() : null;
                var body = m.Groups["body"].Value;
                // Store scope info for selector matching
                var scopeCondition = "@scope" + (scopeRoot != null ? " (" + scopeRoot + ")" : "") + (scopeLimit != null ? " to (" + scopeLimit + ")" : "");
                ParseRuleBlocks(sheet, body, scopeCondition, ref order);
            }

            // remove @scope blocks processed
            cssText = scopeRegex.Replace(cssText, string.Empty);

            // Parse @starting-style blocks (for entry animations)
            var startingStyleRegex = new Regex(@"@starting-style\s*\{(?<body>(?:[^\{\}]|\{[^\}]*\})*)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in startingStyleRegex.Matches(cssText))
            {
                var body = m.Groups["body"].Value;
                // Starting-style rules apply before element enters DOM
                ParseRuleBlocks(sheet, body, "@starting-style", ref order);
            }

            // remove @starting-style blocks processed
            cssText = startingStyleRegex.Replace(cssText, string.Empty);

            // Parse @supports blocks
            var supportsRegex = new Regex(@"@supports\s*(?<cond>[^\{]+)\{(?<body>(?:[^\{\}]|\{[^\}]*\})*)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match m in supportsRegex.Matches(cssText))
            {
                var cond = m.Groups["cond"].Value.Trim();
                var body = m.Groups["body"].Value;
                if (EvaluateSupports(cond))
                {
                    ParseRuleBlocks(sheet, body, null, ref order);
                }
            }

            // remove @supports blocks processed
            cssText = supportsRegex.Replace(cssText, string.Empty);

            // top-level rules
            ParseRuleBlocks(sheet, cssText, null, ref order);

            return sheet;
        }

        /// <summary>
        /// Evaluates a @supports condition to determine if the CSS should be applied
        /// </summary>
        private bool EvaluateSupports(string condition)
        {
            if (string.IsNullOrWhiteSpace(condition))
            {
                return false;
            }

            condition = condition.Trim();

            // Handle "not" operator
            if (condition.StartsWith("not ", StringComparison.OrdinalIgnoreCase) ||
                condition.StartsWith("not(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = condition.Substring(3).Trim();
                if (inner.StartsWith("(") && inner.EndsWith(")"))
                {
                    inner = inner.Substring(1, inner.Length - 2);
                }
                return !EvaluateSupports(inner);
            }

            // Handle "and" operator (split and check both sides)
            var andParts = SplitSupportsCondition(condition, "and");
            if (andParts.Count > 1)
            {
                return andParts.All(p => EvaluateSupports(p.Trim()));
            }

            // Handle "or" operator (split and check if any matches)
            var orParts = SplitSupportsCondition(condition, "or");
            if (orParts.Count > 1)
            {
                return orParts.Any(p => EvaluateSupports(p.Trim()));
            }

            // Handle parenthesized condition
            if (condition.StartsWith("(") && condition.EndsWith(")"))
            {
                return EvaluateSupports(condition.Substring(1, condition.Length - 2));
            }

            // Handle selector() function
            if (condition.StartsWith("selector(", StringComparison.OrdinalIgnoreCase))
            {
                // selector() checks if a selector is supported
                var selectorMatch = Regex.Match(condition, @"selector\s*\((?<sel>[^)]+)\)", RegexOptions.IgnoreCase);
                if (selectorMatch.Success)
                {
                    var sel = selectorMatch.Groups["sel"].Value.Trim();
                    return IsSelectorSupported(sel);
                }
                return false;
            }

            // Check for property:value pair
            var colonIdx = condition.IndexOf(':');
            if (colonIdx > 0)
            {
                var property = condition.Substring(0, colonIdx).Trim().ToLowerInvariant();
                var value = condition.Substring(colonIdx + 1).Trim().TrimEnd(')');

                return IsPropertySupported(property, value);
            }

            return false;
        }

        private List<string> SplitSupportsCondition(string condition, string op)
        {
            var parts = new List<string>();
            int depth = 0;
            int start = 0;
            string pattern = " " + op + " ";

            for (int i = 0; i < condition.Length; i++)
            {
                if (condition[i] == '(') depth++;
                else if (condition[i] == ')') depth--;
                else if (depth == 0 && i + pattern.Length <= condition.Length)
                {
                    if (condition.Substring(i, pattern.Length).Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(condition.Substring(start, i - start));
                        i += pattern.Length - 1;
                        start = i + 1;
                    }
                }
            }

            if (start < condition.Length)
            {
                parts.Add(condition.Substring(start));
            }

            return parts.Count > 1 ? parts : new List<string> { condition };
        }

        private bool IsSelectorSupported(string selector)
        {
            // Check if selector uses supported features
            var supportedSelectors = new[]
            {
                ":has(", ":is(", ":where(", ":not(", ":first-child", ":last-child",
                ":nth-child(", ":nth-of-type(", ":focus", ":hover", ":active",
                ":empty", ":root", ":lang(", ":focus-within", ":focus-visible",
                "::before", "::after", "[", ".", "#", ">", "+", "~"
            };

            // If it's just a basic selector, it's supported
            if (!selector.Contains(":") && !selector.Contains("["))
            {
                return true;
            }

            // Check if it uses any of our supported selectors
            foreach (var supported in supportedSelectors)
            {
                if (selector.Contains(supported)) return true;
            }

            return true; // Assume basic selectors are supported
        }

        private bool IsPropertySupported(string property, string value)
        {
            // List of supported CSS properties
            var supportedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Box model
                "display", "position", "top", "right", "bottom", "left", "float", "clear",
                "width", "height", "min-width", "min-height", "max-width", "max-height",
                "margin", "margin-top", "margin-right", "margin-bottom", "margin-left",
                "padding", "padding-top", "padding-right", "padding-bottom", "padding-left",
                "border", "border-width", "border-style", "border-color",
                "border-top", "border-right", "border-bottom", "border-left",
                "border-radius", "box-sizing", "overflow", "overflow-x", "overflow-y",
                
                // Flexbox
                "flex", "flex-direction", "flex-wrap", "flex-flow", "justify-content",
                "align-items", "align-content", "flex-grow", "flex-shrink", "flex-basis",
                "align-self", "order", "gap", "row-gap", "column-gap",
                
                // Grid
                "grid", "grid-template-columns", "grid-template-rows", "grid-template-areas",
                "grid-column", "grid-row", "grid-area", "grid-gap", "place-items", "place-content",
                
                // Typography
                "font", "font-family", "font-size", "font-weight", "font-style",
                "line-height", "text-align", "text-decoration", "text-transform",
                "color", "background", "background-color", "background-image",
                
                // Visual effects
                "opacity", "visibility", "z-index", "transform", "transform-origin",
                "transition", "animation", "box-shadow", "text-shadow", "filter",
                
                // Lists
                "list-style", "list-style-type", "list-style-position", "list-style-image"
            };

            // Check if property is supported
            if (!supportedProperties.Contains(property))
            {
                return false;
            }

            // Check specific value support
            if (property == "display")
            {
                var supportedDisplays = new[] { "block", "inline", "inline-block", "flex", "inline-flex", "grid", "inline-grid", "none", "table", "table-row", "table-cell" };
                return supportedDisplays.Any(d => value.Equals(d, StringComparison.OrdinalIgnoreCase));
            }

            if (property == "position")
            {
                var supportedPositions = new[] { "static", "relative", "absolute", "fixed", "sticky" };
                return supportedPositions.Any(p => value.Equals(p, StringComparison.OrdinalIgnoreCase));
            }

            // For most properties, we support them
            return true;
        }

        private CssFontFace ParseFontFace(string body)
        {
            var fontFace = new CssFontFace();
            var declarations = body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var decl in declarations)
            {
                var kv = decl.Split(new[] { ':' }, 2);
                if (kv.Length != 2) continue;

                var name = kv[0].Trim().ToLowerInvariant();
                var value = kv[1].Trim().Trim('"', '\'');

                switch (name)
                {
                    case "font-family":
                        fontFace.FontFamily = value.Trim('"', '\'');
                        break;
                    case "src":
                        fontFace.Src = value;
                        break;
                    case "font-weight":
                        fontFace.FontWeight = value;
                        break;
                    case "font-style":
                        fontFace.FontStyle = value;
                        break;
                    case "font-display":
                        fontFace.FontDisplay = value;
                        break;
                }
            }
            return string.IsNullOrEmpty(fontFace.FontFamily) ? null : fontFace;
        }

        private CssKeyframesRule ParseKeyframes(string name, string body)
        {
            var keyframes = new CssKeyframesRule { Name = name };
            var frameRegex = new Regex(@"(?<selector>[\d%,\s]+|from|to)\s*\{(?<decls>[^\}]*)\}", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
            foreach (Match m in frameRegex.Matches(body))
            {
                var selector = m.Groups["selector"].Value.Trim().ToLowerInvariant();
                var decls = m.Groups["decls"].Value;

                var frame = new CssKeyframe();
                
                if (selector == "from")
                {
                    frame.Percentage = 0;
                }
                else if (selector == "to")
                {
                    frame.Percentage = 100;
                }
                else
                {
                    double percent;
                    if (double.TryParse(selector.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out percent))
                    {
                        frame.Percentage = percent;
                    }
                }

                MergeDeclarations(frame.Declarations, decls);
                keyframes.Keyframes.Add(frame);
            }

            return keyframes;
        }

        private void ParseRuleBlocks(CssStyleSheet sheet, string css, string mediaCondition, ref int order, string layerName = null)
        {
            ParseRuleBlocksWithNesting(sheet, css, mediaCondition, null, ref order, layerName);
        }

        private void ParseRuleBlocksWithNesting(CssStyleSheet sheet, string css, string mediaCondition, string parentSelector, ref int order, string layerName = null)
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

                // Handle CSS Nesting: & selector
                if (!string.IsNullOrEmpty(parentSelector) && selectorText.Contains("&"))
                {
                    selectorText = selectorText.Replace("&", parentSelector);
                }
                else if (!string.IsNullOrEmpty(parentSelector) && !selectorText.StartsWith("@"))
                {
                    // Implicit nesting - prepend parent selector
                    selectorText = parentSelector + " " + selectorText;
                }

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
                    else if (trimmedSel.EndsWith("::marker", StringComparison.OrdinalIgnoreCase))
                    {
                        pseudoElement = "marker";
                        trimmedSel = trimmedSel.Substring(0, trimmedSel.Length - "::marker".Length).Trim();
                    }
                    else if (trimmedSel.EndsWith("::placeholder", StringComparison.OrdinalIgnoreCase))
                    {
                        pseudoElement = "placeholder";
                        trimmedSel = trimmedSel.Substring(0, trimmedSel.Length - "::placeholder".Length).Trim();
                    }
                    else if (trimmedSel.EndsWith("::selection", StringComparison.OrdinalIgnoreCase))
                    {
                        pseudoElement = "selection";
                        trimmedSel = trimmedSel.Substring(0, trimmedSel.Length - "::selection".Length).Trim();
                    }
                    else if (trimmedSel.EndsWith("::first-line", StringComparison.OrdinalIgnoreCase))
                    {
                        pseudoElement = "first-line";
                        trimmedSel = trimmedSel.Substring(0, trimmedSel.Length - "::first-line".Length).Trim();
                    }
                    else if (trimmedSel.EndsWith("::first-letter", StringComparison.OrdinalIgnoreCase))
                    {
                        pseudoElement = "first-letter";
                        trimmedSel = trimmedSel.Substring(0, trimmedSel.Length - "::first-letter".Length).Trim();
                    }
                    else if (trimmedSel.EndsWith("::backdrop", StringComparison.OrdinalIgnoreCase))
                    {
                        pseudoElement = "backdrop";
                        trimmedSel = trimmedSel.Substring(0, trimmedSel.Length - "::backdrop".Length).Trim();
                    }
                    // Handle vendor-prefixed pseudo-elements
                    else if (trimmedSel.Contains("::-webkit-") || trimmedSel.Contains("::-moz-") || trimmedSel.Contains("::-ms-"))
                    {
                        var prefixMatch = Regex.Match(trimmedSel, @"::(-webkit-|-moz-|-ms-)([a-z-]+)$", RegexOptions.IgnoreCase);
                        if (prefixMatch.Success)
                        {
                            pseudoElement = prefixMatch.Value.Substring(2); // Remove ::
                            trimmedSel = trimmedSel.Substring(0, trimmedSel.Length - prefixMatch.Length).Trim();
                        }
                    }

                    var rule = new CssRule
                    {
                        SelectorText = trimmedSel,
                        SourceOrder = order++,
                        Specificity = CalculateSpecificity(trimmedSel),
                        MediaCondition = mediaCondition,
                        PseudoElement = pseudoElement,
                        LayerName = layerName,
                        LayerPriority = string.IsNullOrEmpty(layerName) ? int.MaxValue : GetLayerPriority(layerName)
                    };
                    MergeDeclarations(rule.Declarations, decls);
                    sheet.Rules.Add(rule);
                }
            }
        }

        private static double MediaViewportWidth = 1024.0;

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
                if (!string.IsNullOrEmpty(rule.MediaCondition) && !EvaluateMedia(rule.MediaCondition, node))
                {
                    continue;
                }

                if (MatchesSelector(node, rule.SelectorText))
                {
                    foreach (var kv in rule.Declarations)
                    {
                        if (!string.IsNullOrEmpty(rule.PseudoElement))
                        {
                            ApplyProperty(result, node, rule.PseudoElement + "::" + kv.Key, kv.Value, rule.Specificity, rule.SourceOrder, rule.LayerPriority);
                        }
                        else
                        {
                            ApplyProperty(result, node, kv.Key, kv.Value, rule.Specificity, rule.SourceOrder, rule.LayerPriority);
                        }
                    }
                }
            }

            if (node.Attributes.ContainsKey("style"))
            {
                MergeDeclarations(result.Styles[node], node.Attributes["style"], result.Weights[node], inlineSpecificity: 1000, inlineOrder: int.MaxValue, parentStyles: result.Styles[node], layerPriority: int.MaxValue);
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

        // Media feature defaults (can be updated from host viewport/preferences)
        private static double MediaViewportHeight = 768.0;
        private static string MediaColorScheme = "light"; // "light" or "dark"
        private static string MediaReducedMotion = "no-preference"; // "reduce" or "no-preference"
        private static string MediaContrast = "no-preference"; // "more", "less", or "no-preference"
        private static string MediaReducedTransparency = "no-preference";
        private static string MediaOrientation = "landscape"; // "portrait" or "landscape"
        private static bool MediaHover = true; // true if primary input can hover
        private static string MediaPointer = "fine"; // "none", "coarse", or "fine"
        private static bool MediaScripting = true; // true if scripting enabled
        private static string MediaDisplayMode = "browser"; // "browser", "standalone", "minimal-ui", "fullscreen"
        private static int MediaColorBits = 24; // bits per color
        private static int MediaResolution = 96; // dpi

        public void UpdateMediaContext(double viewportWidth, double viewportHeight, string colorScheme = null, string reducedMotion = null, string contrast = null, string reducedTransparency = null)
        {
            MediaViewportWidth = Math.Max(1, viewportWidth);
            MediaViewportHeight = Math.Max(1, viewportHeight);
            MediaOrientation = MediaViewportWidth >= MediaViewportHeight ? "landscape" : "portrait";

            if (!string.IsNullOrWhiteSpace(colorScheme))
            {
                MediaColorScheme = colorScheme.ToLowerInvariant();
            }
            else
            {
                var glass = SystemParameters.WindowGlassColor;
                var luminance = (0.299 * glass.R + 0.587 * glass.G + 0.114 * glass.B);
                MediaColorScheme = luminance < 128 ? "dark" : "light";
            }

            if (!string.IsNullOrWhiteSpace(reducedMotion))
            {
                MediaReducedMotion = reducedMotion.ToLowerInvariant();
            }
            else
            {
                MediaReducedMotion = SystemParameters.ClientAreaAnimation ? "no-preference" : "reduce";
            }

            if (!string.IsNullOrWhiteSpace(contrast))
            {
                MediaContrast = contrast.ToLowerInvariant();
            }
            else
            {
                MediaContrast = SystemParameters.HighContrast ? "more" : "no-preference";
            }

            MediaReducedTransparency = string.IsNullOrWhiteSpace(reducedTransparency)
                ? (SystemParameters.HighContrast ? "reduce" : "no-preference")
                : reducedTransparency.ToLowerInvariant();

            MediaHover = true;
            MediaPointer = "fine";
            MediaResolution = 96; // desktop default

            // default container context representing the viewport
            _containerContexts["viewport"] = new ContainerContext
            {
                Name = "viewport",
                Width = MediaViewportWidth,
                Height = MediaViewportHeight,
                ContainerType = "size"
            };
        }

        private bool EvaluateMedia(string condition, HtmlNode node = null)
        {
            if (string.IsNullOrWhiteSpace(condition))
            {
                return true;
            }

            condition = condition.Trim();

            if (condition.StartsWith("@container", StringComparison.OrdinalIgnoreCase))
            {
                var inner = condition.Substring("@container".Length).Trim();
                string containerName = null;
                string cond = inner;

                if (!string.IsNullOrEmpty(inner) && !inner.StartsWith("(", StringComparison.Ordinal))
                {
                    var parts1 = inner.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts1.Length == 2)
                    {
                        containerName = parts1[0];
                        cond = parts1[1];
                    }
                }

                var ctx = (!string.IsNullOrEmpty(containerName) && _containerContexts.ContainsKey(containerName)) ? _containerContexts[containerName] : null;
                if (ctx == null && _containerContexts.ContainsKey("viewport"))
                {
                    ctx = _containerContexts["viewport"];
                }

                return EvaluateContainerQuery(cond, ctx);
            }

            // Handle "not" operator
            if (condition.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
            {
                return !EvaluateMedia(condition.Substring(4).Trim(), node);
            }

            // Handle "only" keyword (just ignore it)
            if (condition.StartsWith("only ", StringComparison.OrdinalIgnoreCase))
            {
                condition = condition.Substring(5).Trim();
            }

            // Handle media type first
            var mediaTypes = new[] { "all", "screen", "print", "speech" };
            foreach (var mt in mediaTypes)
            {
                if (condition.StartsWith(mt, StringComparison.OrdinalIgnoreCase))
                {
                    if (mt == "print" || mt == "speech")
                    {
                        return false; // We only support screen
                    }
                    var remaining = condition.Substring(mt.Length).Trim();
                    if (remaining.StartsWith("and", StringComparison.OrdinalIgnoreCase))
                    {
                        condition = remaining.Substring(3).Trim();
                    }
                    else if (string.IsNullOrEmpty(remaining))
                    {
                        return true;
                    }
                    break;
                }
            }

            // Split by "and" but not inside parentheses
            var parts = SplitMediaCondition(condition, "and");
            foreach (var part in parts)
            {
                if (!EvaluateMediaFeature(part.Trim()))
                {
                    return false;
                }
            }

            return true;
        }

        private List<string> SplitMediaCondition(string condition, string separator)
        {
            var parts = new List<string>();
            int depth = 0;
            int start = 0;
            string sep = " " + separator + " ";

            for (int i = 0; i < condition.Length; i++)
            {
                if (condition[i] == '(') depth++;
                else if (condition[i] == ')') depth--;
                else if (depth == 0 && i + sep.Length <= condition.Length)
                {
                    if (condition.Substring(i, sep.Length).Equals(sep, StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(condition.Substring(start, i - start));
                        i += sep.Length - 1;
                        start = i + 1;
                    }
                }
            }

            if (start < condition.Length)
            {
                parts.Add(condition.Substring(start));
            }

            return parts;
        }

        private bool EvaluateMediaFeature(string feature)
        {
            feature = feature.Trim().Trim('(', ')').Trim();
            if (string.IsNullOrEmpty(feature)) return true;

            // Parse feature name and value
            var colonIdx = feature.IndexOf(':');
            string name, value;
            if (colonIdx > 0)
            {
                name = feature.Substring(0, colonIdx).Trim().ToLowerInvariant();
                value = feature.Substring(colonIdx + 1).Trim();
            }
            else
            {
                name = feature.ToLowerInvariant();
                value = null;
            }

            // Boolean features (no value)
            if (value == null)
            {
                switch (name)
                {
                    case "color": return MediaColorBits > 0;
                    case "monochrome": return false;
                    case "grid": return false;
                    case "hover": return MediaHover;
                    case "any-hover": return MediaHover;
                    case "scripting": return MediaScripting;
                    default: return true;
                }
            }

            value = value.ToLowerInvariant();

            // Width/Height features
            switch (name)
            {
                case "min-width":
                    return MediaViewportWidth >= ParseMediaLength(value, 0);
                case "max-width":
                    return MediaViewportWidth <= ParseMediaLength(value, double.MaxValue);
                case "width":
                    return Math.Abs(MediaViewportWidth - ParseMediaLength(value, 0)) < 1;
                case "min-height":
                    return MediaViewportHeight >= ParseMediaLength(value, 0);
                case "max-height":
                    return MediaViewportHeight <= ParseMediaLength(value, double.MaxValue);
                case "height":
                    return Math.Abs(MediaViewportHeight - ParseMediaLength(value, 0)) < 1;
                
                // Aspect ratio
                case "aspect-ratio":
                case "min-aspect-ratio":
                case "max-aspect-ratio":
                    return EvaluateAspectRatio(name, value);
                
                // Orientation
                case "orientation":
                    if (value == "portrait") return MediaViewportHeight > MediaViewportWidth;
                    if (value == "landscape") return MediaViewportWidth >= MediaViewportHeight;
                    return true;
                
                // Color scheme
                case "prefers-color-scheme":
                    return value == MediaColorScheme;
                
                // Reduced motion
                case "prefers-reduced-motion":
                    return value == MediaReducedMotion;
                
                // Contrast
                case "prefers-contrast":
                    return value == MediaContrast || value == "no-preference";
                
                // Reduced transparency
                case "prefers-reduced-transparency":
                    return value == MediaReducedTransparency || value == "no-preference";
                
                // Hover capability
                case "hover":
                    if (value == "hover") return MediaHover;
                    if (value == "none") return !MediaHover;
                    return true;
                case "any-hover":
                    if (value == "hover") return MediaHover;
                    if (value == "none") return !MediaHover;
                    return true;
                
                // Pointer precision
                case "pointer":
                case "any-pointer":
                    return value == MediaPointer || value == "fine";
                
                // Scripting
                case "scripting":
                    if (value == "enabled") return MediaScripting;
                    if (value == "none") return !MediaScripting;
                    return true;
                
                // Display mode
                case "display-mode":
                    return value == MediaDisplayMode || value == "browser";
                
                // Color depth
                case "color":
                    int colorBits;
                    if (int.TryParse(value, out colorBits))
                        return MediaColorBits >= colorBits;
                    return true;
                case "min-color":
                    int minColor;
                    if (int.TryParse(value, out minColor))
                        return MediaColorBits >= minColor;
                    return true;
                case "max-color":
                    int maxColor;
                    if (int.TryParse(value, out maxColor))
                        return MediaColorBits <= maxColor;
                    return true;
                
                // Resolution
                case "resolution":
                case "min-resolution":
                case "max-resolution":
                    return EvaluateResolution(name, value);
                
                // Color gamut
                case "color-gamut":
                    return value == "srgb" || value == "p3";
                
                // Dynamic range
                case "dynamic-range":
                    return value == "standard";
                
                // Update frequency
                case "update":
                    return value == "fast";
                
                // Overflow
                case "overflow-block":
                    return value == "scroll";
                case "overflow-inline":
                    return value == "scroll";
                
                default:
                    return true;
            }
        }

        private bool EvaluateAspectRatio(string name, string value)
        {
            var parts = value.Split('/');
            if (parts.Length != 2) return true;

            double num, denom;
            if (!double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out num)) return true;
            if (!double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out denom)) return true;
            if (denom == 0) return true;

            double targetRatio = num / denom;
            double viewportRatio = MediaViewportWidth / MediaViewportHeight;

            switch (name)
            {
                case "min-aspect-ratio": return viewportRatio >= targetRatio;
                case "max-aspect-ratio": return viewportRatio <= targetRatio;
                case "aspect-ratio": return Math.Abs(viewportRatio - targetRatio) < 0.01;
                default: return true;
            }
        }

        private bool EvaluateResolution(string name, string value)
        {
            double res = 0;
            if (value.EndsWith("dpi"))
            {
                double.TryParse(value.Replace("dpi", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out res);
            }
            else if (value.EndsWith("dpcm"))
            {
                double.TryParse(value.Replace("dpcm", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out res);
                res *= 2.54; // Convert to dpi
            }
            else if (value.EndsWith("dppx") || value.EndsWith("x"))
            {
                double.TryParse(value.Replace("dppx", "").Replace("x", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out res);
                res *= 96; // Convert to dpi
            }

            switch (name)
            {
                case "min-resolution": return MediaResolution >= res;
                case "max-resolution": return MediaResolution <= res;
                case "resolution": return Math.Abs(MediaResolution - res) < 1;
                default: return true;
            }
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

        private string ResolveKeyword(string propertyName, string value, Dictionary<string, string> parentStyles)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var trimmed = StripImportant(value).Trim().ToLowerInvariant();

            // Handle 'inherit' keyword - use parent value
            if (trimmed == "inherit")
            {
                if (parentStyles != null && parentStyles.ContainsKey(propertyName))
                {
                    return parentStyles[propertyName];
                }
                // If no parent value, use initial
                return GetInitialValue(propertyName);
            }

            // Handle 'initial' keyword - use CSS spec initial value
            if (trimmed == "initial")
            {
                return GetInitialValue(propertyName);
            }

            // Handle 'unset' keyword - inherit if inheritable, otherwise initial
            if (trimmed == "unset")
            {
                if (InheritableProperties.Contains(propertyName))
                {
                    if (parentStyles != null && parentStyles.ContainsKey(propertyName))
                    {
                        return parentStyles[propertyName];
                    }
                }
                return GetInitialValue(propertyName);
            }

            // Handle 'revert' keyword - same as unset for user stylesheets
            if (trimmed == "revert")
            {
                if (InheritableProperties.Contains(propertyName))
                {
                    if (parentStyles != null && parentStyles.ContainsKey(propertyName))
                    {
                        return parentStyles[propertyName];
                    }
                }
                return GetInitialValue(propertyName);
            }

            return value;
        }

        private string GetInitialValue(string propertyName)
        {
            string initial;
            if (InitialValues.TryGetValue(propertyName, out initial))
            {
                return initial;
            }
            return "0";
        }

        private void MergeDeclarations(Dictionary<string, string> target, string decls, Dictionary<string, StyleEntry> weights = null, int inlineSpecificity = 0, int inlineOrder = 0, Dictionary<string, string> parentStyles = null, int layerPriority = int.MaxValue)
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

                // Handle CSS Custom Properties (CSS Variables)
                if (name.StartsWith("--", StringComparison.Ordinal))
                {
                    _customProperties[name] = StripImportant(value);
                    target[name] = StripImportant(value);
                    continue;
                }

                // Handle inherit/initial/unset/revert keywords
                value = ResolveKeyword(name, value, parentStyles);

                if (string.Equals(name, "margin", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBoxShorthand(target, "margin", value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // CSS Logical Properties for margin
                if (string.Equals(name, "margin-inline", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandLogicalShorthand(target, "margin", "inline", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "margin-block", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandLogicalShorthand(target, "margin", "block", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "margin-inline-start", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "margin-left", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "margin-inline-end", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "margin-right", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "margin-block-start", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "margin-top", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "margin-block-end", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "margin-bottom", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }

                if (string.Equals(name, "padding", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBoxShorthand(target, "padding", value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // CSS Logical Properties for padding
                if (string.Equals(name, "padding-inline", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandLogicalShorthand(target, "padding", "inline", value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }
                if (string.Equals(name, "padding-block", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandLogicalShorthand(target, "padding", "block", value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }
                if (string.Equals(name, "padding-inline-start", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "padding-left", value, inlineSpecificity, inlineOrder);
                    continue;
                }
                if (string.Equals(name, "padding-inline-end", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "padding-right", value, inlineSpecificity, inlineOrder);
                    continue;
                }
                if (string.Equals(name, "padding-block-start", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "padding-top", value, inlineSpecificity, inlineOrder);
                    continue;
                }
                if (string.Equals(name, "padding-block-end", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "padding-bottom", value, inlineSpecificity, inlineOrder);
                    continue;
                }

                // CSS Logical Properties for size
                if (string.Equals(name, "inline-size", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "width", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "block-size", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "height", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "min-inline-size", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "min-width", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "min-block-size", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "min-height", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "max-inline-size", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "max-width", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "max-block-size", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "max-height", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }

                // CSS Logical Properties for inset (positioning)
                if (string.Equals(name, "inset", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandInsetShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }
                if (string.Equals(name, "inset-inline", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandLogicalShorthand(target, "inset", "inline", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "inset-block", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandLogicalShorthand(target, "inset", "block", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "inset-inline-start", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "left", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "inset-inline-end", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "right", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "inset-block-start", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "top", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "inset-block-end", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "bottom", value, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }

                // CSS Logical Properties for border
                if (string.Equals(name, "border-inline", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderSideShorthand(target, "border-left", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    ExpandBorderSideShorthand(target, "border-right", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "border-block", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderSideShorthand(target, "border-top", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    ExpandBorderSideShorthand(target, "border-bottom", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "border-inline-start", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderSideShorthand(target, "border-left", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "border-inline-end", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderSideShorthand(target, "border-right", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "border-block-start", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderSideShorthand(target, "border-top", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }
                if (string.Equals(name, "border-block-end", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderSideShorthand(target, "border-bottom", value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }

                if (string.Equals(name, "border", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandBorderShorthand(target, value, weights, inlineSpecificity, inlineOrder, layerPriority);
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

                // Handle outline shorthand
                if (string.Equals(name, "outline", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandOutlineShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // Handle flex shorthand
                if (string.Equals(name, "flex", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandFlexShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // Handle flex-flow shorthand
                if (string.Equals(name, "flex-flow", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandFlexFlowShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // Handle gap shorthand (for grid and flex)
                if (string.Equals(name, "gap", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandGapShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // Handle place-items shorthand
                if (string.Equals(name, "place-items", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandPlaceItemsShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // Handle place-content shorthand
                if (string.Equals(name, "place-content", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandPlaceContentShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // Handle transition shorthand
                if (string.Equals(name, "transition", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandTransitionShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // Handle animation shorthand
                if (string.Equals(name, "animation", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandAnimationShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // Handle columns shorthand (multi-column layout)
                if (string.Equals(name, "columns", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandColumnsShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // Handle column-rule shorthand
                if (string.Equals(name, "column-rule", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandColumnRuleShorthand(target, value, weights, inlineSpecificity, inlineOrder);
                    continue;
                }

                // Handle text-decoration shorthand
                if (string.Equals(name, "text-decoration", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandTextDecorationShorthand(target, value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }

                // Handle scroll-snap-type shorthand
                if (string.Equals(name, "scroll-snap-type", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandScrollSnapTypeShorthand(target, value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }

                // Handle scroll-margin shorthand
                if (string.Equals(name, "scroll-margin", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandScrollMarginShorthand(target, value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }

                // Handle scroll-padding shorthand
                if (string.Equals(name, "scroll-padding", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandScrollPaddingShorthand(target, value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }

                // Handle container shorthand
                if (string.Equals(name, "container", StringComparison.OrdinalIgnoreCase))
                {
                    ExpandContainerShorthand(target, value, weights, inlineSpecificity, inlineOrder, layerPriority);
                    continue;
                }

                if (weights != null)
                {
                    ApplyProperty(target, weights, name, value, inlineSpecificity, inlineOrder, IsImportant(value), layerPriority);
                }
                else
                {
                    target[name] = StripImportant(value);
                }
            }

        }

        private void ExpandColumnsShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var p = part.Trim().ToLowerInvariant();
                if (p == "auto")
                {
                    continue;
                }

                // If it's a number, it's column-count
                int count;
                if (int.TryParse(p, out count))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "column-count", p, specificity, order);
                }
                // Otherwise it's a width (column-width)
                else
                {
                    ApplyPropertyMaybeWeighted(target, weights, "column-width", p, specificity, order);
                }
            }
        }

        private void ExpandColumnRuleShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string width = null, style = null, color = null;
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

            if (!string.IsNullOrWhiteSpace(width))
            {
                ApplyPropertyMaybeWeighted(target, weights, "column-rule-width", width, specificity, order);
            }

            if (!string.IsNullOrWhiteSpace(style))
            {
                ApplyPropertyMaybeWeighted(target, weights, "column-rule-style", style, specificity, order);
            }

            if (!string.IsNullOrWhiteSpace(color))
            {
                ApplyPropertyMaybeWeighted(target, weights, "column-rule-color", color, specificity, order);
            }
        }

        private void ExpandOutlineShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string width = null, style = null, color = null;
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

            if (!string.IsNullOrWhiteSpace(width))
            {
                ApplyPropertyMaybeWeighted(target, weights, "outline-width", width, specificity, order);
            }

            if (!string.IsNullOrWhiteSpace(style))
            {
                ApplyPropertyMaybeWeighted(target, weights, "outline-style", style, specificity, order);
            }

            if (!string.IsNullOrWhiteSpace(color))
            {
                ApplyPropertyMaybeWeighted(target, weights, "outline-color", color, specificity, order);
            }
        }

        private void ExpandLogicalShorthand(Dictionary<string, string> target, string property, string axis, string value, Dictionary<string, StyleEntry> weights, int specificity, int order, int layerPriority = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string startValue = parts[0];
            string endValue = parts.Length > 1 ? parts[1] : parts[0];

            // Map logical to physical properties (assuming LTR writing mode)
            if (axis == "inline")
            {
                if (property == "margin")
                {
                    ApplyPropertyMaybeWeighted(target, weights, "margin-left", startValue, specificity, order, layerPriority);
                    ApplyPropertyMaybeWeighted(target, weights, "margin-right", endValue, specificity, order, layerPriority);
                }
                else if (property == "padding")
                {
                    ApplyPropertyMaybeWeighted(target, weights, "padding-left", startValue, specificity, order, layerPriority);
                    ApplyPropertyMaybeWeighted(target, weights, "padding-right", endValue, specificity, order, layerPriority);
                }
                else if (property == "inset")
                {
                    ApplyPropertyMaybeWeighted(target, weights, "left", startValue, specificity, order, layerPriority);
                    ApplyPropertyMaybeWeighted(target, weights, "right", endValue, specificity, order, layerPriority);
                }
            }
            else if (axis == "block")
            {
                if (property == "margin")
                {
                    ApplyPropertyMaybeWeighted(target, weights, "margin-top", startValue, specificity, order, layerPriority);
                    ApplyPropertyMaybeWeighted(target, weights, "margin-bottom", endValue, specificity, order, layerPriority);
                }
                else if (property == "padding")
                {
                    ApplyPropertyMaybeWeighted(target, weights, "padding-top", startValue, specificity, order, layerPriority);
                    ApplyPropertyMaybeWeighted(target, weights, "padding-bottom", endValue, specificity, order, layerPriority);
                }
                else if (property == "inset")
                {
                    ApplyPropertyMaybeWeighted(target, weights, "top", startValue, specificity, order, layerPriority);
                    ApplyPropertyMaybeWeighted(target, weights, "bottom", endValue, specificity, order, layerPriority);
                }
            }
        }

        private void ExpandInsetShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
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

            ApplyPropertyMaybeWeighted(target, weights, "top", top, specificity, order);
            ApplyPropertyMaybeWeighted(target, weights, "right", right, specificity, order);
            ApplyPropertyMaybeWeighted(target, weights, "bottom", bottom, specificity, order);
            ApplyPropertyMaybeWeighted(target, weights, "left", left, specificity, order);
        }

        private void ExpandFlexShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var trimmed = value.Trim().ToLowerInvariant();

            // Handle keyword values
            if (trimmed == "none")
            {
                ApplyPropertyMaybeWeighted(target, weights, "flex-grow", "0", specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "flex-shrink", "0", specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "flex-basis", "auto", specificity, order);
                return;
            }

            if (trimmed == "auto")
            {
                ApplyPropertyMaybeWeighted(target, weights, "flex-grow", "1", specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "flex-shrink", "1", specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "flex-basis", "auto", specificity, order);
                return;
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1)
            {
                // Single value: could be flex-grow or flex-basis
                double num;
                if (double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "flex-grow", parts[0], specificity, order);
                    ApplyPropertyMaybeWeighted(target, weights, "flex-shrink", "1", specificity, order);
                    ApplyPropertyMaybeWeighted(target, weights, "flex-basis", "0", specificity, order);
                }
                else
                {
                    ApplyPropertyMaybeWeighted(target, weights, "flex-grow", "1", specificity, order);
                    ApplyPropertyMaybeWeighted(target, weights, "flex-shrink", "1", specificity, order);
                    ApplyPropertyMaybeWeighted(target, weights, "flex-basis", parts[0], specificity, order);
                }
            }
            else if (parts.Length == 2)
            {
                ApplyPropertyMaybeWeighted(target, weights, "flex-grow", parts[0], specificity, order);
                double num;
                if (double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out num))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "flex-shrink", parts[1], specificity, order);
                    ApplyPropertyMaybeWeighted(target, weights, "flex-basis", "0", specificity, order);
                }
                else
                {
                    ApplyPropertyMaybeWeighted(target, weights, "flex-shrink", "1", specificity, order);
                    ApplyPropertyMaybeWeighted(target, weights, "flex-basis", parts[1], specificity, order);
                }
            }
            else if (parts.Length >= 3)
            {
                ApplyPropertyMaybeWeighted(target, weights, "flex-grow", parts[0], specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "flex-shrink", parts[1], specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "flex-basis", parts[2], specificity, order);
            }
        }

        private void ExpandFlexFlowShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var directions = new[] { "row", "row-reverse", "column", "column-reverse" };
            var wraps = new[] { "nowrap", "wrap", "wrap-reverse" };

            foreach (var part in parts)
            {
                var p = part.Trim().ToLowerInvariant();
                if (directions.Contains(p))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "flex-direction", p, specificity, order);
                }
                else if (wraps.Contains(p))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "flex-wrap", p, specificity, order);
                }
            }
        }

        private void ExpandGapShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                ApplyPropertyMaybeWeighted(target, weights, "row-gap", parts[0], specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "column-gap", parts[0], specificity, order);
            }
            else if (parts.Length >= 2)
            {
                ApplyPropertyMaybeWeighted(target, weights, "row-gap", parts[0], specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "column-gap", parts[1], specificity, order);
            }
        }

        private void ExpandPlaceItemsShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                ApplyPropertyMaybeWeighted(target, weights, "align-items", parts[0], specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "justify-items", parts[0], specificity, order);
            }
            else if (parts.Length >= 2)
            {
                ApplyPropertyMaybeWeighted(target, weights, "align-items", parts[0], specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "justify-items", parts[1], specificity, order);
            }
        }

        private void ExpandPlaceContentShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                ApplyPropertyMaybeWeighted(target, weights, "align-content", parts[0], specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "justify-content", parts[0], specificity, order);
            }
            else if (parts.Length >= 2)
            {
                ApplyPropertyMaybeWeighted(target, weights, "align-content", parts[0], specificity, order);
                ApplyPropertyMaybeWeighted(target, weights, "justify-content", parts[1], specificity, order);
            }
        }

        private void ExpandTransitionShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            // Simple implementation: just store the raw value
            ApplyPropertyMaybeWeighted(target, weights, "transition", value, specificity, order);

            // Parse individual transitions
            var transitions = value.Split(',');
            foreach (var trans in transitions)
            {
                var parts = trans.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1)
                {
                    ApplyPropertyMaybeWeighted(target, weights, "transition-property", parts[0], specificity, order);
                }
                if (parts.Length >= 2)
                {
                    ApplyPropertyMaybeWeighted(target, weights, "transition-duration", parts[1], specificity, order);
                }
                if (parts.Length >= 3)
                {
                    ApplyPropertyMaybeWeighted(target, weights, "transition-timing-function", parts[2], specificity, order);
                }
                if (parts.Length >= 4)
                {
                    ApplyPropertyMaybeWeighted(target, weights, "transition-delay", parts[3], specificity, order);
                }
                break; // Only process first transition for individual properties
            }
        }

        private void ExpandAnimationShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            // Store the raw value
            ApplyPropertyMaybeWeighted(target, weights, "animation", value, specificity, order);

            // Parse animation
            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var timingFunctions = new[] { "ease", "ease-in", "ease-out", "ease-in-out", "linear", "step-start", "step-end" };
            var directions = new[] { "normal", "reverse", "alternate", "alternate-reverse" };
            var fillModes = new[] { "none", "forwards", "backwards", "both" };
            var playStates = new[] { "running", "paused" };

            int timeIndex = 0;
            foreach (var part in parts)
            {
                var p = part.Trim().ToLowerInvariant();

                if (p.EndsWith("s") || p.EndsWith("ms"))
                {
                    if (timeIndex == 0)
                    {
                        ApplyPropertyMaybeWeighted(target, weights, "animation-duration", p, specificity, order);
                        timeIndex++;
                    }
                    else
                    {
                        ApplyPropertyMaybeWeighted(target, weights, "animation-delay", p, specificity, order);
                    }
                }
                else if (timingFunctions.Contains(p) || p.StartsWith("cubic-bezier") || p.StartsWith("steps"))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "animation-timing-function", p, specificity, order);
                }
                else if (directions.Contains(p))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "animation-direction", p, specificity, order);
                }
                else if (fillModes.Contains(p))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "animation-fill-mode", p, specificity, order);
                }
                else if (playStates.Contains(p))
                {
                    ApplyPropertyMaybeWeighted(target, weights, "animation-play-state", p, specificity, order);
                }
                else if (p == "infinite")
                {
                    ApplyPropertyMaybeWeighted(target, weights, "animation-iteration-count", p, specificity, order);
                }
                else
                {
                    int iterations;
                    if (int.TryParse(p, out iterations))
                    {
                        ApplyPropertyMaybeWeighted(target, weights, "animation-iteration-count", p, specificity, order);
                    }
                    else
                    {
                        // Assume it's the animation name
                        ApplyPropertyMaybeWeighted(target, weights, "animation-name", p, specificity, order);
                    }
                }
            }
        }

        private void ExpandBorderShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order, int layerPriority = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string width, style, color;
            ParseBorderComponents(value, out width, out style, out color);

            ApplyBorderAttributes(target, "border-top", width, style, color, weights, specificity, order, layerPriority);
            ApplyBorderAttributes(target, "border-right", width, style, color, weights, specificity, order, layerPriority);
            ApplyBorderAttributes(target, "border-bottom", width, style, color, weights, specificity, order, layerPriority);
            ApplyBorderAttributes(target, "border-left", width, style, color, weights, specificity, order, layerPriority);
        }

        private void ExpandTextDecorationShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order, int layerPriority = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (value.Trim().ToLowerInvariant() == "none")
            {
                ApplyPropertyMaybeWeighted(target, weights, "text-decoration-line", "none", specificity, order);
                return;
            }

            var lines = new[] { "underline", "overline", "line-through", "blink" };
            var styles = new[] { "solid", "double", "dotted", "dashed", "wavy" };
            
            string line = null, style = null, color = null, thickness = null;
            var tokens = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                var t = token.Trim().ToLowerInvariant();
                
                if (lines.Contains(t))
                {
                    line = line == null ? t : line + " " + t;
                }
                else if (styles.Contains(t))
                {
                    style = t;
                }
                else if (IsColorToken(t))
                {
                    color = t;
                }
                else if (t.EndsWith("px") || t.EndsWith("em") || t.EndsWith("%") || t == "auto" || t == "from-font")
                {
                    thickness = t;
                }
            }

            if (!string.IsNullOrEmpty(line))
                ApplyPropertyMaybeWeighted(target, weights, "text-decoration-line", line, specificity, order, layerPriority);
            if (!string.IsNullOrEmpty(style))
                ApplyPropertyMaybeWeighted(target, weights, "text-decoration-style", style, specificity, order, layerPriority);
            if (!string.IsNullOrEmpty(color))
                ApplyPropertyMaybeWeighted(target, weights, "text-decoration-color", color, specificity, order, layerPriority);
            if (!string.IsNullOrEmpty(thickness))
                ApplyPropertyMaybeWeighted(target, weights, "text-decoration-thickness", thickness, specificity, order, layerPriority);
        }

        private void ExpandScrollSnapTypeShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order, int layerPriority = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var tokens = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var axes = new[] { "x", "y", "block", "inline", "both" };
            var strictness = new[] { "mandatory", "proximity" };

            string axis = null, strict = null;
            foreach (var token in tokens)
            {
                var t = token.Trim().ToLowerInvariant();
                if (axes.Contains(t)) axis = t;
                else if (strictness.Contains(t)) strict = t;
            }

            if (!string.IsNullOrEmpty(axis))
                ApplyPropertyMaybeWeighted(target, weights, "scroll-snap-type", axis + (strict != null ? " " + strict : ""), specificity, order, layerPriority);
        }

        private void ExpandScrollMarginShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order, int layerPriority = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string top, right, bottom, left;

            if (parts.Length == 1)
                top = right = bottom = left = parts[0];
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

            ApplyPropertyMaybeWeighted(target, weights, "scroll-margin-top", top, specificity, order, layerPriority);
            ApplyPropertyMaybeWeighted(target, weights, "scroll-margin-right", right, specificity, order, layerPriority);
            ApplyPropertyMaybeWeighted(target, weights, "scroll-margin-bottom", bottom, specificity, order, layerPriority);
            ApplyPropertyMaybeWeighted(target, weights, "scroll-margin-left", left, specificity, order, layerPriority);
        }

        private void ExpandScrollPaddingShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order, int layerPriority = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string top, right, bottom, left;

            if (parts.Length == 1)
                top = right = bottom = left = parts[0];
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

            ApplyPropertyMaybeWeighted(target, weights, "scroll-padding-top", top, specificity, order, layerPriority);
            ApplyPropertyMaybeWeighted(target, weights, "scroll-padding-right", right, specificity, order, layerPriority);
            ApplyPropertyMaybeWeighted(target, weights, "scroll-padding-bottom", bottom, specificity, order, layerPriority);
            ApplyPropertyMaybeWeighted(target, weights, "scroll-padding-left", left, specificity, order, layerPriority);
        }

        private void ExpandContainerShorthand(Dictionary<string, string> target, string value, Dictionary<string, StyleEntry> weights, int specificity, int order, int layerPriority = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            // Format: name / type
            var parts = value.Split('/');
            if (parts.Length >= 1)
            {
                var name = parts[0].Trim();
                if (name != "none" && !string.IsNullOrEmpty(name))
                    ApplyPropertyMaybeWeighted(target, weights, "container-name", name, specificity, order, layerPriority);
            }
            if (parts.Length >= 2)
            {
                var type = parts[1].Trim();
                ApplyPropertyMaybeWeighted(target, weights, "container-type", type, specificity, order, layerPriority);
            }
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

        private void ExpandBorderSideShorthand(Dictionary<string, string> target, string name, string value, Dictionary<string, StyleEntry> weights, int specificity, int order, int layerPriority = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            string width, style, color;
            ParseBorderComponents(value, out width, out style, out color);
            ApplyBorderAttributes(target, name, width, style, color, weights, specificity, order, layerPriority);
        }

        private void ExpandBorderBoxProperty(Dictionary<string, string> target, string prefix, string suffix, string value, Dictionary<string, StyleEntry> weights, int specificity, int order, int layerPriority = int.MaxValue)
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

            ApplyPropertyMaybeWeighted(target, weights, prefix + "top" + suffix, top, specificity, order, layerPriority);
            ApplyPropertyMaybeWeighted(target, weights, prefix + "right" + suffix, right, specificity, order, layerPriority);
            ApplyPropertyMaybeWeighted(target, weights, prefix + "bottom" + suffix, bottom, specificity, order, layerPriority);
            ApplyPropertyMaybeWeighted(target, weights, prefix + "left" + suffix, left, specificity, order, layerPriority);
        }

        private void ApplyBorderAttributes(Dictionary<string, string> target, string prefix, string width, string style, string color, Dictionary<string, StyleEntry> weights, int specificity, int order, int layerPriority = int.MaxValue)
        {
            if (!string.IsNullOrWhiteSpace(width))
            {
                ApplyPropertyMaybeWeighted(target, weights, prefix + "-width", width, specificity, order, layerPriority);
            }

            if (!string.IsNullOrWhiteSpace(style))
            {
                ApplyPropertyMaybeWeighted(target, weights, prefix + "-style", style, specificity, order, layerPriority);
            }

            if (!string.IsNullOrWhiteSpace(color))
            {
                ApplyPropertyMaybeWeighted(target, weights, prefix + "-color", color, specificity, order, layerPriority);
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

        private void ApplyPropertyMaybeWeighted(Dictionary<string, string> target, Dictionary<string, StyleEntry> weights, string name, string value, int specificity, int order, int layerPriority = int.MaxValue)
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
                ApplyProperty(target, weights, name, cleaned, specificity, order, IsImportant(value), layerPriority);
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

        public static bool TryParseColor(string input, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            input = input.Trim();
            var lower = input.ToLowerInvariant();

            // currentcolor should propagate
            if (lower == "currentcolor")
            {
                normalized = "currentcolor";
                return true;
            }

            // Map CSS system colors to concrete values
            var systemColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                {"canvas", "#ffffff"},
                {"canvastext", "#000000"},
                {"linktext", "#0000ee"},
                {"graytext", "#808080"},
                {"highlight", "#0a64ad"},
                {"highlighttext", "#ffffff"},
                {"buttonface", "#f0f0f0"},
                {"buttontext", "#000000"},
                {"buttonborder", "#8a8a8a"},
                {"field", "#ffffff"},
                {"fieldtext", "#000000"}
            };

            string systemHex;
            if (systemColors.TryGetValue(lower.Replace("-", string.Empty), out systemHex))
            {
                return TryParseColor(systemHex, out normalized);
            }

            if (NamedColors.Contains(lower))
            {
                normalized = lower;
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

            // lab() color function (CIE Lab)
            if (input.StartsWith("lab(", StringComparison.OrdinalIgnoreCase))
            {
                var start = input.IndexOf('(');
                var end = input.IndexOf(')');
                if (start > 0 && end > start)
                {
                    var inner = input.Substring(start + 1, end - start - 1);
                    var slashIdx = inner.IndexOf('/');
                    double a = 1.0;
                    if (slashIdx > 0)
                    {
                        var alphaPart = inner.Substring(slashIdx + 1).Trim();
                        inner = inner.Substring(0, slashIdx).Trim();
                        if (alphaPart.EndsWith("%"))
                            a = double.Parse(alphaPart.TrimEnd('%'), CultureInfo.InvariantCulture) / 100.0;
                        else
                            double.TryParse(alphaPart, NumberStyles.Any, CultureInfo.InvariantCulture, out a);
                    }
                    var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        double L, A, B;
                        if (double.TryParse(parts[0].Trim().TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out L) &&
                            double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out A) &&
                            double.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out B))
                        {
                            if (parts[0].Contains("%")) L = L / 100.0 * 100.0; // L is 0-100
                            int r, g, b;
                            LabToRgb(L, A, B, out r, out g, out b);
                            normalized = ToRgba(r, g, b, a);
                            return true;
                        }
                    }
                }
            }

            // lch() color function (CIE LCH)
            if (input.StartsWith("lch(", StringComparison.OrdinalIgnoreCase))
            {
                var start = input.IndexOf('(');
                var end = input.IndexOf(')');
                if (start > 0 && end > start)
                {
                    var inner = input.Substring(start + 1, end - start - 1);
                    var slashIdx = inner.IndexOf('/');
                    double a = 1.0;
                    if (slashIdx > 0)
                    {
                        var alphaPart = inner.Substring(slashIdx + 1).Trim();
                        inner = inner.Substring(0, slashIdx).Trim();
                        if (alphaPart.EndsWith("%"))
                            a = double.Parse(alphaPart.TrimEnd('%'), CultureInfo.InvariantCulture) / 100.0;
                        else
                            double.TryParse(alphaPart, NumberStyles.Any, CultureInfo.InvariantCulture, out a);
                    }
                    var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        double L, C, H;
                        if (double.TryParse(parts[0].Trim().TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out L) &&
                            double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out C) &&
                            double.TryParse(parts[2].Trim().TrimEnd('°'), NumberStyles.Any, CultureInfo.InvariantCulture, out H))
                        {
                            if (parts[0].Contains("%")) L = L / 100.0 * 100.0;
                            int r, g, b;
                            LchToRgb(L, C, H, out r, out g, out b);
                            normalized = ToRgba(r, g, b, a);
                            return true;
                        }
                    }
                }
            }

            // color() generic function
            if (input.StartsWith("color(", StringComparison.OrdinalIgnoreCase))
            {
                var start = input.IndexOf('(');
                var end = input.LastIndexOf(')');
                if (start > 0 && end > start)
                {
                    var inner = input.Substring(start + 1, end - start - 1).Trim();
                    var slashIdx = inner.IndexOf('/');
                    double alpha = 1.0;
                    if (slashIdx > 0)
                    {
                        var alphaPart = inner.Substring(slashIdx + 1).Trim();
                        inner = inner.Substring(0, slashIdx).Trim();
                        if (alphaPart.EndsWith("%"))
                            alpha = double.Parse(alphaPart.TrimEnd('%'), CultureInfo.InvariantCulture) / 100.0;
                        else
                            double.TryParse(alphaPart, NumberStyles.Any, CultureInfo.InvariantCulture, out alpha);
                    }
                    var parts = inner.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        var colorSpace = parts[0].ToLowerInvariant();
                        double c1, c2, c3;
                        if (double.TryParse(parts[1].TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out c1) &&
                            double.TryParse(parts[2].TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out c2) &&
                            double.TryParse(parts[3].TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out c3))
                        {
                            // Convert percentages to 0-1 range
                            if (parts[1].EndsWith("%")) c1 /= 100.0;
                            if (parts[2].EndsWith("%")) c2 /= 100.0;
                            if (parts[3].EndsWith("%")) c3 /= 100.0;

                            int r, g, b;
                            switch (colorSpace)
                            {
                                case "srgb":
                                    r = (int)Math.Round(c1 * 255);
                                    g = (int)Math.Round(c2 * 255);
                                    b = (int)Math.Round(c3 * 255);
                                    break;
                                case "display-p3":
                                case "a98-rgb":
                                case "prophoto-rgb":
                                case "rec2020":
                                    // Approximate by clamping to sRGB
                                    r = (int)Math.Round(Math.Max(0, Math.Min(1, c1)) * 255);
                                    g = (int)Math.Round(Math.Max(0, Math.Min(1, c2)) * 255);
                                    b = (int)Math.Round(Math.Max(0, Math.Min(1, c3)) * 255);
                                    break;
                                case "xyz":
                                case "xyz-d50":
                                case "xyz-d65":
                                    XyzToRgb(c1, c2, c3, out r, out g, out b);
                                    break;
                                default:
                                    r = (int)Math.Round(c1 * 255);
                                    g = (int)Math.Round(c2 * 255);
                                    b = (int)Math.Round(c3 * 255);
                                    break;
                            }
                            r = Math.Max(0, Math.Min(255, r));
                            g = Math.Max(0, Math.Min(255, g));
                            b = Math.Max(0, Math.Min(255, b));
                            normalized = ToRgba(r, g, b, alpha);
                            return true;
                        }
                    }
                }
            }

            // Relative color syntax: rgb(from color r g b / a)
            if (input.Contains(" from "))
            {
                var fromMatch = Regex.Match(input, @"(\w+)\s*\(\s*from\s+(\S+)\s+(.+)\)", RegexOptions.IgnoreCase);
                if (fromMatch.Success)
                {
                    var colorFunc = fromMatch.Groups[1].Value.ToLowerInvariant();
                    var baseColorStr = fromMatch.Groups[2].Value;
                    var channels = fromMatch.Groups[3].Value;

                    string baseNormalized;
                    if (TryParseColor(baseColorStr, out baseNormalized))
                    {
                        double br, bg, bb, ba;
                        ParseRgbaValues(baseNormalized, out br, out bg, out bb, out ba);

                        // Parse channel values with r/g/b/h/s/l substitution
                        var slashIdx = channels.IndexOf('/');
                        double newA = ba;
                        if (slashIdx > 0)
                        {
                            var alphaPart = channels.Substring(slashIdx + 1).Trim();
                            channels = channels.Substring(0, slashIdx).Trim();
                            alphaPart = alphaPart.Replace("alpha", (ba).ToString(CultureInfo.InvariantCulture));
                            double.TryParse(alphaPart.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out newA);
                            if (alphaPart.EndsWith("%")) newA /= 100.0;
                        }

                        var channelParts = channels.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (channelParts.Length >= 3)
                        {
                            // Replace channel keywords
                            var c1Str = channelParts[0].Replace("r", (br / 255.0).ToString(CultureInfo.InvariantCulture))
                                                        .Replace("g", (bg / 255.0).ToString(CultureInfo.InvariantCulture))
                                                        .Replace("b", (bb / 255.0).ToString(CultureInfo.InvariantCulture));
                            var c2Str = channelParts[1].Replace("r", (br / 255.0).ToString(CultureInfo.InvariantCulture))
                                                        .Replace("g", (bg / 255.0).ToString(CultureInfo.InvariantCulture))
                                                        .Replace("b", (bb / 255.0).ToString(CultureInfo.InvariantCulture));
                            var c3Str = channelParts[2].Replace("r", (br / 255.0).ToString(CultureInfo.InvariantCulture))
                                                        .Replace("g", (bg / 255.0).ToString(CultureInfo.InvariantCulture))
                                                        .Replace("b", (bb / 255.0).ToString(CultureInfo.InvariantCulture));

                            double c1, c2, c3;
                            if (double.TryParse(c1Str.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out c1) &&
                                double.TryParse(c2Str.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out c2) &&
                                double.TryParse(c3Str.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out c3))
                            {
                                if (c1Str.EndsWith("%")) c1 /= 100.0;
                                if (c2Str.EndsWith("%")) c2 /= 100.0;
                                if (c3Str.EndsWith("%")) c3 /= 100.0;

                                int r = (int)Math.Round(c1 * 255);
                                int g = (int)Math.Round(c2 * 255);
                                int b = (int)Math.Round(c3 * 255);
                                r = Math.Max(0, Math.Min(255, r));
                                g = Math.Max(0, Math.Min(255, g));
                                b = Math.Max(0, Math.Min(255, b));
                                normalized = ToRgba(r, g, b, newA);
                                return true;
                            }
                        }
                    }
                }
            }

            // hwb() color function
            if (input.StartsWith("hwb(", StringComparison.OrdinalIgnoreCase))
            {
                var start = input.IndexOf('(');
                var end = input.IndexOf(')');
                if (start > 0 && end > start)
                {
                    var inner = input.Substring(start + 1, end - start - 1);
                    // hwb uses space-separated values: hwb(H W B / A)
                    var slashIdx = inner.IndexOf('/');
                    double a = 1.0;
                    if (slashIdx > 0)
                    {
                        var alphaPart = inner.Substring(slashIdx + 1).Trim();
                        inner = inner.Substring(0, slashIdx).Trim();
                        if (alphaPart.EndsWith("%"))
                            a = double.Parse(alphaPart.TrimEnd('%'), CultureInfo.InvariantCulture) / 100.0;
                        else
                            double.TryParse(alphaPart, NumberStyles.Any, CultureInfo.InvariantCulture, out a);
                    }
                    var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        double h, w, bl;
                        if (double.TryParse(parts[0].Trim().TrimEnd('°'), NumberStyles.Any, CultureInfo.InvariantCulture, out h) &&
                            double.TryParse(parts[1].Trim().TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out w) &&
                            double.TryParse(parts[2].Trim().TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out bl))
                        {
                            w /= 100.0; bl /= 100.0;
                            int r, g, b;
                            HwbToRgb(h, w, bl, out r, out g, out b);
                            normalized = ToRgba(r, g, b, a);
                            return true;
                        }
                    }
                }
            }

            // oklch() color function
            if (input.StartsWith("oklch(", StringComparison.OrdinalIgnoreCase))
            {
                var start = input.IndexOf('(');
                var end = input.IndexOf(')');
                if (start > 0 && end > start)
                {
                    var inner = input.Substring(start + 1, end - start - 1);
                    var slashIdx = inner.IndexOf('/');
                    double a = 1.0;
                    if (slashIdx > 0)
                    {
                        var alphaPart = inner.Substring(slashIdx + 1).Trim();
                        inner = inner.Substring(0, slashIdx).Trim();
                        if (alphaPart.EndsWith("%"))
                            a = double.Parse(alphaPart.TrimEnd('%'), CultureInfo.InvariantCulture) / 100.0;
                        else
                            double.TryParse(alphaPart, NumberStyles.Any, CultureInfo.InvariantCulture, out a);
                    }
                    var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        double L, C, H;
                        if (double.TryParse(parts[0].Trim().TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out L) &&
                            double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out C) &&
                            double.TryParse(parts[2].Trim().TrimEnd('°'), NumberStyles.Any, CultureInfo.InvariantCulture, out H))
                        {
                            if (parts[0].Contains("%")) L /= 100.0;
                            int r, g, b;
                            OklchToRgb(L, C, H, out r, out g, out b);
                            normalized = ToRgba(r, g, b, a);
                            return true;
                        }
                    }
                }
            }

            // oklab() color function
            if (input.StartsWith("oklab(", StringComparison.OrdinalIgnoreCase))
            {
                var start = input.IndexOf('(');
                var end = input.IndexOf(')');
                if (start > 0 && end > start)
                {
                    var inner = input.Substring(start + 1, end - start - 1);
                    var slashIdx = inner.IndexOf('/');
                    double a = 1.0;
                    if (slashIdx > 0)
                    {
                        var alphaPart = inner.Substring(slashIdx + 1).Trim();
                        inner = inner.Substring(0, slashIdx).Trim();
                        if (alphaPart.EndsWith("%"))
                            a = double.Parse(alphaPart.TrimEnd('%'), CultureInfo.InvariantCulture) / 100.0;
                        else
                            double.TryParse(alphaPart, NumberStyles.Any, CultureInfo.InvariantCulture, out a);
                    }
                    var parts = inner.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        double L, A, B;
                        if (double.TryParse(parts[0].Trim().TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out L) &&
                            double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out A) &&
                            double.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out B))
                        {
                            if (parts[0].Contains("%")) L /= 100.0;
                            int r, g, b;
                            OklabToRgb(L, A, B, out r, out g, out b);
                            normalized = ToRgba(r, g, b, a);
                            return true;
                        }
                    }
                }
            }

            // color-mix() function
            if (input.StartsWith("color-mix(", StringComparison.OrdinalIgnoreCase))
            {
                normalized = EvaluateColorMix(input);
                return !string.IsNullOrEmpty(normalized);
            }

            // light-dark() function
            if (input.StartsWith("light-dark(", StringComparison.OrdinalIgnoreCase))
            {
                var start = input.IndexOf('(');
                var end = input.LastIndexOf(')');
                if (start > 0 && end > start)
                {
                    var inner = input.Substring(start + 1, end - start - 1);
                    var parts = SplitColorMixArgs(inner);
                    if (parts.Count >= 2)
                    {
                        // Use light color by default (can be changed based on MediaColorScheme)
                        var selectedColor = MediaColorScheme == "dark" ? parts[1].Trim() : parts[0].Trim();
                        return TryParseColor(selectedColor, out normalized);
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Evaluates color-mix() CSS function
        /// Format: color-mix(in colorspace, color1 percentage?, color2 percentage?)
        /// </summary>
        private static string EvaluateColorMix(string colorMixExpr)
        {
            var match = Regex.Match(colorMixExpr, @"color-mix\s*\(\s*in\s+(?<space>\w+)\s*,\s*(?<color1>[^,]+)\s*,\s*(?<color2>[^)]+)\s*\)", RegexOptions.IgnoreCase);
            if (!match.Success) return null;

            var colorSpace = match.Groups["space"].Value.ToLowerInvariant();
            var color1Str = match.Groups["color1"].Value.Trim();
            var color2Str = match.Groups["color2"].Value.Trim();

            // Parse percentages
            double percent1 = 50, percent2 = 50;
            var pct1Match = Regex.Match(color1Str, @"(\d+(?:\.\d+)?)\s*%");
            if (pct1Match.Success)
            {
                percent1 = double.Parse(pct1Match.Groups[1].Value, CultureInfo.InvariantCulture);
                color1Str = color1Str.Replace(pct1Match.Value, "").Trim();
            }
            var pct2Match = Regex.Match(color2Str, @"(\d+(?:\.\d+)?)\s*%");
            if (pct2Match.Success)
            {
                percent2 = double.Parse(pct2Match.Groups[1].Value, CultureInfo.InvariantCulture);
                color2Str = color2Str.Replace(pct2Match.Value, "").Trim();
            }

            // Normalize percentages
            var total = percent1 + percent2;
            if (total > 0)
            {
                percent1 = percent1 / total;
                percent2 = percent2 / total;
            }

            // Parse colors to RGBA
            string norm1, norm2;
            if (!TryParseColor(color1Str, out norm1) || !TryParseColor(color2Str, out norm2))
                return null;

            // Extract RGBA values
            double r1, g1, b1, a1, r2, g2, b2, a2;
            ParseRgbaValues(norm1, out r1, out g1, out b1, out a1);
            ParseRgbaValues(norm2, out r2, out g2, out b2, out a2);

            // Mix colors (simple linear interpolation in sRGB for now)
            double r = r1 * percent1 + r2 * percent2;
            double g = g1 * percent1 + g2 * percent2;
            double b = b1 * percent1 + b2 * percent2;
            double a = a1 * percent1 + a2 * percent2;

            return ToRgba(r, g, b, a);
        }

        public static void ParseRgbaValues(string rgba, out double r, out double g, out double b, out double a)
        {
            r = g = b = 0; a = 1;
            var match = Regex.Match(rgba, @"rgba?\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+))?\s*\)");
            if (match.Success)
            {
                r = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                g = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                b = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                if (match.Groups[4].Success)
                    a = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
            }
        }

        private static List<string> SplitColorMixArgs(string args)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            int depth = 0;
            foreach (char c in args)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;
                if (c == ',' && depth == 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            if (sb.Length > 0) result.Add(sb.ToString());
            return result;
        }

        /// <summary>
        /// Converts HWB to RGB
        /// </summary>
        private static void HwbToRgb(double h, double w, double b, out int r, out int g, out int bl)
        {
            // Normalize
            if (w + b >= 1)
            {
                var gray = (int)Math.Round(w / (w + b) * 255);
                r = g = bl = gray;
                return;
            }

            int hr, hg, hb;
            HslToRgb(h, 1, 0.5, out hr, out hg, out hb);

            r = (int)Math.Round(hr / 255.0 * (1 - w - b) * 255 + w * 255);
            g = (int)Math.Round(hg / 255.0 * (1 - w - b) * 255 + w * 255);
            bl = (int)Math.Round(hb / 255.0 * (1 - w - b) * 255 + w * 255);

            r = Math.Max(0, Math.Min(255, r));
            g = Math.Max(0, Math.Min(255, g));
            bl = Math.Max(0, Math.Min(255, bl));
        }

        /// <summary>
        /// Converts OKLCH to RGB
        /// </summary>
        private static void OklchToRgb(double L, double C, double H, out int r, out int g, out int b)
        {
            // Convert LCH to Lab
            double hRad = H * Math.PI / 180.0;
            double a = C * Math.Cos(hRad);
            double bb = C * Math.Sin(hRad);
            OklabToRgb(L, a, bb, out r, out g, out b);
        }

        /// <summary>
        /// Converts OKLab to RGB
        /// </summary>
        private static void OklabToRgb(double L, double A, double B, out int r, out int g, out int b)
        {
            // OKLab to linear sRGB
            double l_ = L + 0.3963377774 * A + 0.2158037573 * B;
            double m_ = L - 0.1055613458 * A - 0.0638541728 * B;
            double s_ = L - 0.0894841775 * A - 1.2914855480 * B;

            double l = l_ * l_ * l_;
            double m = m_ * m_ * m_;
            double s = s_ * s_ * s_;

            double lr = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
            double lg = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
            double lb = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

            // Linear to sRGB gamma
            r = (int)Math.Round(LinearToSrgb(lr) * 255);
            g = (int)Math.Round(LinearToSrgb(lg) * 255);
            b = (int)Math.Round(LinearToSrgb(lb) * 255);

            r = Math.Max(0, Math.Min(255, r));
            g = Math.Max(0, Math.Min(255, g));
            b = Math.Max(0, Math.Min(255, b));
        }

        private static double LinearToSrgb(double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            return x <= 0.0031308 ? 12.92 * x : 1.055 * Math.Pow(x, 1.0 / 2.4) - 0.055;
        }

        private static double SrgbToLinear(double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }

        /// <summary>
        /// Converts CIE Lab to RGB
        /// </summary>
        private static void LabToRgb(double L, double a, double b, out int r, out int g, out int bl)
        {
            // Lab to XYZ (D65 illuminant)
            double fy = (L + 16.0) / 116.0;
            double fx = a / 500.0 + fy;
            double fz = fy - b / 200.0;

            double xr = fx > 0.206897 ? fx * fx * fx : (fx - 16.0 / 116.0) / 7.787;
            double yr = fy > 0.206897 ? fy * fy * fy : (fy - 16.0 / 116.0) / 7.787;
            double zr = fz > 0.206897 ? fz * fz * fz : (fz - 16.0 / 116.0) / 7.787;

            // D65 white point
            double X = xr * 0.95047;
            double Y = yr * 1.00000;
            double Z = zr * 1.08883;

            XyzToRgb(X, Y, Z, out r, out g, out bl);
        }

        /// <summary>
        /// Converts CIE LCH to RGB
        /// </summary>
        private static void LchToRgb(double L, double C, double H, out int r, out int g, out int b)
        {
            double hRad = H * Math.PI / 180.0;
            double a = C * Math.Cos(hRad);
            double bb = C * Math.Sin(hRad);
            LabToRgb(L, a, bb, out r, out g, out b);
        }

        /// <summary>
        /// Converts XYZ to RGB (sRGB, D65)
        /// </summary>
        private static void XyzToRgb(double X, double Y, double Z, out int r, out int g, out int b)
        {
            // XYZ to linear sRGB (D65)
            double lr = X * 3.2404542 + Y * -1.5371385 + Z * -0.4985314;
            double lg = X * -0.9692660 + Y * 1.8760108 + Z * 0.0415560;
            double lb = X * 0.0556434 + Y * -0.2040259 + Z * 1.0572252;

            // Linear to sRGB gamma
            r = (int)Math.Round(LinearToSrgb(lr) * 255);
            g = (int)Math.Round(LinearToSrgb(lg) * 255);
            b = (int)Math.Round(LinearToSrgb(lb) * 255);

            r = Math.Max(0, Math.Min(255, r));
            g = Math.Max(0, Math.Min(255, g));
            b = Math.Max(0, Math.Min(255, b));
        }

        private static bool TryParseColorComponent(string input, out double value)
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

        private static bool TryParsePercentage(string input, out double value)
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

        private static void HslToRgb(double h, double s, double l, out int r, out int g, out int b)
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

        private static string ToRgba(double r, double g, double b, double a)
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
                ApplyProperty(target, weights, prefix + "-top", top, specificity, order, IsImportant(top), int.MaxValue);
                ApplyProperty(target, weights, prefix + "-right", right, specificity, order, IsImportant(right), int.MaxValue);
                ApplyProperty(target, weights, prefix + "-bottom", bottom, specificity, order, IsImportant(bottom), int.MaxValue);
                ApplyProperty(target, weights, prefix + "-left", left, specificity, order, IsImportant(left), int.MaxValue);
            }
            else
            {
                target[prefix + "-top"] = StripImportant(top);
                target[prefix + "-right"] = StripImportant(right);
                target[prefix + "-bottom"] = StripImportant(bottom);
                target[prefix + "-left"] = StripImportant(left);
            }
        }

        private void ApplyProperty(CssCascadeResult result, HtmlNode node, string name, string value, int specificity, int order, int layerPriority = int.MaxValue)
        {
            ApplyProperty(result.Styles[node], result.Weights[node], name, value, specificity, order, IsImportant(value), layerPriority);
            result.AppliedCount++;
        }

        private void ApplyProperty(Dictionary<string, string> styles, Dictionary<string, StyleEntry> weights, string name, string value, int specificity, int order, bool important, int layerPriority)
        {
            if (styles == null || weights == null || string.IsNullOrEmpty(name))
            {
                return;
            }

            var cleanedValue = StripImportant(value);
            StyleEntry current;
            if (!weights.TryGetValue(name, out current))
            {
                weights[name] = new StyleEntry { Value = cleanedValue, Specificity = specificity, Important = important, Order = order, LayerPriority = layerPriority };
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

            // Layer priority: higher priority wins before specificity/order
            if (layerPriority != current.LayerPriority)
            {
                if (layerPriority > current.LayerPriority)
                {
                    weights[name] = new StyleEntry { Value = cleanedValue, Specificity = specificity, Important = important, Order = order, LayerPriority = layerPriority };
                    styles[name] = cleanedValue;
                }
                return;
            }

            if (specificity > current.Specificity || (specificity == current.Specificity && order >= current.Order))
            {
                weights[name] = new StyleEntry { Value = cleanedValue, Specificity = specificity, Important = important, Order = order, LayerPriority = layerPriority };
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
            bool requireFirstOfType = false;
            bool requireLastOfType = false;
            bool requireOnlyChild = false;
            bool requireOnlyOfType = false;
            int? requireNthChild = null;
            int? requireNthLastChild = null;
            int? requireNthOfType = null;
            int? requireNthLastOfType = null;
            string nthChildFormula = null;
            string nthLastChildFormula = null;
            string nthOfTypeFormula = null;
            string nthLastOfTypeFormula = null;
            string notSelector = null;
            List<string> isSelectors = null;
            List<string> whereSelectors = null;
            List<string> hasSelectors = null;
            string langValue = null;
            bool requireEmpty = false;
            bool requireHover = false;
            bool requireFocus = false;
            bool requireFocusWithin = false;
            bool requireFocusVisible = false;
            bool requireActive = false;
            bool requireVisited = false;
            bool requireLink = false;
            bool requireEnabled = false;
            bool requireDisabled = false;
            bool requireChecked = false;
            bool requireRequired = false;
            bool requireOptional = false;
            bool requireReadOnly = false;
            bool requireReadWrite = false;
            bool requireTarget = false;
            bool requireRoot = false;
            bool requireIndeterminate = false;
            bool requireValid = false;
            bool requireInvalid = false;
            bool requireInRange = false;
            bool requireOutOfRange = false;
            bool requirePlaceholderShown = false;
            bool requireAutofill = false;
            bool requireFullscreen = false;
            bool requireDefined = false;
            bool requireDefault = false;
            bool requireCurrent = false;
            bool requirePast = false;
            bool requireFuture = false;
            bool requirePlaying = false;
            bool requirePaused = false;
            bool requireSeeking = false;
            bool requireBuffering = false;
            bool requireStalled = false;
            bool requireMuted = false;
            bool requireVolumeLocked = false;
            bool requireScope = false;
            bool requireBlank = false;
            bool requireAnyLink = false;

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
                else if (pseudo.StartsWith(":first-of-type", StringComparison.OrdinalIgnoreCase))
                {
                    requireFirstOfType = true;
                    pseudo = pseudo.Substring(":first-of-type".Length);
                }
                else if (pseudo.StartsWith(":last-of-type", StringComparison.OrdinalIgnoreCase))
                {
                    requireLastOfType = true;
                    pseudo = pseudo.Substring(":last-of-type".Length);
                }
                else if (pseudo.StartsWith(":only-child", StringComparison.OrdinalIgnoreCase))
                {
                    requireOnlyChild = true;
                    pseudo = pseudo.Substring(":only-child".Length);
                }
                else if (pseudo.StartsWith(":only-of-type", StringComparison.OrdinalIgnoreCase))
                {
                    requireOnlyOfType = true;
                    pseudo = pseudo.Substring(":only-of-type".Length);
                }
                else if (pseudo.StartsWith(":nth-child(", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = FindClosingParen(pseudo, 10);
                    if (endIdx > 0)
                    {
                        var inner = pseudo.Substring(":nth-child(".Length, endIdx - ":nth-child(".Length);
                        int n;
                        if (int.TryParse(inner.Trim(), out n) && n > 0)
                        {
                            requireNthChild = n;
                        }
                        else
                        {
                            nthChildFormula = inner.Trim().ToLowerInvariant();
                        }
                        pseudo = pseudo.Substring(endIdx + 1);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (pseudo.StartsWith(":nth-last-child(", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = FindClosingParen(pseudo, 15);
                    if (endIdx > 0)
                    {
                        var inner = pseudo.Substring(":nth-last-child(".Length, endIdx - ":nth-last-child(".Length);
                        int n;
                        if (int.TryParse(inner.Trim(), out n) && n > 0)
                        {
                            requireNthLastChild = n;
                        }
                        else
                        {
                            nthLastChildFormula = inner.Trim().ToLowerInvariant();
                        }
                        pseudo = pseudo.Substring(endIdx + 1);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (pseudo.StartsWith(":nth-of-type(", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = FindClosingParen(pseudo, 12);
                    if (endIdx > 0)
                    {
                        var inner = pseudo.Substring(":nth-of-type(".Length, endIdx - ":nth-of-type(".Length);
                        int n;
                        if (int.TryParse(inner.Trim(), out n) && n > 0)
                        {
                            requireNthOfType = n;
                        }
                        else
                        {
                            nthOfTypeFormula = inner.Trim().ToLowerInvariant();
                        }
                        pseudo = pseudo.Substring(endIdx + 1);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (pseudo.StartsWith(":nth-last-of-type(", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = FindClosingParen(pseudo, 17);
                    if (endIdx > 0)
                    {
                        var inner = pseudo.Substring(":nth-last-of-type(".Length, endIdx - ":nth-last-of-type(".Length);
                        int n;
                        if (int.TryParse(inner.Trim(), out n) && n > 0)
                        {
                            requireNthLastOfType = n;
                        }
                        else
                        {
                            nthLastOfTypeFormula = inner.Trim().ToLowerInvariant();
                        }
                        pseudo = pseudo.Substring(endIdx + 1);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (pseudo.StartsWith(":not(", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = FindClosingParen(pseudo, 4);
                    if (endIdx > 0)
                    {
                        var inner = pseudo.Substring(5, endIdx - 5).Trim();
                        notSelector = inner;
                        pseudo = pseudo.Substring(endIdx + 1);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (pseudo.StartsWith(":is(", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = FindClosingParen(pseudo, 3);
                    if (endIdx > 0)
                    {
                        var inner = pseudo.Substring(4, endIdx - 4).Trim();
                        isSelectors = inner.Split(',').Select(s => s.Trim()).ToList();
                        pseudo = pseudo.Substring(endIdx + 1);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (pseudo.StartsWith(":where(", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = FindClosingParen(pseudo, 6);
                    if (endIdx > 0)
                    {
                        var inner = pseudo.Substring(7, endIdx - 7).Trim();
                        whereSelectors = inner.Split(',').Select(s => s.Trim()).ToList();
                        pseudo = pseudo.Substring(endIdx + 1);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (pseudo.StartsWith(":has(", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = FindClosingParen(pseudo, 4);
                    if (endIdx > 0)
                    {
                        var inner = pseudo.Substring(5, endIdx - 5).Trim();
                        hasSelectors = inner.Split(',').Select(s => s.Trim()).ToList();
                        pseudo = pseudo.Substring(endIdx + 1);
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (pseudo.StartsWith(":lang(", StringComparison.OrdinalIgnoreCase))
                {
                    var endIdx = FindClosingParen(pseudo, 5);
                    if (endIdx > 0)
                    {
                        langValue = pseudo.Substring(6, endIdx - 6).Trim().Trim('"', '\'');
                        pseudo = pseudo.Substring(endIdx + 1);
                    }
                    else
                    {
                        return false;
                    }
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
                else if (pseudo.StartsWith(":focus-within", StringComparison.OrdinalIgnoreCase))
                {
                    requireFocusWithin = true;
                    pseudo = pseudo.Substring(":focus-within".Length);
                }
                else if (pseudo.StartsWith(":focus-visible", StringComparison.OrdinalIgnoreCase))
                {
                    requireFocusVisible = true;
                    pseudo = pseudo.Substring(":focus-visible".Length);
                }
                else if (pseudo.StartsWith(":focus", StringComparison.OrdinalIgnoreCase))
                {
                    requireFocus = true;
                    pseudo = pseudo.Substring(":focus".Length);
                }
                else if (pseudo.StartsWith(":active", StringComparison.OrdinalIgnoreCase))
                {
                    requireActive = true;
                    pseudo = pseudo.Substring(":active".Length);
                }
                else if (pseudo.StartsWith(":visited", StringComparison.OrdinalIgnoreCase))
                {
                    requireVisited = true;
                    pseudo = pseudo.Substring(":visited".Length);
                }
                else if (pseudo.StartsWith(":link", StringComparison.OrdinalIgnoreCase))
                {
                    requireLink = true;
                    pseudo = pseudo.Substring(":link".Length);
                }
                else if (pseudo.StartsWith(":any-link", StringComparison.OrdinalIgnoreCase))
                {
                    requireAnyLink = true;
                    pseudo = pseudo.Substring(":any-link".Length);
                }
                else if (pseudo.StartsWith(":scope", StringComparison.OrdinalIgnoreCase))
                {
                    requireScope = true;
                    pseudo = pseudo.Substring(":scope".Length);
                }
                else if (pseudo.StartsWith(":blank", StringComparison.OrdinalIgnoreCase))
                {
                    requireBlank = true;
                    pseudo = pseudo.Substring(":blank".Length);
                }
                else if (pseudo.StartsWith(":enabled", StringComparison.OrdinalIgnoreCase))
                {
                    requireEnabled = true;
                    pseudo = pseudo.Substring(":enabled".Length);
                }
                else if (pseudo.StartsWith(":disabled", StringComparison.OrdinalIgnoreCase))
                {
                    requireDisabled = true;
                    pseudo = pseudo.Substring(":disabled".Length);
                }
                else if (pseudo.StartsWith(":checked", StringComparison.OrdinalIgnoreCase))
                {
                    requireChecked = true;
                    pseudo = pseudo.Substring(":checked".Length);
                }
                else if (pseudo.StartsWith(":required", StringComparison.OrdinalIgnoreCase))
                {
                    requireRequired = true;
                    pseudo = pseudo.Substring(":required".Length);
                }
                else if (pseudo.StartsWith(":optional", StringComparison.OrdinalIgnoreCase))
                {
                    requireOptional = true;
                    pseudo = pseudo.Substring(":optional".Length);
                }
                else if (pseudo.StartsWith(":read-only", StringComparison.OrdinalIgnoreCase))
                {
                    requireReadOnly = true;
                    pseudo = pseudo.Substring(":read-only".Length);
                }
                else if (pseudo.StartsWith(":read-write", StringComparison.OrdinalIgnoreCase))
                {
                    requireReadWrite = true;
                    pseudo = pseudo.Substring(":read-write".Length);
                }
                else if (pseudo.StartsWith(":target", StringComparison.OrdinalIgnoreCase))
                {
                    requireTarget = true;
                    pseudo = pseudo.Substring(":target".Length);
                }
                else if (pseudo.StartsWith(":root", StringComparison.OrdinalIgnoreCase))
                {
                    requireRoot = true;
                    pseudo = pseudo.Substring(":root".Length);
                }
                else if (pseudo.StartsWith(":indeterminate", StringComparison.OrdinalIgnoreCase))
                {
                    requireIndeterminate = true;
                    pseudo = pseudo.Substring(":indeterminate".Length);
                }
                else if (pseudo.StartsWith(":valid", StringComparison.OrdinalIgnoreCase))
                {
                    requireValid = true;
                    pseudo = pseudo.Substring(":valid".Length);
                }
                else if (pseudo.StartsWith(":invalid", StringComparison.OrdinalIgnoreCase))
                {
                    requireInvalid = true;
                    pseudo = pseudo.Substring(":invalid".Length);
                }
                else if (pseudo.StartsWith(":in-range", StringComparison.OrdinalIgnoreCase))
                {
                    requireInRange = true;
                    pseudo = pseudo.Substring(":in-range".Length);
                }
                else if (pseudo.StartsWith(":out-of-range", StringComparison.OrdinalIgnoreCase))
                {
                    requireOutOfRange = true;
                    pseudo = pseudo.Substring(":out-of-range".Length);
                }
                else if (pseudo.StartsWith(":placeholder-shown", StringComparison.OrdinalIgnoreCase))
                {
                    requirePlaceholderShown = true;
                    pseudo = pseudo.Substring(":placeholder-shown".Length);
                }
                else if (pseudo.StartsWith(":autofill", StringComparison.OrdinalIgnoreCase))
                {
                    requireAutofill = true;
                    pseudo = pseudo.Substring(":autofill".Length);
                }
                else if (pseudo.StartsWith(":-webkit-autofill", StringComparison.OrdinalIgnoreCase))
                {
                    requireAutofill = true;
                    pseudo = pseudo.Substring(":-webkit-autofill".Length);
                }
                else if (pseudo.StartsWith(":fullscreen", StringComparison.OrdinalIgnoreCase))
                {
                    requireFullscreen = true;
                    pseudo = pseudo.Substring(":fullscreen".Length);
                }
                else if (pseudo.StartsWith(":-webkit-full-screen", StringComparison.OrdinalIgnoreCase))
                {
                    requireFullscreen = true;
                    pseudo = pseudo.Substring(":-webkit-full-screen".Length);
                }
                else if (pseudo.StartsWith(":defined", StringComparison.OrdinalIgnoreCase))
                {
                    requireDefined = true;
                    pseudo = pseudo.Substring(":defined".Length);
                }
                else if (pseudo.StartsWith(":default", StringComparison.OrdinalIgnoreCase))
                {
                    requireDefault = true;
                    pseudo = pseudo.Substring(":default".Length);
                }
                else if (pseudo.StartsWith(":current", StringComparison.OrdinalIgnoreCase))
                {
                    requireCurrent = true;
                    pseudo = pseudo.Substring(":current".Length);
                }
                else if (pseudo.StartsWith(":past", StringComparison.OrdinalIgnoreCase))
                {
                    requirePast = true;
                    pseudo = pseudo.Substring(":past".Length);
                }
                else if (pseudo.StartsWith(":future", StringComparison.OrdinalIgnoreCase))
                {
                    requireFuture = true;
                    pseudo = pseudo.Substring(":future".Length);
                }
                else if (pseudo.StartsWith(":playing", StringComparison.OrdinalIgnoreCase))
                {
                    requirePlaying = true;
                    pseudo = pseudo.Substring(":playing".Length);
                }
                else if (pseudo.StartsWith(":paused", StringComparison.OrdinalIgnoreCase))
                {
                    requirePaused = true;
                    pseudo = pseudo.Substring(":paused".Length);
                }
                else if (pseudo.StartsWith(":seeking", StringComparison.OrdinalIgnoreCase))
                {
                    requireSeeking = true;
                    pseudo = pseudo.Substring(":seeking".Length);
                }
                else if (pseudo.StartsWith(":buffering", StringComparison.OrdinalIgnoreCase))
                {
                    requireBuffering = true;
                    pseudo = pseudo.Substring(":buffering".Length);
                }
                else if (pseudo.StartsWith(":stalled", StringComparison.OrdinalIgnoreCase))
                {
                    requireStalled = true;
                    pseudo = pseudo.Substring(":stalled".Length);
                }
                else if (pseudo.StartsWith(":muted", StringComparison.OrdinalIgnoreCase))
                {
                    requireMuted = true;
                    pseudo = pseudo.Substring(":muted".Length);
                }
                else if (pseudo.StartsWith(":volume-locked", StringComparison.OrdinalIgnoreCase))
                {
                    requireVolumeLocked = true;
                    pseudo = pseudo.Substring(":volume-locked".Length);
                }
                else
                {
                    // unsupported pseudo - skip but don't fail
                    int nextColon = pseudo.IndexOf(':', 1);
                    if (nextColon > 0)
                    {
                        pseudo = pseudo.Substring(nextColon);
                    }
                    else
                    {
                        pseudo = string.Empty;
                    }
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

            // Check pseudo-class conditions
            if (requireFirstChild && !IsFirstChild(node)) return false;
            if (requireLastChild && !IsLastChild(node)) return false;
            if (requireFirstOfType && !IsFirstOfType(node)) return false;
            if (requireLastOfType && !IsLastOfType(node)) return false;
            if (requireOnlyChild && !IsOnlyChild(node)) return false;
            if (requireOnlyOfType && !IsOnlyOfType(node)) return false;

            if (requireNthChild.HasValue)
            {
                if (node.Parent == null) return false;
                var idx = node.Parent.Children.IndexOf(node) + 1;
                if (idx != requireNthChild.Value) return false;
            }

            if (nthChildFormula != null)
            {
                if (!MatchesNthFormula(node, nthChildFormula, false, false)) return false;
            }

            if (requireNthLastChild.HasValue)
            {
                if (node.Parent == null) return false;
                var idx = node.Parent.Children.Count - node.Parent.Children.IndexOf(node);
                if (idx != requireNthLastChild.Value) return false;
            }

            if (nthLastChildFormula != null)
            {
                if (!MatchesNthFormula(node, nthLastChildFormula, true, false)) return false;
            }

            if (requireNthOfType.HasValue)
            {
                if (!IsNthOfType(node, requireNthOfType.Value)) return false;
            }

            if (nthOfTypeFormula != null)
            {
                if (!MatchesNthFormula(node, nthOfTypeFormula, false, true)) return false;
            }

            if (requireNthLastOfType.HasValue)
            {
                if (!IsNthLastOfType(node, requireNthLastOfType.Value)) return false;
            }

            if (nthLastOfTypeFormula != null)
            {
                if (!MatchesNthFormula(node, nthLastOfTypeFormula, true, true)) return false;
            }

            if (requireEmpty)
            {
                bool hasChildren = node.Children != null && node.Children.Any(c => !string.Equals(c.Tag, "#text", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(c.Text));
                bool hasText = !string.IsNullOrWhiteSpace(node.Text);
                if (hasChildren || hasText) return false;
            }

            if (requireHover && !(node.Attributes.ContainsKey("data-hover") || node.Attributes.ContainsKey("hover")))
                return false;

            if (requireFocus && !(node.Attributes.ContainsKey("data-focus") || node.Attributes.ContainsKey("focus")))
                return false;

            if (requireActive && !(node.Attributes.ContainsKey("data-active") || node.Attributes.ContainsKey("active")))
                return false;

            if (requireVisited && !node.Attributes.ContainsKey("data-visited"))
                return false;

            if (requireLink)
            {
                if (!string.Equals(node.Tag, "a", StringComparison.OrdinalIgnoreCase) || !node.Attributes.ContainsKey("href"))
                    return false;
            }

            // :any-link matches any element that matches :link or :visited
            if (requireAnyLink)
            {
                var linkTags = new[] { "a", "area", "link" };
                if (!linkTags.Any(t => string.Equals(node.Tag, t, StringComparison.OrdinalIgnoreCase)) || !node.Attributes.ContainsKey("href"))
                    return false;
            }

            // :scope matches the scoping root (usually :root or the element querySelectorAll was called on)
            if (requireScope)
            {
                if (node.Parent != null && !string.Equals(node.Tag, "html", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            // :blank matches empty inputs/textareas with no user input
            if (requireBlank)
            {
                if (!string.Equals(node.Tag, "input", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(node.Tag, "textarea", StringComparison.OrdinalIgnoreCase))
                    return false;
                string val;
                if (node.Attributes.TryGetValue("value", out val) && !string.IsNullOrEmpty(val))
                    return false;
                if (!string.IsNullOrWhiteSpace(node.Text))
                    return false;
            }

            if (requireEnabled)
            {
                if (node.Attributes.ContainsKey("disabled")) return false;
            }

            if (requireDisabled)
            {
                if (!node.Attributes.ContainsKey("disabled")) return false;
            }

            if (requireChecked)
            {
                if (!node.Attributes.ContainsKey("checked")) return false;
            }

            if (requireRequired)
            {
                if (!node.Attributes.ContainsKey("required")) return false;
            }

            if (requireOptional)
            {
                if (node.Attributes.ContainsKey("required")) return false;
            }

            if (requireReadOnly)
            {
                if (!node.Attributes.ContainsKey("readonly") && !node.Attributes.ContainsKey("disabled")) return false;
            }

            if (requireReadWrite)
            {
                if (node.Attributes.ContainsKey("readonly") || node.Attributes.ContainsKey("disabled")) return false;
            }

            if (requireTarget)
            {
                if (!node.Attributes.ContainsKey("id") || !node.Attributes.ContainsKey("data-target")) return false;
            }

            if (requireRoot)
            {
                if (node.Parent != null && !string.Equals(node.Tag, "html", StringComparison.OrdinalIgnoreCase)) return false;
            }

            if (requireFocusWithin)
            {
                // :focus-within matches if the element or any descendant has focus
                if (!HasFocusedDescendant(node)) return false;
            }

            if (requireFocusVisible)
            {
                // :focus-visible matches if element has focus via keyboard navigation
                if (!(node.Attributes.ContainsKey("data-focus-visible") || 
                     (node.Attributes.ContainsKey("data-focus") && node.Attributes.ContainsKey("data-keyboard"))))
                    return false;
            }

            if (!string.IsNullOrEmpty(langValue))
            {
                // Check lang attribute on element or ancestors
                if (!MatchesLang(node, langValue)) return false;
            }

            if (!string.IsNullOrEmpty(notSelector) && MatchesSimpleSelector(node, notSelector))
                return false;

            if (isSelectors != null && isSelectors.Count > 0)
            {
                if (!isSelectors.Any(s => MatchesSimpleSelector(node, s))) return false;
            }

            if (whereSelectors != null && whereSelectors.Count > 0)
            {
                if (!whereSelectors.Any(s => MatchesSimpleSelector(node, s))) return false;
            }

            // :has() - check if any descendant matches the selector
            if (hasSelectors != null && hasSelectors.Count > 0)
            {
                if (!hasSelectors.Any(s => HasMatchingDescendant(node, s))) return false;
            }

            // Form validation pseudo-classes
            if (requireIndeterminate)
            {
                // :indeterminate matches checkboxes/radios in indeterminate state
                if (!node.Attributes.ContainsKey("data-indeterminate") && !node.Attributes.ContainsKey("indeterminate"))
                    return false;
            }

            if (requireValid)
            {
                // :valid matches form elements that pass validation
                if (node.Attributes.ContainsKey("data-invalid") || !IsFormElement(node))
                    return false;
            }

            if (requireInvalid)
            {
                // :invalid matches form elements that fail validation
                if (!node.Attributes.ContainsKey("data-invalid") && !node.Attributes.ContainsKey("aria-invalid"))
                    return false;
            }

            if (requireInRange)
            {
                // :in-range matches input elements with value within min/max
                if (!IsInputWithinRange(node, inRange: true)) return false;
            }

            if (requireOutOfRange)
            {
                // :out-of-range matches input elements with value outside min/max
                if (!IsInputWithinRange(node, inRange: false)) return false;
            }

            if (requirePlaceholderShown)
            {
                // :placeholder-shown matches input/textarea showing placeholder
                if (!node.Attributes.ContainsKey("placeholder")) return false;
                if (node.Attributes.ContainsKey("value") && !string.IsNullOrEmpty(node.Attributes["value"])) return false;
                if (!string.IsNullOrWhiteSpace(node.Text)) return false;
            }

            if (requireAutofill)
            {
                // :autofill matches inputs that have been autofilled by browser
                if (!node.Attributes.ContainsKey("data-autofill") && !node.Attributes.ContainsKey("autofill"))
                    return false;
            }

            if (requireFullscreen)
            {
                // :fullscreen matches elements in fullscreen mode
                if (!node.Attributes.ContainsKey("data-fullscreen") && !node.Attributes.ContainsKey("fullscreen"))
                    return false;
            }

            if (requireDefined)
            {
                // :defined matches defined custom elements (all standard elements are defined)
                var tag = node.Tag ?? "";
                if (tag.Contains("-") && !node.Attributes.ContainsKey("data-defined"))
                    return false;
            }

            if (requireDefault)
            {
                // :default matches the default button/input in a form
                if (!node.Attributes.ContainsKey("data-default") && !node.Attributes.ContainsKey("default"))
                    return false;
            }

            // Media pseudo-classes
            if (requirePlaying)
            {
                if (!node.Attributes.ContainsKey("data-playing")) return false;
            }

            if (requirePaused)
            {
                if (!node.Attributes.ContainsKey("data-paused") && !node.Attributes.ContainsKey("paused")) return false;
            }

            if (requireSeeking)
            {
                if (!node.Attributes.ContainsKey("data-seeking")) return false;
            }

            if (requireBuffering)
            {
                if (!node.Attributes.ContainsKey("data-buffering")) return false;
            }

            if (requireStalled)
            {
                if (!node.Attributes.ContainsKey("data-stalled")) return false;
            }

            if (requireMuted)
            {
                if (!node.Attributes.ContainsKey("muted") && !node.Attributes.ContainsKey("data-muted")) return false;
            }

            if (requireVolumeLocked)
            {
                if (!node.Attributes.ContainsKey("data-volume-locked")) return false;
            }

            // Timeline pseudo-classes
            if (requireCurrent)
            {
                if (!node.Attributes.ContainsKey("data-current")) return false;
            }

            if (requirePast)
            {
                if (!node.Attributes.ContainsKey("data-past")) return false;
            }

            if (requireFuture)
            {
                if (!node.Attributes.ContainsKey("data-future")) return false;
            }

            return true;
        }

        private bool IsFormElement(HtmlNode node)
        {
            var formTags = new[] { "input", "select", "textarea", "button" };
            return formTags.Any(t => string.Equals(node.Tag, t, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsInputWithinRange(HtmlNode node, bool inRange)
        {
            if (!string.Equals(node.Tag, "input", StringComparison.OrdinalIgnoreCase))
                return false;

            string min, max, value;
            node.Attributes.TryGetValue("min", out min);
            node.Attributes.TryGetValue("max", out max);
            node.Attributes.TryGetValue("value", out value);

            if (string.IsNullOrEmpty(min) && string.IsNullOrEmpty(max))
                return false;

            double valNum;
            if (!double.TryParse(value ?? "", NumberStyles.Any, CultureInfo.InvariantCulture, out valNum))
                return false;

            bool withinRange = true;
            double minNum, maxNum;

            if (!string.IsNullOrEmpty(min) && double.TryParse(min, NumberStyles.Any, CultureInfo.InvariantCulture, out minNum))
            {
                if (valNum < minNum) withinRange = false;
            }

            if (!string.IsNullOrEmpty(max) && double.TryParse(max, NumberStyles.Any, CultureInfo.InvariantCulture, out maxNum))
            {
                if (valNum > maxNum) withinRange = false;
            }

            return inRange ? withinRange : !withinRange;
        }

        private bool HasFocusedDescendant(HtmlNode node)
        {
            if (node == null) return false;
            if (node.Attributes.ContainsKey("data-focus") || node.Attributes.ContainsKey("focus"))
                return true;
            foreach (var child in node.Children)
            {
                if (HasFocusedDescendant(child)) return true;
            }
            return false;
        }

        private bool MatchesLang(HtmlNode node, string lang)
        {
            if (node == null) return false;
            
            // Check this element's lang attribute
            if (node.Attributes.ContainsKey("lang"))
            {
                var nodeLang = node.Attributes["lang"].ToLowerInvariant();
                var targetLang = lang.ToLowerInvariant();
                // Match exact or prefix (e.g., "en" matches "en-US")
                if (nodeLang == targetLang || nodeLang.StartsWith(targetLang + "-"))
                    return true;
            }
            
            // Check ancestors
            if (node.Parent != null)
                return MatchesLang(node.Parent, lang);
            
            return false;
        }

        private bool HasMatchingDescendant(HtmlNode node, string selector)
        {
            if (node == null || string.IsNullOrWhiteSpace(selector)) return false;
            
            // Handle relative selectors that start with > or + or ~
            var trimmed = selector.TrimStart();
            
            if (trimmed.StartsWith(">"))
            {
                // Direct child selector
                var childSelector = trimmed.Substring(1).Trim();
                foreach (var child in node.Children)
                {
                    if (MatchesSimpleSelector(child, childSelector)) return true;
                }
                return false;
            }
            
            if (trimmed.StartsWith("+"))
            {
                // Adjacent sibling - doesn't make sense for :has(), skip
                return false;
            }
            
            if (trimmed.StartsWith("~"))
            {
                // General sibling - doesn't make sense for :has(), skip  
                return false;
            }
            
            // Check all descendants
            foreach (var child in node.Children)
            {
                if (MatchesSelector(child, selector)) return true;
                if (HasMatchingDescendant(child, selector)) return true;
            }
            
            return false;
        }

        private int FindClosingParen(string text, int startAfter)
        {
            int depth = 0;
            for (int i = startAfter; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')')
                {
                    if (depth == 0) return i;
                    depth--;
                }
            }
            return -1;
        }

        private bool IsFirstOfType(HtmlNode node)
        {
            if (node?.Parent == null) return false;
            return node.Parent.Children.FirstOrDefault(c => string.Equals(c.Tag, node.Tag, StringComparison.OrdinalIgnoreCase)) == node;
        }

        private bool IsLastOfType(HtmlNode node)
        {
            if (node?.Parent == null) return false;
            return node.Parent.Children.LastOrDefault(c => string.Equals(c.Tag, node.Tag, StringComparison.OrdinalIgnoreCase)) == node;
        }

        private bool IsOnlyChild(HtmlNode node)
        {
            if (node?.Parent == null) return false;
            return node.Parent.Children.Count(c => !string.Equals(c.Tag, "#text", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(c.Text)) == 1;
        }

        private bool IsOnlyOfType(HtmlNode node)
        {
            if (node?.Parent == null) return false;
            return node.Parent.Children.Count(c => string.Equals(c.Tag, node.Tag, StringComparison.OrdinalIgnoreCase)) == 1;
        }

        private bool IsNthOfType(HtmlNode node, int n)
        {
            if (node?.Parent == null) return false;
            var sameType = node.Parent.Children.Where(c => string.Equals(c.Tag, node.Tag, StringComparison.OrdinalIgnoreCase)).ToList();
            var idx = sameType.IndexOf(node) + 1;
            return idx == n;
        }

        private bool IsNthLastOfType(HtmlNode node, int n)
        {
            if (node?.Parent == null) return false;
            var sameType = node.Parent.Children.Where(c => string.Equals(c.Tag, node.Tag, StringComparison.OrdinalIgnoreCase)).ToList();
            var idx = sameType.Count - sameType.IndexOf(node);
            return idx == n;
        }

        private bool MatchesNthFormula(HtmlNode node, string formula, bool fromEnd, bool ofType)
        {
            if (node?.Parent == null) return false;

            formula = formula.Trim().ToLowerInvariant();
            int index;

            if (ofType)
            {
                var sameType = node.Parent.Children.Where(c => string.Equals(c.Tag, node.Tag, StringComparison.OrdinalIgnoreCase)).ToList();
                index = fromEnd ? sameType.Count - sameType.IndexOf(node) : sameType.IndexOf(node) + 1;
            }
            else
            {
                index = fromEnd ? node.Parent.Children.Count - node.Parent.Children.IndexOf(node) : node.Parent.Children.IndexOf(node) + 1;
            }

            // Handle keywords
            if (formula == "odd") return index % 2 == 1;
            if (formula == "even") return index % 2 == 0;

            // Parse An+B syntax
            int a = 0, b = 0;
            formula = formula.Replace(" ", "");

            if (formula.Contains("n"))
            {
                var parts = formula.Split('n');
                var aStr = parts[0].Replace("+", "");
                if (string.IsNullOrEmpty(aStr) || aStr == "-") aStr = aStr + "1";
                int.TryParse(aStr, out a);

                if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                {
                    int.TryParse(parts[1].Replace("+", ""), out b);
                }
            }
            else
            {
                int.TryParse(formula, out b);
                return index == b;
            }

            // Check if index matches An+B for some non-negative n
            if (a == 0) return index == b;
            if ((index - b) % a != 0) return false;
            var n = (index - b) / a;
            return n >= 0;
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
        public string Type { get; set; } // "linear", "radial", "conic"
        public double Angle { get; set; } = 180; // degrees for linear, start angle for conic
        public string Direction { get; set; } // "to top", "to bottom right", etc.
        public string Shape { get; set; } // for radial: "circle", "ellipse", etc.
        public List<CssColorStop> ColorStops { get; set; } = new List<CssColorStop>();
    }

    public class CssColorStop
    {
        public string Color { get; set; }
        public double Position { get; set; } // 0-1 for percentage
        public double PositionPx { get; set; } // absolute position in px
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
        public string LayerName { get; set; }
        public int LayerPriority { get; set; } = int.MaxValue;
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

    /// <summary>
    /// Represents a CSS clip-path value
    /// </summary>
    public class CssClipPath
    {
        public string Type { get; set; } = "none"; // none, inset, circle, ellipse, polygon, path, url, box
        public string Url { get; set; }
        public List<string> Insets { get; set; } = new List<string>(); // top, right, bottom, left
        public string BorderRadius { get; set; }
        public string Radius { get; set; } // for circle
        public string RadiusX { get; set; } // for ellipse
        public string RadiusY { get; set; } // for ellipse
        public string Position { get; set; } // center position
        public List<(string X, string Y)> Points { get; set; } = new List<(string, string)>(); // for polygon
        public string FillRule { get; set; } = "nonzero"; // nonzero or evenodd
        public string PathData { get; set; } // SVG path data for path()
        public string Box { get; set; } // margin-box, border-box, etc.
    }

    /// <summary>
    /// Container context for container queries
    /// </summary>
    public class ContainerContext
    {
        public string Name { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string ContainerType { get; set; } = "normal"; // normal, size, inline-size
    }

    /// <summary>
    /// Option for image-set() CSS function
    /// </summary>
    public class ImageSetOption
    {
        public string Url { get; set; }
        public double Resolution { get; set; } = 1.0;
        public string Type { get; set; } // MIME type hint
    }

    /// <summary>
    /// Represents CSS text-decoration longhand properties
    /// </summary>
    public class CssTextDecoration
    {
        public string Line { get; set; } = "none"; // none, underline, overline, line-through
        public string Style { get; set; } = "solid"; // solid, double, dotted, dashed, wavy
        public string Color { get; set; } = "currentcolor";
        public string Thickness { get; set; } = "auto";
    } 

    /// <summary>
    /// Represents scroll-snap properties
    /// </summary>
    public class CssScrollSnap
    {
        public string Type { get; set; } = "none"; // none, x, y, block, inline, both
        public string Strictness { get; set; } = "proximity"; // mandatory, proximity
        public string Align { get; set; } = "none"; // none, start, end, center
        public string Stop { get; set; } = "normal"; // normal, always
    }
}
