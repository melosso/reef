An invoice approval workflow example that sends multiple PDF invoices to approvers for review, demonstrating the per-row document generation feature.

# Invoice Approval Process - Documentation

This example demonstrates sending invoice approval requests to finance managers, with each approver receiving multiple invoice PDFs attached to a single email.

## Overview

This workflow combines:

1. **SQL Query** returns invoice data with multiple invoices per approver
2. **Split Key Column** (`approver_email`) groups rows by approver
3. **Email Group By Split Key** enabled to send one email per approver
4. **Generate Per Row** enabled to create one PDF per invoice
5. **Document Template** generates individual invoice PDFs
6. **Email Template** creates the approval request HTML

## How It Works

- Query returns multiple rows (one row per invoice)
- Each row contains complete invoice details plus approver information
- SplitKeyColumn (`approver_email`) groups invoices by approver
- For each approver group:
  - Multiple PDFs are generated (one per invoice due to GeneratePerRow=true)
  - Email template creates the HTML approval request
  - All PDFs are attached to one email
  - Email is sent to that approver only

## Key Configuration

**Profile Settings:**
- **Type**: Email Export
- **Split Key Column**: `approver_email`
- **Email Group By Split Key**: ✓ Enabled
- **Email Template**: This template (Invoice Approval Request)

**Attachment Configuration:**
- **Mode**: DocumentTemplate
- **Template**: Invoice Approval PDF
- **Generate Per Row**: ✓ Enabled (creates one PDF per invoice)
- **Filename Column**: `invoice_filename`

---

## Static Query Example (MySQL - No Tables Required)

Test the approval workflow with 2 approvers and 5 total invoices:

