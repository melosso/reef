using HtmlAgilityPack;
using System.Text;

namespace Reef.Core.DocumentGeneration.HtmlParsing;

/// <summary>
/// Parses HTML text with formatting into structured segments
/// </summary>
public class HtmlTextParser
{
    public static List<TextSegment> ParseFormattedText(string html)
    {
        var segments = new List<TextSegment>();
        
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        
        ProcessNode(doc.DocumentNode, segments, new TextStyle());
        
        return segments;
    }
    
    private static void ProcessNode(HtmlNode node, List<TextSegment> segments, TextStyle currentStyle)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = HtmlEntity.DeEntitize(node.InnerText);
            if (!string.IsNullOrWhiteSpace(text))
            {
                segments.Add(new TextSegment
                {
                    Content = text,
                    Style = currentStyle.Clone()
                });
            }
            return;
        }
        
        // Update style based on element
        var newStyle = currentStyle.Clone();
        
        switch (node.Name.ToLower())
        {
            case "strong":
            case "b":
                newStyle.IsBold = true;
                break;
            case "em":
            case "i":
                newStyle.IsItalic = true;
                break;
            case "u":
                newStyle.IsUnderline = true;
                break;
            case "h1":
                newStyle.IsHeading = true;
                newStyle.HeadingLevel = 1;
                newStyle.FontSize = 24;
                newStyle.IsBold = true;
                break;
            case "h2":
                newStyle.IsHeading = true;
                newStyle.HeadingLevel = 2;
                newStyle.FontSize = 20;
                newStyle.IsBold = true;
                break;
            case "h3":
                newStyle.IsHeading = true;
                newStyle.HeadingLevel = 3;
                newStyle.FontSize = 16;
                newStyle.IsBold = true;
                break;
            case "h4":
            case "h5":
            case "h6":
                newStyle.IsHeading = true;
                newStyle.HeadingLevel = int.Parse(node.Name.Substring(1));
                newStyle.FontSize = 14;
                newStyle.IsBold = true;
                break;
            case "br":
                segments.Add(new TextSegment { Content = "\n", Style = currentStyle.Clone() });
                return;
            case "hr":
                segments.Add(new TextSegment { Content = "―――――――――――――――――――――――――――――――――\n", Style = currentStyle.Clone() });
                return;
            case "p":
                // Add paragraph break before
                if (segments.Any() && !segments.Last().Content.EndsWith("\n"))
                {
                    segments.Add(new TextSegment { Content = "\n", Style = currentStyle.Clone() });
                }
                break;
        }
        
        // Process child nodes
        foreach (var child in node.ChildNodes)
        {
            ProcessNode(child, segments, newStyle);
        }
        
        // Add paragraph break after
        if (node.Name.ToLower() == "p" && segments.Any() && !segments.Last().Content.EndsWith("\n"))
        {
            segments.Add(new TextSegment { Content = "\n", Style = currentStyle.Clone() });
        }
    }
}

/// <summary>
/// Text segment with formatting information
/// </summary>
public class TextSegment
{
    public string Content { get; set; } = string.Empty;
    public TextStyle Style { get; set; } = new();
}

/// <summary>
/// Text formatting style
/// </summary>
public class TextStyle
{
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public bool IsUnderline { get; set; }
    public bool IsHeading { get; set; }
    public int HeadingLevel { get; set; }
    public int FontSize { get; set; } = 10;
    
    public TextStyle Clone()
    {
        return new TextStyle
        {
            IsBold = this.IsBold,
            IsItalic = this.IsItalic,
            IsUnderline = this.IsUnderline,
            IsHeading = this.IsHeading,
            HeadingLevel = this.HeadingLevel,
            FontSize = this.FontSize
        };
    }
}
