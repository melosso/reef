namespace Reef.Core.Recipes;

// Four flows (Invoice/Order/Despatch Advice/Inventory) as one RecipeDefinition, sharing
// Connection/Group (IsShared=true) - same shape as ExactGlobeRecipe's Debtors/Items pair,
// just twice as many flows. Unlike ExactGlobeRecipe this isn't tied to one ERP vendor: the
// XML produced is plain UBL 2.1, so the Connection is still the user's own real business
// database (never auto-provisioned staging - same reasoning as ExactGlobe), and there's no
// import side and no email Destination step, just a file export using Profile's inline
// OutputDestinationType="Local"/OutputDestinationConfig path.
public static class UblExportRecipe
{
    public const string Key = "ubl-standard-export";

    public const string InvoiceFlowGroup = "Invoice Export";
    public const string OrderFlowGroup = "Order Export";
    public const string DespatchFlowGroup = "Despatch Advice Export";
    public const string InventoryFlowGroup = "Inventory Export";

    // Trimmed from the example in ERP_Example_-_Invoice_UBL_2.1_ScribanTemplate.md - the doc's
    // SQL Server example builds the *_json columns with JSON_QUERY()/string literals; here
    // they're built generically from the user's own tables with a vendor-neutral subquery
    // shape (JSON building syntax differs across SQL Server/MySQL/PostgreSQL, so this is an
    // illustrative starting point, not a copy-pasteable query for a specific database).
    public const string DefaultInvoiceExportQuery = """
        SELECT
            i.InvoiceNumber AS invoice_id,
            i.SupplierName AS supplier_name,
            i.CustomerName AS customer_name,
            i.InvoiceTypeCode AS invoice_type_code,
            '2.1' AS ubl_version,
            i.CustomizationId AS customization_id,
            i.IssueDate AS issue_date,
            i.DueDate AS due_date,
            i.Currency AS currency,
            i.TotalNet AS total_net,
            i.TotalTax AS total_tax,
            i.TotalGross AS total_gross,
            i.PaymentMeansCode AS payment_means_code,
            i.PayeeIban AS payee_iban,
            i.SupplierPartyJson AS supplier_party_json,
            i.CustomerPartyJson AS customer_party_json,
            i.TaxSummaryJson AS tax_summary_json,
            i.LineItemsJson AS line_items_json
        FROM Invoices i
        WHERE i.Exported = 0
        ORDER BY i.InvoiceNumber
        """;

    // Trimmed from ERP_Example_-_Order_UBL_2.1_ScribanTemplate.md, same generic-JSON-column
    // approach as the invoice default above.
    public const string DefaultOrderExportQuery = """
        SELECT
            o.OrderNumber AS order_id,
            o.IssueDate AS issue_date,
            o.Currency AS currency,
            o.Note AS note,
            '2.1' AS ubl_version,
            o.CustomizationId AS customization_id,
            o.TotalNet AS total_net,
            o.TotalTax AS total_tax,
            o.TotalGross AS total_gross,
            o.BuyerPartyJson AS buyer_party_json,
            o.SellerPartyJson AS seller_party_json,
            o.DeliveryJson AS delivery_json,
            o.LineItemsJson AS line_items_json
        FROM Orders o
        WHERE o.Exported = 0
        ORDER BY o.OrderNumber
        """;

    // Trimmed from ERP_Example_-_Despatch_Advice_UBL_2.1_ScribanTemplate.md.
    public const string DefaultDespatchAdviceExportQuery = """
        SELECT
            d.DespatchNumber AS despatch_id,
            d.OrderReferenceId AS order_reference_id,
            d.IssueDate AS issue_date,
            d.IssueTime AS issue_time,
            '2.1' AS ubl_version,
            d.CustomizationId AS customization_id,
            d.Note AS note,
            d.SupplierPartyJson AS supplier_party_json,
            d.DeliveryPartyJson AS delivery_party_json,
            d.ShipmentJson AS shipment_json,
            d.DespatchLinesJson AS despatch_lines_json
        FROM DespatchAdvices d
        WHERE d.Exported = 0
        ORDER BY d.DespatchNumber
        """;

    // Trimmed from ERP_Example_-_Inventory_UBL_2.1_ScribanTemplate.md.
    public const string DefaultInventoryExportQuery = """
        SELECT
            r.ReportNumber AS report_id,
            r.IssueDate AS issue_date,
            r.PeriodStartDate AS period_start_date,
            r.PeriodEndDate AS period_end_date,
            '2.1' AS ubl_version,
            r.CustomizationId AS customization_id,
            r.RetailerPartyJson AS retailer_party_json,
            r.LocationJson AS location_json,
            r.InventoryLinesJson AS inventory_lines_json
        FROM InventoryReports r
        ORDER BY r.ReportNumber
        """;

