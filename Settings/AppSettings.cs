using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FawkesWeb
{
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

        public bool IsJsBlocked(string content) => MatchesPatterns(BlockedJs, content);

        public bool IsCssBlocked(string content) => MatchesPatterns(BlockedCss, content);

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
}
