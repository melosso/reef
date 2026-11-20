using Microsoft.Data.Sqlite;
using Reef.Core.Exceptions;

namespace Reef.Api;

// Name collisions from services or sqlite unique indexes are answered with 409.
public static class UniqueName
{
    public static bool Violated(Exception ex) =>
        ex is DuplicateNameException
        || ex is SqliteException { SqliteErrorCode: 19 } sqlite && sqlite.Message.Contains("UNIQUE constraint failed") && sqlite.Message.Contains(".Name");

    public static IResult Conflict(Exception ex) =>
        Results.Conflict(new { message = ex is DuplicateNameException ? ex.Message : "This name already exists. Choose another name.", field = "Name" });
}
