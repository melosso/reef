A complete email export example that generates personalized PDF invoices and emails them to customers using document templates and split functionality.

# Email with PDF Invoice Attachment - Documentation

This example shows how to combine email export with document generation to send individual PDF invoices to customers.

## Overview

The system combines three components:

1. **SQL** returns invoice data with one row per line item, grouped by customer email
2. **Split Key Column** groups rows by customer email, creating one email per customer
3. **Document Template** generates a PDF invoice from the grouped data
4. **Email Template** creates the HTML email body
5. **Execution Service** attaches the PDF to each email and sends

## How It Works

- Query returns multiple rows (all invoice line items for all customers)
- SplitKeyColumn (`customer_email`) groups rows by customer
- For each customer group:
  - Document template generates a PDF with all their line items
  - Email template creates the HTML email body
  - PDF is attached to the email
  - Email is sent to that customer only

---

## Quick Start - Static Example

Test the feature without any database tables:

```sql
SELECT 
    -- Email recipient
    'customer@example.com' AS customer_email,
    
    -- Invoice header
    'INV-2025-001' AS invoice_number,
    '2025-01-15' AS invoice_date,
    '2025-02-14' AS due_date,
    
    -- Customer details
    'Acme Industries Ltd.' AS customer_name,
    '456 Client Avenue, Building B, New York, NY 10001' AS billing_address,
    '555-0199' AS phone_number,
    
    -- Company details
    'ACME Corporation' AS company_name,
    '123 Business Street, Suite 100, Los Angeles, CA 90001' AS company_address,
    'support@acme.com' AS company_email,
    '555-0100' AS company_phone,
    
    -- Line item 1
    'Professional Services - January 2025' AS item_description,
    40.00 AS quantity,
    125.00 AS unit_price,
    500.00 AS tax_amount,
    5500.00 AS line_total
    
UNION ALL

SELECT 
    'customer@example.com',
    'INV-2025-001',
    '2025-01-15',
    '2025-02-14',
    'Acme Industries Ltd.',
    '456 Client Avenue, Building B, New York, NY 10001',
    '555-0199',
    'ACME Corporation',
    '123 Business Street, Suite 100, Los Angeles, CA 90001',
    'support@acme.com',
    '555-0100',
    
    -- Line item 2
    'Cloud Hosting - Monthly Subscription',
    1.00,
    299.00,
    29.90,
    328.90
    
UNION ALL

SELECT 
    'customer@example.com',
    'INV-2025-001',
    '2025-01-15',
    '2025-02-14',
    'Acme Industries Ltd.',
    '456 Client Avenue, Building B, New York, NY 10001',
    '555-0199',
    'ACME Corporation',
    '123 Business Street, Suite 100, Los Angeles, CA 90001',
    'support@acme.com',
    '555-0100',
    
    -- Line item 3
    'Software License - Enterprise (Annual)',
    5.00,
    499.00,
    249.50,
    2744.50

-- Add calculated totals (same across all rows for this invoice)
-- Note: In production, use window functions or join to a totals CTE
-- For this example, we'll calculate in the PDF template from line items
```

**Result**: One email to `customer@example.com` with a PDF invoice containing 3 line items.

---

## Production Query Example

For use with actual database tables:

```sql
SELECT 
    -- Email recipient (used as SplitKeyColumn)
    c.Email AS customer_email,
    
    -- Invoice header data
    i.InvoiceNumber AS invoice_number,
    i.InvoiceDate AS invoice_date,
    i.DueDate AS due_date,
    
    -- Customer information
    c.CustomerName AS customer_name,
    c.BillingAddress AS billing_address,
    c.PhoneNumber AS phone_number,
    
    -- Company information
    'ACME Corporation' AS company_name,
    '123 Business Street, Suite 100' AS company_address,
    'support@acme.com' AS company_email,
    '555-0100' AS company_phone,
    
    -- Line item details
    li.ItemDescription AS item_description,
    li.Quantity AS quantity,
    li.UnitPrice AS unit_price,
    li.LineTotal AS line_total,
    li.TaxAmount AS tax_amount,
    
    -- Invoice totals (same for all rows of same invoice)
    i.Subtotal AS subtotal,
    i.TotalTax AS total_tax,
    i.TotalAmount AS total_amount,
    i.AmountPaid AS amount_paid,
    i.AmountDue AS amount_due,
    i.PaymentTerms AS payment_terms,
    i.Notes AS notes
    
FROM Invoices i
JOIN Customers c ON i.CustomerId = c.Id
JOIN InvoiceLines li ON i.Id = li.InvoiceId
WHERE i.InvoiceDate >= DATEADD(day, -7, GETDATE())
  AND i.Status = 'Ready to Send'
ORDER BY c.Email, li.LineNumber
```

