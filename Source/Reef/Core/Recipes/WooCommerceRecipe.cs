namespace Reef.Core.Recipes;

// Order Confirmation (Flow A) + Tracking Link (Flow B) as one RecipeDefinition, both flows
// sharing steps 1-3 (IsShared=true) and grouped in the UI via FlowGroup.
public static class WooCommerceRecipe
{
    public const string Key = "woocommerce-order-confirmation";
    public const string StagingTableName = "StoreOrders";
    public const string ShipmentsStagingTableName = "StoreShipments";

    // Per-database-type identity/text syntax for Reef's supported query sources.
    // Sqlite here is the user's chosen staging file (never Reef's own app database).
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
        "Sqlite" => """
            CREATE TABLE StoreOrders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WooOrderId INTEGER NOT NULL,
                OrderNumber TEXT NULL,
                OrderDate TEXT NULL,
                OrderStatus TEXT NULL,
                CustomerName TEXT NULL,
                CustomerEmail TEXT NULL,
                Company TEXT NULL,
                CurrencySymbol TEXT NULL,
                Total TEXT NULL,
                Subtotal TEXT NULL,
                Discount TEXT NULL,
                Shipping TEXT NULL,
                Tax TEXT NULL,
                ShippingAddressJson TEXT NULL,
                BillingAddressJson TEXT NULL,
                ItemsJson TEXT NULL,
                EmailSent INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UNIQUE (WooOrderId)
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

    public const string DefaultExportQuery = "SELECT * FROM StoreOrders WHERE EmailSent = 0";

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
        "Sqlite" => """
            CREATE TABLE StoreShipments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WooOrderId INTEGER NOT NULL,
                OrderNumber TEXT NULL,
                CustomerName TEXT NULL,
                CustomerEmail TEXT NULL,
                Company TEXT NULL,
                TrackingNumber TEXT NULL,
                Carrier TEXT NULL,
                TrackingUrl TEXT NULL,
                EstimatedDeliveryDate TEXT NULL,
                ShippingAddressJson TEXT NULL,
                ItemsJson TEXT NULL,
                EmailSent INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UNIQUE (WooOrderId)
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

    public const string DefaultShipmentsExportQuery = "SELECT * FROM StoreShipments WHERE EmailSent = 0";

    public const string OrderConfirmationFlowGroup = "Order Confirmation";
    public const string TrackingLinkFlowGroup = "Tracking Link";

    public static RecipeDefinition Definition { get; } = new()
    {
        Key = Key,
        Name = "WooCommerce Order Confirmation",
        Description = "Pull new orders from your WooCommerce store and automatically email customers a confirmation, plus shipment tracking links.",
        Category = "E-commerce",
        Icon = "shopping-cart",
        Steps = new List<RecipeStepDefinition>
        {
            new()
            {
                StepKey = "connection",
                Title = "Database Connection",
                EntityType = RecipeEntityType.Connection,
                IsShared = true,
                VerifierKind = RecipeVerifierKind.Connection,
                CanAutoProvision = true
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
                StepKey = "destination",
                Title = "Email Destination",
                EntityType = RecipeEntityType.Destination,
                IsShared = true,
                VerifierKind = RecipeVerifierKind.EmailDestination
            },

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
                VerifierKind = null
            },

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
                VerifierKind = null
            }
        }
    };
}
