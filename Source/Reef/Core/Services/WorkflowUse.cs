namespace Reef.Core.Services;

// Counts workflows whose step config names an id
public static class WorkflowUse
{
    public static string Sql(string key) =>
        $$"""SELECT COUNT(*) FROM Workflows WHERE DocumentJson LIKE '%"{{key}}":' || @Id || ',%' OR DocumentJson LIKE '%"{{key}}":' || @Id || '}%'""";
}
