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
                    MaxButton.Content = "?";
                }
            }
            else
            {
                WindowState = WindowState.Maximized;
                if (MaxButton != null)
                {
                    MaxButton.Content = "?";
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

}
