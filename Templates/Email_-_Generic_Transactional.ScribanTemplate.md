A structured email template built for clear updates, with spots for your message, a button, and any key details you need to share.

# Email Template Documentation

This file explains how the HTML email template works, how Scriban fills it, and how SQL provides the data. It’s meant as a practical reference, not a deep technical write‑up.

## Overview

The system has three pieces working together:

1. **SQL** returns multiple rows (or a single row), each containing all fields needed for one email. Dynamic data (like detail labels/values) are returned as JSON strings.
2. **Scriban** receives all rows and loops through each one with `{{~ for row in rows ~}}`, rendering a complete HTML email per row.
3. The final output contains multiple complete HTML documents (concatenated), one per email, separated by `<!doctype html>` boundaries.
4. **Execution service** detects the split points and sends one email per row to its respective recipient.

SQL doesn't format anything. It only provides data. Scriban does all HTML rendering and looping.

---

## SQL Output Structure

The query produces one row with simple fields and JSON arrays:

* `subject` – Email subject line
* `preheader` – Preview text shown in email clients
* `company` – Company/sender name
* `logo_url` – URL to company logo image
* `greeting` – Personalized greeting (e.g., "Hi Alex,")
* `body` – Main body text or introduction
* `details_json` – JSON array of label/value pairs for the details table
* `action_text` – Text for the call-to-action button
* `action_url` – URL for the call-to-action button
* `sender_name` – Name of the person sending (for signature)
* `company_address` – Company address for footer
* `note` – Small optional note at the bottom
* `unsubscribe_url` – URL for unsubscribe link
* `year` – Copyright year (defaults to 2025)

The `details_json` array is left as JSON so Scriban can loop through and render label/value pairs dynamically.

---

## Example SQL

```sql
CREATE OR ALTER VIEW dbo.GetTemplateForMail
AS
SELECT
    'Alex T. Great;alex@company.com' AS email,
    '' AS email_cc,
    'Welcome to Acme Labs!' AS subject,
    'REC001' AS primaryKey,
    'Quick preview: here''s an example message layout.' AS preheader,
    'Acme Labs' AS company,
    'https://cdn-icons-png.flaticon.com/512/5332/5332306.png' AS logo_url,
    'Hi Alex,' AS greeting,
    'Just sending over a sample so you can see how the template renders.' AS [body],
    JSON_QUERY('[
        { "label": "Template type", "value": "Scriban HTML" },
        { "label": "Preview", "value": "Looking very nice" },
        { "label": "Rendering engine", "value": "Dynamic and JSON-based" }
    ]') AS details_json,
    'View details' AS action_text,
    'https://example.com/view' AS action_url,
    'Ben' AS sender_name,
    '1 Example Plaza, Brussels' AS company_address,
    'Small note here, something the reader doesn''t need to act on.' AS note,
    'https://example.com/unsubscribe' AS unsubscribe_url,
    2025 AS year

UNION ALL

SELECT
    'Jorden Reef;jordan@company.com' AS email,
    'manager@company.com' AS email_cc,
    'Project Status Update' AS subject,
    'REC002' AS primaryKey,
    'New updates for your project dashboard.' AS preheader,
    'Acme Labs' AS company,
    'https://cdn-icons-png.flaticon.com/512/5332/5332306.png' AS logo_url,
    'Hi Jordan,' AS greeting,
    'Here are the latest updates from your team this week.' AS [body],
    JSON_QUERY('[
        { "label": "Status", "value": "On Track" },
        { "label": "Completed Tasks", "value": "12 of 15" },
        { "label": "Team Members", "value": "5 Active" },
        { "label": "Next Review", "value": "Friday 3 PM" }
    ]') AS details_json,
    'View Project' AS action_text,
    'https://example.com/project/123' AS action_url,
    'Sarah' AS sender_name,
    '1 Example Plaza, Brussels' AS company_address,
    'This is an automated update from the project management system.' AS note,
    'https://example.com/unsubscribe' AS unsubscribe_url,
    2025 AS year

UNION ALL

SELECT
    'Morgan Freeman;morgan@company.com' AS email,
    'supervisor@company.com' AS email_cc,
    'Quarterly Performance Review' AS subject,
    'REC003' AS primaryKey,
    'Your Q4 review is ready to view.' AS preheader,
    'Acme Labs' AS company,
    'https://cdn-icons-png.flaticon.com/512/5332/5332306.png' AS logo_url,
    'Hi Morgan,' AS greeting,
    'We''ve completed your quarterly performance review and would like to discuss it with you.' AS [body],
    JSON_QUERY('[
        { "label": "Review Period", "value": "Oct - Dec 2024" },
        { "label": "Overall Rating", "value": "Exceeds Expectations" },
        { "label": "Key Achievements", "value": "3 Major Projects" },
        { "label": "Scheduled Meeting", "value": "Dec 20, 2 PM" }
    ]') AS details_json,
    'View Full Review' AS action_text,
    'https://example.com/reviews/morgan-q4' AS action_url,
    'Chris' AS sender_name,
    '1 Example Plaza, Brussels' AS company_address,
    'Please keep your login credentials confidential.' AS note,
    'https://example.com/unsubscribe' AS unsubscribe_url,
    2025 AS year;
```

