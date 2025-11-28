This Scriban template generates a complete General Ledger export in CSV (comma-separated values) format, flattened for spreadsheet analysis and bulk imports.

# Accounting GL Export Example (CSV)

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

    -- GL Accounts with Transactions (same JSON structure as YAML)
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

## CSV Output Structure

Headers:
- `Company` – Legal entity name
- `ExportDate` – Date the export was generated
- `ExportPeriod` – Period description
- `Currency` – Reporting currency code
- `FiscalYear` – Fiscal year for the export
- `AccountNumber` – Chart of accounts ID
- `AccountName` – Account description
- `AccountType` – Classification (Asset, Liability, Equity, Revenue, Expense)
- `OpeningBalance` – Balance at period start
- `ClosingBalance` – Balance at period end
- `TransactionID` – Individual transaction identifier
- `TransactionDate` – Date of the transaction
- `TransactionDescription` – Transaction narrative
- `Reference` – External reference number (check, invoice, etc.)
- `Debit` – Debit amount (if any)
- `Credit` – Credit amount (if any)
- `CostCenter` – Cost center allocation

## Key Features

- **Flat Structure**: One transaction per row for easy analysis and filtering
- **Excel Ready**: Standard CSV with proper escaping for spreadsheet compatibility
- **Account-Level Data**: Account balances repeated for every transaction (enables pivot table analysis)
- **Cost Center Tracking**: Cost center visible for each transaction for departmental analysis
- **Safe String Escaping**: Uses `csv_escape` and `safe_string` filters to handle special characters and quotes

## Typical CSV Output

```
Company,ExportDate,ExportPeriod,Currency,FiscalYear,AccountNumber,AccountName,AccountType,OpeningBalance,ClosingBalance,TransactionID,TransactionDate,TransactionDescription,Reference,Debit,Credit,CostCenter
Acme Corporation,2025-10-31,October 2025,USD,2025,1010,Cash - Operating Account,Asset,15000.00,21250.00,TXN001,2025-10-01,Opening balance,OPEN/001,15000.00,0.00,0000
Acme Corporation,2025-10-31,October 2025,USD,2025,1010,Cash - Operating Account,Asset,15000.00,21250.00,TXN002,2025-10-05,Customer payment - Invoice INV-2025-0847,DEP/045,5000.00,0.00,1001
Acme Corporation,2025-10-31,October 2025,USD,2025,1010,Cash - Operating Account,Asset,15000.00,21250.00,TXN003,2025-10-10,Payroll processing,PAY/102,0.00,2250.00,5010
Acme Corporation,2025-10-31,October 2025,USD,2025,2010,Accounts Payable,Liability,8750.00,11250.00,TXN005,2025-10-01,Opening balance,OPEN/001,0.00,8750.00,0000
```

## Notes

- The CSV format is optimized for spreadsheet tools (Excel, Google Sheets, etc.)
- All text fields use `csv_escape` to safely handle commas, quotes, and newlines within values
- The flat structure means account information repeats for each transaction - this enables pivot table analysis by account type, cost center, etc.
- Multiple GL exports (multiple rows) are concatenated with headers appearing only once at the top
- Cost center can be grouped for departmental P&L analysis
- The same SQL query works for both YAML and CSV outputs - only the Scriban template changes
