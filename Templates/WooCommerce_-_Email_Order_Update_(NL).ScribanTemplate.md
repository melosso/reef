
This Scriban template generates a Dutch (NL) HTML email notifying customers about an update on their order, specifically for orders in 'Waiting' status in WooCommerce.

# WooCommerce Order Update Email (NL)

## Example SQL Query for WooCommerce Orders with Status 'Waiting'

```sql
SELECT
    CONCAT('Update over je bestelling #', orders.id) AS subject,
    orders.id AS order_number,
    DATE_FORMAT(orders.date_created_gmt, '%e %M %Y') AS order_date,
    -- Customer Information
    orders.billing_first_name AS customer_name,
    orders.billing_email AS customer_email,
    -- Company/Store Information
    (SELECT option_value FROM wp_options WHERE option_name = 'blogname' LIMIT 1) AS company,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_default_country' LIMIT 1) AS company_country,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_email_from_address' LIMIT 1) AS support_email,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_store_address' LIMIT 1) AS store_address,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_store_address_2' LIMIT 1) AS store_address_2,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_store_city' LIMIT 1) AS store_city,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_store_postcode' LIMIT 1) AS store_postcode,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_currency' LIMIT 1) AS currency,
    '€' AS currency_symbol,
    -- Order Items as JSON
    (
        SELECT JSON_ARRAYAGG(
            JSON_OBJECT(
                'product_name', order_items.order_item_name,
                'quantity', item_data.qty,
                'line_total', item_data.line_total,
                'product_image', (
                    SELECT guid
                    FROM wp_posts AS img_posts
                    INNER JOIN wp_postmeta AS img_meta ON img_posts.ID = img_meta.meta_value
                    WHERE img_meta.post_id = item_data.product_id
                    AND img_meta.meta_key = '_thumbnail_id'
                    AND img_posts.post_type = 'attachment'
                    LIMIT 1
                ),
                'product_note', item_data.custom_note,
                'attributes', NULL
            )
        )
        FROM wp_woocommerce_order_items AS order_items
        INNER JOIN (
            SELECT 
                order_item_id,
                MAX(CASE WHEN meta_key = '_qty' THEN meta_value END) AS qty,
                MAX(CASE WHEN meta_key = '_line_total' THEN meta_value END) AS line_total,
                MAX(CASE WHEN meta_key = '_product_id' THEN meta_value END) AS product_id,
                MAX(CASE WHEN meta_key = '_custom_note' THEN meta_value END) AS custom_note
            FROM wp_woocommerce_order_itemmeta
            GROUP BY order_item_id
        ) AS item_data ON order_items.order_item_id = item_data.order_item_id
        WHERE order_items.order_id = orders.id
        AND order_items.order_item_type = 'line_item'
    ) AS items_json
FROM wp_wc_orders AS orders
WHERE orders.status = 'wc-on-hold'
AND orders.type = 'shop_order'
ORDER BY orders.date_created_gmt DESC;
```

## Sample Test Query

```mysql
SELECT
    'Update over je bestelling #13001' AS subject,
    '13001' AS order_number,
    '22 november 2025' AS order_date,
    'Dane' AS customer_name,
    'Dane@example.nl' AS customer_email,
    'Company Name' AS company,
    'Nederland' AS company_country,
    'support@company.com' AS support_email,
    '€' AS currency_symbol,

    -- Order Items
    JSON_ARRAY(
        JSON_OBJECT(
            'product_name', 'Bundel dicht',
            'quantity', '1',
            'line_total', '14,95',
            'product_image', 'https://example.com/wp-content/uploads/product-image.webp',
            'product_note', 'Dit product is eind december beschikbaar en kan worden opgehaald of verzonden, afhankelijk van je keuze.',
            'attributes', NULL
        )
    ) AS items_json;
```

## Field Descriptions

### Required Fields
- `subject` - Email subject line (typically includes order number)
- `order_number` - WooCommerce order ID
- `order_date` - Order date in Dutch format (e.g., "22 november 2025")
- `customer_name` - Customer's first name
- `customer_email` - Customer's email address
- `items_json` - JSON array of order items

### Optional Fields
- `company` - Company name (default: "Company Name")
- `company_country` - Company country (default: "Nederland")
- `support_email` - Support contact email (default: "support@company.com")
- `currency_symbol` - Currency symbol (default: "€")

### Item JSON Structure
Each item in `items_json` should have:
- `product_name` - Product name
- `quantity` - Quantity ordered
- `line_total` - Total price for this line item
- `product_image` - (Optional) URL to product image
- `product_note` - (Optional) Additional note about the product
- `attributes` - (Optional) Product attributes/variations