---

## Template Behavior

The HTML email template:

* **Loops through rows**: `{{~ for row in rows ~}}` renders one complete `<!doctype html>` document per row
* **Accesses row data**: References like `row.subject`, `row.company`, `row.details_json` pull data from each row
* **Parses JSON fields**: Uses `parse_json` filter to convert `row.details_json` string into an array for looping
* **Conditional rendering**: Sections only appear if their row fields exist (e.g., `if row.action_url`)
* **Escapes output**: All variables use `| html.escape` to prevent injection attacks
* **Styling**: Uses inline CSS for email client compatibility (Gmail, Outlook, Apple Mail, etc.)

Each row produces one complete, independent HTML document. When 3 rows are passed, 3 HTML documents are concatenated in the output.

---

## Rendering Flow

1. SQL query executes and returns multiple rows (3 in the example: Alex, Jordan, Morgan).
2. ScribanTemplateEngine receives:

   ```csharp
   [
     { "email": "alex@company.com", "subject": "Welcome...", "details_json": "[{...}]", ... },
     { "email": "jordan@company.com", "subject": "Project Status...", "details_json": "[{...}]", ... },
     { "email": "morgan@company.com", "subject": "Quarterly Review...", "details_json": "[{...}]", ... }
   ]
   ```

3. Template loops: `{{~ for row in rows ~}}`
   - For each row, renders a complete `<!doctype html>` document
   - References `row.subject`, `row.company`, `row.details_json`, etc.
   - Parses `row.details_json` with `parse_json` to create dynamic detail rows

4. Output is 3 concatenated HTML documents:
   ```
   <!doctype html><html>...Alex's email...</html>
   <!doctype html><html>...Jordan's email...</html>
   <!doctype html><html>...Morgan's email...</html>
   ```

5. EmailExportService detects split points on `<!doctype html>` and sends 3 separate emails:
   - Email 1 → alex@company.com
   - Email 2 → jordan@company.com with CC to manager@company.com
   - Email 3 → morgan@company.com with CC to supervisor@company.com

---

## Notes

* **SQL Fields**: The query must return all fields listed in "SQL Output Structure" above. Missing fields will cause template sections to not render.
* **JSON Format**: `details_json` must be valid JSON. Use `JSON_QUERY()` in SQL to ensure proper formatting. Example: `JSON_QUERY('[{"label":"Status","value":"Active"}]')`
* **HTML Escaping**: All user-facing values are escaped with `| html.escape` in the template to prevent injection. Do NOT embed raw HTML in SQL.
* **Null Handling**: Use `IF` conditions to check for `null` or empty strings before rendering optional sections (e.g., `if row.action_url`).
* **Email Compatibility**: The layout is intentionally minimal (inline CSS) to survive email client rendering quirks (Outlook, Gmail, Apple Mail, etc.).
* **No Styling in SQL**: All presentation logic (colors, fonts, spacing) lives in the HTML template, not SQL.
* **Multi-row rendering**: When SQL returns multiple rows, the template renders each as a separate HTML document. The execution service automatically detects and splits these on `<!doctype html>` delimiters, sending one email per row.
* **Recipients and CC**: Each row must have an `email` column (for the "to" recipient). Optionally include `email_cc` and `subject` columns for per-row customization.
