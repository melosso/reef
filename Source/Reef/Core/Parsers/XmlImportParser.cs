using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Parsers;

/// <summary>
/// Parses XML data streams into rows using XPath element selection.
/// </summary>
public class XmlImportParser : IImportParser
{
    public async IAsyncEnumerable<ParsedRow> ParseAsync(
        Stream content,
        ImportFormatConfig config,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string xml;
        using (var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        {
            xml = await reader.ReadToEndAsync(ct);
        }

        XmlDocument? doc = null;
        ParsedRow? parseErrorRow = null;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml(xml);
        }
        catch (Exception ex)
        {
            parseErrorRow = new ParsedRow { LineNumber = 0, ParseError = $"XML parse error: {ex.Message}" };
        }

        if (parseErrorRow != null) { yield return parseErrorRow; yield break; }

        // Set up namespace manager if needed
        XmlNamespaceManager? nsMgr = null;
        if (!string.IsNullOrWhiteSpace(config.XmlNamespace))
        {
            nsMgr = new XmlNamespaceManager(doc!.NameTable);
            nsMgr.AddNamespace("ns", config.XmlNamespace);
        }

        // Determine element XPath (default: all children of root)
        var xpath = string.IsNullOrWhiteSpace(config.RecordElement)
            ? $"/{doc!.DocumentElement!.Name}/*"
            : config.RecordElement;

        XmlNodeList? nodes = null;
        ParsedRow? xpathErrorRow = null;
        try
        {
            nodes = nsMgr != null
                ? doc!.SelectNodes(xpath, nsMgr)
                : doc!.SelectNodes(xpath);
        }
        catch (Exception ex)
        {
            xpathErrorRow = new ParsedRow { LineNumber = 0, ParseError = $"XPath error '{xpath}': {ex.Message}" };
        }

        if (xpathErrorRow != null) { yield return xpathErrorRow; yield break; }

        if (nodes == null || nodes.Count == 0)
        {
            Log.Warning("XML parser: no nodes matched XPath '{XPath}'", xpath);
            yield break;
        }

        int lineNumber = 0;
        foreach (XmlNode node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;

            var row = new ParsedRow { LineNumber = lineNumber };

            // Attributes
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attr in node.Attributes)
                {
                    row.Columns[$"@{attr.LocalName}"] = attr.Value;
                }
            }

            // Child elements (leaf text nodes)
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element)
                {
                    var key = child.LocalName;
                    // If child has sub-children, serialize as raw XML string
                    var value = child.HasChildNodes && child.FirstChild!.NodeType == XmlNodeType.Element
                        ? child.OuterXml
                        : (object?)child.InnerText;
                    row.Columns[key] = value;
                }
            }

            // If node is a simple text/value node
            if (!row.Columns.Any() && node.NodeType == XmlNodeType.Element)
            {
                row.Columns["value"] = node.InnerText;
            }

            yield return row;
        }
    }
}
