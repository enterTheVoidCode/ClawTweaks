using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClawTweaksSetup.Ui
{
    /// <summary>
    /// Renders a GitHub release body as a lightweight "What's new" panel: only the "## What's new"
    /// section is shown (install instructions above it are dropped), headings/bullets/blockquotes get
    /// simple styling, inline markdown is stripped, and image tags (HTML &lt;img src&gt; or markdown
    /// ![](...)) load as actual images straight from their URL. Ported 1:1 from
    /// XboxGamingBar/Features/GoTweaksUpdate/GamingWidget.AppUpdate.cs (RenderReleaseBody /
    /// ExtractWhatsNew / TryExtractImageUrl / StripInline) — the widget's in-app updater already does
    /// exactly this; only the UI-framework types differ (WPF Image/BitmapImage here vs. UWP there).
    /// </summary>
    public static class ReleaseNotes
    {
        public static void RenderInto(StackPanel parent, string body)
        {
            if (parent == null) return;
            if (string.IsNullOrWhiteSpace(body))
            {
                parent.Children.Add(BodyText("No release notes."));
                return;
            }

            string whatsNew = ExtractWhatsNew(body);
            var lines = whatsNew.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            bool any = false;
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                // Section divider / front-matter markers we don't want to show.
                if (line == "---" || line.StartsWith("> [!") || line.StartsWith("```")) continue;

                // Image (HTML <img src="..."> or markdown ![alt](url)).
                if (TryExtractImageUrl(line, out string imgUrl))
                {
                    var img = MakeImage(imgUrl);
                    if (img != null) { parent.Children.Add(img); any = true; }
                    continue;
                }

                // Heading (### / ## / #).
                if (line.StartsWith("#"))
                {
                    string h = StripInline(line.TrimStart('#').Trim());
                    if (h.Length == 0) continue;
                    parent.Children.Add(new TextBlock
                    {
                        Text = h,
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = UiHelpers.Text,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 10, 0, 3),
                    });
                    any = true;
                    continue;
                }

                // Blockquote → muted note.
                if (line.StartsWith(">"))
                {
                    string q = StripInline(line.TrimStart('>').Trim());
                    if (q.Length == 0) continue;
                    var note = BodyText(q);
                    note.Foreground = UiHelpers.Subtle;
                    parent.Children.Add(note);
                    any = true;
                    continue;
                }

                // Bullet.
                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    parent.Children.Add(BodyText("•  " + StripInline(line.Substring(2).Trim())));
                    any = true;
                    continue;
                }

                // Plain paragraph.
                parent.Children.Add(BodyText(StripInline(line)));
                any = true;
            }

            if (!any) parent.Children.Add(BodyText("No release notes."));
        }

        private static TextBlock BodyText(string text) => new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = UiHelpers.Subtle,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 1),
        };

        private static Image MakeImage(string url)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
                return new Image
                {
                    Source = new BitmapImage(uri),
                    Stretch = Stretch.Uniform,
                    MaxWidth = 420,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 6, 0, 6),
                };
            }
            catch { return null; }
        }

        // Returns the text from the "## What's new" heading onward (heading excluded). If no such
        // heading exists, returns the whole body so nothing is lost.
        private static string ExtractWhatsNew(string body)
        {
            var lines = body.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("#") && t.TrimStart('#').Trim().StartsWith("What's new", StringComparison.OrdinalIgnoreCase))
                    return string.Join("\n", lines, i + 1, lines.Length - (i + 1));
            }
            return body;
        }

        // Extracts an image URL from an HTML <img ... src="..."> tag or a markdown ![alt](url).
        private static bool TryExtractImageUrl(string line, out string url)
        {
            url = "";
            int img = line.IndexOf("<img", StringComparison.OrdinalIgnoreCase);
            if (img >= 0)
            {
                int s = line.IndexOf("src", img, StringComparison.OrdinalIgnoreCase);
                if (s >= 0)
                {
                    int q = line.IndexOfAny(new[] { '"', '\'' }, s);
                    if (q >= 0)
                    {
                        char quote = line[q];
                        int end = line.IndexOf(quote, q + 1);
                        if (end > q) { url = line.Substring(q + 1, end - q - 1); return url.Length > 0; }
                    }
                }
            }
            // Markdown: ![alt](URL)
            int bang = line.IndexOf("![", StringComparison.Ordinal);
            if (bang >= 0)
            {
                int open = line.IndexOf('(', bang);
                int close = open >= 0 ? line.IndexOf(')', open) : -1;
                if (open >= 0 && close > open)
                {
                    url = line.Substring(open + 1, close - open - 1).Trim();
                    int sp = url.IndexOf(' ');
                    if (sp > 0) url = url.Substring(0, sp); // strips a trailing "title"
                    return url.Length > 0;
                }
            }
            return false;
        }

        // Strips the inline markdown release notes use: **bold**, `code`, and [text](url) → text.
        private static string StripInline(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("**", "").Replace("`", "");
            int b;
            while ((b = s.IndexOf('[')) >= 0)
            {
                int mid = s.IndexOf("](", b, StringComparison.Ordinal);
                if (mid < 0) break;
                int end = s.IndexOf(')', mid + 2);
                if (end < 0) break;
                string text = s.Substring(b + 1, mid - b - 1);
                s = s.Substring(0, b) + text + s.Substring(end + 1);
            }
            return s.Trim();
        }
    }
}
