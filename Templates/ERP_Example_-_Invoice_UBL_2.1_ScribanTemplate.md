This Scriban template is designed to generate a fully compliant UBL 2.1 XML document for electronic invoicing.

# ERP Invoice Example for UBL 2.1 (XML)

## Example SQL

```sql
SELECT
    'INV-2025-09-00123' AS invoice_id,
    'ACME Consulting GmbH' AS supplier_name,
    'Global Innovations B.V.' AS customer_name,
    '380' AS invoice_type_code, -- Commercial Invoice (Invoice Type Code)
    '2.1' AS ubl_version,
    'urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0' AS customization_id,
    FORMAT(GETDATE(), 'yyyy-MM-dd') AS issue_date,
    FORMAT(DATEADD(day, 30, GETDATE()), 'yyyy-MM-dd') AS due_date,
    'EUR' AS currency,
    '1000.00' AS total_net,
    '190.00' AS total_tax,
    '1190.00' AS total_gross,
    '30' AS payment_means_code, -- Credit Transfer (Payment Means Code)
    'DE98765432101234567890' AS payee_iban,

    -- Supplier Party JSON
    JSON_QUERY(
        '{
            "endpoint_id": "DE123456789",
            "vat_id": "DE123456789",
            "street": "Hauptstraße 42",
            "city": "Berlin",
            "postal": "10115",
            "country_code": "DE",
            "contact_email": "billing@acme-consulting.de"
        }'
    ) AS supplier_party_json,

    -- Customer Party JSON
    JSON_QUERY(
        '{
            "endpoint_id": "NL987654321",
            "vat_id": "NL987654321",
            "street": "Keizersgracht 100",
            "city": "Amsterdam",
            "postal": "1015 CN",
            "country_code": "NL",
            "contact_email": "ap@global-innovations.nl"
        }'
    ) AS customer_party_json,

    -- Tax Summary JSON
    JSON_QUERY('[
        { 
            "tax_rate": "19.0", 
            "taxable_amount": "1000.00", 
            "tax_amount": "190.00", 
            "category_code": "S" 
        }
    ]') AS tax_summary_json,

    -- Line Items JSON
    JSON_QUERY('[
        {
            "id": 1,
            "description": "Senior Consultant Services (Project Apollo)",
            "quantity": "20",
            "unit_code": "HUR",
            "unit_price": "40.00",
            "net_total": "800.00",
            "tax_rate": "19.0"
        },
        {
            "id": 2,
            "description": "Standard Annual License for ACME Analytics Pro",
            "quantity": "1",
            "unit_code": "EA",
            "unit_price": "200.00",
            "net_total": "200.00",
            "tax_rate": "19.0"
        }
    ]') AS line_items_json;
```
