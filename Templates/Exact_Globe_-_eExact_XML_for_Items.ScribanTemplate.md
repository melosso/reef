# XML Export for Items with Custom Templates (+ Smart Sync)

## Overview

This example demonstrates how to export item/product data from an ERP database to a custom XML format using Reef's templating engine and Smart Sync capabilities. This pattern is ideal exporting item/product data from an Exact Globe+ database to their standardized XML format that includes:


---

## SQL Query Pattern

### Data Source

This query demonstrates the single-row-per-item pattern with all related data flattened for easy templating. Instead of generating XML in SQL using CTEs and FOR XML, we retrieve raw data and let Scriban handle formatting.

```sql
-- Query: Item/Product Export with All Related Data
-- ReefId Column: ItemCode (primary identifier for Smart Sync tracking)
-- Output Format: Custom Template (Scriban)

SELECT 
    i.ItemCode AS ItemCode,                           -- Primary identifier (used for Smart Sync)
    i.Type AS Type,                                   -- Item type
    TRIM(i.SearchCode) AS SearchCode,                 -- Search/alternate code
    i.Description AS Description,                     -- Primary description
        i.ItemCode AS ItemCode,                           -- Primary identifier (used for Smart Sync)
    i.Type AS Type,                                   -- Item type
    TRIM(i.SearchCode) AS SearchCode,                 -- Search/alternate code
    i.Description AS Description,                     -- Primary description
    
    -- Multi-language Descriptions (flattened)
    COALESCE(i.Description_0, '') AS Description_0,
    COALESCE(i.Description_1, '') AS Description_1,
    COALESCE(i.Description_2, '') AS Description_2,
    COALESCE(i.Description_3, '') AS Description_3,
    COALESCE(i.Description_4, '') AS Description_4,
    
    -- Assortment Information
    ia.Assortment AS AssortmentNumber,
    ia.Code AS AssortmentCode,
    ia.Description AS AssortmentDescription,
    
    -- Availability
    FORMAT(ic.AvailableFrom, 'yyyy-MM-dd') AS AvailableFrom,
    
    -- Item Characteristics (Boolean flags)
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
    
    -- Sales Information
    i.SalesPackagePrice AS SalesPrice,
    i.CostPriceCurrency AS SalesCurrency,
    TRIM(i.PackageDescription) AS SalesUnit,
    RTRIM(i.SalesVatCode) AS SalesVatCode,
    vat_sales.oms30_0 AS SalesVatDescription,
    TRY_CONVERT(INT, vat_sales.btwper) AS SalesVatPercentage,
    
    -- Cost Information
    i.CostPriceCurrency AS CostCurrency,
    i.CostPriceStandard AS CostPrice,
    
    -- Purchase Information
    COALESCE(i.PurchaseCurrency, (SELECT TOP 1 bd.valcode FROM bedryf bd)) AS PurchaseCurrency,
    i.PurchasePrice AS PurchasePrice,
    
    -- Dimensions
    i.NetWeight AS NetWeight,
    i.GrossWeight AS GrossWeight,
    
    -- Custom/User Fields - Text
    COALESCE(i.UserField_01, '') AS FreeText1,
    COALESCE(i.UserField_02, '') AS FreeText2,
    COALESCE(i.UserField_03, '') AS FreeText3,
    COALESCE(i.UserField_04, '') AS FreeText4,
    COALESCE(i.UserField_05, '') AS FreeText5,
    
    -- Custom/User Fields - Dates
    FORMAT(i.UserDate_01, 'yyyy-MM-dd HH:mm:ss') AS FreeDate1,
    FORMAT(i.UserDate_02, 'yyyy-MM-dd HH:mm:ss') AS FreeDate2,
    FORMAT(i.UserDate_03, 'yyyy-MM-dd HH:mm:ss') AS FreeDate3,
    FORMAT(i.UserDate_04, 'yyyy-MM-dd HH:mm:ss') AS FreeDate4,
    FORMAT(i.UserDate_05, 'yyyy-MM-dd HH:mm:ss') AS FreeDate5,
    
    -- Custom/User Fields - Numbers
    COALESCE(i.UserNumber_01, 0) AS FreeNumber1,
    COALESCE(i.UserNumber_02, 0) AS FreeNumber2,
    COALESCE(i.UserNumber_03, 0) AS FreeNumber3,
    COALESCE(i.UserNumber_04, 0) AS FreeNumber4,
    COALESCE(i.UserNumber_05, 0) AS FreeNumber5,
    
    -- Custom/User Fields - Boolean
    COALESCE(i.UserYesNo_01, 0) AS FreeYesNo1,
    COALESCE(i.UserYesNo_02, 0) AS FreeYesNo2,
    COALESCE(i.UserYesNo_03, 0) AS FreeYesNo3,
    COALESCE(i.UserYesNo_04, 0) AS FreeYesNo4,
    COALESCE(i.UserYesNo_05, 0) AS FreeYesNo5,
    
    -- Item Categories/Classes (all 5 classes)
    ic1.ClassId AS Class1_Id,
    ic1.ItemClassCode AS Class1_Code,
    ic1.[Description] AS Class1_Description,
    
    ic2.ClassId AS Class2_Id,
    ic2.ItemClassCode AS Class2_Code,
    ic2.[Description] AS Class2_Description,
    
    ic3.ClassId AS Class3_Id,
    ic3.ItemClassCode AS Class3_Code,
    ic3.[Description] AS Class3_Description,
    
    ic4.ClassId AS Class4_Id,
    ic4.ItemClassCode AS Class4_Code,
    ic4.[Description] AS Class4_Description,
    
    ic5.ClassId AS Class5_Id,
    ic5.ItemClassCode AS Class5_Code,
    ic5.[Description] AS Class5_Description,
    
    -- Main Supplier Information (MainAccount = 1)
    supplier.AccountCode AS SupplierAccountCode,
    supplier.AccountName AS SupplierAccountName,
    supplier.AccountType AS SupplierAccountType,
    supplier.AccountStatus AS SupplierAccountStatus,
    supplier.CreditorNumber AS SupplierCreditorNumber,
    supplier.CreditorCode AS SupplierCreditorCode,
    supplier.EANCode AS SupplierEANCode,
    supplier.SupplierPreference AS SupplierPreference,
    supplier.DeliveryTimeInDays AS SupplierDeliveryDays,
    supplier.DeliverableFromStock AS SupplierDeliverableFromStock,
    supplier.DropShip AS SupplierDropShip,
    
    -- Primary Warehouse Information
    warehouse.Warehouse AS PrimaryWarehouse,
    warehouse.WarehouseDescription AS PrimaryWarehouseDescription,
    warehouse.WarehouseBlocked AS PrimaryWarehouseBlocked,
    
    -- Additional Supplier List (JSON format for template iteration)
    (
        SELECT 
            ia2.AccountCode,
            ia2.AccountName,
            ia2.AccountType,
            ia2.AccountStatus,
            ia2.CreditorNumber,
            ia2.CreditorCode,
            ia2.EANCode,
            ia2.SupplierPreference,
            ia2.DeliveryTimeInDays,
            ia2.DeliverableFromStock,
            ia2.DropShip,
            CASE WHEN ia2.MainAccount = 1 THEN 1 ELSE 0 END AS IsDefault
        FROM (
            SELECT 
                c.cmp_code AS AccountCode, 
                TRIM(c.cmp_name) AS AccountName, 
                c.crdnr AS CreditorNumber, 
                c.crdcode AS CreditorCode, 
                c.cmp_type AS AccountType, 
                c.cmp_status AS AccountStatus, 
                ia_sub.ItemCode, 
                ia_sub.EANCode, 
                COALESCE(ia_sub.SupplierPreference, 0) AS SupplierPreference, 
                ia_sub.MainAccount,
                ia_sub.DeliveryTimeInDays,
                ia_sub.DeliverableFromStock,
                ia_sub.DropShip
            FROM dbo.ItemAccounts ia_sub
            JOIN dbo.cicmpy c ON ia_sub.crdnr = c.crdnr 
            WHERE ia_sub.ItemCode = i.ItemCode
        ) AS ia2
        FOR JSON PATH
    ) AS SuppliersJSON,
    
    -- Warehouse List (JSON format for template iteration)
    (
        SELECT 
            CASE WHEN i.Warehouse = m.Magcode THEN 1 ELSE 0 END AS IsDefault, 
            TRIM(v.magcode) AS Warehouse, 
            TRIM(m.naam) AS Description,
            m.blokkeer AS Blocked
        FROM voorrd v
        JOIN magaz m ON v.magcode = m.magcode
        WHERE v.artcode = i.ItemCode
            AND v.artcode IS NOT NULL
            AND i.IsSalesItem = 1
        FOR JSON PATH
    ) AS WarehousesJSON,
    
    -- Additional Properties
    i.ShelfLife AS ShelfLife,
    i.Warranty AS Warranty,
    i.IntrastatEnabled AS IntrastatEnabled,
    i.AddExtraReceiptToOrder AS AddExtraReceiptToOrder,
    i.IsCommissionable AS IsCommissionable,
    TRIM(i.CommissionMethod) AS CommissionMethod,
    i.CommissionValue AS CommissionValue,
    i.TaxItemClassification AS TaxItemClassification,
    
    -- Audit Metadata
    FORMAT(i.syscreated, 'yyyy-MM-dd HH:mm') AS CreatedDate,
    FORMAT(
        (SELECT MAX(ModifiedDate) 
         FROM (VALUES (i.sysmodified), (iam.sysmodified)) AS v(ModifiedDate)), 
        'yyyy-MM-dd HH:mm'
    ) AS ModifiedDate,
    REPLACE(REPLACE(CAST(i.sysguid AS VARCHAR(36)), '{', ''), '}', '') AS UUID

FROM dbo.Items i
LEFT JOIN dbo.ItemAssortment ia ON i.Assortment = ia.Assortment
LEFT JOIN dbo.ItemCountries ic ON ic.ItemCode = i.ItemCode AND ic.CountryCode IS NULL
LEFT JOIN dbo.BTWtrs vat_sales ON i.SalesVatCode = vat_sales.btwtrans

-- Item Classes/Categories (1-5)
LEFT JOIN dbo.ItemClasses ic1 ON ic1.ItemClassCode = i.Class_01 AND ic1.ClassId = 1
LEFT JOIN dbo.ItemClasses ic2 ON ic2.ItemClassCode = i.Class_02 AND ic2.ClassId = 2
LEFT JOIN dbo.ItemClasses ic3 ON ic3.ItemClassCode = i.Class_03 AND ic3.ClassId = 3
LEFT JOIN dbo.ItemClasses ic4 ON ic4.ItemClassCode = i.Class_04 AND ic4.ClassId = 4
LEFT JOIN dbo.ItemClasses ic5 ON ic5.ItemClassCode = i.Class_05 AND ic5.ClassId = 5

-- Main Supplier (MainAccount = 1)
OUTER APPLY (
    SELECT TOP 1
        c.cmp_code AS AccountCode, 
        TRIM(c.cmp_name) AS AccountName, 
        c.crdnr AS CreditorNumber, 
        c.crdcode AS CreditorCode, 
        c.cmp_type AS AccountType, 
        c.cmp_status AS AccountStatus, 
        ia_main.EANCode, 
        COALESCE(ia_main.SupplierPreference, 0) AS SupplierPreference, 
        ia_main.DeliveryTimeInDays,
        ia_main.DeliverableFromStock,
        ia_main.DropShip
    FROM dbo.ItemAccounts ia_main
    JOIN dbo.cicmpy c ON ia_main.crdnr = c.crdnr 
    WHERE ia_main.ItemCode = i.ItemCode 
        AND ia_main.MainAccount = 1
) AS supplier

-- Primary Warehouse
OUTER APPLY (
    SELECT TOP 1
        TRIM(v.magcode) AS Warehouse, 
        TRIM(m.naam) AS WarehouseDescription,
        m.blokkeer AS WarehouseBlocked
    FROM voorrd v
    JOIN magaz m ON v.magcode = m.magcode
    WHERE v.artcode = i.ItemCode
        AND v.artcode IS NOT NULL
        AND i.IsSalesItem = 1
        AND i.Warehouse = m.Magcode
) AS warehouse

-- Main ItemAccount for modification tracking
LEFT JOIN dbo.ItemAccounts iam ON iam.ItemCode = i.ItemCode AND iam.MainAccount = 1

WHERE 
    i.IsSalesItem = 1
AND i.Type NOT IN ('L', 'P')
ORDER BY i.ItemCode;
```

