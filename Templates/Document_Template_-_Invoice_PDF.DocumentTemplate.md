A professional PDF invoice template with company header, itemized line items table, and payment terms footer using DocumentTemplate format.

# Document Template - Invoice PDF

## Overview

This DocumentTemplate generates a multi-page PDF invoice with automatic page numbering. The template combines Scriban data binding with HTML layout markup, which gets converted to PDF using QuestPDF at runtime.

**Key Features:**
- A4 portrait layout with company header on every page
- Itemized table that flows across multiple pages if needed
- Subtotal, tax, and total calculations
- Terms and conditions footer
- Automatic page numbers

## SQL Output Structure

Your query must return one row per invoice line item with the following columns:

**Invoice Header** (same value on all rows for same invoice):
- `invoice_number` – Invoice ID
- `invoice_date` – Invoice date
- `customer_name` – Customer/client name
- `customer_address` – Billing address

**Company Information** (same on all rows):
- `company_name` – Your company name
- `company_address` – Your company address
- `company_phone` – Contact phone number
- `company_terms` – Payment terms text

**Line Items** (varies per row):
- `item_description` – Product/service description
- `quantity` – Quantity ordered
- `unit_price` – Price per unit
- `line_total` – Extended line total

**Invoice Totals** (same on all rows):
- `subtotal` – Sum before tax
- `tax_amount` – Total tax
- `total_amount` – Grand total

## Example SQL

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
ORDER BY li.LineNumber;
```

## Template Content

The template uses DocumentTemplate syntax with directives and sections. Copy the content from `Document_Template_-_Invoice_PDF.DocumentTemplate.txt` into the Template Content field when creating a new template in the Templates page.

## Usage

1. Navigate to **Templates** page
2. Click **Create Template**
3. Set **Type**: `DocumentTemplate`
4. Set **Output Format**: `PDF`
5. Set **Name**: `Invoice - Professional`
6. Paste template content from `.txt` file
7. Click **Save**

Then create a profile using your invoice query and select this template. The PDF will automatically handle multi-page invoices with repeated headers and footers.
