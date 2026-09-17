namespace Reef.Core.Recipes;

// Debtors Export (Flow A) + Items Export (Flow B) as one RecipeDefinition, both flows
// sharing Connection/Group (IsShared=true). Unlike WooCommerce/Magento, the Connection here
// is the user's own Exact Globe+ business database - a real, manual pick, never auto-provisioned
// staging. There's no import side (queries the user's existing tables directly, same shape as
// ErrorDigestRecipe) and no email Destination step - this is a file export to XML, using
// Profile's inline OutputDestinationType="Local"/OutputDestinationConfig path so no separate
// Destination entity needs to be created first.
public static class ExactGlobeRecipe
{
    public const string Key = "exact-globe-data-export";

    public const string DebtorsFlowGroup = "Debtors Export";
    public const string ItemsFlowGroup = "Items Export";

    // Trimmed from the full example in Exact_Globe_-_eExact_XML_for_Debtors.ScribanTemplate.md -
    // same column aliases the template expects, with the OUTER APPLY/VAT join dropped since
    // they're optional enrichments, not required for a working starting query.
    public const string DefaultDebtorsExportQuery = """
        SELECT
            c.debcode AS EntityCode,
            c.cmp_code AS Code,
            c.cmp_status AS Status,
            c.cmp_type AS Type,
            TRIM(c.cmp_name) AS Name,
            COALESCE(c.TextField1, '') AS FreeText1,
            COALESCE(c.TextField2, '') AS FreeText2,
            COALESCE(c.TextField3, '') AS FreeText3,
            COALESCE(c.TextField4, '') AS FreeText4,
            COALESCE(c.TextField5, '') AS FreeText5,
            COALESCE(c.TextField6, '') AS FreeText6,
            COALESCE(c.TextField7, '') AS FreeText7,
            COALESCE(c.TextField8, '') AS FreeText8,
            COALESCE(c.TextField9, '') AS FreeText9,
            COALESCE(c.TextField10, '') AS FreeText10,
            COALESCE(c.NumberField1, 0) AS FreeNumber1,
            COALESCE(c.NumberField2, 0) AS FreeNumber2,
            COALESCE(c.NumberField3, 0) AS FreeNumber3,
            COALESCE(c.NumberField4, 0) AS FreeNumber4,
            COALESCE(c.NumberField5, 0) AS FreeNumber5,
            COALESCE(c.YesNoField1, 0) AS FreeYesNo1,
            COALESCE(c.YesNoField2, 0) AS FreeYesNo2,
            COALESCE(c.YesNoField3, 0) AS FreeYesNo3,
            COALESCE(c.YesNoField4, 0) AS FreeYesNo4,
            COALESCE(c.YesNoField5, 0) AS FreeYesNo5,
            FORMAT(c.syscreated, 'yyyy-MM-dd HH:mm') AS CreatedDate,
            FORMAT(c.sysmodified, 'yyyy-MM-dd HH:mm') AS ModifiedDate,
            REPLACE(REPLACE(CAST(c.sysguid AS VARCHAR(36)), '{', ''), '}', '') AS UUID
        FROM dbo.cicmpy c
        WHERE c.debcode IS NOT NULL
        ORDER BY c.debcode
        """;

