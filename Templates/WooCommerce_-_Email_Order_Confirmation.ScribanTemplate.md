
This Scriban template generates a professional HTML email confirming a customer's order with full details including products, pricing, shipping and billing addresses.

# WooCommerce Order Confirmation Email

## Example SQL

```sql
SELECT
    'Your order WOO-2025-00089 is confirmed' AS subject,
    'WOO-2025-00089' AS order_number,
    FORMAT(GETDATE(), 'MMMM dd, yyyy') AS order_date,
    'COMPLETED' AS order_status,
    'Processing' AS order_status,
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
    '3-5 business days' AS estimated_delivery,
    'https://myaccount.example.de/orders/89' AS account_url,
    'https://tracking.example.de/shipment/xyz789' AS tracking_url,
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

    -- Billing Address
    JSON_QUERY(
        '{
            "name": "Maria Müller",
            "street": "Kaiserdamm 42",
            "city": "Berlin",
            "state": "",
            "postal": "14057",
            "country": "Germany"
        }'
    ) AS billing_address_json,

    -- Order Items
    JSON_QUERY('[
        {
            "product_name": "Ultra HD 4K Webcam Pro",
            "sku": "CAM-4K-PRO-001",
            "attributes": "Color: Black, Resolution: 4K",
            "quantity": "1",
            "unit_price": "89.99",
            "line_total": "89.99"
        },
        {
            "product_name": "Premium USB-C Cable (3-pack)",
            "sku": "CABLE-USBC-3PK",
            "attributes": "Length: 6ft, Color: White",
            "quantity": "2",
            "unit_price": "19.99",
            "line_total": "39.98"
        },
        {
            "product_name": "Wireless Keyboard & Mouse Combo",
            "sku": "KBM-WIRELESS-BLK",
            "attributes": "Color: Black, Layout: US",
            "quantity": "1",
            "unit_price": "49.99",
            "line_total": "49.99"
        }
    ]') AS items_json,

    -- Pricing
    '169,99' AS subtotal,
    '0,00' AS discount,
    'New Customer Discount' AS discount_label,
    '15,99' AS shipping,
    '35,21' AS tax,
    '221,19' AS total;
```

### MySQL Version

```mysql
SELECT
    'Your order WOO-2025-00089 is confirmed' AS subject,
    'WOO-2025-00089' AS order_number,
    DATE_FORMAT(NOW(), '%M %d, %Y') AS order_date,
    'COMPLETED' AS order_status,
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
    '3-5 business days' AS estimated_delivery,
    'https://myaccount.example.de/orders/89' AS account_url,
    'https://tracking.example.de/shipment/xyz789' AS tracking_url,
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

    -- Billing Address
    JSON_OBJECT(
        'name', 'Maria Müller',
        'street', 'Kaiserdamm 42',
        'city', 'Berlin',
        'state', '',
        'postal', '14057',
        'country', 'Germany'
    ) AS billing_address_json,

    -- Order Items
    JSON_ARRAY(
        JSON_OBJECT(
            'product_name', 'Ultra HD 4K Webcam Pro',
            'sku', 'CAM-4K-PRO-001',
            'attributes', 'Color: Black, Resolution: 4K',
            'quantity', '1',
            'unit_price', '89.99',
            'line_total', '89.99'
        ),
        JSON_OBJECT(
            'product_name', 'Premium USB-C Cable (3-pack)',
            'sku', 'CABLE-USBC-3PK',
            'attributes', 'Length: 6ft, Color: White',
            'quantity', '2',
            'unit_price', '19.99',
            'line_total', '39.98'
        ),
        JSON_OBJECT(
            'product_name', 'Wireless Keyboard & Mouse Combo',
            'sku', 'KBM-WIRELESS-BLK',
            'attributes', 'Color: Black, Layout: US',
            'quantity', '1',
            'unit_price', '49.99',
            'line_total', '49.99'
        )
    ) AS items_json,

    -- Pricing
    '169,99' AS subtotal,
    '0,00' AS discount,
    'New Customer Discount' AS discount_label,
    '15,99' AS shipping,
    '35,21' AS tax,
    '221,19' AS total;
```

### PostgreSQL Version

```postgresql
SELECT
    'Your order WOO-2025-00089 is confirmed' AS subject,
    'WOO-2025-00089' AS order_number,
    TO_CHAR(NOW(), 'Month DD, YYYY') AS order_date,
    'COMPLETED' AS order_status,
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
    '3-5 business days' AS estimated_delivery,
    'https://myaccount.example.de/orders/89' AS account_url,
    'https://tracking.example.de/shipment/xyz789' AS tracking_url,
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

    -- Billing Address
    jsonb_build_object(
        'name', 'Maria Müller',
        'street', 'Kaiserdamm 42',
        'city', 'Berlin',
        'state', '',
        'postal', '14057',
        'country', 'Germany'
    ) AS billing_address_json,

    -- Order Items
    jsonb_build_array(
        jsonb_build_object(
            'product_name', 'Ultra HD 4K Webcam Pro',
            'sku', 'CAM-4K-PRO-001',
            'attributes', 'Color: Black, Resolution: 4K',
            'quantity', '1',
            'unit_price', '89.99',
            'line_total', '89.99'
        ),
        jsonb_build_object(
            'product_name', 'Premium USB-C Cable (3-pack)',
            'sku', 'CABLE-USBC-3PK',
            'attributes', 'Length: 6ft, Color: White',
            'quantity', '2',
            'unit_price', '19.99',
            'line_total', '39.98'
        ),
        jsonb_build_object(
            'product_name', 'Wireless Keyboard & Mouse Combo',
            'sku', 'KBM-WIRELESS-BLK',
            'attributes', 'Color: Black, Layout: US',
            'quantity', '1',
            'unit_price', '49.99',
            'line_total', '49.99'
        )
    ) AS items_json,

    -- Pricing
    '169,99' AS subtotal,
    '0,00' AS discount,
    'New Customer Discount' AS discount_label,
    '15,99' AS shipping,
    '35,21' AS tax,
    '221,19' AS total;
```
