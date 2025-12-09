
This Scriban template generates a Dutch (NL) HTML email notifying customers about an update on their order, specifically for orders in 'Waiting' status in WooCommerce.

# WooCommerce Order Update Email (NL)

## MySQL Query for WooCommerce Orders with Status 'Waiting'

```mysql
SELECT
    CONCAT('Update over je bestelling #', posts.ID) AS subject,
    posts.ID AS order_number,
    DATE_FORMAT(posts.post_date, '%e %M %Y') AS order_date,

    -- Customer Information
    MAX(CASE WHEN meta_billing.meta_key = '_billing_first_name' THEN meta_billing.meta_value END) AS customer_name,
    MAX(CASE WHEN meta_billing.meta_key = '_billing_email' THEN meta_billing.meta_value END) AS customer_email,

    -- Company Information
    'Company Name' AS company,
    'Nederland' AS company_country,
    'support@company.com' AS support_email,
    '€' AS currency_symbol,

    -- Order Items as JSON
    (
        SELECT JSON_ARRAYAGG(
            JSON_OBJECT(
                'product_name', order_items.order_item_name,
                'quantity', MAX(CASE WHEN item_meta.meta_key = '_qty' THEN item_meta.meta_value END),
                'line_total', MAX(CASE WHEN item_meta.meta_key = '_line_total' THEN item_meta.meta_value END),
                'product_image', (
                    SELECT guid
                    FROM wp_posts AS img_posts
                    INNER JOIN wp_postmeta AS img_meta ON img_posts.ID = img_meta.meta_value
                    WHERE img_meta.post_id = MAX(CASE WHEN item_meta.meta_key = '_product_id' THEN item_meta.meta_value END)
                    AND img_meta.meta_key = '_thumbnail_id'
                    AND img_posts.post_type = 'attachment'
                    LIMIT 1
                ),
                'product_note', MAX(CASE WHEN item_meta.meta_key = '_custom_note' THEN item_meta.meta_value END),
                'attributes', NULL
            )
        )
        FROM wp_woocommerce_order_items AS order_items
        LEFT JOIN wp_woocommerce_order_itemmeta AS item_meta
            ON order_items.order_item_id = item_meta.order_item_id
        WHERE order_items.order_id = posts.ID
        AND order_items.order_item_type = 'line_item'
        GROUP BY order_items.order_item_id
    ) AS items_json

FROM wp_posts AS posts
LEFT JOIN wp_postmeta AS meta_billing
    ON posts.ID = meta_billing.post_id
    AND meta_billing.meta_key IN ('_billing_first_name', '_billing_email')

WHERE posts.post_type = 'shop_order'
AND posts.post_status = 'wc-on-hold'  -- 'Waiting' status in WooCommerce

GROUP BY posts.ID, posts.post_date

ORDER BY posts.post_date DESC;
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
