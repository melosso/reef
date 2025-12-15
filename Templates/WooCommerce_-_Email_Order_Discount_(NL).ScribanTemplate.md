
This Scriban template generates a Dutch (NL) HTML email welcoming new customers with a 10% discount code for their first order.

# WooCommerce Welkomstkorting Email (NL)

## Example SQL Query for New Users with 10% Welcome Discount

```sql
SELECT
    users.ID AS reef_id,
    CONCAT('Als bedankje voor jouw bestelling bij ', (SELECT option_value FROM wp_options WHERE option_name = 'blogname' LIMIT 1)) AS subject,
    users.user_email AS customer_email,
    SUBSTRING_INDEX(users.display_name, ' ', 1) AS customer_name,
    
    -- Company/Store Information
    (SELECT option_value FROM wp_options WHERE option_name = 'blogname' LIMIT 1) AS company,
    (SELECT option_value FROM wp_options WHERE option_name = 'woocommerce_email_from_address' LIMIT 1) AS support_email,
    
    -- 10% Welcome Coupon Code
    (SELECT p.post_title 
     FROM wp_posts p
     INNER JOIN wp_postmeta pm ON p.ID = pm.post_id
     WHERE p.post_type = 'shop_coupon'
     AND pm.meta_key = '_user_email'
     AND pm.meta_value = users.user_email
     AND p.post_title LIKE 'W10-%'
     AND p.post_status = 'publish'
     ORDER BY p.post_date DESC
     LIMIT 1
    ) AS coupon_code,
    
    -- Expiry date (90 days from now)
    DATE_FORMAT(DATE_ADD(NOW(), INTERVAL 90 DAY), '%e %M %Y') AS expiry_date
    
FROM wp_users AS users
WHERE users.ID IN (
    -- New users who don't have a welcome coupon yet
    SELECT DISTINCT u.ID
    FROM wp_users u
    LEFT JOIN wp_postmeta pm ON pm.meta_value = u.user_email AND pm.meta_key = '_user_email'
    LEFT JOIN wp_posts p ON p.ID = pm.post_id 
        AND p.post_type = 'shop_coupon' 
        AND p.post_title LIKE 'W10-%'
    WHERE pm.post_id IS NULL
)
ORDER BY users.user_registered DESC;
```

## Sample Test Query

```mysql
SELECT
    'Welkom bij Jouw Boekwinkel – een cadeautje voor je eerste bestelling' AS subject,
    'emma@voorbeeld.nl' AS customer_email,
    'Emma' AS customer_name,
    'Jouw Boekwinkel' AS company,
    'support@jouwboekwinkel.nl' AS support_email,
    'W10-ABC12345' AS coupon_code,
    '15 maart 2026' AS expiry_date;
```