```sql
-- Approver 1: John Smith - 2 invoices
SELECT
    -- Approver details (split key)
    'john.smith@company.com' AS approver_email,
    'John Smith' AS approver_name,
    'Finance Manager' AS approver_title,
    
    -- Invoice header
    'INV-2025-001' AS invoice_number,
    'Invoice_INV-2025-001.pdf' AS invoice_filename,
    DATE('2025-01-15') AS invoice_date,
    DATE('2025-02-14') AS due_date,
    'Pending Approval' AS invoice_status,
    
    -- Vendor details
    'Tech Solutions Inc.' AS vendor_name,
    '123 Vendor Street, Tech City, CA 90210' AS vendor_address,
    'vendor@techsolutions.com' AS vendor_email,
    '555-0123' AS vendor_phone,
    'Bank: First National Bank, Account: ****1234' AS payment_details,
    
    -- Company details
    'ACME Corporation' AS company_name,
    '456 Corporate Blvd, Business City, NY 10001' AS company_address,
    'AP Department' AS department,
    'Purchase Order #PO-2025-045' AS reference,
    
    -- Financial details
    4250.00 AS subtotal,
    382.50 AS tax_amount,
    4632.50 AS total_amount,
    'NET 30' AS payment_terms,
    'Monthly Software License - January 2025' AS description,
    'Software & IT Services' AS category,
    1 AS priority

UNION ALL

SELECT
    'john.smith@company.com',
    'John Smith',
    'Finance Manager',
    'INV-2025-002',
    'Invoice_INV-2025-002.pdf',
    DATE('2025-01-16'),
    DATE('2025-02-15'),
    'Pending Approval',
    'Office Supplies Plus' AS vendor_name,
    '789 Supply Ave, Office Park, TX 75001' AS vendor_address,
    'orders@officesupplies.com' AS vendor_email,
    '555-0456' AS vendor_phone,
    'Bank: Commerce Bank, Account: ****5678' AS payment_details,
    'ACME Corporation',
    '456 Corporate Blvd, Business City, NY 10001',
    'Operations',
    'Purchase Order #PO-2025-078',
    892.50 AS subtotal,
    71.40 AS tax_amount,
    963.90 AS total_amount,
    'NET 15',
    'Office Furniture & Equipment',
    'Office Supplies',
    2

UNION ALL

-- Approver 2: Sarah Johnson - 3 invoices
SELECT
    'sarah.johnson@company.com' AS approver_email,
    'Sarah Johnson' AS approver_name,
    'VP Finance' AS approver_title,
    'INV-2025-003',
    'Invoice_INV-2025-003.pdf',
    DATE('2025-01-17'),
    DATE('2025-02-16'),
    'Pending Approval',
    'Cloud Services LLC' AS vendor_name,
    '321 Cloud Drive, Data Center, WA 98001' AS vendor_address,
    'billing@cloudservices.com' AS vendor_email,
    '555-0789' AS vendor_phone,
    'Bank: Tech Credit Union, Account: ****9012' AS payment_details,
    'ACME Corporation',
    '456 Corporate Blvd, Business City, NY 10001',
    'IT Department',
    'Contract #CSA-2025-Q1',
    12500.00 AS subtotal,
    1125.00 AS tax_amount,
    13625.00 AS total_amount,
    'NET 45',
    'Q1 2025 Cloud Infrastructure Hosting',
    'IT Infrastructure',
    1

UNION ALL

SELECT
    'sarah.johnson@company.com',
    'Sarah Johnson',
    'VP Finance',
    'INV-2025-004',
    'Invoice_INV-2025-004.pdf',
    DATE('2025-01-18'),
    DATE('2025-03-04'),
    'Pending Approval',
    'Legal Partners LLP' AS vendor_name,
    '555 Law Street, Legal District, IL 60601' AS vendor_address,
    'billing@legalpartners.com' AS vendor_email,
    '555-0321' AS vendor_phone,
    'Wire Transfer Details Provided Separately' AS payment_details,
    'ACME Corporation',
    '456 Corporate Blvd, Business City, NY 10001',
    'Legal',
    'Matter #2025-LP-012',
    7850.00 AS subtotal,
    0.00 AS tax_amount,
    7850.00 AS total_amount,
    'NET 30',
    'Corporate Legal Services - January 2025',
    'Professional Services',
    1

UNION ALL

SELECT
    'sarah.johnson@company.com',
    'Sarah Johnson',
    'VP Finance',
    'INV-2025-005',
    'Invoice_INV-2025-005.pdf',
    DATE('2025-01-19'),
    DATE('2025-02-18'),
    'Pending Approval',
    'Marketing Agency Pro' AS vendor_name,
    '888 Creative Blvd, Ad City, CA 90028' AS vendor_address,
    'invoices@marketingpro.com' AS vendor_email,
    '555-0654' AS vendor_phone,
    'Bank: Business Bank, Account: ****3456' AS payment_details,
    'ACME Corporation',
    '456 Corporate Blvd, Business City, NY 10001',
    'Marketing',
    'Campaign #2025-Q1-LAUNCH',
    15200.00 AS subtotal,
    1368.00 AS tax_amount,
    16568.00 AS total_amount,
    'NET 30',
    'Q1 Product Launch Campaign',
    'Marketing & Advertising',
    2;
```

**Expected Result:**
- Email to `john.smith@company.com` with **2 PDF attachments** (INV-2025-001.pdf, INV-2025-002.pdf)
- Email to `sarah.johnson@company.com` with **3 PDF attachments** (INV-2025-003.pdf, INV-2025-004.pdf, INV-2025-005.pdf)

---

## Production Query Example

For use with actual database tables:

