This Scriban template generates a structured XML General Ledger export suitable for system imports, auditing, and financial reporting integration.

# Accounting GL Export Example (XML)

## Example SQL

```sql
SELECT
    'Acme Corporation' AS company_name,
    FORMAT(GETDATE(), 'yyyy-MM-dd') AS export_date,
    'October 2025' AS period_name,
    'USD' AS currency,
    2025 AS fiscal_year,
    12 AS total_accounts,
    '45250.00' AS total_debit,
    '45250.00' AS total_credit,
    'BALANCED' AS trial_balance,
    'Valid' AS validation_status,
    'GL export generated for month-end close process.' AS notes,

    -- GL Accounts with Transactions
    JSON_QUERY('[
        {
            "account_number": "1010",
            "account_name": "Cash - Operating Account",
            "account_type": "Asset",
            "opening_balance": 15000.00,
            "debit_total": 8500.00,
            "credit_total": 2250.00,
            "closing_balance": 21250.00,
            "transactions_json": "[
                {\"id\": \"TXN001\", \"date\": \"2025-10-01\", \"description\": \"Opening balance\", \"reference\": \"OPEN/001\", \"debit\": 15000.00, \"credit\": 0.00, \"cost_center\": \"0000\"},
                {\"id\": \"TXN002\", \"date\": \"2025-10-05\", \"description\": \"Customer payment - Invoice INV-2025-0847\", \"reference\": \"DEP/045\", \"debit\": 5000.00, \"credit\": 0.00, \"cost_center\": \"1001\"},
                {\"id\": \"TXN003\", \"date\": \"2025-10-10\", \"description\": \"Payroll processing\", \"reference\": \"PAY/102\", \"debit\": 0.00, \"credit\": 2250.00, \"cost_center\": \"5010\"}
            ]"
        },
        {
            "account_number": "2010",
            "account_name": "Accounts Payable",
            "account_type": "Liability",
            "opening_balance": 8750.00,
            "debit_total": 3000.00,
            "credit_total": 5500.00,
            "closing_balance": 11250.00,
            "transactions_json": "[
                {\"id\": \"TXN005\", \"date\": \"2025-10-01\", \"description\": \"Opening balance\", \"reference\": \"OPEN/001\", \"debit\": 0.00, \"credit\": 8750.00, \"cost_center\": \"0000\"},
                {\"id\": \"TXN006\", \"date\": \"2025-10-08\", \"description\": \"Invoice received from supplier\", \"reference\": \"PO/1247\", \"debit\": 0.00, \"credit\": 3200.00, \"cost_center\": \"5020\"},
                {\"id\": \"TXN007\", \"date\": \"2025-10-20\", \"description\": \"Payment to vendor\", \"reference\": \"CHK/7823\", \"debit\": 2500.00, \"credit\": 0.00, \"cost_center\": \"5020\"}
            ]"
        }
    ]') AS accounts_json;
```
