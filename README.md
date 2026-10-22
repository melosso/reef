# 〰️ Reef

[![License](https://img.shields.io/badge/license-AGPL%203.0-blue)](LICENSE)
[![Latest Release](https://img.shields.io/github/v/release/melosso/reef)](https://github.com/melosso/reef/releases/latest)
[![Last commit](https://img.shields.io/github/last-commit/melosso/reef)](https://github.com/melosso/reef/commits/main)

**Reef** is a low-code web platform built to orchestrate your data workflows. Query databases, transform payloads, and deliver files to any destination from a single dashboard. Effortlessly manage connections, export profiles, and automated schedules with zero scripting required.

![Screenshot of Reef](https://github.com/melosso/reef/blob/main/.github/images/screenshot.webp?raw=true)

## What is Reef?

Reef automates data exports, integration pipelines, and background synchronizations all from an intuitive interface. Query databases using native SQL, transform outputs with optional Scriban templates, and generate raw data or formatted documents directly through your browser.

> [!IMPORTANT]  
> Reef is actively in development. While stable for production use, expect breaking changes during updates.

**Key Capabilities**

* **Database Connections**: Connect natively to PostgreSQL, MySQL, or SQL Server with reusable connection profiles.
* **Formats & Documents**: Export directly to JSON, XML, CSV, or YAML, or generate paginated PDF and DOCX files for invoices and reports.
* **Destinations**: Deliver files to local storage, FTP/SFTP, AWS S3, Azure Blob, HTTP webhooks, SMB, or email.
* **Automation**: Trigger jobs via cron expressions, fixed time intervals, or incoming webhooks.
* **Security & Observability**: Includes credential encryption, JWT authentication, record validation, and complete execution history logging.

Set up low-code export workflows in minutes using standard database queries and clean, lightweight tools.

---

## Getting Started

The fastest way to get Reef running, is by using Docker:


### Docker Compose (Recommended)
```yaml
services:
  reef:
    container_name: reef
    image: ghcr.io/melosso/reef:latest
    ports:
      - "8085:8085"
    volumes:
      - reef_core:/app/.core
      - reef_logs:/app/log
      - reef_db:/app/data
      - ./exports:/app/exports
    environment:
      - REEF_ENCRYPTION_KEY=YourKeyHere
      - Reef__DatabasePath=/app/data/Reef.db
      - Reef__AllowedOrigins=http://localhost:8085,http://localhost:3000

volumes:
  reef_core:
  reef_logs:
  reef_db:
```
```bash
mkdir -p exports && docker compose up -d
```

Access at **http://localhost:8085**

Upon starting the first time, you can login with the default credentials `admin@reef.local` / `admin123`. After you log in, follow the steps to change your password right away.

<summary>
<details>How to install on Windows</details>

**Windows Installation**

Download the latest release from Releases.

1. **Install .NET 10 Runtime:**
```powershell
   winget install --id Microsoft.DotNet.Runtime.10 -e
```

2. **Set encryption key:**
```powershell
   $bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); [Environment]::SetEnvironmentVariable("REEF_ENCRYPTION_KEY", [Convert]::ToBase64String($bytes), "Machine")
```

3. **Install service:**
```powershell
   .\Reef.bat install
   .\Reef.bat start
```

4. Open browser → **http://localhost:8085**

As mentioned hereabove, upon starting the first time, you can login with the default credentials `admin@reef.local` / `admin123`. You will be prompted to change them immediately.

</summary>

---

## How It Works

You can centralize your database credentials easily; create one connection for many profiles. You can tag these profiles by assigning a `Group` to them. Then, create an export definition by creating a `Profile` that'll be assigned a destination and you'll be ready.

<summary>
<details>Multiple destinations</details>

We support various destinations:

- **Local filesystem**: with date/profile variables
- **FTP/SFTP**: with SSL and passive mode
- **Cloud storage**: AWS S3, Azure Blob
- **HTTP endpoints**: POST to REST APIs
- **SMB shares**: Windows network drives
- **E-mail**: various SMTP providers supported

</summary>

<summary>
<details>Custom Templates</details>

Use Scriban for advanced transformations. If you're interested in more examples, make sure to checkout our `Examples/` folder.

```scriban
{
  "export_date": "{{ date.now }}",
  "records": [
    {{~ for row in rows ~}}
    { "id": {{ row.ID }}, "name": "{{ row.Name }}" }
    {{~ end ~}}
  ]
}
```

</summary>

<summary>
<details>Document Generation</details>

Reef includes built-in document generation capabilities for creating professional PDF and DOCX documents directly from your query results:

**Features:**
- **PDF & DOCX Support**: Generate paginated documents with automatic page numbering
- **Multi-page Documents**: Headers and footers repeat on every page automatically
- **Scriban Data Binding**: Full template syntax support within documents
- **Flexible Layouts**: Control page size (A4/Letter/Legal), orientation, margins
- **Document Options**: Watermarks, custom page numbering formats

**Example Invoice Template:**

```liquid
{{! format: pdf }}
{{! pageSize: A4 }}
{{! orientation: Portrait }}

{{# header }}
<div style="text-align: center; font-weight: bold;">
  {{ .[0].company_name }}
</div>
{{/ header }}

{{# content }}
<h2>Invoice {{ .[0].invoice_number }}</h2>
<p>Date: {{ .[0].invoice_date }}</p>
<p>Customer: {{ .[0].customer_name }}</p>

<table>
  <tr><th>Description</th><th>Qty</th><th>Price</th><th>Total</th></tr>
  {{~ for line in . ~}}
  <tr>
    <td>{{ line.item_description }}</td>
    <td>{{ line.quantity }}</td>
    <td>{{ line.unit_price }}</td>
    <td>{{ line.line_total }}</td>
  </tr>
  {{~ end ~}}
</table>

<p><strong>Total: ${{ .[0].total_amount }}</strong></p>
{{/ content }}

{{# footer }}
<div style="text-align: center; font-size: 8pt;">
  Thank you for your business
</div>
{{/ footer }}
```

</summary>

<summary>
<details>Scheduling</details>

After creating a job, you can schedule it using various methods such as:

- **Cron:** `0 2 * * *` (daily at 02:00)
- **Interval:** Every 15 minutes
- **Webhooks:** Trigger via HTTP POST

</summary>

> [!IMPORTANT] 
> Since this application is built for local data orchistration, make sure to **never expose** Reef outside of your network. If you need to reach it from outside, expose only the `/api` routes and run them through Nginx or whatever reverse proxy you use. There's an IIS example in [web.config.md](web.config.md) if you need a starting point, though you'l likely have to tweak it for your setup.

## License

Free for open source projects and personal use under the **AGPL 3.0** license. For more information, please see the [license](LICENSE) file.

## Contributing

Contributions welcome! Please submit issues and pull requests, using the templates we provided.