    // Trimmed from Exact_Globe_-_eExact_XML_for_Items.ScribanTemplate.md - drops the
    // supplier/warehouse JSON subqueries and item-class joins (optional enrichments the
    // template already guards with null checks) but keeps every column the template expects
    // a value for so the default query renders cleanly out of the box.
    public const string DefaultItemsExportQuery = """
        SELECT
            i.ItemCode AS ItemCode,
            i.Type AS Type,
            TRIM(i.SearchCode) AS SearchCode,
            i.Description AS Description,
            COALESCE(i.Description_0, '') AS Description_0,
            COALESCE(i.Description_1, '') AS Description_1,
            COALESCE(i.Description_2, '') AS Description_2,
            COALESCE(i.Description_3, '') AS Description_3,
            COALESCE(i.Description_4, '') AS Description_4,
            i.Condition AS Condition,
            i.IsSalesItem AS IsSalesItem,
            i.IsPurchaseItem AS IsPurchaseItem,
            i.IsSerialNumberItem AS IsSerialNumberItem,
            i.IsBatchItem AS IsBatchItem,
            i.IsSubAssemblyItem AS IsSubAssemblyItem,
            i.IsAssembled AS IsAssembledItem,
            i.IsStockItem AS IsStockItem,
            i.IsBackOrderItem AS IsBackOrderItem,
            i.IsFractionAllowedItem AS IsFractionAllowedItem,
            i.IsPriceRegulationItem AS IsPriceRegulationItem,
            i.IsTextItem AS IsTextItem,
            i.IsDiscount AS IsDiscountItem,
            i.IsExplode AS IsExplodeItem,
            i.IsPrintItem AS IsPrintItem,
            i.IsOutsourcedItem AS IsOutsourcedItem,
            i.IsServiceItem AS IsServiceItem,
            i.SalesPackagePrice AS SalesPrice,
            i.CostPriceCurrency AS SalesCurrency,
            TRIM(i.PackageDescription) AS SalesUnit,
            i.CostPriceCurrency AS CostCurrency,
            i.CostPriceStandard AS CostPrice,
            i.PurchaseCurrency AS PurchaseCurrency,
            i.PurchasePrice AS PurchasePrice,
            i.NetWeight AS NetWeight,
            i.GrossWeight AS GrossWeight,
            COALESCE(i.UserField_01, '') AS FreeText1,
            COALESCE(i.UserField_02, '') AS FreeText2,
            COALESCE(i.UserField_03, '') AS FreeText3,
            COALESCE(i.UserField_04, '') AS FreeText4,
            COALESCE(i.UserField_05, '') AS FreeText5,
            COALESCE(i.UserNumber_01, 0) AS FreeNumber1,
            COALESCE(i.UserNumber_02, 0) AS FreeNumber2,
            COALESCE(i.UserNumber_03, 0) AS FreeNumber3,
            COALESCE(i.UserNumber_04, 0) AS FreeNumber4,
            COALESCE(i.UserNumber_05, 0) AS FreeNumber5,
            COALESCE(i.UserYesNo_01, 0) AS FreeYesNo1,
            COALESCE(i.UserYesNo_02, 0) AS FreeYesNo2,
            COALESCE(i.UserYesNo_03, 0) AS FreeYesNo3,
            COALESCE(i.UserYesNo_04, 0) AS FreeYesNo4,
            COALESCE(i.UserYesNo_05, 0) AS FreeYesNo5,
            i.ShelfLife AS ShelfLife,
            i.Warranty AS Warranty,
            i.IntrastatEnabled AS IntrastatEnabled,
            i.AddExtraReceiptToOrder AS AddExtraReceiptToOrder,
            i.IsCommissionable AS IsCommissionable,
            TRIM(i.CommissionMethod) AS CommissionMethod,
            i.CommissionValue AS CommissionValue,
            i.TaxItemClassification AS TaxItemClassification,
            FORMAT(i.syscreated, 'yyyy-MM-dd HH:mm') AS CreatedDate,
            FORMAT(i.sysmodified, 'yyyy-MM-dd HH:mm') AS ModifiedDate,
            REPLACE(REPLACE(CAST(i.sysguid AS VARCHAR(36)), '{', ''), '}', '') AS UUID
        FROM dbo.Items i
        WHERE i.IsSalesItem = 1
        AND i.Type NOT IN ('L', 'P')
        ORDER BY i.ItemCode
        """;