We'll be using Scriban templates to make this happen.

---

## Custom XML Template (Scriban)

Scriban is a powerful, lightweight templating language that transforms query results into any text format. This template generates the same complex XML structure as the original SQL function.

### Template Setup

Create a new Query Template in Reef with the following configuration:

**Template Name:** `eExact XML Template for Items`  
**Template Type:** `Custom (Scriban)`  
**Description:** Generates eExact-compliant XML output for item/product data with nested structures

### Template Code

```xml
<?xml version="1.0" ?>
<eExact xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="eExact-Schema.xsd">
  <Timestamp>{{ date.now | date.to_string '%Y-%m-%d.%H:%M' }}</Timestamp>
  
  <Items>
{{~ for row in rows ~}}
    <Item code="{{ row.ItemCode }}" type="{{ row.Type }}" searchcode="{{ row.SearchCode }}">
      <Description>{{ row.Description }}</Description>
      
      <MultiDescriptions>
        {{~ for i in 0..4 ~}}
        {{~ desc_field = "Description_" + i }}
        {{~ desc_value = row[desc_field] }}
        {{~ if desc_value != "" && desc_value != null ~}}
        <MultiDescription number="{{ i }}">{{ desc_value }}</MultiDescription>
        {{~ end ~}}
        {{~ end ~}}
      </MultiDescriptions>
      
      {{~ if row.AssortmentNumber != null ~}}
      <Assortment number="{{ row.AssortmentNumber }}" Code="{{ row.AssortmentCode }}">
        <Description>{{ row.AssortmentDescription }}</Description>
      </Assortment>
      {{~ end ~}}
      
      {{~ if row.AvailableFrom != null && row.AvailableFrom != "" ~}}
      <Availability>
        <DateStart>{{ row.AvailableFrom }}</DateStart>
      </Availability>
      {{~ end ~}}
      
      {{~ if row.Condition != null ~}}
      <Condition>{{ row.Condition }}</Condition>
      {{~ end ~}}
      
      <IsSalesItem>{{ row.IsSalesItem }}</IsSalesItem>
      <IsPurchaseItem>{{ row.IsPurchaseItem }}</IsPurchaseItem>
      <IsSerialNumberItem>{{ row.IsSerialNumberItem }}</IsSerialNumberItem>
      <IsBatchItem>{{ row.IsBatchItem }}</IsBatchItem>
      <IsSubAssemblyItem>{{ row.IsSubAssemblyItem }}</IsSubAssemblyItem>
      <IsAssembledItem>{{ row.IsAssembledItem }}</IsAssembledItem>
      <IsStockItem>{{ row.IsStockItem }}</IsStockItem>
      <IsBackOrderItem>{{ row.IsBackOrderItem }}</IsBackOrderItem>
      <IsFractionAllowedItem>{{ row.IsFractionAllowedItem }}</IsFractionAllowedItem>
      <IsPriceRegulationItem>{{ row.IsPriceRegulationItem }}</IsPriceRegulationItem>
      <IsTextItem>{{ row.IsTextItem }}</IsTextItem>
      <IsDiscountItem>{{ row.IsDiscountItem }}</IsDiscountItem>
      <IsExplodeItem>{{ row.IsExplodeItem }}</IsExplodeItem>
      <IsPrintItem>{{ row.IsPrintItem }}</IsPrintItem>
      <IsOutsourcedItem>{{ row.IsOutsourcedItem }}</IsOutsourcedItem>
      
      <Sales>
        <Price type="S">
          <Currency code="{{ row.SalesCurrency }}" />
                    <Value>{{ (row.SalesPrice | default 0) | math.format '0.000' }}</Value>
          {{~ if row.SalesVatCode != null && row.SalesVatCode != "" ~}}
          <VAT code="{{ row.SalesVatCode }}">
            <Description>{{ row.SalesVatDescription }}</Description>
            <Percentage>{{ row.SalesVatPercentage }}</Percentage>
          </VAT>
          {{~ end ~}}
        </Price>
        <Unit code="{{ row.SalesUnit }}" />
      </Sales>
      
      <Costs>
        <Price>
          <Currency code="{{ row.CostCurrency }}" />
                    <Value>{{ (row.CostPrice | default 0) | math.format '0.00000' }}</Value>
        </Price>
      </Costs>
      
      {{~ if row.NetWeight != null || row.GrossWeight != null ~}}
      <Dimension>
        {{~ if row.NetWeight != null ~}}
        <WeightNet>{{ row.NetWeight | math.format '0.00000' }}</WeightNet>
        {{~ end ~}}
        {{~ if row.GrossWeight != null ~}}
        <WeightGross>{{ row.GrossWeight | math.format '0.00000' }}</WeightGross>
        {{~ end ~}}
      </Dimension>
      {{~ end ~}}
      
      <FreeFields>
        <FreeTexts>
          {{~ for i in 1..5 ~}}
          {{~ field_name = "FreeText" + i }}
          {{~ field_value = row[field_name] }}
                    {{~ if field_value != "" && field_value != null ~}}
          <FreeText number="{{ i }}">{{ field_value }}</FreeText>
          {{~ end ~}}
          {{~ end ~}}
        </FreeTexts>
        
        <FreeDates>
          {{~ for i in 1..5 ~}}
          {{~ field_name = "FreeDate" + i }}
          {{~ field_value = row[field_name] }}
                    {{~ if field_value != "" && field_value != null ~}}
          <FreeDate number="{{ i }}">{{ field_value }}</FreeDate>
          {{~ end ~}}
          {{~ end ~}}
        </FreeDates>
        
        <FreeNumbers>
          {{~ for i in 1..5 ~}}
          {{~ field_name = "FreeNumber" + i }}
          {{~ field_value = row[field_name] }}
                    {{~ if field_value != "" && field_value != null ~}}
          <FreeNumber number="{{ i }}">{{ field_value | math.format '0.00000' }}</FreeNumber>
          {{~ end ~}}
          {{~ end ~}}
        </FreeNumbers>
        
        <FreeYesNos>
          {{~ for i in 1..5 ~}}
          {{~ field_name = "FreeYesNo" + i }}
          {{~ field_value = row[field_name] }}
                    {{~ if field_value != "" && field_value != null ~}}
          <FreeYesNo number="{{ i }}">{{ field_value }}</FreeYesNo>
          {{~ end ~}}
          {{~ end ~}}
        </FreeYesNos>
      </FreeFields>
      
      {{~ if row.Class1_Code != null && row.Class1_Code != "" ~}}
      <ItemCategory number="{{ row.Class1_Id }}" code="{{ row.Class1_Code }}">
        <Description>{{ row.Class1_Description }}</Description>
      </ItemCategory>
      {{~ end ~}}
      {{~ if row.Class2_Code != null && row.Class2_Code != "" ~}}
      <ItemCategory number="{{ row.Class2_Id }}" code="{{ row.Class2_Code }}">
        <Description>{{ row.Class2_Description }}</Description>
      </ItemCategory>
      {{~ end ~}}
      {{~ if row.Class3_Code != null && row.Class3_Code != "" ~}}
      <ItemCategory number="{{ row.Class3_Id }}" code="{{ row.Class3_Code }}">
        <Description>{{ row.Class3_Description }}</Description>
      </ItemCategory>
      {{~ end ~}}
      {{~ if row.Class4_Code != null && row.Class4_Code != "" ~}}
      <ItemCategory number="{{ row.Class4_Id }}" code="{{ row.Class4_Code }}">
        <Description>{{ row.Class4_Description }}</Description>
      </ItemCategory>
      {{~ end ~}}
      {{~ if row.Class5_Code != null && row.Class5_Code != "" ~}}
      <ItemCategory number="{{ row.Class5_Id }}" code="{{ row.Class5_Code }}">
        <Description>{{ row.Class5_Description }}</Description>
      </ItemCategory>
      {{~ end ~}}
      
            {{~ if row.SuppliersJSON != null ~}}
      {{~ suppliers = row.SuppliersJSON | parse_json }}
      {{~ for supplier in suppliers ~}}
      <ItemAccounts default="{{ supplier.IsDefault }}">
        <ItemAccount>
          <Account code="{{ supplier.AccountCode }}" type="{{ supplier.AccountType }}" status="{{ supplier.AccountStatus }}">
            <Name>{{ supplier.AccountName }}</Name>
            <Creditor number="{{ supplier.CreditorNumber }}" code="{{ supplier.CreditorCode }}" />
            <ItemCode>{{ row.ItemCode }}</ItemCode>
            <SupplierPreference>{{ supplier.SupplierPreference }}</SupplierPreference>
            {{~ if supplier.EANCode != null && supplier.EANCode != "" ~}}
            <EANCode>{{ supplier.EANCode }}</EANCode>
            {{~ end ~}}
            <Purchase>
              <Price type="P">
                <Currency code="{{ row.PurchaseCurrency }}" />
                                <Value>{{ (row.PurchasePrice | default 0) | math.format '0.000' }}</Value>
              </Price>
            </Purchase>
            <Delivery>
              <TimeInDays>{{ supplier.DeliveryTimeInDays }}</TimeInDays>
              <FromStock>{{ supplier.DeliverableFromStock }}</FromStock>
              <DropShip>{{ supplier.DropShip }}</DropShip>
            </Delivery>
          </Account>
        </ItemAccount>
      </ItemAccounts>
      {{~ end ~}}
      {{~ end ~}}
      
      {{~ if row.WarehousesJSON != null ~}}
      {{~ warehouses = row.WarehousesJSON | parse_json }}
      <ItemWarehouses>
        {{~ for warehouse in warehouses ~}}
        <ItemWarehouse default="{{ warehouse.IsDefault }}">
          <Warehouse code="{{ warehouse.Warehouse }}" blocked="{{ warehouse.Blocked }}">
            <Description>{{ warehouse.Description }}</Description>
          </Warehouse>
        </ItemWarehouse>
        {{~ end ~}}
      </ItemWarehouses>
      {{~ end ~}}
      
      {{~ if row.ShelfLife != null ~}}
      <ShelfLife>{{ row.ShelfLife }}</ShelfLife>
      {{~ end ~}}
      {{~ if row.Warranty != null ~}}
      <Warranty>{{ row.Warranty }}</Warranty>
      {{~ end ~}}
      <IsServiceItem>{{ row.IsServiceItem }}</IsServiceItem>
      {{~ if row.IntrastatEnabled != null ~}}
      <IntrastatEnabled>{{ row.IntrastatEnabled }}</IntrastatEnabled>
      {{~ end ~}}
      {{~ if row.AddExtraReceiptToOrder != null ~}}
      <AddExtraReceiptToOrder>{{ row.AddExtraReceiptToOrder }}</AddExtraReceiptToOrder>
      {{~ end ~}}
      {{~ if row.IsCommissionable != null ~}}
      <IsCommissionable>{{ row.IsCommissionable }}</IsCommissionable>
      {{~ end ~}}
      {{~ if row.CommissionMethod != null && row.CommissionMethod != "" ~}}
      <CommissionMethod>{{ row.CommissionMethod }}</CommissionMethod>
      {{~ end ~}}
      {{~ if row.CommissionValue != null ~}}
      <CommissionValue>{{ row.CommissionValue | math.format '0.00000' }}</CommissionValue>
      {{~ end ~}}
      {{~ if row.TaxItemClassification != null ~}}
      <TaxItemClassification>{{ row.TaxItemClassification }}</TaxItemClassification>
      {{~ end ~}}
      
      <Created>{{ row.CreatedDate }}</Created>
      <Modified>{{ row.ModifiedDate }}</Modified>
            <UUID>{{ row.UUID }}</UUID>
    </Item>
{{~ end ~}}
  </Items>
</eExact>
```

