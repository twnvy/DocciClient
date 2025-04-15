using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions; // Required for Regex
using HtmlAgilityPack;          // Required for HTML parsing (Install NuGet package)

namespace docci
{
    public partial class Form1: Form
    {
        public async Task<string> GetAsync(string uri)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.UserAgent = "Docci/1.0 (compatible; DocciClient; Lynx/2.8.8dev.3; +about:blank)"; // Customize this!
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = 15000; // Example: Set a timeout (in milliseconds)

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    return await reader.ReadToEndAsync();
                }
            }
            catch (WebException ex)
            {
                // Display errors in a message box for the user
                string errorMsg = $"Error fetching {uri}:\n{ex.Message}";
                if (ex.Response is HttpWebResponse errorResponse)
                {
                    errorMsg += $"\nStatus Code: {errorResponse.StatusCode} ({errorResponse.StatusDescription})";
                }
                MessageBox.Show(errorMsg, "Network Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null; // Indicate failure
            }
            catch (Exception ex) // Catch other potential exceptions
            {
                MessageBox.Show($"An unexpected error occurred:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }
        public Form1()
        {
            InitializeComponent();
        }
        async private void NavigateToUri(string InputUri)
        {
            string outputUriString = InputUri;
            if (!Uri.IsWellFormedUriString(outputUriString, UriKind.Absolute))
            {
                if (Uri.IsWellFormedUriString("http://" + outputUriString, UriKind.Absolute))
                {
                    outputUriString = "http://" + outputUriString;
                }
                else
                {
                    outputUriString = "https://html.duckduckgo.com/html/search?q=" + outputUriString;
                }
            }
            textBox1.Text = outputUriString;
            string html = await GetAsync(outputUriString);
            string output = HtmlToTextConverter.ConvertHtmlToPlainText(html);
            richTextBox1.Text = output;
        }
        private void button1_Click(object sender, EventArgs e)
        {
            NavigateToUri(textBox1.Text);
        }
        private void richTextBox1_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            NavigateToUri(e.LinkText);
        }
    }
}
public static class HtmlToTextConverter
{
    /// <summary>
    /// Converts an HTML string to plain text, formatting links as [text - url]
    /// and attempting to add line breaks for block elements.
    /// Requires the HtmlAgilityPack NuGet package.
    /// </summary>
    /// <param name="html">The HTML string to parse.</param>
    /// <returns>Plain text representation with formatted links and block formatting.</returns>
    public static string ConvertHtmlToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        var sb = new StringBuilder();

        // Define block-level elements that should introduce line breaks AFTER their content
        var blockTags = new HashSet<string> {
            "p", "h1", "h2", "h3", "h4", "h5", "h6", "div",
            "li", /* "ul", "ol", */ // Let li handle newlines for lists often looks better
            "table", "tr", "blockquote", "address",
            "article", "aside", "details", "dialog", "dd", "dl", "dt",
            "fieldset", "figcaption", "figure", "footer", "form",
            "header", "hr", "main", "nav", "pre", "section"
        };

        // Special handling for BR tag
        var brTag = "br";
        var aTag = "a"; // Define the anchor tag name

        // Define tags whose content should be completely ignored
        var excludedTags = new HashSet<string> {
            "script", "style", "svg", "path", "noscript", "head", "meta", "link"
        };


        // Use a recursive helper function to traverse the nodes
        ConvertNodeToTextRecursive(doc.DocumentNode, sb, blockTags, brTag, aTag, excludedTags);

        // Final cleanup: Normalize line breaks and remove excessive ones
        string result = sb.ToString();
        // Normalize line breaks first (\r\n or \r -> \n)
        result = result.Replace("\r\n", "\n").Replace("\r", "\n");
        // Collapse 3 or more newlines into 2 (effectively one blank line)
        result = Regex.Replace(result, @"\n{3,}", "\n\n");
        // Collapse 2 or more spaces into 1, but preserve newlines
        result = Regex.Replace(result, @"[ \t]{2,}", " ");


