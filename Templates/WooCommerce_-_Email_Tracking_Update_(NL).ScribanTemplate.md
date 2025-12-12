
This Scriban template generates a Dutch (NL) HTML email notifying customers about an update on their order, when the product sent (e.g. contains a 'TrackingUrl') and in 'Completed/Sent' status in WooCommerce.

# WooCommerce Tracking Update Email (NL)

## Example SQL Query for WooCommerce Orders with Status 'Completed/Sent' with Tracking Url

```sql
SELECT
    orders.id As reef_id,
    CONCAT('Update over je bestelling #', orders.id) AS subject,
    orders.id AS order_number,
    DATE_FORMAT(orders.date_created_gmt, '%e %M %Y') AS order_date,
    -- Customer Information
    billing.first_name AS customer_name,
    orders.billing_email AS customer_email,
    -- Company/Store Information
    (SELECT option_value FROM wp_options WHERE option_name = 'blogname' LIMIT 1) AS company,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_default_country' LIMIT 1) AS company_country,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_email_from_address' LIMIT 1) AS support_email,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_store_address' LIMIT 1) AS store_address,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_store_address_2' LIMIT 1) AS store_address_2,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_store_city' LIMIT 1) AS store_city,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_store_postcode' LIMIT 1) AS store_postcode,
    orders.currency AS currency,
    '€' AS currency_symbol,
    -- Tracking Information
    (SELECT meta_value FROM wp_postmeta WHERE post_id = orders.id AND meta_key = 'tracking_number' LIMIT 1) AS tracking_number,
    CASE 
        WHEN LENGTH((SELECT meta_value FROM wp_postmeta WHERE post_id = orders.id AND meta_key = 'tracking_number' LIMIT 1)) > 8
        THEN CONCAT('https://jouw.postnl.nl/track-and-trace/', (SELECT meta_value FROM wp_postmeta WHERE post_id = orders.id AND meta_key = 'tracking_number' LIMIT 1))
        ELSE NULL
    END AS tracking_url,
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
LEFT JOIN wp_wc_order_addresses AS billing 
    ON orders.id = billing.order_id 
    AND billing.address_type = 'billing'
WHERE 
    orders.status = 'wc-completed'
AND orders.type = 'shop_order'
AND EXISTS (
    SELECT 1
    FROM wp_woocommerce_order_items AS shipping_items
    WHERE shipping_items.order_id = orders.id
    AND shipping_items.order_item_type = 'shipping'
    AND shipping_items.order_item_name NOT LIKE '%Pick%'
)
AND EXISTS (
    SELECT 1
    FROM wp_postmeta
    WHERE post_id = orders.id
    AND meta_key = 'tracking_number'
    AND meta_value IS NOT NULL
    AND meta_value != ''
)
ORDER BY orders.date_created_gmt DESC;
```
