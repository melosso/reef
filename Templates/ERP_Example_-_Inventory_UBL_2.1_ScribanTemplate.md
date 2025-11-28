This Scriban template is designed to generate a fully compliant UBL 2.1 XML document for inventory reports.

# ERP Inventory Example for UBL 2.1 (XML)

## Example SQL

```sql
SELECT
    'INV-RPT-2025-W04' AS report_id,
    FORMAT(GETDATE(), 'yyyy-MM-dd') AS issue_date,
    FORMAT(GETDATE(), 'yyyy-MM-dd') AS period_start_date,
    FORMAT(DATEADD(day, 7, GETDATE()), 'yyyy-MM-dd') AS period_end_date,
    '2.1' AS ubl_version,
    'urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0' AS customization_id,

    -- Retailer / Owner of Inventory
    JSON_QUERY(
        '{
            "endpoint_id": "NL987654321",
            "name": "Global Innovations B.V."
        }'
    ) AS retailer_party_json,

    -- Inventory Location (Warehouse)
    JSON_QUERY(
        '{
            "id": "WH-AMSTERDAM-01",
            "name": "Main Distribution Center",
            "street": "Logistics Way 12",
            "city": "Amsterdam",
            "postal": "1011 AZ",
            "country_code": "NL"
        }'
    ) AS location_json,

    -- Inventory Lines (Stock Levels)
    JSON_QUERY('[
        {
            "id": 1,
            "item_name": "Wireless Mouse M100",
            "item_id": "WM-100-BLK",
            "quantity": "450",
            "unit_code": "EA", 
            "location_zone": "Aisle 4, Shelf B"
        },
        {
            "id": 2,
            "item_name": "Mechanical Keyboard K500",
            "item_id": "MK-500-RGB",
            "quantity": "120",
            "unit_code": "EA",
            "location_zone": "Aisle 4, Shelf C"
        },
        {
            "id": 3,
            "item_name": "USB-C Hub Multiport",
            "item_id": "USB-C-HUB-01",
            "quantity": "85",
            "unit_code": "EA",
            "location_zone": "Aisle 2, Bin 12"
        }
    ]') AS inventory_lines_json;
```
