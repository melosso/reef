
This Scriban template generates a professional HTML email notifying customers that their order is ready for pick-up, featuring pickup location details, store hours, and specific instructions for the handover.

# WooCommerce Order Update Mail

## Example SQL

```sql
SELECT
    'Your order WOO-2025-00089 is ready for pickup' AS subject,
    'WOO-2025-00089' AS order_number,
    'Maria Müller' AS customer_name,
    'maria.mueller@example.de' AS customer_email,
    'Your TechStore' AS company,
    '[https://example.com/logo.png](https://example.com/logo.png)' AS company_logo_url,
    'Kurfürstendamm 123, 10711 Berlin, Germany' AS company_address,
    'support@example.de' AS support_email,
    'The TechStore Team' AS sender_name,
    YEAR(GETDATE()) AS year,

    -- Pickup Details
    'Berlin Flagship Store' AS pickup_location_name,
    'Kurfürstendamm 123, 10711 Berlin' AS pickup_address,
    'Mon-Fri: 09:00 - 20:00' AS pickup_hours,
    'Please bring a valid photo ID and this email.' AS pickup_instructions,
    '[https://maps.google.com/?q=Kurfürstendamm+123+Berlin](https://maps.google.com/?q=Kurfürstendamm+123+Berlin)' AS directions_url,
    '[https://example.de/unsubscribe](https://example.de/unsubscribe)' AS unsubscribe_url,

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
        }
    ]') AS items_json;
````

### MySQL Version

```mysql
SELECT
    'Your order WOO-2025-00089 is ready for pickup' AS subject,
    'WOO-2025-00089' AS order_number,
    'Maria Müller' AS customer_name,
    'maria.mueller@example.de' AS customer_email,
    'Your TechStore' AS company,
    '[https://example.com/logo.png](https://example.com/logo.png)' AS company_logo_url,
    'Kurfürstendamm 123, 10711 Berlin, Germany' AS company_address,
    'support@example.de' AS support_email,
    'The TechStore Team' AS sender_name,
    YEAR(NOW()) AS year,

    -- Pickup Details
    'Berlin Flagship Store' AS pickup_location_name,
    'Kurfürstendamm 123, 10711 Berlin' AS pickup_address,
    'Mon-Fri: 09:00 - 20:00' AS pickup_hours,
    'Please bring a valid photo ID and this email.' AS pickup_instructions,
    '[https://maps.google.com/?q=Kurfürstendamm+123+Berlin](https://maps.google.com/?q=Kurfürstendamm+123+Berlin)' AS directions_url,
    '[https://example.de/unsubscribe](https://example.de/unsubscribe)' AS unsubscribe_url,

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
        )
    ) AS items_json;
```

### PostgreSQL Version

```postgresql
SELECT
    'Your order WOO-2025-00089 is ready for pickup' AS subject,
    'WOO-2025-00089' AS order_number,
    'Maria Müller' AS customer_name,
    'maria.mueller@example.de' AS customer_email,
    'Your TechStore' AS company,
    '[https://example.com/logo.png](https://example.com/logo.png)' AS company_logo_url,
    'Kurfürstendamm 123, 10711 Berlin, Germany' AS company_address,
    'support@example.de' AS support_email,
    'The TechStore Team' AS sender_name,
    EXTRACT(YEAR FROM NOW()) AS year,

    -- Pickup Details
    'Berlin Flagship Store' AS pickup_location_name,
    'Kurfürstendamm 123, 10711 Berlin' AS pickup_address,
    'Mon-Fri: 09:00 - 20:00' AS pickup_hours,
    'Please bring a valid photo ID and this email.' AS pickup_instructions,
    '[https://maps.google.com/?q=Kurfürstendamm+123+Berlin](https://maps.google.com/?q=Kurfürstendamm+123+Berlin)' AS directions_url,
    '[https://example.de/unsubscribe](https://example.de/unsubscribe)' AS unsubscribe_url,

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
        )
    ) AS items_json;
```

````