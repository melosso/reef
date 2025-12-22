namespace Reef.Core.Models;

public class FilterState
{
    public string SearchTerm { get; set; } = "";
    public string Status { get; set; } = "All";
    public string Type { get; set; } = "";
}

public class FilterOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}