### Template Features

- **Dynamic Content**: `{{ date.now }}` generates current timestamp
- **Loops**: `{{~ for row in rows ~}}` iterates over all query results
- **Variable Access**: `{{ row.FieldName }}` accesses query column values
- **Conditional Rendering**: `{{~ if ... ~}}` only outputs elements when data exists
- **Filters**: `| math.format` for number formatting, `| parse_json` for JSON arrays
- **Whitespace Control**: `{{~ ... ~}}` strips surrounding whitespace for cleaner output
- **Dynamic Property Access**: `row[field_name]` for computed field names
- **Nested Loops**: Parse JSON arrays and iterate over related records (suppliers, warehouses)

### XML Structure Notes

**Important:** This template generates XML that matches the structure produced by SQL Server's `FOR XML PATH` in the original `AdvGenerateXml$Items` function:

- **ItemAccounts**: One `<ItemAccounts>` element per supplier (not wrapped in outer container)
- **ItemWarehouses**: Multiple `<ItemWarehouse>` elements wrapped in single `<ItemWarehouses>` container
- **Attributes**: `@default`, `@code`, `@type`, etc. match SQL function output
- **Namespace**: Includes `xmlns:xsi` and `xsi:noNamespaceSchemaLocation` for schema validation

This ensures compatibility with downstream systems expecting the original SQL-generated format.

