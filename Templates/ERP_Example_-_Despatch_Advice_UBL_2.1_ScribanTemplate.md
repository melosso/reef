This Scriban template is designed to generate a fully compliant UBL 2.1 XML document for (advanced) shipping notices.

# ERP Despatch Advise Example for UBL 2.1 (XML)

## Example SQL

```sql
SELECT
    'DES-2025-09-9988' AS despatch_id,
    'PO-2025-10-00456' AS order_reference_id,
    FORMAT(GETDATE(), 'yyyy-MM-dd') AS issue_date,
    FORMAT(GETDATE(), 'HH:mm:ss') AS issue_time,
    '2.1' AS ubl_version,
    'urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0' AS customization_id,
    'Partial shipment of server blades' AS note,

    -- Supplier (Despatch Party)
    JSON_QUERY(
        '{
            "endpoint_id": "DE123456789",
            "name": "ACME Consulting GmbH",
            "street": "Hauptstraße 42",
            "city": "Berlin",
            "postal": "10115",
            "country_code": "DE"
        }'
    ) AS supplier_party_json,

    -- Customer (Delivery Party)
    JSON_QUERY(
        '{
            "endpoint_id": "NL987654321",
            "name": "Global Innovations B.V.",
            "street": "Warehouse Dock 4, Port Road 1",
            "city": "Rotterdam",
            "postal": "3011 AA",
            "country_code": "NL"
        }'
    ) AS delivery_party_json,

    -- Shipment Details
    JSON_QUERY(
        '{
            "gross_weight_measure": "45.5",
            "weight_unit": "KGM",
            "handling_code": "Fragile",
            "carrier_name": "FastLogistics EU",
            "tracking_id": "TRK-9988776655"
        }'
    ) AS shipment_json,

    -- Despatch Lines
    JSON_QUERY('[
        {
            "id": 1,
            "item_name": "High-Performance Server Blade",
            "delivered_qty": "2",
            "unit_code": "C62", 
            "order_line_id": "1",
            "sellers_item_id": "SRV-BLD-09"
        },
        {
            "id": 2,
            "item_name": "Rack Mount Kit",
            "delivered_qty": "2",
            "unit_code": "EA",
            "order_line_id": "2",
            "sellers_item_id": "RCK-MNT-01"
        }
    ]') AS despatch_lines_json;
```
