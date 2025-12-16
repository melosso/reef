
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
    
    -- 10% Welcome Coupon Code (existing, unused)
    coupons.coupon_code,
    
    -- Expiry date (90 days from now)
    DATE_FORMAT(DATE_ADD(users.user_registered, INTERVAL 90 DAY), '%e %M %Y') AS expiry_date
    
FROM wp_users AS users
INNER JOIN (
    SELECT 
        pm_email.meta_value AS user_email,
        p.post_title AS coupon_code
    FROM wp_posts p
    INNER JOIN wp_postmeta pm_email ON p.ID = pm_email.post_id AND pm_email.meta_key = '_user_email'
    LEFT JOIN wp_postmeta pm_usage ON p.ID = pm_usage.post_id AND pm_usage.meta_key = 'usage_count'
    INNER JOIN (
        SELECT 
            pm.meta_value AS email,
            MAX(p2.post_date) AS latest_date
        FROM wp_posts p2
        INNER JOIN wp_postmeta pm ON p2.ID = pm.post_id AND pm.meta_key = '_user_email'
        LEFT JOIN wp_postmeta pm_u ON p2.ID = pm_u.post_id AND pm_u.meta_key = 'usage_count'
        WHERE p2.post_type = 'shop_coupon'
        AND p2.post_title LIKE 'W10-%'
        AND p2.post_status = 'publish'
        AND (pm_u.meta_value IS NULL OR pm_u.meta_value = '0')
        GROUP BY pm.meta_value
    ) AS latest ON pm_email.meta_value = latest.email AND p.post_date = latest.latest_date
    WHERE p.post_type = 'shop_coupon'
    AND p.post_title LIKE 'W10-%'
    AND p.post_status = 'publish'
    AND (pm_usage.meta_value IS NULL OR pm_usage.meta_value = '0')
) AS coupons ON coupons.user_email = users.user_email
WHERE users.user_registered <= DATE_SUB(NOW(), INTERVAL 36 HOUR)
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