    // Mock rows for the ScribanTemplateVerifier when the staging/query table can't be probed
    // for a real row (this recipe has no staging table at all) - shaped to match every field
    // the two XML templates reference so a preview render always succeeds.
    public static Dictionary<string, object> MockDebtorRow() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "DEB001",
        ["Status"] = "Active",
        ["Type"] = "C",
        ["Name"] = "Reef Demo Customer BV",
        ["FreeText1"] = "", ["FreeText2"] = "", ["FreeText3"] = "", ["FreeText4"] = "", ["FreeText5"] = "",
        ["FreeText6"] = "", ["FreeText7"] = "", ["FreeText8"] = "", ["FreeText9"] = "", ["FreeText10"] = "",
        ["FreeNumber1"] = 0, ["FreeNumber2"] = 0, ["FreeNumber3"] = 0, ["FreeNumber4"] = 0, ["FreeNumber5"] = 0,
        ["FreeYesNo1"] = 0, ["FreeYesNo2"] = 0, ["FreeYesNo3"] = 0, ["FreeYesNo4"] = 0, ["FreeYesNo5"] = 0,
        ["CreatedDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
        ["ModifiedDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
        ["UUID"] = Guid.NewGuid().ToString()
    };

    public static Dictionary<string, object> MockItemRow() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["ItemCode"] = "ITEM001",
        ["Type"] = "V",
        ["SearchCode"] = "ITEM001",
        ["Description"] = "Reef Demo Item",
        ["Description_0"] = "", ["Description_1"] = "", ["Description_2"] = "", ["Description_3"] = "", ["Description_4"] = "",
        ["IsSalesItem"] = true, ["IsPurchaseItem"] = true, ["IsSerialNumberItem"] = false, ["IsBatchItem"] = false,
        ["IsSubAssemblyItem"] = false, ["IsAssembledItem"] = false, ["IsStockItem"] = true, ["IsBackOrderItem"] = false,
        ["IsFractionAllowedItem"] = false, ["IsPriceRegulationItem"] = false, ["IsTextItem"] = false,
        ["IsDiscountItem"] = false, ["IsExplodeItem"] = false, ["IsPrintItem"] = true, ["IsOutsourcedItem"] = false,
        ["IsServiceItem"] = false,
        ["SalesPrice"] = 19.99m, ["SalesCurrency"] = "EUR", ["SalesUnit"] = "PCS",
        ["CostPrice"] = 9.99m, ["CostCurrency"] = "EUR",
        ["PurchasePrice"] = 9.99m, ["PurchaseCurrency"] = "EUR",
        ["FreeText1"] = "", ["FreeText2"] = "", ["FreeText3"] = "", ["FreeText4"] = "", ["FreeText5"] = "",
        ["FreeNumber1"] = 0, ["FreeNumber2"] = 0, ["FreeNumber3"] = 0, ["FreeNumber4"] = 0, ["FreeNumber5"] = 0,
        ["FreeYesNo1"] = 0, ["FreeYesNo2"] = 0, ["FreeYesNo3"] = 0, ["FreeYesNo4"] = 0, ["FreeYesNo5"] = 0,
        ["CreatedDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
        ["ModifiedDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
        ["UUID"] = Guid.NewGuid().ToString()
    };

    public static RecipeDefinition Definition { get; } = new()
    {
        Key = Key,
        Name = "Exact Globe+ Data Export",
        Description = "Export Debtors (customers) or Items (products) from your Exact Globe+ database to eExact-compliant XML files - no import required, queries your own tables directly.",
        Category = "ERP",
        Icon = "database",
        Steps = new List<RecipeStepDefinition>
        {
            new()
            {
                StepKey = "connection",
                Title = "Database Connection",
                EntityType = RecipeEntityType.Connection,
                IsShared = true,
                VerifierKind = RecipeVerifierKind.Connection
                // Deliberately no CanAutoProvision: this Connection is the user's real Exact
                // Globe+ business database, never a Reef-managed staging file.
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
                StepKey = "debtors-query-template",
                Title = "Debtors XML Template",
                EntityType = RecipeEntityType.QueryTemplate,
                FlowGroup = DebtorsFlowGroup,
                VerifierKind = RecipeVerifierKind.ScribanTemplate
            },
            new()
            {
                StepKey = "debtors-export-profile",
                Title = "Export Debtors to XML",
                EntityType = RecipeEntityType.Profile,
                FlowGroup = DebtorsFlowGroup,
                VerifierKind = RecipeVerifierKind.ExportQuery
            },
            new()
            {
                StepKey = "debtors-jobs",
                Title = "Scheduling (optional)",
                EntityType = RecipeEntityType.Job,
                IsOptional = true,
                FlowGroup = DebtorsFlowGroup,
                VerifierKind = null
            },

            new()
            {
                StepKey = "items-query-template",
                Title = "Items XML Template",
                EntityType = RecipeEntityType.QueryTemplate,
                FlowGroup = ItemsFlowGroup,
                VerifierKind = RecipeVerifierKind.ScribanTemplate
            },
            new()
            {
                StepKey = "items-export-profile",
                Title = "Export Items to XML",
                EntityType = RecipeEntityType.Profile,
                FlowGroup = ItemsFlowGroup,
                VerifierKind = RecipeVerifierKind.ExportQuery
            },
            new()
            {
                StepKey = "items-jobs",
                Title = "Scheduling (optional)",
                EntityType = RecipeEntityType.Job,
                IsOptional = true,
                FlowGroup = ItemsFlowGroup,
                VerifierKind = null
            }
        }
    };
}