---

## Configuration in Reef

### Step 1: Create the Template

1. Navigate to **Templates** in Reef
2. Click **New Template**
3. Enter template details:
   - **Name**: `eExact XML Template for Items`
   - **Type**: `Custom (Scriban)`
   - **Content**: Paste the XML template above
4. Click **Save**

### Step 2: Create the Profile

1. Navigate to **Profiles** in Reef
2. Click **New Profile**
3. Configure the profile:

#### Basic Settings
- **Name**: `Item Export - XML with Smart Sync`
- **Description**: Exports item/product data to eExact XML format with change tracking
- **Connection**: Select your database connection
- **Query**: Paste the SQL query from above

#### Output Settings
- **Output Format**: `Custom Template`
- **Template**: Select `eExact XML Template for Items`
- **File Extension**: `.xml`

#### Destination
- **Type**: Choose your destination (Local, S3, SFTP, Azure Blob, etc.)
- **Path**: Configure destination-specific settings

#### Smart Sync Configuration

Enable Smart Sync to track changes and only export modified items:

- **Enable Smart Sync**: Yes
- **ReefId Column**: `ItemCode` (must match the unique identifier in your query)
- **Hash Algorithm**: `SHA256` (recommended for security and collision resistance)
- **Duplicate Strategy**: `Strict` (ensures data integrity)
- **Null Strategy**: `Strict` (consistent NULL handling)
- **Numeric Precision**: `6` (for floating-point comparison)
- **Track Deletions**: Yes (optional - detects removed items)
- **Exclude ReefId from Output**: Yes (recommended - keeps `ItemCode` internal)

