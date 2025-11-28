using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Services;

namespace Reef.Api;

public static class DestinationsEndpoints
{
    public static void MapDestinationsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/destinations")
            .RequireAuthorization()
            .WithTags("Destinations");

        // POST: Test destination configuration without creating
        group.MapPost("/test", async (
            [FromBody] DestinationTestConfigRequest request,
            [FromServices] DestinationService service) =>
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.ConfigurationJson))
                {
                    return Results.BadRequest(new { message = "Configuration is required" });
                }

                // Test the destination configuration
                var result = await service.TestDestinationConfigurationAsync(
                    request.Type,
                    request.ConfigurationJson,
                    request.TestFileName,
                    request.TestContent);
                
                return result.Success 
                    ? Results.Ok(result) 
                    : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new DestinationTestResult
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        })
        .WithName("TestDestinationConfiguration")
        .Produces<DestinationTestResult>(200)
        .Produces<DestinationTestResult>(400);

        // GET: List all destinations
        group.MapGet("/", async (
            [FromServices] DestinationService service,
            [FromQuery] bool activeOnly = false) =>
        {
            var destinations = await service.GetAllAsync(activeOnly);
            return Results.Ok(destinations);
        })
        .WithName("GetAllDestinations")
        .Produces<IEnumerable<Destination>>(200);

        // GET: Get destination by ID
        group.MapGet("/{id:int}", async (
            int id,
            [FromServices] DestinationService service) =>
        {
            var destination = await service.GetByIdAsync(id);
            return destination != null 
                ? Results.Ok(destination) 
                : Results.NotFound(new { message = "Destination not found" });
        })
        .WithName("GetDestinationById")
        .Produces<Destination>(200)
        .Produces(404);

        // POST: Create new destination
        group.MapPost("/", async (
            [FromBody] Destination destination,
            [FromServices] DestinationService service) =>
        {
            try
            {
                var created = await service.CreateAsync(destination);

                // Reload from DB to get masked version (never return plaintext to client)
                var masked = await service.GetByIdAsync(created.Id);

                return Results.Created($"/api/destinations/{masked?.Id}", masked);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("CreateDestination")
        .Produces<Destination>(201)
        .Produces(400);

        // PUT: Update destination
        group.MapPut("/{id:int}", async (
            int id,
            [FromBody] Destination destination,
            [FromServices] DestinationService service) =>
        {
            if (id != destination.Id)
            {
                return Results.BadRequest(new { message = "ID mismatch" });
            }

            try
            {
                var success = await service.UpdateAsync(destination);
                if (!success)
                {
                    return Results.NotFound(new { message = "Destination not found" });
                }

                // Reload from DB to get masked version (never return plaintext to client)
                var masked = await service.GetByIdAsync(id);
                return Results.Ok(masked);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("UpdateDestination")
        .Produces<Destination>(200)
        .Produces(400)
        .Produces(404);

        // DELETE: Delete destination
        group.MapDelete("/{id:int}", async (
            int id,
            [FromServices] DestinationService service) =>
        {
            try
            {
                var success = await service.DeleteAsync(id);
                return success 
                    ? Results.NoContent() 
                    : Results.NotFound(new { message = "Destination not found" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("DeleteDestination")
        .Produces(204)
        .Produces(404)
        .Produces(409);

        // POST: Test destination connection
        group.MapPost("/{id:int}/test", async (
            int id,
            [FromBody] DestinationTestRequest? request,
            [FromServices] DestinationService service) =>
        {
            try
            {
                var result = await service.TestConnectionAsync(
                    request?.DestinationId ?? id, 
                    request?.TestFileName, 
                    request?.TestContent);
                
                return result.Success 
                    ? Results.Ok(result) 
                    : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new DestinationTestResult
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        })
        .WithName("TestDestination")
        .Produces<DestinationTestResult>(200)
        .Produces<DestinationTestResult>(400);

        // GET: Get destinations by tag
        group.MapGet("/tags/{tag}", async (
            string tag,
            [FromServices] DestinationService service) =>
        {
            var destinations = await service.GetByTagAsync(tag);
            return Results.Ok(destinations);
        })
        .WithName("GetDestinationsByTag")
        .Produces<IEnumerable<Destination>>(200);

        // GET: Get destination types
        group.MapGet("/types", () =>
        {
            var types = Enum.GetValues<DestinationType>()
                .Select(t => new 
                { 
                    value = (int)t, 
                    name = t.ToString(),
                    description = GetDestinationTypeDescription(t)
                });
            return Results.Ok(types);
        })
        .WithName("GetDestinationTypes")
        .Produces(200);
    }

    private static string GetDestinationTypeDescription(DestinationType type)
    {
        return type switch
        {
            DestinationType.Local => "Local file system",
            DestinationType.Ftp => "FTP/FTPS server",
            DestinationType.Sftp => "SFTP (SSH File Transfer Protocol)",
            DestinationType.S3 => "Amazon S3",
            DestinationType.AzureBlob => "Azure Blob Storage",
            DestinationType.Http => "HTTP/HTTPS REST API (POST)",
            DestinationType.WebDav => "WebDAV",
            DestinationType.Email => "Email attachment",
            DestinationType.NetworkShare => "Network share (SMB/CIFS)",
            _ => type.ToString()
        };
    }
}