using System.Data;
using System.Text.Json;
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

        // POST: Preview template with mock data
        group.MapPost("/preview", async (
            [FromBody] PreviewTemplateRequest request,
            [FromServices] IConfiguration configuration) =>
        {
            try
            {
                var engine = new Reef.Core.TemplateEngines.ScribanTemplateEngine(configuration);

                // Generate mock data
                var mockData = GenerateMockData(request.Type);

                // Transform using the template engine
                var preview = await engine.TransformAsync(mockData, request.Template);

                return Results.Ok(new PreviewTemplateResponse
                {
                    Preview = preview,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new PreviewTemplateResponse
                {
                    Preview = $"Error generating preview: {ex.Message}",
                    Success = false,
                    ErrorMessage = ex.Message
                });
            }
        })
        .WithName("PreviewTemplate")
        .Produces<PreviewTemplateResponse>(200);
    }

    /// <summary>
    /// Generates mock data appropriate for the given template type
    /// </summary>
    private static List<Dictionary<string, object>> GenerateMockData(QueryTemplateType type)
    {
        // Create 1 mock row for preview purposes (sufficient to show template structure)
        var mockRows = new List<Dictionary<string, object>>();

        for (int i = 1; i <= 1; i++)
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                // Common fields
                { "id", 12345 + i },
                { "name", $"Sample Name {i}" },
                { "first_name", "John" },
                { "last_name", "Doe" },
                { "company_name", "Acme Corporation" },
                { "email", $"sample{i}@example.com" },
                { "phone", "+1 (555) 123-4567" },
                { "address", "123 Main Street" },
                { "street", "123 Main Street" },
                { "city", "New York" },
                { "state", "NY" },
                { "province", "ON" },
                { "country", "USA" },
                { "country_code", "US" },
                { "postal_code", "10001" },
                { "zip", "10001" },
                { "postal", "10001" },

                // Catalog/Item fields
                { "ItemCode", $"ITEM-{12345 + i}" },
                { "SKU", $"SKU-{98765 + i}" },
                { "EAN", $"978{10000000 + i}" },
                { "UPC", $"123456789{i}" },
                { "ProductCode", $"PROD-{55555 + i}" },
                { "ManufacturerCode", $"MFG-{77777 + i}" },

                { "Description", "Sample Product Description" },
                { "ProductName", "Premium Sample Product" },
                { "Title", "Premium Sample Product" },
                { "ShortDescription", "High-quality sample product" },
                { "LongDescription", "This is a detailed product description for the premium sample product with all specifications and features" },
                { "Category", "Electronics" },
                { "Subcategory", "Accessories" },
                { "Brand", "Acme Brand" },
                { "Manufacturer", "Acme Manufacturing" },

                { "SalesPackagePrice", 99.99 },
                { "UnitPrice", 99.99 },
                { "ListPrice", 129.99 },
                { "CostPrice", 50.00 },
                { "RetailPrice", 129.99 },
                { "DiscountPrice", 89.99 },
                { "Discount", 10.00 },
                { "DiscountPercent", 7.7 },

                { "Stock", 150 },
                { "Quantity", 150 },
                { "MinStock", 10 },
                { "MaxStock", 500 },
                { "ReorderLevel", 25 },
                { "WarehouseStock", 75 },
                { "ShelfStock", 40 },

                { "Unit", "EA" },
                { "UnitCode", "EA" },
                { "UnitOfMeasure", "Each" },
                { "Weight", 2.5 },
                { "WeightUnit", "KG" },
                { "Dimensions", "10x10x5" },
                { "Volume", 500 },

                { "Color", "Black" },
                { "Size", "Medium" },
                { "Material", "Aluminum" },
                { "Variant", "Standard Edition" },
                { "SerialNumber", $"SN-{DateTime.UtcNow.Ticks % 1000000}" },

                { "IsActive", true },
                { "IsAvailable", true },
                { "Discontinued", false },
                { "InStock", true },

                { "CreatedDate", DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd") },
                { "ModifiedDate", DateTime.UtcNow.ToString("yyyy-MM-dd") },
                { "LastRestockDate", DateTime.UtcNow.AddDays(-5).ToString("yyyy-MM-dd") },

                // Date/Time fields
                { "date", DateTime.UtcNow.ToString("yyyy-MM-dd") },
                { "issue_date", DateTime.UtcNow.ToString("yyyy-MM-dd") },
                { "created_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                { "updated_at", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                { "issue_time", DateTime.UtcNow.ToString("HH:mm:ss") },

                // Amount/Price fields (additional variants)
                { "amount", 99.99 },
                { "price", 99.99 },
                { "total", 1234.56 },
                { "subtotal", 1200.00 },
                { "tax", 123.45 },
                { "qty", 10 },

                // UBL/EDI specific fields (for Despatch Advice example)
                { "ubl_version", "2.1" },
                { "customization_id", "Sample CustomID" },
                { "despatch_id", $"DSP-{12345 + i}" },
                { "order_reference_id", $"ORD-{54321 + i}" },
                { "order_id", 54321 + i },
                { "tracking_id", $"TRK-{99999 + i}" },
                { "code", "ABC123" },
                { "status", "Active" },
                { "note", "Sample note for order" },
                { "reference", "Sample reference" },
                { "url", "https://example.com" },
                { "website", "https://example.com" },

                // JSON-like fields that might be parsed
                { "supplier_party_json", JsonSerializer.Serialize(new
                {
                    endpoint_id = "9906012345678",
                    name = "Supplier Company",
                    street = "456 Supply Lane",
                    city = "Toronto",
                    postal = "M5H 2N2",
                    country_code = "CA"
                }) },
                { "delivery_party_json", JsonSerializer.Serialize(new
                {
                    endpoint_id = "9906087654321",
                    name = "Customer Company",
                    street = "789 Delivery Ave",
                    city = "Vancouver",
                    postal = "V6B 4X8",
                    country_code = "CA"
                }) },
                { "shipment_json", JsonSerializer.Serialize(new
                {
                    tracking_id = $"SHIP-{88888 + i}",
                    weight_unit = "KGM",
                    gross_weight_measure = 25.50,
                    carrier_name = "Express Shipping Co"
                }) },
                { "despatch_lines_json", JsonSerializer.Serialize(new[] {
                    new {
                        id = "LINE-001",
                        unit_code = "EA",
                        delivered_qty = 5,
                        order_line_id = "1",
                        item_name = "Widget A",
                        sellers_item_id = "WIDGET-A-001"
                    },
                    new {
                        id = "LINE-002",
                        unit_code = "EA",
                        delivered_qty = 5,
                        order_line_id = "2",
                        item_name = "Widget B",
                        sellers_item_id = "WIDGET-B-001"
                    }
                }) },

                // GL Export mock data
                { "accounts_json", JsonSerializer.Serialize(new[] {
                    new {
                        account_number = "1010",
                        account_name = "Cash Account",
                        account_type = "Asset",
                        opening_balance = 10000.00,
                        debit_total = 5000.00,
                        credit_total = 2500.00,
                        closing_balance = 12500.00,
                        transactions_json = JsonSerializer.Serialize(new[] {
                            new { id = "TXN001", date = "2025-10-01", description = "Sample transaction", reference = "REF001", debit = 5000.00, credit = 0.00, cost_center = "0000" },
                            new { id = "TXN002", date = "2025-10-05", description = "Another transaction", reference = "REF002", debit = 0.00, credit = 2500.00, cost_center = "1001" }
                        })
                    }
                }) },

                { "line_items_json", JsonSerializer.Serialize(new[] {
                    new { id = 1, description = "Sample Item 1", quantity = 2, unit_code = "EA", unit_price = 100.00, net_total = 200.00, status_code = "Confirmed", confirmed_quantity = 2, line_note = "In stock" },
                    new { id = 2, description = "Sample Item 2", quantity = 1, unit_code = "EA", unit_price = 50.00, net_total = 50.00, status_code = "Pending", confirmed_quantity = 1, line_note = "Partial shipment" }
                }) },

                { "details_json", JsonSerializer.Serialize(new[] {
                    new { label = "Status", value = "Active" },
                    new { label = "Amount", value = "1234.56" },
                    new { label = "Date", value = "2025-10-31" }
                }) },

                // For templates that use 'data' instead of 'rows'
                { "export_date", DateTime.UtcNow.ToString("yyyy-MM-dd") },
                { "export_period", "October 2025" },
                { "period_name", "October 2025" },
                { "currency", "USD" },
                { "fiscal_year", 2025 },
                { "total_accounts", 5 },
                { "total_debit", 45250.00 },
                { "total_credit", 45250.00 },
                { "trial_balance", "BALANCED" },
                { "validation_status", "Valid" },
                { "notes", "Mock data for preview" },

                // UBL party/delivery references
                { "buyer_party_json", JsonSerializer.Serialize(new {
                    endpoint_id = "NL123456789",
                    name = "Sample Buyer Company",
                    street = "Sample Street 42",
                    city = "Amsterdam",
                    postal = "1000 AA",
                    country_code = "NL",
                    contact_email = "buyer@example.com"
                }) },
                { "seller_party_json", JsonSerializer.Serialize(new {
                    endpoint_id = "DE987654321",
                    name = "Sample Seller Company",
                    street = "Seller Street 10",
                    city = "Berlin",
                    postal = "1000 BB",
                    country_code = "DE"
                }) },
                { "delivery_json", JsonSerializer.Serialize(new {
                    street = "Delivery Street 1",
                    city = "Rotterdam",
                    postal = "3000 AA",
                    country_code = "NL",
                    delivery_date = "2025-11-15",
                    promised_delivery_date = "2025-11-20"
                }) },

                // Boolean-like fields
                { "is_active", true },
                { "active", "true" },
                { "enabled", "true" },
                { "deleted", "false" },
                { "archived", "false" }
            };

            mockRows.Add(row);
        }

        return mockRows;
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

public class PreviewTemplateRequest
{
    public string Template { get; set; } = string.Empty;
    public QueryTemplateType Type { get; set; }
    public string OutputFormat { get; set; } = string.Empty;
}

public class PreviewTemplateResponse
{
    public string Preview { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}