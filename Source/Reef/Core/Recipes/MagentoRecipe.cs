namespace Reef.Core.Recipes;

// Single-flow recipe (unlike WooCommerceRecipe's two flows) - pulls shipment tracking data
// from Magento's REST API and emails customers an update. Step keys are plain (no
// "shipments-" prefix) since there's only one flow here; RecipeService dispatches on
// recipe.Key instead, the same (recipeKey, isShipments) switch-tuple pattern already used
// for ErrorDigestRecipe vs WooCommerceRecipe.
public static class MagentoRecipe
{
    public const string Key = "magento-tracking-link";
    public const string StagingTableName = "MagentoShipments";

    // Per-database-type identity/text syntax for Reef's supported query sources.
    // Sqlite here is the user's chosen staging file (never Reef's own app database).
    public static string GetStagingTableDdl(string connectionType) => connectionType switch
    {
        "SqlServer" => """
            CREATE TABLE MagentoShipments (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                MagentoOrderId BIGINT NOT NULL,
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
                CONSTRAINT UQ_MagentoShipments_MagentoOrderId UNIQUE (MagentoOrderId)
            )
            """,
        "MySQL" or "MariaDB" => """
            CREATE TABLE MagentoShipments (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                MagentoOrderId BIGINT NOT NULL,
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
                UNIQUE KEY UQ_MagentoShipments_MagentoOrderId (MagentoOrderId)
            )
            """,
        "Sqlite" => """
            CREATE TABLE MagentoShipments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MagentoOrderId INTEGER NOT NULL,
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
                UNIQUE (MagentoOrderId)
            )
            """,
        _ => """
            CREATE TABLE MagentoShipments (
                Id SERIAL PRIMARY KEY,
                MagentoOrderId BIGINT NOT NULL UNIQUE,
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

    public const string DefaultExportQuery = "SELECT * FROM MagentoShipments WHERE EmailSent = 0";

    public static RecipeDefinition Definition { get; } = new()
    {
        Key = Key,
        Name = "Magento Tracking Link",
        Description = "Pull shipment tracking data from your Magento store's REST API and automatically email customers a tracking-link update.",
        Category = "E-commerce",
        Icon = "package",
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
                VerifierKind = RecipeVerifierKind.StagingTable
            },
            new()
            {
                StepKey = "import-profile",
                Title = "Pull Shipments from Magento",
                EntityType = RecipeEntityType.ImportProfile,
                VerifierKind = RecipeVerifierKind.HttpSource
            },
            new()
            {
                StepKey = "query-template",
                Title = "Email Template",
                EntityType = RecipeEntityType.QueryTemplate,
                VerifierKind = RecipeVerifierKind.ScribanTemplate
            },
            new()
            {
                StepKey = "export-profile",
                Title = "Send Tracking Emails",
                EntityType = RecipeEntityType.Profile,
                VerifierKind = RecipeVerifierKind.ExportQuery
            },
            new()
            {
                StepKey = "jobs",
                Title = "Scheduling (optional)",
                EntityType = RecipeEntityType.Job,
                IsOptional = true,
                VerifierKind = null
            }
        }
    };
}