---

## Advanced: Conditional Output Techniques

### Example 1: Only Include Non-Empty Multi-Descriptions

```xml
<MultiDescriptions>
{{~ for i in 0..4 ~}}
  {{~ desc_field = "Description_" + i }}
  {{~ desc_value = row[desc_field] }}
  {{~ if desc_value != "" && desc_value != null ~}}
  <MultiDescription number="{{ i }}">{{ desc_value }}</MultiDescription>
  {{~ end ~}}
{{~ end ~}}
</MultiDescriptions>
```

### Example 2: Conditional Item Category Output

Only output category elements that have actual values:

```xml
{{~ categories = [
  { id: row.Class1_Id, code: row.Class1_Code, desc: row.Class1_Description },
  { id: row.Class2_Id, code: row.Class2_Code, desc: row.Class2_Description },
  { id: row.Class3_Id, code: row.Class3_Code, desc: row.Class3_Description },
  { id: row.Class4_Id, code: row.Class4_Code, desc: row.Class4_Description },
  { id: row.Class5_Id, code: row.Class5_Code, desc: row.Class5_Description }
] }}
{{~ for cat in categories ~}}
  {{~ if cat.code != null && cat.code != "" ~}}
<ItemCategory number="{{ cat.id }}" code="{{ cat.code }}">
  <Description>{{ cat.desc }}</Description>
</ItemCategory>
  {{~ end ~}}
{{~ end ~}}
```

