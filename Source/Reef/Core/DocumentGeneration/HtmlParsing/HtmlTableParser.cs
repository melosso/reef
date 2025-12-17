using HtmlAgilityPack;

namespace Reef.Core.DocumentGeneration.HtmlParsing;

/// <summary>
/// Parses HTML table elements into structured data for rendering
/// </summary>
public class HtmlTableParser
{
    public static List<TableData> ParseTables(string html)
    {
        var tables = new List<TableData>();
        
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        
        var tableNodes = doc.DocumentNode.SelectNodes("//table");
        if (tableNodes == null) return tables;
        
        foreach (var tableNode in tableNodes)
        {
            var tableData = new TableData();
            
            // Parse header row (thead > tr > th or first tr with th)
            var headerRow = tableNode.SelectSingleNode(".//thead/tr") ?? 
                           tableNode.SelectSingleNode(".//tr[th]");
            
            if (headerRow != null)
            {
                var headerCells = headerRow.SelectNodes(".//th");
                if (headerCells != null)
                {
                    tableData.Headers = headerCells.Select(cell => cell.InnerText.Trim()).ToList();
                    tableData.ColumnCount = tableData.Headers.Count;
                }
            }
            
            // Parse data rows (tbody > tr or all tr elements without th)
            var bodyRows = tableNode.SelectNodes(".//tbody/tr") ?? 
                          tableNode.SelectNodes(".//tr[td]");
            
            if (bodyRows != null)
            {
                foreach (var row in bodyRows)
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells != null)
                    {
                        var rowData = cells.Select(cell => cell.InnerText.Trim()).ToList();
                        tableData.Rows.Add(rowData);
                        
                        // Update column count if no headers
                        if (tableData.ColumnCount == 0)
                            tableData.ColumnCount = Math.Max(tableData.ColumnCount, rowData.Count);
                    }
                }
            }
            
            // Only add if table has data
            if (tableData.Headers.Any() || tableData.Rows.Any())
            {
                tables.Add(tableData);
            }
        }
        
        return tables;
    }
}

/// <summary>
/// Structured table data extracted from HTML
/// </summary>
public class TableData
{
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
    public int ColumnCount { get; set; } = 0;
}
