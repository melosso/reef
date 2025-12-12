using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Services;
using Serilog;

namespace Reef.Api;

/// <summary>
/// API endpoints for connection management
/// </summary>
public static class ConnectionsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/connections").RequireAuthorization();

        group.MapGet("/", GetAllConnections);
        group.MapGet("/{id:int}", GetConnectionById);
        group.MapPost("/", CreateConnection);
        group.MapPut("/{id:int}", UpdateConnection);
        group.MapDelete("/{id:int}", DeleteConnection);
        group.MapPost("/test", TestConnection);
    }

    private static async Task<IResult> GetAllConnections([FromServices] ConnectionService service)
    {
        try
        {
            var connections = await service.GetAllAsync();
            return Results.Ok(connections);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting all connections");
            return Results.Problem("Error retrieving connections");
        }
    }

    private static async Task<IResult> GetConnectionById(int id, [FromServices] ConnectionService service)
    {
        try
        {
            var connection = await service.GetByIdAsync(id);
            return connection != null ? Results.Ok(connection) : Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting connection {Id}", id);
            return Results.Problem("Error retrieving connection");
        }
    }

    private static async Task<IResult> CreateConnection(
        [FromBody] Connection connection,
        [FromServices] ConnectionService service,
        [FromServices] AuditService auditService,
        HttpContext context)
    {
        try
        {
            var username = context.User.Identity?.Name ?? "Unknown";
            var id = await service.CreateAsync(connection, username);
            
            await auditService.LogAsync("Connection", id, "Created", username, null, null);
            
            return Results.Created($"/api/connections/{id}", new { id });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating connection");
            return Results.Problem("Error creating connection");
        }
    }

    private static async Task<IResult> UpdateConnection(
        int id,
        [FromBody] Connection connection,
        [FromServices] ConnectionService service,
        [FromServices] AuditService auditService,
        HttpContext context)
    {
        try
        {
            connection.Id = id;
            var success = await service.UpdateAsync(connection);

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("Connection", id, "Updated", username, null, null);

                // Retrieve and return the updated connection
                var updatedConnection = await service.GetByIdAsync(id);
                return Results.Ok(updatedConnection);
            }

            return Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating connection {Id}", id);
            return Results.Problem("Error updating connection");
        }
    }

    private static async Task<IResult> DeleteConnection(
        int id,
        [FromServices] ConnectionService service,
        [FromServices] AuditService auditService,
        HttpContext context)
    {
        try
        {
            var success = await service.DeleteAsync(id);
            
            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("Connection", id, "Deleted", username, (string?)null, null);
                return Results.Ok();
            }
            
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting connection {Id}", id);
            return Results.Problem("Error deleting connection");
        }
    }

    private static async Task<IResult> TestConnection(
        [FromBody] TestConnectionRequest request,
        [FromServices] ConnectionService service)
    {
        try
        {
            var (success, message, responseTimeMs) = await service.TestConnectionAsync(request.Type, request.ConnectionString);

            // If a ConnectionId is provided, update LastTestedAt and LastTestResult
            if (request.ConnectionId.HasValue)
            {
                await service.UpdateTestResultAsync(request.ConnectionId.Value, success, message);
            }

            return Results.Ok(new TestConnectionResponse
            {
                Success = success,
                Message = message,
                ResponseTimeMs = responseTimeMs
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error testing connection");
            // If a ConnectionId is provided, update LastTestedAt and LastTestResult as failed
            if (request.ConnectionId.HasValue)
            {
                await service.UpdateTestResultAsync(request.ConnectionId.Value, false, ex.Message);
            }
            return Results.Ok(new TestConnectionResponse
            {
                Success = false,
                Message = ex.Message,
                ResponseTimeMs = 0
            });
        }
    }
}