    // Mock rows for the ScribanTemplateVerifier when the staging/query table can't be probed
    // for a real row (this recipe has no staging table at all) - shaped to match every field
    // the four XML templates reference so a preview render always succeeds.
    public static Dictionary<string, object> MockInvoiceRow() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["invoice_id"] = "INV-2025-09-00123",
        ["supplier_name"] = "ACME Consulting GmbH",
        ["customer_name"] = "Global Innovations B.V.",
        ["invoice_type_code"] = "380",
        ["ubl_version"] = "2.1",
        ["customization_id"] = "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0",
        ["issue_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["due_date"] = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
        ["currency"] = "EUR",
        ["total_net"] = "1000.00",
        ["total_tax"] = "190.00",
        ["total_gross"] = "1190.00",
        ["payment_means_code"] = "30",
        ["payee_iban"] = "DE98765432101234567890",
        ["supplier_party_json"] = """{"endpoint_id":"DE123456789","vat_id":"DE123456789","street":"Hauptstrasse 42","city":"Berlin","postal":"10115","country_code":"DE","contact_email":"billing@acme-consulting.de"}""",
        ["customer_party_json"] = """{"endpoint_id":"NL987654321","vat_id":"NL987654321","street":"Keizersgracht 100","city":"Amsterdam","postal":"1015 CN","country_code":"NL","contact_email":"ap@global-innovations.nl"}""",
        ["tax_summary_json"] = """[{"tax_rate":"19.0","taxable_amount":"1000.00","tax_amount":"190.00","category_code":"S"}]""",
        ["line_items_json"] = """[{"id":1,"description":"Reef Demo Service","quantity":"20","unit_code":"HUR","unit_price":"40.00","net_total":"800.00","tax_rate":"19.0"},{"id":2,"description":"Reef Demo License","quantity":"1","unit_code":"EA","unit_price":"200.00","net_total":"200.00","tax_rate":"19.0"}]"""
    };

    public static Dictionary<string, object> MockOrderRow() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["order_id"] = "PO-2025-10-00456",
        ["issue_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["currency"] = "EUR",
        ["note"] = "Urgent delivery required. Please confirm receipt.",
        ["ubl_version"] = "2.1",
        ["customization_id"] = "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0",
        ["total_net"] = "1200.00",
        ["total_tax"] = "228.00",
        ["total_gross"] = "1428.00",
        ["buyer_party_json"] = """{"endpoint_id":"NL987654321","name":"Global Innovations B.V.","street":"Keizersgracht 100","city":"Amsterdam","postal":"1015 CN","country_code":"NL","contact_email":"purchasing@global-innovations.nl"}""",
        ["seller_party_json"] = """{"endpoint_id":"DE123456789","name":"ACME Consulting GmbH","street":"Hauptstrasse 42","city":"Berlin","postal":"10115","country_code":"DE"}""",
        ["delivery_json"] = $$"""{"street":"Warehouse Dock 4, Port Road 1","city":"Rotterdam","postal":"3011 AA","country_code":"NL","delivery_date":"{{DateTime.UtcNow.AddDays(14):yyyy-MM-dd}}"}""",
        ["line_items_json"] = """[{"id":1,"description":"Reef Demo Server Blade","quantity":"2","unit_code":"C62","unit_price":"500.00","net_total":"1000.00"},{"id":2,"description":"Reef Demo Rack Kit","quantity":"2","unit_code":"EA","unit_price":"100.00","net_total":"200.00"}]"""
    };

    public static Dictionary<string, object> MockDespatchAdviceRow() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["despatch_id"] = "DES-2025-09-9988",
        ["order_reference_id"] = "PO-2025-10-00456",
        ["issue_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["issue_time"] = DateTime.UtcNow.ToString("HH:mm:ss"),
        ["ubl_version"] = "2.1",
        ["customization_id"] = "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0",
        ["note"] = "Partial shipment of server blades",
        ["supplier_party_json"] = """{"endpoint_id":"DE123456789","name":"ACME Consulting GmbH","street":"Hauptstrasse 42","city":"Berlin","postal":"10115","country_code":"DE"}""",
        ["delivery_party_json"] = """{"endpoint_id":"NL987654321","name":"Global Innovations B.V.","street":"Warehouse Dock 4, Port Road 1","city":"Rotterdam","postal":"3011 AA","country_code":"NL"}""",
        ["shipment_json"] = """{"gross_weight_measure":"45.5","weight_unit":"KGM","handling_code":"Fragile","carrier_name":"FastLogistics EU","tracking_id":"TRK-9988776655"}""",
        ["despatch_lines_json"] = """[{"id":1,"item_name":"Reef Demo Server Blade","delivered_qty":"2","unit_code":"C62","order_line_id":"1","sellers_item_id":"SRV-BLD-09"},{"id":2,"item_name":"Reef Demo Rack Kit","delivered_qty":"2","unit_code":"EA","order_line_id":"2","sellers_item_id":"RCK-MNT-01"}]"""
    };

