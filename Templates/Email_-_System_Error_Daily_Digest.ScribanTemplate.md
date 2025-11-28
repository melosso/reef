A mail template containing daily digest/reports.

# Email Template Documentation

## Example SQL

```sql
WITH Generator AS (
    -- 1. Create numbers 1 to 25 to simulate rows
    SELECT 1 AS ID
    UNION ALL
    SELECT ID + 1 FROM Generator WHERE ID < 25
),
MockData AS (
    -- 2. Generate random error data for each row
    SELECT 
        ID,
        -- Generate random time within last 24 hours
        DATEADD(MINUTE, -(ABS(CHECKSUM(NEWID()) % 1440)), GETDATE()) AS LogTime,
        -- Weighted Random Severity: 20% Critical, 30% Warning, 50% Info
        CASE 
            WHEN ABS(CHECKSUM(NEWID()) % 10) < 2 THEN 'CRITICAL'
            WHEN ABS(CHECKSUM(NEWID()) % 10) < 5 THEN 'WARNING'
            ELSE 'INFO'
        END AS [Level],
        -- Random Source Component
        CHOOSE(ABS(CHECKSUM(NEWID()) % 5) + 1, 'AuthService', 'PaymentGateway', 'OrderAPI', 'Database', 'Frontend') AS Source,
        -- Random Error Messages
        CHOOSE(ABS(CHECKSUM(NEWID()) % 6) + 1, 
            'Connection timed out waiting for response', 
            'NullReferenceException in handler', 
            'Transaction deadlock victim', 
            'API Rate limit exceeded', 
            'Invalid payload format received',
            'Cache miss ratio too high'
        ) AS [Message],
        -- Random ID
        CONCAT('ERR-', ABS(CHECKSUM(NEWID()) % 10000)) AS ErrorID
    FROM Generator
),
Stats AS (
    -- 3. Calculate summary stats for the email header
    SELECT 
        COUNT(*) as TotalCount,
        SUM(CASE WHEN [Level] = 'CRITICAL' THEN 1 ELSE 0 END) as CriticalCount,
        SUM(CASE WHEN [Level] = 'WARNING' THEN 1 ELSE 0 END) as WarnCount
    FROM MockData
)
SELECT 
    'vana@company.com' AS email,
    '' AS email_cc,
    CONCAT('Daily System Report: ', (SELECT CriticalCount FROM Stats), ' Critical Events') AS subject,
    CONCAT('RPT-', FORMAT(GETDATE(), 'yyyyMMdd')) AS report_id,
    'Acme Corp' AS company,
    FORMAT(GETDATE(), 'MMM dd, yyyy HH:mm') AS generated_at,
    (SELECT TotalCount FROM Stats) AS total_count,
    (SELECT CriticalCount FROM Stats) AS critical_count,
    (SELECT WarnCount FROM Stats) AS warn_count,
    'https://dashboard.example.com/daily' AS dashboard_url,
    'https://example.com/unsubscribe' AS unsubscribe_url,
    2025 AS year,
    
    -- 4. Pack the table rows into a JSON string
    (
        SELECT 
            FORMAT(LogTime, 'HH:mm:ss') AS [time],
            [Level] AS [level],
            Source AS [source],
            [Message] AS [message],
            ErrorID AS [id]
        FROM MockData
        ORDER BY LogTime DESC
        FOR JSON PATH
    ) AS error_list_json

FROM Stats;
```