        return result.Trim(); // Trim leading/trailing whitespace/newlines
    }

    /// <summary>
    /// Recursive helper to process HTML nodes.
    /// </summary>
    private static void ConvertNodeToTextRecursive(
        HtmlNode node,
        StringBuilder sb,
        HashSet<string> blockTags,
        string brTag,
        string aTag, // Pass 'a' tag name
        HashSet<string> excludedTags) // Pass excluded tags
    {
        if (node == null) return;

        // Skip excluded tags entirely
        if (node.NodeType == HtmlNodeType.Element && excludedTags.Contains(node.Name.ToLowerInvariant()))
        {
            return; // Don't process this node or its children
        }

        // 1. Handle Text Nodes
        if (node.NodeType == HtmlNodeType.Text)
        {
            // Decode HTML entities like &amp;
            string text = HtmlEntity.DeEntitize(node.InnerText);
            // Replace multiple whitespace chars (including newlines within the text node) with a single space
            string cleanedText = Regex.Replace(text, @"\s+", " ").Trim();

            if (!string.IsNullOrEmpty(cleanedText))
            {
                // Add a space before adding text if the last char wasn't whitespace or a block boundary
                if (sb.Length > 0 &&
                    !char.IsWhiteSpace(sb[sb.Length - 1]) &&
                    !IsPrecededByBlockBoundary(sb) &&
                    !IsStartingPunctuation(cleanedText)) // Don't add space before punctuation
                {
                    sb.Append(" ");
                }
                sb.Append(cleanedText);
            }
        }
        // 2. Handle Element Nodes
        else if (node.NodeType == HtmlNodeType.Element)
        {
            string tagName = node.Name.ToLowerInvariant();

            if (tagName == brTag)
            {
                TrimEndWhitespace(sb);
                sb.AppendLine(); // Append Environment.NewLine
            }
            // **** START Specific A Tag Handling ****
            else if (tagName == aTag)
            {
                string linkUrl = node.Attributes["href"]?.Value;
                linkUrl = !string.IsNullOrWhiteSpace(linkUrl) ? HtmlEntity.DeEntitize(linkUrl.Trim()) : null; // Decode and trim URL

                // Get the text content of the link by processing its children
                var linkTextBuilder = new StringBuilder();
                foreach (var child in node.ChildNodes)
                {
                    // Recursively call, but output to the temporary builder
                    ConvertNodeToTextRecursive(child, linkTextBuilder, blockTags, brTag, aTag, excludedTags);
                }
                string linkText = linkTextBuilder.ToString().Trim(); // Trim the extracted text


                if (!string.IsNullOrEmpty(linkText) && !string.IsNullOrEmpty(linkUrl))
                {
                    // Add a space before the link if needed
                    if (sb.Length > 0 &&
                        !char.IsWhiteSpace(sb[sb.Length - 1]) &&
                        !IsPrecededByBlockBoundary(sb))
                    {
                        sb.Append(" ");
                    }
                    // Append in the desired format
                    sb.Append($"{linkText} => {linkUrl}");
                    sb.AppendLine();
                }
                else if (!string.IsNullOrEmpty(linkText)) // Fallback: If no URL, just append the text
                {
                    // Add a space before the text if needed (same logic as text nodes)
                    if (sb.Length > 0 &&
                       !char.IsWhiteSpace(sb[sb.Length - 1]) &&
                       !IsPrecededByBlockBoundary(sb) &&
                       !IsStartingPunctuation(linkText))
                    {
                        sb.Append(" ");
                    }
                    sb.Append(linkText);
                }
                // If linkText is empty but URL exists, we currently output nothing extra.
                // If both are empty, nothing happens.

                // We've handled the 'a' tag and its children, so we don't recurse further here.
            }
            // **** END Specific A Tag Handling ****
            else // Handle other non-excluded elements (including block tags)
            {
                // Recurse into child nodes BEFORE adding block separators
                foreach (var child in node.ChildNodes)
                {
                    ConvertNodeToTextRecursive(child, sb, blockTags, brTag, aTag, excludedTags);
                }

                // AFTER processing children, add newlines if it's a block tag
                if (blockTags.Contains(tagName))
                {
                    TrimEndWhitespace(sb);
                    // Ensure we add a newline only if one isn't already there
                    // Check for single or double newline ending
                    if (sb.Length > 0 && !EndsWithNewline(sb))
                    {
                        sb.AppendLine(); // Add first newline
                    }
                    // Add second newline for block separation, unless already preceded by boundary
                    if (sb.Length > 0 && !IsPrecededByBlockBoundary(sb))
                    {
                        sb.AppendLine(); // Add second newline
                    }
                }
            }
        }
        // 3. Handle Other Nodes (like Comments, etc.) - Recurse if they have children
        //    (excluding those already filtered out like <script>)
        else if (node.HasChildNodes)
        {
            foreach (var child in node.ChildNodes)
            {
                ConvertNodeToTextRecursive(child, sb, blockTags, brTag, aTag, excludedTags);
            }
        }
    }

    /// <summary>
    /// Removes trailing whitespace characters (space, tab, newline, cr) from a StringBuilder.
    /// </summary>
    private static void TrimEndWhitespace(StringBuilder sb)
    {
        while (sb.Length > 0 && char.IsWhiteSpace(sb[sb.Length - 1]))
        {
            sb.Length--;
        }
    }

    /// <summary>
    /// Checks if the StringBuilder ends with a newline character sequence.
    /// </summary>
    private static bool EndsWithNewline(StringBuilder sb)
    {
        int len = sb.Length;
        string nl = Environment.NewLine; // Platform specific newline (\r\n or \n)
        if (len < nl.Length) return false;

        for (int i = 0; i < nl.Length; ++i)
        {
            if (sb[len - 1 - i] != nl[nl.Length - 1 - i])
                return false;
        }
        return true;
    }


    /// <summary>
    /// Checks if the StringBuilder ends with characters indicating a block boundary (effectively double newline).
    /// Handles both \n\n and \r\n\r\n.
    /// </summary>
    private static bool IsPrecededByBlockBoundary(StringBuilder sb)
    {
        int len = sb.Length;
        string nl = Environment.NewLine; // Platform specific newline (\r\n or \n)
        int nlLen = nl.Length;

        if (len < nlLen * 2) return false; // Too short for double newline

        // Check for the last newline
        for (int i = 0; i < nlLen; ++i)
        {
            if (sb[len - 1 - i] != nl[nlLen - 1 - i]) return false;
        }
        // Check for the second-to-last newline
        for (int i = 0; i < nlLen; ++i)
        {
            if (sb[len - 1 - i - nlLen] != nl[nlLen - 1 - i]) return false;
        }

        return true; // Ends in double newline
    }


    /// <summary>
    /// Basic check if a string starts with common punctuation that shouldn't follow a space.
    /// </summary>
    private static bool IsStartingPunctuation(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        char firstChar = s[0];
        // Punctuation like .,;:!?)]} should not have a preceding space.
        // Punctuation like "'([{ might depending on context, but generally okay to not add space.
        return char.IsPunctuation(firstChar) && !("\"'([{".Contains(firstChar));
    }
}