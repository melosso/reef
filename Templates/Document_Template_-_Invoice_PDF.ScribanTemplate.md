# Invoice Document Template (PDF)

This template demonstrates how to create a PDF invoice using the Document Template feature in Reef.

## Features Demonstrated

- **Page Setup**: A4 size, portrait orientation
- **Header Section**: Company information (repeats on every page)
- **Content Section**: Invoice details with table of line items
- **Footer Section**: Terms and conditions
- **Page Numbering**: Automatic page numbers
- **Scriban Data Binding**: Dynamic data from query results

## Template Usage

1. Create a profile with a query that returns invoice data
2. Select **Document Template** as the template type
3. Set **Output Format** to `PDF`
4. Use this template content

## Query Requirements

Your query should return the following columns:
- Invoice header: `invoice_number`, `invoice_date`, `customer_name`, `customer_address`
- Company info: `company_name`, `company_address`, `company_phone`, `company_terms`
- Line items: `item_description`, `quantity`, `unit_price`, `line_total`
- Summary: `subtotal`, `tax_amount`, `total_amount`

## Example Query

```sql
SELECT 
    -- Invoice header
    inv.InvoiceNumber AS invoice_number,
    inv.InvoiceDate AS invoice_date,
    cust.Name AS customer_name,
    cust.Address AS customer_address,
    
    -- Company info (same for all rows)
    'ACME Corporation' AS company_name,
    '123 Business St, Suite 100' AS company_address,
    '(555) 123-4567' AS company_phone,
    'Payment due within 30 days. Late fees may apply.' AS company_terms,
    
    -- Line items
    li.Description AS item_description,
    li.Quantity AS quantity,
    li.UnitPrice AS unit_price,
    li.Quantity * li.UnitPrice AS line_total,
    
    -- Summary
    inv.Subtotal AS subtotal,
    inv.TaxAmount AS tax_amount,
    inv.TotalAmount AS total_amount
FROM Invoices inv
JOIN Customers cust ON inv.CustomerId = cust.Id
JOIN InvoiceLines li ON inv.Id = li.InvoiceId
WHERE inv.Id = @InvoiceId
ORDER BY li.LineNumber
```

## Template Content

Copy the template below into the Template Content field:
