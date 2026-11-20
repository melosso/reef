using System.Globalization;

namespace Reef.Core.Workflows.Engine;

// Numeric compare when both parse
public static class RowValues
{
    public static string Text(object? value) => value switch
    {
        null or DBNull => "",
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    public static bool Matches(object? value, string op, string expected)
    {
        var text = Text(value);
        return op switch
        {
            "equals" => Compare(text, expected) == 0,
            "notEquals" => Compare(text, expected) != 0,
            "contains" => text.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "greaterThan" => Compare(text, expected) > 0,
            "lessThan" => Compare(text, expected) < 0,
            "isEmpty" => text.Length == 0,
            "isNotEmpty" => text.Length > 0,
            _ => false
        };
    }

    private static int Compare(string left, string right) =>
        decimal.TryParse(left, NumberStyles.Number, CultureInfo.InvariantCulture, out var a) && decimal.TryParse(right, NumberStyles.Number, CultureInfo.InvariantCulture, out var b)
            ? a.CompareTo(b)
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
}
