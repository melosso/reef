using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Reef.Core.Models;
using Reef.Core.Services;

namespace Reef.Api;

public static class QueryTemplatesEndpoints
{
    public static void MapQueryTemplatesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/templates")
            .RequireAuthorization()
            .WithTags("Query Templates");

        // GET: List all templates
        group.MapGet("/", async (
            [FromServices] QueryTemplateService service,
            [FromQuery] bool activeOnly = false) =>
        {
            var templates = await service.GetAllAsync(activeOnly);
            return Results.Ok(templates);
        })
        .WithName("GetAllQueryTemplates")
        .Produces<IEnumerable<QueryTemplate>>(200);

        // GET: Get template by ID
        group.MapGet("/{id:int}", async (
            int id,
            [FromServices] QueryTemplateService service) =>
        {
            var template = await service.GetByIdAsync(id);
            return template != null 
                ? Results.Ok(template) 
                : Results.NotFound(new { message = "Template not found" });
        })
        .WithName("GetQueryTemplateById")
        .Produces<QueryTemplate>(200)
        .Produces(404);

        // POST: Create new template
        group.MapPost("/", async (
            [FromBody] QueryTemplate template,
            [FromServices] QueryTemplateService service) =>
        {
            try
            {
                var created = await service.CreateAsync(template);
                return Results.Created($"/api/templates/{created.Id}", created);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("CreateQueryTemplate")
        .Produces<QueryTemplate>(201)
        .Produces(400);

        // PUT: Update template
        group.MapPut("/{id:int}", async (
            int id,
            [FromBody] QueryTemplate template,
            [FromServices] QueryTemplateService service) =>
        {
            if (id != template.Id)
            {
                return Results.BadRequest(new { message = "ID mismatch" });
            }

            try
            {
                var success = await service.UpdateAsync(template);
                return success 
                    ? Results.Ok(template) 
                    : Results.NotFound(new { message = "Template not found" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        })
        .WithName("UpdateQueryTemplate")
        .Produces<QueryTemplate>(200)
        .Produces(400)
        .Produces(404);

        // DELETE: Delete template
        group.MapDelete("/{id:int}", async (
            int id,
            [FromServices] QueryTemplateService service) =>
        {
            try
            {
                var success = await service.DeleteAsync(id);
                return success 
                    ? Results.NoContent() 
                    : Results.NotFound(new { message = "Template not found" });
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
        .WithName("DeleteQueryTemplate")
        .Produces(204)
        .Produces(404)
        .Produces(409);

        // POST: Test transformation
        group.MapPost("/test", async (
            [FromBody] TestTransformationRequest request,
            [FromServices] QueryTemplateService service,
            [FromServices] ConnectionService connectionService) =>
        {
            try
            {
                // Get connection
                var connection = await connectionService.GetByIdAsync(request.ConnectionId);
                if (connection == null)
                {
                    return Results.NotFound(new { message = "Connection not found" });
                }

                // Create database connection using ConnectionService (handles ApplicationName properly)
                await using var dbConnection = await connectionService.CreateDatabaseConnectionAsync(request.ConnectionId);
                await dbConnection.OpenAsync();

                // Apply transformation
                var result = await service.ApplyTransformationAsync(dbConnection, new TransformationRequest
                {
                    Query = request.Query,
                    TemplateId = request.TemplateId,
                    InlineTemplate = request.InlineTemplate,
                    Type = request.Type,
                    Options = request.Options
                });

                return result.Success 
                    ? Results.Ok(result) 
                    : Results.BadRequest(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new TransformationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        })
        .WithName("TestTransformation")
        .Produces<TransformationResult>(200)
        .Produces<TransformationResult>(400);

        // GET: Get templates by type
        group.MapGet("/type/{type}", async (
            QueryTemplateType type,
            [FromServices] QueryTemplateService service) =>
        {
            var templates = await service.GetByTypeAsync(type);
            return Results.Ok(templates);
        })
        .WithName("GetQueryTemplatesByType")
        .Produces<IEnumerable<QueryTemplate>>(200);

        // GET: Get template types
        group.MapGet("/types", () =>
        {
            var types = Enum.GetValues<QueryTemplateType>()
                .Select(t => new 
                { 
                    value = (int)t, 
                    name = t.ToString(),
                    description = GetTemplateTypeDescription(t),
                    category = GetTemplateTypeCategory(t)
                });
            return Results.Ok(types);
        })
        .WithName("GetQueryTemplateTypes")
        .Produces(200);

        // POST: Generate FOR XML query
        group.MapPost("/generate/forxml", (
            [FromBody] ForXmlGeneratorRequest request) =>
        {
            var query = GenerateForXmlQuery(request);
            return Results.Ok(new { query });
        })
        .WithName("GenerateForXmlQuery")
        .Produces(200);

        // POST: Generate FOR JSON query
        group.MapPost("/generate/forjson", (
            [FromBody] ForJsonGeneratorRequest request) =>
        {
            var query = GenerateForJsonQuery(request);
            return Results.Ok(new { query });
        })
        .WithName("GenerateForJsonQuery")
        .Produces(200);

        // POST: Validate Scriban template
        group.MapPost("/validate", (
            [FromBody] ValidateTemplateRequest request,
            [FromServices] IConfiguration configuration) =>
        {
            try
            {
                // Only validate Scriban templates
                if (request.Type != QueryTemplateType.XmlTemplate && 
                    request.Type != QueryTemplateType.JsonTemplate &&
                    request.Type != QueryTemplateType.ScribanTemplate)
                {
                    return Results.Ok(new ValidateTemplateResponse
                    {
                        IsValid = true,
                        Message = "Validation not required for this template type"
                    });
                }

                var engine = new Reef.Core.TemplateEngines.ScribanTemplateEngine(configuration);
                var (isValid, errorMessage) = engine.ValidateTemplate(request.Template);

                return Results.Ok(new ValidateTemplateResponse
                {
                    IsValid = isValid,
                    ErrorMessage = errorMessage
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new ValidateTemplateResponse
                {
                    IsValid = false,
                    ErrorMessage = ex.Message
                });
            }
        })
        .WithName("ValidateTemplate")
        .Produces<ValidateTemplateResponse>(200);
    }

    private static string GetTemplateTypeDescription(QueryTemplateType type)
    {
        return type switch
        {
            QueryTemplateType.XmlTemplate => "Custom XML template with placeholders (legacy)",
            QueryTemplateType.JsonTemplate => "Custom JSON template with placeholders (legacy)",
            QueryTemplateType.ScribanTemplate => "Scriban template - supports CSV, XML, JSON, EDI, and custom formats",
            QueryTemplateType.XsltTransform => "XSLT transformation",
            QueryTemplateType.ForXmlRaw => "SQL Server FOR XML RAW",
            QueryTemplateType.ForXmlAuto => "SQL Server FOR XML AUTO",
            QueryTemplateType.ForXmlPath => "SQL Server FOR XML PATH",
            QueryTemplateType.ForXmlExplicit => "SQL Server FOR XML EXPLICIT",
            QueryTemplateType.ForJson => "SQL Server FOR JSON AUTO",
            QueryTemplateType.ForJsonPath => "SQL Server FOR JSON PATH",
            _ => type.ToString()
        };
    }

    private static string GetTemplateTypeCategory(QueryTemplateType type)
    {
        return type switch
        {
            QueryTemplateType.ForXmlRaw => "SQL Server Native",
            QueryTemplateType.ForXmlAuto => "SQL Server Native",
            QueryTemplateType.ForXmlPath => "SQL Server Native",
            QueryTemplateType.ForXmlExplicit => "SQL Server Native",
            QueryTemplateType.ForJson => "SQL Server Native",
            QueryTemplateType.ForJsonPath => "SQL Server Native",
            QueryTemplateType.XmlTemplate => "Legacy Template",
            QueryTemplateType.JsonTemplate => "Legacy Template",
            QueryTemplateType.ScribanTemplate => "Custom Template",
            QueryTemplateType.XsltTransform => "Transformation",
            _ => "Other"
        };
    }

    private static string GenerateForXmlQuery(ForXmlGeneratorRequest request)
    {
        var parts = new List<string>();
        
        switch (request.Type)
        {
            case QueryTemplateType.ForXmlRaw:
                parts.Add("FOR XML RAW");
                if (!string.IsNullOrEmpty(request.RowElement))
                    parts.Add($"('{request.RowElement}')");
                break;
                
            case QueryTemplateType.ForXmlAuto:
                parts.Add("FOR XML AUTO");
                break;
                
            case QueryTemplateType.ForXmlPath:
                parts.Add("FOR XML PATH");
                if (!string.IsNullOrEmpty(request.RowElement))
                    parts.Add($"('{request.RowElement}')");
                break;
        }

        if (request.ElementsMode)
            parts.Add("ELEMENTS");
            
        if (!string.IsNullOrEmpty(request.RootElement))
            parts.Add($"ROOT('{request.RootElement}')");

        var forXmlClause = string.Join(", ", parts);
        return $"{request.BaseQuery.TrimEnd().TrimEnd(';')} {forXmlClause}";
    }

    private static string GenerateForJsonQuery(ForJsonGeneratorRequest request)
    {
        var parts = new List<string>();
        
        if (request.Type == QueryTemplateType.ForJsonPath)
        {
            parts.Add("FOR JSON PATH");
            if (!string.IsNullOrEmpty(request.RootElement))
                parts.Add($"ROOT('{request.RootElement}')");
        }
        else
        {
            parts.Add("FOR JSON AUTO");
        }

        if (request.IncludeNullValues)
            parts.Add("INCLUDE_NULL_VALUES");
            
        if (request.WithoutArrayWrapper)
            parts.Add("WITHOUT_ARRAY_WRAPPER");

        var forJsonClause = string.Join(", ", parts);
        return $"{request.BaseQuery.TrimEnd().TrimEnd(';')} {forJsonClause}";
    }
}

public class TestTransformationRequest
{
    public int ConnectionId { get; set; }
    public string Query { get; set; } = string.Empty;
    public int? TemplateId { get; set; }
    public string? InlineTemplate { get; set; }
    public QueryTemplateType Type { get; set; }
    public Dictionary<string, object>? Options { get; set; }
}

public class ForXmlGeneratorRequest
{
    public string BaseQuery { get; set; } = string.Empty;
    public QueryTemplateType Type { get; set; } = QueryTemplateType.ForXmlRaw;
    public string? RootElement { get; set; }
    public string? RowElement { get; set; }
    public bool ElementsMode { get; set; }
}

public class ForJsonGeneratorRequest
{
    public string BaseQuery { get; set; } = string.Empty;
    public QueryTemplateType Type { get; set; } = QueryTemplateType.ForJson;
    public string? RootElement { get; set; }
    public bool IncludeNullValues { get; set; }
    public bool WithoutArrayWrapper { get; set; }
}

public class ValidateTemplateRequest
{
    public string Template { get; set; } = string.Empty;
    public QueryTemplateType Type { get; set; }
}

public class ValidateTemplateResponse
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }
}