### Example 3: Custom Formatting for Suppliers

Add additional logic for supplier preference indicators:

```xml
{{~ if row.SuppliersJSON != null ~}}
{{~ suppliers = row.SuppliersJSON | parse_json }}
{{~ suppliers_sorted = suppliers | array.sort "SupplierPreference" | array.reverse }}
{{~ for supplier in suppliers_sorted ~}}
<ItemAccounts default="{{ supplier.IsDefault }}" preferred="{{ supplier.SupplierPreference > 0 }}">
  <!-- Rest of supplier XML -->
</ItemAccounts>
{{~ end ~}}
{{~ end ~}}
```

---

## Advanced Output Options

Reef provides fine-grained control over what appears in your exports through Advanced Output Options.

### Exclude Sensitive or Internal Fields

```json
{
  "excludeFields": ["FreeText5", "FreeNumber5", "CommissionValue"]
}
```

### Custom Field Mapping

Rename fields in the output without changing SQL or template:

```json
{
  "fieldMappings": {
    "ItemCode": "ProductCode",
    "Description": "ProductName",
    "SalesPrice": "Price"
  }
}
```

### Value Transformations

Apply transformations to specific fields:

```json
{
  "valueTransformations": {
    "SalesPrice": "{{ value * 1.21 }}",
    "Description": "{{ value | string.upcase }}"
  }
}
```

