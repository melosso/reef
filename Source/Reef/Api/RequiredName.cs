namespace Reef.Api;

// Blank names are answered with 400 and a field error
public static class RequiredName
{
    public static IResult? Check(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? Results.BadRequest(new { message = "Enter a name", errors = new[] { new { field = "Name", message = "Enter a name" } } })
            : null;
}