---

## Profile Setup

### Step 1: Create Document Template (PDF Invoice)
1. Navigate to **Templates** page
2. Click **Create Template**
3. Set **Type**: `DocumentTemplate`
4. Set **Output Format**: `PDF`
5. Set **Name**: `Invoice - Customer PDF`
6. In **Template Content** textarea, paste the template code:
   - This is a TEXT-based template with special syntax (see `Document_Template_-_Invoice_PDF.DocumentTemplate.txt` for reference)
   - Contains directives like `{{! format: pdf }}` for metadata
   - Contains sections like `{{# header }}...{{/ header }}`, `{{# content }}...{{/ content }}`
   - Uses HTML markup for layout (converted to PDF by QuestPDF)
   - Uses Scriban expressions for data binding: `{{ customer_name }}`, loops, etc.
7. Click **Save**

### Step 2: Create Email Body Template (HTML)
1. Click **Create Template** again
2. Set **Type**: `ScribanTemplate`
3. Set **Output Format**: `HTML`
4. Set **Name**: `Email - Invoice Notification`
5. In **Template Content** textarea, paste the content from `Email_-_Invoice_with_PDF_Attachment.ScribanTemplate.html`
6. Click **Save**

### Step 3: Create Email Export Profile
1. Navigate to **Profiles** page
2. Click **Create Profile**
3. Configure **General** tab:
   - Name: `Send Customer Invoices`
   - Connection: Select your database
   - Query: Paste query from above
   - **Enable Email Export**: ✓
4. Configure **Email** tab:
   - Email Body Template: Select `Email - Invoice Notification`
   - Recipients Column: `customer_email`
   - Subject Hardcoded: `Your Invoice #{{invoice_number}} from {{company_name}}`
5. Configure **Attachments**:
   - **Enable Email Attachments**: ✓
   - **Document Template**: Select `Invoice - Customer PDF`
   - **Filename Column**: Create calculated column or hardcode `Invoice_{{invoice_number}}.pdf`
   - **Page Size**: A4
   - **Orientation**: Portrait
6. Configure **Split Output**:
   - **Enable Splitting**: ✓
   - **Split Key Column**: `customer_email`
7. Save profile

### Step 4: Execute Profile
- Click **Execute** on the profile
- Each customer receives one email with their PDF invoice attached

## Notes

- **DocumentTemplate Explained**: 
  - DocumentTemplate is a **text-based template format** (not a binary PDF/DOCX file)
  - It uses special syntax: `{{! format: pdf }}` for directives, `{{# content }}...{{/ content }}` for sections
  - Combines HTML markup (for layout) with Scriban expressions (for data binding)
  - Gets parsed and **converted to actual PDF/DOCX** files by QuestPDF/OpenXML at runtime
  
- **Template Files**: This documentation references two template files:
  - `Email_-_Invoice_with_PDF_Attachment.ScribanTemplate.html` - Email body template (ScribanTemplate type, HTML output)
  - `Document_Template_-_Invoice_PDF.DocumentTemplate.txt` - PDF document template (DocumentTemplate type, PDF output)
  
- **How to Use Templates**: 
  1. Open the template file (`.html` or `.txt`) in a text editor
  2. Copy the entire text content
  3. In Templates page UI, paste into "Template Content" textarea
  4. Save - the system parses the template text and generates PDFs/DOCX at execution time
  
- **Totals Calculation**: The static example repeats totals across rows. In production, use window functions or CTEs to calculate once per invoice.
- **Multiple Attachments**: You can also include binary attachments from JSON columns alongside the generated PDF.
- **Watermarks**: Add watermark text in document options (e.g., "DRAFT", "PAID", "OVERDUE").
- **Custom Fonts**: PDF generation supports standard embedded fonts.