---

## Performance Tips

### 1. Use Indexes

Ensure proper indexes on:
- `Items.ItemCode` (primary key)
- `Items.IsSalesItem, Items.Type` (filter columns)
- `ItemAccounts.ItemCode, ItemAccounts.MainAccount`
- `voorrd.artcode`

### 2. Filter Early

Add WHERE clauses to reduce data:

```sql
WHERE 
    i.IsSalesItem = 1
    AND i.Type NOT IN ('L', 'P')
    AND i.sysmodified >= DATEADD(day, -7, GETDATE())  -- Only last 7 days
```

### 3. Use Smart Sync

Enable Smart Sync to export only changed items, dramatically reducing:
- Database load
- Network transfer
- Processing time
- Destination storage

### 4. Batch Processing

For very large catalogs, consider:
- Running exports during off-peak hours
- Breaking into smaller batches by category or date range
- Using pagination in the query

---

## Troubleshooting

### Issue: JSON Arrays Not Parsing

**Symptom**: Suppliers or warehouses not appearing in output

**Solution**: Ensure SQL Server version supports `FOR JSON PATH` (2016+) and check for NULL values:

```sql
-- Add ISNULL wrapper
ISNULL(
    (SELECT ... FOR JSON PATH),
    '[]'
) AS SuppliersJSON
```

### Issue: Number Formatting Errors

**Symptom**: Numbers appearing with wrong decimal places

**Solution**: Use consistent formatting in template:

```xml
<!-- For prices (3 decimals) -->
{{ row.SalesPrice | math.format '0.000' }}

<!-- For costs/weights (5 decimals) -->
{{ row.CostPrice | math.format '0.00000' }}
```

### Issue: Special Characters in XML

**Symptom**: XML parsing errors due to special characters (&, <, >)

**Solution**: Scriban automatically escapes XML. For raw output use triple braces:

```xml
<!-- Auto-escaped -->
<Description>{{ row.Description }}</Description>

<!-- Raw (use cautiously) -->
<Description>{{{ row.Description }}}</Description>
```

---


### Query Design Principles

1. **One Row Per Entity**: Each row represents a complete entity with all related data
2. **Flatten Relationships**: Use `OUTER APPLY` and `LEFT JOIN` to bring related data into the main row
3. **Consistent Naming**: Use clear column aliases that map directly to template variables
4. **NULL Handling**: Use `COALESCE` to provide default values and avoid template errors
5. **Date Formatting**: Format dates in SQL for consistent output across all destinations
6. **Stable Ordering**: Always include `ORDER BY` for predictable results

---

## Custom XML Template (Scriban)

Scriban is a powerful, lightweight templating language that transforms query results into any text format. This example shows how to generate a complex XML structure.

### Template Setup

Create a new Query Template in Reef with the following configuration:

**Template Name:** `eExact XML Template for Debtors`  
**Template Type:** `Custom (Scriban)`  
**Description:** Generates eExact-compliant XML output for debtor/customer data with nested custom field

### Template Features

- **Dynamic Content**: `{{ date.now }}` generates current timestamp
- **Loops**: `{{~ for row in rows ~}}` iterates over all query results
- **Variable Access**: `{{ row.FieldName }}` accesses query column values
- **Filters**: `| math.format` and `| date.to_string` for formatting
- **Whitespace Control**: `{{~ ... ~}}` strips surrounding whitespace for cleaner output

---

## Configuration in Reef

### Step 1: Create the Template

1. Navigate to **Templates** in Reef
2. Click **New Template**
3. Enter template details:
   - **Name**: `eExact XML Template for Debtors`
   - **Type**: `Custom (Scriban)`
   - **Content**: Paste the XML template above
4. Click **Save**

