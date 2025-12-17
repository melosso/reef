A warehouse picking list PDF with ship-to address, SKU locations, quantities, and signature fields using DocumentTemplate format.

# Document Template - Picklist PDF

This DocumentTemplate generates a PDF picking list for warehouse operations. The template shows which items to pick, their locations, and provides checkboxes for picker verification.

## Example SQL

### With Database Tables

```sql
SELECT 
    ord.OrderNumber AS order_number,
    GETDATE() AS pick_date,
    cust.Name AS customer_name,
    cust.ShipAddress AS ship_address,
    li.SKU AS sku,
    li.Description AS description,
    inv.Location AS warehouse_location,
    li.Quantity AS quantity
FROM Orders ord
JOIN Customers cust ON ord.CustomerId = cust.Id
JOIN OrderLines li ON ord.Id = li.OrderId
JOIN Inventory inv ON li.SKU = inv.SKU
WHERE ord.Id = @OrderId
ORDER BY inv.Location, li.SKU;
```

### Static Values (MySQL - No Tables Required)

```sql
SELECT
    -- Order header (repeated on every line)
    'ORD-2025-1234' AS order_number,
    DATE('2025-12-17') AS pick_date,
    'Acme Industries Ltd.' AS customer_name,
    '456 Client Ave, Building B, New York, NY 10001' AS ship_address,
    
    -- Line 1
    'SKU-001' AS sku,
    'Widget Assembly Kit' AS description,
    'A12-3' AS warehouse_location,
    5 AS quantity

UNION ALL

SELECT
    'ORD-2025-1234',
    DATE('2025-12-17'),
    'Acme Industries Ltd.',
    '456 Client Ave, Building B, New York, NY 10001',
    
    -- Line 2
    'SKU-042',
    'Mounting Bracket (Large)',
    'B05-1',
    10

UNION ALL

SELECT
    'ORD-2025-1234',
    DATE('2025-12-17'),
    'Acme Industries Ltd.',
    '456 Client Ave, Building B, New York, NY 10001',
    
    -- Line 3
    'SKU-128',
    'Instruction Manual - EN',
    'C01-7',
    5;
```

## Template Content

The template uses DocumentTemplate syntax with directives and sections. Copy the content from `Document_Template_-_Picklist_PDF.DocumentTemplate.txt` into the Template Content field when creating a new template in the Templates page.

## Usage

1. Navigate to **Templates** page
2. Click **Create Template**
3. Set **Type**: `DocumentTemplate`
4. Set **Output Format**: `PDF`
5. Set **Name**: `Warehouse Picklist`
6. Paste template content from `.txt` file
7. Click **Save**

Then create a profile using your picking query and select this template. The PDF will show all items with checkboxes for warehouse staff to mark as picked.
