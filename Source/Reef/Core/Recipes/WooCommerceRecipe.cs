namespace Reef.Core.Recipes;

/// <summary>
/// WooCommerce recipe: Order Confirmation emails (Flow A) + Tracking Link emails (Flow B).
/// Both flows share steps 1-3 (Connection/Group/Destination, IsShared=true) and are grouped
/// in the UI via RecipeStepDefinition.FlowGroup ("Order Confirmation" / "Tracking Link") -
/// one RecipeDefinition, two flows as sections in its Steps list, per the Phase 1 step shape
/// that already anticipated this (IsShared flag) rather than splitting into two recipes or
/// redesigning the engine for cross-recipe-run lookups.
/// </summary>
public static class WooCommerceRecipe
{
    public const string Key = "woocommerce-order-confirmation";

    /// <summary>Default staging table the wizard creates via raw DDL (Reef does not auto-create import target tables) - Flow A (Order Confirmation).</summary>
    public const string StagingTableName = "StoreOrders";

    /// <summary>Default staging table for Flow B (Tracking Link).</summary>
    public const string ShipmentsStagingTableName = "StoreShipments";

    /// <summary>
    /// CREATE TABLE DDL for the StoreOrders staging table. Column set matches the fields
    /// the Order Confirmation Scriban template (see WooCommerceEmailTemplates) and the
    /// WooCommerce REST orders endpoint (/wp-json/wc/v3/orders) both expect.
    /// Per-database-type because Reef's supported query sources are SqlServer/MySQL/PostgreSQL
    /// and each has different identity/text-type syntax.
    /// </summary>
    public static string GetStagingTableDdl(string connectionType) => connectionType switch
    {
        "SqlServer" => """
            CREATE TABLE StoreOrders (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                WooOrderId BIGINT NOT NULL,
                OrderNumber NVARCHAR(50) NULL,
                OrderDate DATETIME NULL,
                OrderStatus NVARCHAR(50) NULL,
                CustomerName NVARCHAR(255) NULL,
                CustomerEmail NVARCHAR(255) NULL,
                Company NVARCHAR(255) NULL,
                CurrencySymbol NVARCHAR(10) NULL,
                Total DECIMAL(18,2) NULL,
                Subtotal DECIMAL(18,2) NULL,
                Discount DECIMAL(18,2) NULL,
                Shipping DECIMAL(18,2) NULL,
                Tax DECIMAL(18,2) NULL,
                ShippingAddressJson NVARCHAR(MAX) NULL,
                BillingAddressJson NVARCHAR(MAX) NULL,
                ItemsJson NVARCHAR(MAX) NULL,
                EmailSent BIT NOT NULL DEFAULT 0,
                CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT UQ_StoreOrders_WooOrderId UNIQUE (WooOrderId)
            )
            """,
        "MySQL" or "MariaDB" => """
            CREATE TABLE StoreOrders (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                WooOrderId BIGINT NOT NULL,
                OrderNumber VARCHAR(50) NULL,
                OrderDate DATETIME NULL,
                OrderStatus VARCHAR(50) NULL,
                CustomerName VARCHAR(255) NULL,
                CustomerEmail VARCHAR(255) NULL,
                Company VARCHAR(255) NULL,
                CurrencySymbol VARCHAR(10) NULL,
                Total DECIMAL(18,2) NULL,
                Subtotal DECIMAL(18,2) NULL,
                Discount DECIMAL(18,2) NULL,
                Shipping DECIMAL(18,2) NULL,
                Tax DECIMAL(18,2) NULL,
                ShippingAddressJson TEXT NULL,
                BillingAddressJson TEXT NULL,
                ItemsJson TEXT NULL,
                EmailSent TINYINT(1) NOT NULL DEFAULT 0,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE KEY UQ_StoreOrders_WooOrderId (WooOrderId)
            )
            """,
        _ => """
            CREATE TABLE StoreOrders (
                Id SERIAL PRIMARY KEY,
                WooOrderId BIGINT NOT NULL UNIQUE,
                OrderNumber VARCHAR(50) NULL,
                OrderDate TIMESTAMP NULL,
                OrderStatus VARCHAR(50) NULL,
                CustomerName VARCHAR(255) NULL,
                CustomerEmail VARCHAR(255) NULL,
                Company VARCHAR(255) NULL,
                CurrencySymbol VARCHAR(10) NULL,
                Total DECIMAL(18,2) NULL,
                Subtotal DECIMAL(18,2) NULL,
                Discount DECIMAL(18,2) NULL,
                Shipping DECIMAL(18,2) NULL,
                Tax DECIMAL(18,2) NULL,
                ShippingAddressJson TEXT NULL,
                BillingAddressJson TEXT NULL,
                ItemsJson TEXT NULL,
                EmailSent BOOLEAN NOT NULL DEFAULT FALSE,
                CreatedAt TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """
    };