### Step 2: Create the Profile

1. Navigate to **Profiles** in Reef
2. Click **New Profile**
3. Configure the profile:

#### Basic Settings
- **Name**: `Customer Export - XML with Smart Sync`
- **Description**: Exports customer data to eExact XML format with change tracking
- **Connection**: Select your database connection
- **Query**: Paste the SQL query from above

#### Output Settings
- **Output Format**: `Custom Template`
- **Template**: Select `eExact XML Template for Debtors`
- **File Extension**: `.xml`

#### Destination
- **Type**: Choose your destination (Local, S3, SFTP, Azure Blob, etc.)
- **Path**: Configure destination-specific settings

#### Smart Sync Configuration

Enable Smart Sync to track changes and only export modified records:

- **Enable Smart Sync**: Yes
- **ReefId Column**: `EntityCode` (must match the unique identifier in your query)
- **Hash Algorithm**: `SHA256` (recommended for security and collision resistance)
- **Duplicate Strategy**: `Strict` (ensures data integrity)
- **Null Strategy**: `Strict` (consistent NULL handling)
- **Numeric Precision**: `6` (for floating-point comparison)
- **Track Deletions**: Yes (optional - detects removed records)
- **Exclude ReefId from Output**: Yes (recommended - keeps `EntityCode` internal)

This pattern offers simple, maintainable SQL queries with clear data flow and easy debugging. It improves performance by reducing database overhead and network transfer, especially with Smart Sync for incremental exports. The approach is flexible, allowing easy changes to output formats and fields, and ensures data integrity through consistent hashing and duplicate prevention. Compared to traditional complex SQL, it requires fewer table scans, is much faster, and is easier to optimize and maintain.

## Advanced: Conditional Output with Scriban

### Example 1: Only Include Non-Empty Fields

Instead of always outputting all 10 text fields, conditionally include only those with values:

```xml
<FreeTexts>
{{~ for i in 1..10 ~}}
  {{~ field_name = "FreeText" + i ~}}
  {{~ field_value = row[field_name] ~}}
  {{~ if field_value != "" && field_value != null ~}}
  <FreeText number="{{ i }}">{{ field_value }}</FreeText>
  {{~ end ~}}
{{~ end ~}}
</FreeTexts>
```

This produces cleaner, more compact XML by omitting empty elements.

### Example 2: Conditional Sections

Only include entire sections if they contain data:

```xml
{{~ has_free_fields = false ~}}
{{~ for i in 1..10 ~}}
  {{~ if row["FreeText" + i] != "" ~}}
    {{~ has_free_fields = true ~}}
    {{~ break ~}}
  {{~ end ~}}
{{~ end ~}}

{{~ if has_free_fields ~}}
<FreeFields>
  <!-- ... output fields ... -->
</FreeFields>
{{~ end ~}}
```

### Example 3: Dynamic Formatting

Apply different formatting based on data values:

```xml
<Status class="{{ if row.Status == 'Active' }}active{{ else }}inactive{{ end }}">
  {{ row.Status }}
</Status>

<Amount>
  {{ if row.Amount >= 1000 }}
    {{ row.Amount | math.format '#,##0.00' }}
  {{ else }}
    {{ row.Amount | math.format '0.00' }}
  {{ end }}
</Amount>
```

---

## Alternative Output Formats

The same query can drive multiple output formats by using different templates:

### JSON Template

```json
{
  "timestamp": "{{ date.now | date.to_string '%Y-%m-%dT%H:%M:%SZ' }}",
  "accounts": [
{{~ for row in rows ~}}
    {
      "code": "{{ row.Code }}",
      "status": "{{ row.Status }}",
      "type": "{{ row.Type }}",
      "name": "{{ row.Name }}",
      "customFields": {
        "text": [
          {{~ for i in 1..10 ~}}
          "{{ row["FreeText" + i] }}"{{ if i < 10 }},{{ end }}
          {{~ end ~}}
        ],
        "numbers": [
          {{~ for i in 1..5 ~}}
          {{ row["FreeNumber" + i] }}{{ if i < 5 }},{{ end }}
          {{~ end ~}}
        ]
      },
      "audit": {
        "created": "{{ row.CreatedDate }}",
        "modified": "{{ row.ModifiedDate }}",
        "uuid": "{{ row.UUID }}"
      }
    }{{ if !for.last }},{{ end }}
{{~ end ~}}
  ]
}
```

### CSV Template (with Headers)

```csv
Code,Status,Type,Name,Created,Modified,UUID
{{~ for row in rows ~}}
"{{ row.Code }}","{{ row.Status }}","{{ row.Type }}","{{ row.Name }}","{{ row.CreatedDate }}","{{ row.ModifiedDate }}","{{ row.UUID }}"
{{~ end ~}}
```

**Questions or Issues?**  
Open an issue in the repository or consult the Reef documentation for more examples and troubleshooting tips.
