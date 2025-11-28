# XML Export with Custom Templates (+ Smart Sync)

## Overview

This example demonstrates how to export relational database data to a custom XML format using Reef's templating engine and Smart Sync capabilities. This pattern is ideal for exporting customer/debtor data from an Exact Globe+ database to their standardized XML format that includes:

---

## SQL Query Pattern

### Data Source

This query demonstrates the single-row-per-entity pattern, which simplifies template rendering and improves performance.

```sql
-- Query: Customer/Debtor Export with All Fields
-- ReefId Column: EntityCode (primary identifier for Smart Sync tracking)
-- Output Format: Custom Template (Scriban)

SELECT 
    c.debcode AS EntityCode,           -- Primary identifier (used for Smart Sync)
    c.cmp_code AS Code,                 -- External/display code
    c.cmp_status AS Status,             -- Entity status (Active, Inactive, etc.)
    c.cmp_type AS Type,                 -- Entity classification
    TRIM(c.cmp_name) AS Name,           -- Entity name
    
    -- Custom/Dynamic Fields - Text
    -- Pattern: Flatten dynamic fields into individual columns for easy templating
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
    
    -- Custom/Dynamic Fields - Numeric
    COALESCE(c.NumberField1, 0) AS FreeNumber1,
    COALESCE(c.NumberField2, 0) AS FreeNumber2,
    COALESCE(c.NumberField3, 0) AS FreeNumber3,
    COALESCE(c.NumberField4, 0) AS FreeNumber4,
    COALESCE(c.NumberField5, 0) AS FreeNumber5,
    
    -- Custom/Dynamic Fields - Boolean
    COALESCE(c.YesNoField1, 0) AS FreeYesNo1,
    COALESCE(c.YesNoField2, 0) AS FreeYesNo2,
    COALESCE(c.YesNoField3, 0) AS FreeYesNo3,
    COALESCE(c.YesNoField4, 0) AS FreeYesNo4,
    COALESCE(c.YesNoField5, 0) AS FreeYesNo5,
    
    -- Audit Metadata
    FORMAT(c.syscreated, 'yyyy-MM-dd HH:mm') AS CreatedDate,
    FORMAT(
        (SELECT MAX(ModifiedDate) 
         FROM (VALUES (c.sysmodified), (cp.LastModifiedDate)) AS v(ModifiedDate)), 
        'yyyy-MM-dd HH:mm'
    ) AS ModifiedDate,
    
    -- UUID for integration tracking
    REPLACE(REPLACE(CAST(c.sysguid AS VARCHAR(36)), '{', ''), '}', '') AS UUID
    
FROM dbo.cicmpy c 
OUTER APPLY (
    -- Example: Include modification dates from related tables
    SELECT MAX(sysmodified) AS LastModifiedDate 
    FROM cicntp cp 
    WHERE cp.cmp_wwn = c.cmp_wwn
) AS cp
LEFT JOIN dbo.BTWtrs vat_sales ON c.VatCode = vat_sales.btwtrans
WHERE 
    c.debcode IS NOT NULL        -- Filter to only entities with valid identifiers
ORDER BY c.debcode;
```

We'll be using Scriban templates to make this happen.

---

## Custom XML Template (Scriban)

Scriban is a powerful, lightweight templating language that transforms query results into any text format. This example shows how to generate a complex XML structure.

### Template Setup

Create a new Query Template in Reef with the following configuration:

**Template Name:** `eExact XML Template for Debtors`  
**Template Type:** `Custom (Scriban)`  
**Description:** Generates eExact-compliant XML output for debtor/customer data with nested custom fields

### Template Code

```xml
<?xml version="1.0" ?>
<eExact xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="eExact-Schema.xsd">
  <!-- Dynamic timestamp showing when the export was generated -->
  <Timestamp>{{ date.now | date.to_string '%Y-%m-%d.%H:%M' }}</Timestamp>
  
  <Accounts>
{{~ for row in rows ~}}
    <!-- Each row from the query becomes an Account element -->
    <Account code="{{ row.Code }}" status="{{ row.Status }}" type="{{ row.Type }}">
      <Name>{{ row.Name }}</Name>
      
      <!-- Nested structure for custom fields -->
      <FreeFields>
        <FreeTexts>
          <FreeText number="1">{{ row.FreeText1 }}</FreeText>
          <FreeText number="2">{{ row.FreeText2 }}</FreeText>
          <FreeText number="3">{{ row.FreeText3 }}</FreeText>
          <FreeText number="4">{{ row.FreeText4 }}</FreeText>
          <FreeText number="5">{{ row.FreeText5 }}</FreeText>
          <FreeText number="6">{{ row.FreeText6 }}</FreeText>
          <FreeText number="7">{{ row.FreeText7 }}</FreeText>
          <FreeText number="8">{{ row.FreeText8 }}</FreeText>
          <FreeText number="9">{{ row.FreeText9 }}</FreeText>
          <FreeText number="10">{{ row.FreeText10 }}</FreeText>
        </FreeTexts>
        
        <FreeNumbers>
          <!-- Format numbers with specific precision -->
          <FreeNumber number="1">{{ row.FreeNumber1 | math.format '0.00000' }}</FreeNumber>
          <FreeNumber number="2">{{ row.FreeNumber2 | math.format '0.00000' }}</FreeNumber>
          <FreeNumber number="3">{{ row.FreeNumber3 | math.format '0.00000' }}</FreeNumber>
          <FreeNumber number="4">{{ row.FreeNumber4 | math.format '0.00000' }}</FreeNumber>
          <FreeNumber number="5">{{ row.FreeNumber5 | math.format '0.00000' }}</FreeNumber>
        </FreeNumbers>
        
        <FreeYesNos>
          <FreeYesNo number="1">{{ row.FreeYesNo1 }}</FreeYesNo>
          <FreeYesNo number="2">{{ row.FreeYesNo2 }}</FreeYesNo>
          <FreeYesNo number="3">{{ row.FreeYesNo3 }}</FreeYesNo>
          <FreeYesNo number="4">{{ row.FreeYesNo4 }}</FreeYesNo>
          <FreeYesNo number="5">{{ row.FreeYesNo5 }}</FreeYesNo>
        </FreeYesNos>
      </FreeFields>
      
      <!-- Audit metadata -->
      <CreatedDate>{{ row.CreatedDate }}</CreatedDate>
      <ModifiedDate>{{ row.ModifiedDate }}</ModifiedDate>
      <UUID>{{ row.UUID }}</UUID>
    </Account>
{{~ end ~}}
  </Accounts>
</eExact>
```

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