    public static Dictionary<string, object> MockInventoryRow() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["report_id"] = "INV-RPT-2025-W04",
        ["issue_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["period_start_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["period_end_date"] = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd"),
        ["ubl_version"] = "2.1",
        ["customization_id"] = "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0",
        ["retailer_party_json"] = """{"endpoint_id":"NL987654321","name":"Global Innovations B.V."}""",
        ["location_json"] = """{"id":"WH-AMSTERDAM-01","name":"Main Distribution Center","street":"Logistics Way 12","city":"Amsterdam","postal":"1011 AZ","country_code":"NL"}""",
        ["inventory_lines_json"] = """[{"id":1,"item_name":"Reef Demo Wireless Mouse","item_id":"WM-100-BLK","quantity":"450","unit_code":"EA","location_zone":"Aisle 4, Shelf B"},{"id":2,"item_name":"Reef Demo Keyboard","item_id":"MK-500-RGB","quantity":"120","unit_code":"EA","location_zone":"Aisle 4, Shelf C"}]"""
    };

    public static RecipeDefinition Definition { get; } = new()
    {
        Key = Key,
        Name = "UBL Standard Export",
        Description = "Export Invoices, Orders, Despatch Advices, or Inventory Reports from your own database to generic, vendor-neutral UBL 2.1 XML files - works with any ERP that can read standard UBL, not locked to one vendor. No import required, queries your own tables directly.",
        Category = "ERP",
        Icon = "file-text",
        Steps = new List<RecipeStepDefinition>
        {
            new()
            {
                StepKey = "connection",
                Title = "Database Connection",
                EntityType = RecipeEntityType.Connection,
                IsShared = true,
                VerifierKind = RecipeVerifierKind.Connection
                // Deliberately no CanAutoProvision: this Connection is the user's real business
                // database, never a Reef-managed staging file.
            },
            new()
            {
                StepKey = "group",
                Title = "Organize",
                EntityType = RecipeEntityType.Group,
                IsShared = true,
                VerifierKind = null,
                CanAutoProvision = true
            },

            new()
            {
                StepKey = "invoice-query-template",
                Title = "Invoice XML Template",
                EntityType = RecipeEntityType.QueryTemplate,
                FlowGroup = InvoiceFlowGroup,
                VerifierKind = RecipeVerifierKind.ScribanTemplate
            },
            new()
            {
                StepKey = "invoice-export-profile",
                Title = "Export Invoices to XML",
                EntityType = RecipeEntityType.Profile,
                FlowGroup = InvoiceFlowGroup,
                VerifierKind = RecipeVerifierKind.ExportQuery
            },
            new()
            {
                StepKey = "invoice-jobs",
                Title = "Scheduling (optional)",
                EntityType = RecipeEntityType.Job,
                IsOptional = true,
                FlowGroup = InvoiceFlowGroup,
                VerifierKind = null
            },

            new()
            {
                StepKey = "order-query-template",
                Title = "Order XML Template",
                EntityType = RecipeEntityType.QueryTemplate,
                FlowGroup = OrderFlowGroup,
                VerifierKind = RecipeVerifierKind.ScribanTemplate
            },
            new()
            {
                StepKey = "order-export-profile",
                Title = "Export Orders to XML",
                EntityType = RecipeEntityType.Profile,
                FlowGroup = OrderFlowGroup,
                VerifierKind = RecipeVerifierKind.ExportQuery
            },
            new()
            {
                StepKey = "order-jobs",
                Title = "Scheduling (optional)",
                EntityType = RecipeEntityType.Job,
                IsOptional = true,
                FlowGroup = OrderFlowGroup,
                VerifierKind = null
            },

            new()
            {
                StepKey = "despatch-query-template",
                Title = "Despatch Advice XML Template",
                EntityType = RecipeEntityType.QueryTemplate,
                FlowGroup = DespatchFlowGroup,
                VerifierKind = RecipeVerifierKind.ScribanTemplate
            },
            new()
            {
                StepKey = "despatch-export-profile",
                Title = "Export Despatch Advices to XML",
                EntityType = RecipeEntityType.Profile,
                FlowGroup = DespatchFlowGroup,
                VerifierKind = RecipeVerifierKind.ExportQuery
            },
            new()
            {
                StepKey = "despatch-jobs",
                Title = "Scheduling (optional)",
                EntityType = RecipeEntityType.Job,
                IsOptional = true,
                FlowGroup = DespatchFlowGroup,
                VerifierKind = null
            },

            new()
            {
                StepKey = "inventory-query-template",
                Title = "Inventory XML Template",
                EntityType = RecipeEntityType.QueryTemplate,
                FlowGroup = InventoryFlowGroup,
                VerifierKind = RecipeVerifierKind.ScribanTemplate
            },
            new()
            {
                StepKey = "inventory-export-profile",
                Title = "Export Inventory Reports to XML",
                EntityType = RecipeEntityType.Profile,
                FlowGroup = InventoryFlowGroup,
                VerifierKind = RecipeVerifierKind.ExportQuery
            },
            new()
            {
                StepKey = "inventory-jobs",
                Title = "Scheduling (optional)",
                EntityType = RecipeEntityType.Job,
                IsOptional = true,
                FlowGroup = InventoryFlowGroup,
                VerifierKind = null
            }
        }
    };
}