    /// <summary>Default export query selecting unsent order confirmations from the staging table.</summary>
    public const string DefaultExportQuery = "SELECT * FROM StoreOrders WHERE EmailSent = 0";

    /// <summary>
    /// CREATE TABLE DDL for the StoreShipments staging table (Flow B - Tracking Link).
    /// Column set matches the Tracking Update Scriban template (see WooCommerceEmailTemplates)
    /// and WooCommerce's order/shipment tracking data. Same per-database-type shape as
    /// GetStagingTableDdl, just a different target table and column set.
    /// </summary>
    public static string GetShipmentsStagingTableDdl(string connectionType) => connectionType switch
    {
        "SqlServer" => """
            CREATE TABLE StoreShipments (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                WooOrderId BIGINT NOT NULL,
                OrderNumber NVARCHAR(50) NULL,
                CustomerName NVARCHAR(255) NULL,
                CustomerEmail NVARCHAR(255) NULL,
                Company NVARCHAR(255) NULL,
                TrackingNumber NVARCHAR(100) NULL,
                Carrier NVARCHAR(100) NULL,
                TrackingUrl NVARCHAR(500) NULL,
                EstimatedDeliveryDate NVARCHAR(50) NULL,
                ShippingAddressJson NVARCHAR(MAX) NULL,
                ItemsJson NVARCHAR(MAX) NULL,
                EmailSent BIT NOT NULL DEFAULT 0,
                CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT UQ_StoreShipments_WooOrderId UNIQUE (WooOrderId)
            )
            """,
        "MySQL" or "MariaDB" => """
            CREATE TABLE StoreShipments (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                WooOrderId BIGINT NOT NULL,
                OrderNumber VARCHAR(50) NULL,
                CustomerName VARCHAR(255) NULL,
                CustomerEmail VARCHAR(255) NULL,
                Company VARCHAR(255) NULL,
                TrackingNumber VARCHAR(100) NULL,
                Carrier VARCHAR(100) NULL,
                TrackingUrl VARCHAR(500) NULL,
                EstimatedDeliveryDate VARCHAR(50) NULL,
                ShippingAddressJson TEXT NULL,
                ItemsJson TEXT NULL,
                EmailSent TINYINT(1) NOT NULL DEFAULT 0,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE KEY UQ_StoreShipments_WooOrderId (WooOrderId)
            )
            """,
        _ => """
            CREATE TABLE StoreShipments (
                Id SERIAL PRIMARY KEY,
                WooOrderId BIGINT NOT NULL UNIQUE,
                OrderNumber VARCHAR(50) NULL,
                CustomerName VARCHAR(255) NULL,
                CustomerEmail VARCHAR(255) NULL,
                Company VARCHAR(255) NULL,
                TrackingNumber VARCHAR(100) NULL,
                Carrier VARCHAR(100) NULL,
                TrackingUrl VARCHAR(500) NULL,
                EstimatedDeliveryDate VARCHAR(50) NULL,
                ShippingAddressJson TEXT NULL,
                ItemsJson TEXT NULL,
                EmailSent BOOLEAN NOT NULL DEFAULT FALSE,
                CreatedAt TIMESTAMP NOT NULL DEFAULT NOW()
            )
            """
    };

    /// <summary>Default export query selecting unsent tracking emails from the shipments staging table.</summary>
    public const string DefaultShipmentsExportQuery = "SELECT * FROM StoreShipments WHERE EmailSent = 0";

