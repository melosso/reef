This Scriban template is designed to generate a fully compliant UBL 2.1 XML document for electronic purchase order acknowledgments and confirmations.

# ERP Purchase Order Response Example for UBL 2.1 (XML)

## Example SQL

```sql
SELECT
    'POR-2025-10-00456' AS response_id,
    'PO-2025-10-00456' AS original_po_id,
    FORMAT(DATEADD(day, -5, GETDATE()), 'yyyy-MM-dd') AS original_po_date,
    FORMAT(GETDATE(), 'yyyy-MM-dd') AS issue_date,
    'EUR' AS currency,
    'Order acknowledged. All items confirmed for dispatch.' AS note,
    '2.1' AS ubl_version,
    'urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0' AS customization_id,

    -- Monetary Totals
    '1200.00' AS total_net,
    '228.00' AS total_tax,
    '1428.00' AS total_gross,

    -- Buyer (Customer)
    JSON_QUERY(
        '{
            "endpoint_id": "NL987654321",
            "name": "Global Innovations B.V.",
            "street": "Keizersgracht 100",
            "city": "Amsterdam",
            "postal": "1015 CN",
            "country_code": "NL",
            "contact_email": "purchasing@global-innovations.nl"
        }'
    ) AS buyer_party_json,

    -- Seller (Supplier)
    JSON_QUERY(
        '{
            "endpoint_id": "DE123456789",
            "name": "ACME Consulting GmbH",
            "street": "Hauptstraße 42",
            "city": "Berlin",
            "postal": "10115",
            "country_code": "DE"
        }'
    ) AS seller_party_json,

    -- Delivery Location
    JSON_QUERY(
        '{
            "street": "Warehouse Dock 4, Port Road 1",
            "city": "Rotterdam",
            "postal": "3011 AA",
            "country_code": "NL",
            "promised_delivery_date": "' + FORMAT(DATEADD(day, 14, GETDATE()), 'yyyy-MM-dd') + '"
        }'
    ) AS delivery_json,

    -- Line Items with Acknowledgment Status
    JSON_QUERY('[
        {
            "id": 1,
            "status_code": "Confirmed",
            "description": "High-Performance Server Blade",
            "confirmed_quantity": "2",
            "unit_code": "C62",
            "unit_price": "500.00",
            "net_total": "1000.00",
            "line_note": "In stock, ready for dispatch"
        },
        {
            "id": 2,
            "status_code": "Pending",
            "description": "Rack Mount Kit",
            "confirmed_quantity": "1",
            "unit_code": "EA",
            "unit_price": "100.00",
            "net_total": "100.00",
            "line_note": "Partial shipment. 1 unit expected by 2025-11-20, remaining 1 unit by 2025-12-01"
        }
    ]') AS line_items_json;
```