```sql
SELECT 
    -- Approver details (split key for grouping)
    u.Email AS approver_email,
    u.FullName AS approver_name,
    u.JobTitle AS approver_title,
    
    -- Invoice header
    inv.InvoiceNumber AS invoice_number,
    CONCAT('Invoice_', inv.InvoiceNumber, '.pdf') AS invoice_filename,
    inv.InvoiceDate AS invoice_date,
    inv.DueDate AS due_date,
    inv.Status AS invoice_status,
    
    -- Vendor details
    v.VendorName AS vendor_name,
    v.Address AS vendor_address,
    v.Email AS vendor_email,
    v.Phone AS vendor_phone,
    v.BankDetails AS payment_details,
    
    -- Company details
    comp.CompanyName AS company_name,
    comp.Address AS company_address,
    dept.DepartmentName AS department,
    inv.PurchaseOrderNumber AS reference,
    
    -- Financial details
    inv.Subtotal AS subtotal,
    inv.TaxAmount AS tax_amount,
    inv.TotalAmount AS total_amount,
    inv.PaymentTerms AS payment_terms,
    inv.Description AS description,
    cat.CategoryName AS category,
    inv.Priority AS priority
    
FROM Invoices inv
JOIN Vendors v ON inv.VendorId = v.Id
JOIN Departments dept ON inv.DepartmentId = dept.Id
JOIN Users u ON inv.ApproverId = u.Id
JOIN Companies comp ON inv.CompanyId = comp.Id
LEFT JOIN Categories cat ON inv.CategoryId = cat.Id
WHERE inv.Status = 'Pending Approval'
AND inv.RequiresApproval = 1
ORDER BY u.Email, inv.Priority, inv.InvoiceDate;
```

---

## Setup Instructions

### Step 1: Create Email Template

1. Navigate to **Templates** page
2. Click **Create Template**
3. Set **Type**: `ScribanTemplate`
4. Set **Output Format**: `HTML`
5. Set **Name**: `Invoice Approval Process Email`
6. Paste content from `Email_-_Invoice_Approval_Process.ScribanTemplate.html`
7. Click **Save**

### Step 2: Create Document Template

1. Click **Create Template** again
2. Set **Type**: `DocumentTemplate`
3. Set **Output Format**: `PDF`
4. Set **Name**: `Invoice Approval Process`
5. Paste content from `Document_Template_-_Invoice_Approval_Process.DocumentTemplate.txt`
6. Click **Save**

### Step 3: Create Profile

1. Navigate to **Profiles** page
2. Click **Create Profile**
3. Fill in basic details:
   - **Name**: `Invoice Approval Workflow`
   - **Connection**: Your MySQL connection
   - **Query**: Paste the static query above
4. Configure output:
   - **Output Format**: Email Export
   - **Email Template**: Select `Invoice Approval Process Email`
5. Configure splitting:
   - **Split Key Column**: `approver_email`
   - **Email Group By Split Key**: ✓ Enable
6. Configure email recipients:
   - **Recipients**: Use column `approver_email`
   - **Subject**: Use column or hardcoded: `Invoice Approval Required - {invoice_count} invoices pending`
7. Configure attachments:
   - **Enable Attachments**: ✓
   - **Mode**: DocumentTemplate
   - **Document Template**: Select `Invoice Approval Process`
   - **Filename Column**: `invoice_filename`
   - **Generate Per Row**: ✓ **Enable** (this creates one PDF per invoice)
8. Click **Save**

### Step 4: Test

1. Click **Run Now** on your profile
2. Check the approver inboxes:
   - `john.smith@company.com` should receive 1 email with 2 PDFs
   - `sarah.johnson@company.com` should receive 1 email with 3 PDFs
3. Review the logs for execution details

---

## Troubleshooting

**Issue**: All invoices in one PDF instead of separate PDFs
- **Solution**: Ensure **Generate Per Row** checkbox is enabled in attachment configuration

**Issue**: Multiple emails per approver
- **Solution**: Ensure **Email Group By Split Key** is enabled and **Split Key Column** is set to `approver_email`

**Issue**: Missing approver name in email
- **Solution**: Email template uses `rows[0].approver_name` - ensure query returns this column

**Issue**: PDFs have wrong filenames
- **Solution**: Check the `invoice_filename` column contains `.pdf` extension

---

## Notes

- **Generate Per Row**: This is the key setting that creates one PDF per invoice row
- **Deduplication**: Set to `Auto` or `ByFilename` to prevent duplicate PDFs
- **Max Attachments**: Default is 50 - increase if approvers need to review more invoices
- **Email Limits**: Some email providers limit attachment count - consider batch size
- **Performance**: Generating many PDFs can be resource-intensive - monitor execution time