    public const string OrderConfirmationFlowGroup = "Order Confirmation";
    public const string TrackingLinkFlowGroup = "Tracking Link";

    public static RecipeDefinition Definition { get; } = new()
    {
        Key = Key,
        Name = "WooCommerce Order Confirmation",
        Description = "Pull new orders from your WooCommerce store and automatically email customers a confirmation, plus shipment tracking links.",
        Steps = new List<RecipeStepDefinition>
        {
            // Shared
            new()
            {
                StepKey = "connection",
                Title = "Database Connection",
                EntityType = RecipeEntityType.Connection,
                IsShared = true,
                VerifierKind = RecipeVerifierKind.Connection
            },
            new()
            {
                StepKey = "group",
                Title = "Organize",
                EntityType = RecipeEntityType.Group,
                IsShared = true,
                VerifierKind = null // Groups are pure UI organization, no live check applies
            },
            new()
            {
                StepKey = "destination",
                Title = "Email Destination",
                EntityType = RecipeEntityType.Destination,
                IsShared = true,
                VerifierKind = RecipeVerifierKind.EmailDestination
            },

            // Order Confirmation flow (Flow A)
            new()
            {
                StepKey = "staging-table",
                Title = "Staging Table",
                EntityType = RecipeEntityType.StagingTable,
                FlowGroup = OrderConfirmationFlowGroup,
                VerifierKind = RecipeVerifierKind.StagingTable
            },
            new()
            {
                StepKey = "import-profile",
                Title = "Pull Orders from WooCommerce",
                EntityType = RecipeEntityType.ImportProfile,
                FlowGroup = OrderConfirmationFlowGroup,
                VerifierKind = RecipeVerifierKind.HttpSource
            },
            new()
            {
                StepKey = "query-template",
                Title = "Email Template",
                EntityType = RecipeEntityType.QueryTemplate,
                FlowGroup = OrderConfirmationFlowGroup,
                VerifierKind = RecipeVerifierKind.ScribanTemplate
            },
            new()
            {
                StepKey = "export-profile",
                Title = "Send Confirmation Emails",
                EntityType = RecipeEntityType.Profile,
                FlowGroup = OrderConfirmationFlowGroup,
                VerifierKind = RecipeVerifierKind.ExportQuery
            },
            new()
            {
                StepKey = "jobs",
                Title = "Scheduling (optional)",
                EntityType = RecipeEntityType.Job,
                IsOptional = true,
                FlowGroup = OrderConfirmationFlowGroup,
                VerifierKind = null // Jobs are optional automation on top of already-verified steps; no separate live check
            },

            // Tracking Link flow (Flow B) - same step shape as Flow A, reuses the shared
            // Connection/Group/Destination above instead of re-collecting them.
            new()
            {
                StepKey = "shipments-staging-table",
                Title = "Staging Table",
                EntityType = RecipeEntityType.StagingTable,
                FlowGroup = TrackingLinkFlowGroup,
                VerifierKind = RecipeVerifierKind.StagingTable
            },
            new()
            {
                StepKey = "shipments-import-profile",
                Title = "Pull Tracking Updates from WooCommerce",
                EntityType = RecipeEntityType.ImportProfile,
                FlowGroup = TrackingLinkFlowGroup,
                VerifierKind = RecipeVerifierKind.HttpSource
            },
            new()
            {
                StepKey = "shipments-query-template",
                Title = "Email Template",
                EntityType = RecipeEntityType.QueryTemplate,
                FlowGroup = TrackingLinkFlowGroup,
                VerifierKind = RecipeVerifierKind.ScribanTemplate
            },
            new()
            {
                StepKey = "shipments-export-profile",
                Title = "Send Tracking Emails",
                EntityType = RecipeEntityType.Profile,
                FlowGroup = TrackingLinkFlowGroup,
                VerifierKind = RecipeVerifierKind.ExportQuery
            },
            new()
            {
                StepKey = "shipments-jobs",
                Title = "Scheduling & Webhook (optional)",
                EntityType = RecipeEntityType.Job,
                IsOptional = true,
                FlowGroup = TrackingLinkFlowGroup,
                VerifierKind = null // Jobs/webhook registration are optional automation on top of already-verified steps; no separate live check
            }
        }
    };
}
