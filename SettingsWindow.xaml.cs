using System;
using System.Linq;
using System.Windows;

namespace FawkesWeb
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadFromSettings();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var settings = AppSettings.Current;

            settings.TorEnforce = TorEnforce.IsChecked == true;
            settings.TorReconnect = TorReconnect.IsChecked == true;
            settings.NoReferrer = NoReferrer.IsChecked == true;
            settings.UserAgent = UserAgent.Text ?? string.Empty;
            settings.FingerprintProtection = FingerprintProtection.IsChecked == true;
            settings.PauseOnBlur = PauseOnBlur.IsChecked == true;
            settings.AutoRejectCookies = AutoRejectCookies.IsChecked == true;
            settings.DisableWebGl = DisableWebGl.IsChecked == true;
            settings.AutoRead = AutoRead.IsChecked == true;
            settings.EnableDrm = EnableDrm.IsChecked == true;
            settings.AiEndpoint = AiEndpoint.Text ?? string.Empty;
            settings.AiToken = AiToken.Password ?? string.Empty;
            settings.AiModel = AiModel.Text ?? string.Empty;

            settings.BlockedDomains = SplitLines(BlockedDomains.Text);
            settings.BlockedJs = SplitLines(BlockedJs.Text);
            settings.BlockedCss = SplitLines(BlockedCss.Text);
            settings.CookieAllowList = SplitLines(CookieAllowList.Text);
            settings.RssFeeds = SplitLines(RssFeeds.Text);
            settings.ICalUrl = ICalUrl.Text ?? string.Empty;
            settings.ImapServer = ImapServer.Text ?? string.Empty;
            settings.ImapUser = ImapUser.Text ?? string.Empty;
            settings.ImapPassword = ImapPassword.Password ?? string.Empty;

            MessageBox.Show("Settings saved (in-memory placeholder).", "FakewsWeb", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SetGoogleUA_Click(object sender, RoutedEventArgs e)
        {
            UserAgent.Text = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        }

        private void LoadFromSettings()
        {
            var settings = AppSettings.Current;

            TorEnforce.IsChecked = settings.TorEnforce;
            TorReconnect.IsChecked = settings.TorReconnect;
            NoReferrer.IsChecked = settings.NoReferrer;
            UserAgent.Text = settings.UserAgent;
            FingerprintProtection.IsChecked = settings.FingerprintProtection;
            PauseOnBlur.IsChecked = settings.PauseOnBlur;
            AutoRejectCookies.IsChecked = settings.AutoRejectCookies;
            DisableWebGl.IsChecked = settings.DisableWebGl;
            AutoRead.IsChecked = settings.AutoRead;
            EnableDrm.IsChecked = settings.EnableDrm;
            AiEndpoint.Text = settings.AiEndpoint;
            AiToken.Password = settings.AiToken ?? string.Empty;
            AiModel.Text = settings.AiModel;

            BlockedDomains.Text = string.Join(Environment.NewLine, settings.BlockedDomains ?? Enumerable.Empty<string>());
            BlockedJs.Text = string.Join(Environment.NewLine, settings.BlockedJs ?? Enumerable.Empty<string>());
            BlockedCss.Text = string.Join(Environment.NewLine, settings.BlockedCss ?? Enumerable.Empty<string>());
            CookieAllowList.Text = string.Join(Environment.NewLine, settings.CookieAllowList ?? Enumerable.Empty<string>());
            RssFeeds.Text = string.Join(Environment.NewLine, settings.RssFeeds ?? Enumerable.Empty<string>());
            ICalUrl.Text = settings.ICalUrl;
            ImapServer.Text = settings.ImapServer;
            ImapUser.Text = settings.ImapUser;
            ImapPassword.Password = settings.ImapPassword;
        }

        private static System.Collections.Generic.List<string> SplitLines(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new System.Collections.Generic.List<string>();
            }

            return value.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrEmpty(x))
                        .ToList();
        }
    }
}
