
This Scriban template generates a professional HTML email notifying customers that their order has shipped with tracking information and delivery details.

# WooCommerce Tracking Update Email

## Example SQL

```sql
SELECT
    'A shipment for your order WOO-2025-00089 is on its way' AS subject,
    'WOO-2025-00089' AS order_number,
    'Maria Müller' AS customer_name,
    'maria.mueller@example.de' AS customer_email,
    'Your TechStore' AS company,
    'https://example.com/logo.png' AS company_logo_url,
    'Kurfürstendamm 123, 10711 Berlin, Germany' AS company_address,
    'support@example.de' AS support_email,
    'The TechStore Team' AS sender_name,
    'EUR' AS currency,
    '€' AS currency_symbol,
    YEAR(GETDATE()) AS year,

    -- Shipping Details
    FORMAT(GETDATE(), 'MMMM dd, yyyy') AS shipped_date,
    'TRK123456789ABC' AS tracking_number,
    'DHL' AS carrier,
    FORMAT(DATEADD(day, 5, GETDATE()), 'MMMM dd, yyyy') AS estimated_delivery_date,
    'https://tracking.dhl.de/de/de/paket/status?tracking.id=TRK123456789ABC' AS tracking_url,
    'https://example.de/unsubscribe' AS unsubscribe_url,

    -- Shipping Address
    JSON_QUERY(
        '{
            "name": "Maria Müller",
            "street": "Kaiserdamm 42",
            "city": "Berlin",
            "state": "",
            "postal": "14057",
            "country": "Germany"
        }'
    ) AS shipping_address_json,

    -- Order Items
    JSON_QUERY('[
        {
            "product_name": "Ultra HD 4K Webcam Pro",
            "sku": "CAM-4K-PRO-001",
            "quantity": "1"
        },
        {
            "product_name": "Premium USB-C Cable (3-pack)",
            "sku": "CABLE-USBC-3PK",
            "quantity": "2"
        },
        {
            "product_name": "Wireless Keyboard & Mouse Combo",
            "sku": "KBM-WIRELESS-BLK",
            "quantity": "1"
        }
    ]') AS items_json;
```

### MySQL Version

```mysql
SELECT
    'A shipment for your order WOO-2025-00089 is on its way' AS subject,
    'WOO-2025-00089' AS order_number,
    'Maria Müller' AS customer_name,
    'maria.mueller@example.de' AS customer_email,
    'Your TechStore' AS company,
    'https://example.com/logo.png' AS company_logo_url,
    'Kurfürstendamm 123, 10711 Berlin, Germany' AS company_address,
    'support@example.de' AS support_email,
    'The TechStore Team' AS sender_name,
    'EUR' AS currency,
    '€' AS currency_symbol,
    YEAR(NOW()) AS year,

    -- Shipping Details
    DATE_FORMAT(NOW(), '%M %d, %Y') AS shipped_date,
    'TRK123456789ABC' AS tracking_number,
    'DHL' AS carrier,
    DATE_FORMAT(DATE_ADD(NOW(), INTERVAL 5 DAY), '%M %d, %Y') AS estimated_delivery_date,
    'https://tracking.dhl.de/de/de/paket/status?tracking.id=TRK123456789ABC' AS tracking_url,
    'https://example.de/unsubscribe' AS unsubscribe_url,

    -- Shipping Address
    JSON_OBJECT(
        'name', 'Maria Müller',
        'street', 'Kaiserdamm 42',
        'city', 'Berlin',
        'state', '',
        'postal', '14057',
        'country', 'Germany'
    ) AS shipping_address_json,

    -- Order Items
    JSON_ARRAY(
        JSON_OBJECT(
            'product_name', 'Ultra HD 4K Webcam Pro',
            'sku', 'CAM-4K-PRO-001',
            'quantity', '1'
        ),
        JSON_OBJECT(
            'product_name', 'Premium USB-C Cable (3-pack)',
            'sku', 'CABLE-USBC-3PK',
            'quantity', '2'
        ),
        JSON_OBJECT(
            'product_name', 'Wireless Keyboard & Mouse Combo',
            'sku', 'KBM-WIRELESS-BLK',
            'quantity', '1'
        )
    ) AS items_json;
```

### PostgreSQL Version

```postgresql
SELECT
    'A shipment for your order WOO-2025-00089 is on its way' AS subject,
    'WOO-2025-00089' AS order_number,
    'Maria Müller' AS customer_name,
    'maria.mueller@example.de' AS customer_email,
    'Your TechStore' AS company,
    'https://example.com/logo.png' AS company_logo_url,
    'Kurfürstendamm 123, 10711 Berlin, Germany' AS company_address,
    'support@example.de' AS support_email,
    'The TechStore Team' AS sender_name,
    'EUR' AS currency,
    '€' AS currency_symbol,
    EXTRACT(YEAR FROM NOW()) AS year,

    -- Shipping Details
    TO_CHAR(NOW(), 'Month DD, YYYY') AS shipped_date,
    'TRK123456789ABC' AS tracking_number,
    'DHL' AS carrier,
    TO_CHAR(NOW() + INTERVAL '5 days', 'Month DD, YYYY') AS estimated_delivery_date,
    'https://tracking.dhl.de/de/de/paket/status?tracking.id=TRK123456789ABC' AS tracking_url,
    'https://example.de/unsubscribe' AS unsubscribe_url,

    -- Shipping Address
    jsonb_build_object(
        'name', 'Maria Müller',
        'street', 'Kaiserdamm 42',
        'city', 'Berlin',
        'state', '',
        'postal', '14057',
        'country', 'Germany'
    ) AS shipping_address_json,

    -- Order Items
    jsonb_build_array(
        jsonb_build_object(
            'product_name', 'Ultra HD 4K Webcam Pro',
            'sku', 'CAM-4K-PRO-001',
            'quantity', '1'
        ),
        jsonb_build_object(
            'product_name', 'Premium USB-C Cable (3-pack)',
            'sku', 'CABLE-USBC-3PK',
            'quantity', '2'
        ),
        jsonb_build_object(
            'product_name', 'Wireless Keyboard & Mouse Combo',
            'sku', 'KBM-WIRELESS-BLK',
            'quantity', '1'
        )
    ) AS items_json;
```
