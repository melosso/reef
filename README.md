# 🌟 Reef

[![License](https://img.shields.io/badge/license-AGPL%203.0-blue)](LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/melosso/reef)](https://github.com/melosso/reef/commits/main)
[![Latest Release](https://img.shields.io/github/v/release/melosso/reef)](https://github.com/melosso/reef/releases/latest)

This is **Reef**. It's a web-based integration platform that orchestrates your data workflows. It lets you run queries, transform results, and deliver them to multiple destinations, all from a single easy-to-use interface. Manage connections, profiles, and scheduled executions centrally. All low code, no scripts unless you need them.

![Screenshot of Reef](https://github.com/melosso/reef/blob/main/.github/images/screenshot.webp?raw=true)

## What is Reef?

Reef automates data exports for **reporting workflows**, **integration pipelines**, and **data synchronization** with customizable data structures. Keep it low-code, set-up your own custom templates if necessary, and export data effortlessly. 

**Some of the key capabilities:**

- **Web-based interface**: Manage everything through the browser
- **Connection management**:  PostgreSQL MySQL or SQL Server. Store and reuse database connections across profiles
- **Multiple formats**: JSON, XML, CSV, YAML with optional [Scriban](https://github.com/scriban/scriban) templates
- **Document generation**: Generate paginated PDF and DOCX documents (such as invoices, reports, picklists) 
- **Flexible destinations**: Local, FTP/SFTP, AWS S3, Azure Blob, HTTP, SMB, SMTP
- **Job scheduling**: Cron expressions, intervals, or webhook triggers
- **Execution history**: Track all runs with detailed logging
- **Security first**: Encrypted credentials, record validation and JWT's

In other words, Reef can assist you in quickly getting data synchronisation going. Use your native database languages, or extend your results with (optional) advanced templating.

---

## Getting Started

We've prepared two methods to deploy Reef. It's up to you to choose your preferred method:

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

volumes:
  reef_core:
  reef_logs:
  reef_db:
```
```bash
mkdir -p exports && docker compose up -d
```

Access at **http://localhost:8085**

Upon starting the first time, you can login with the default credentials `admin` / `admin123`. After you log in, follow the steps to change your password right away.

### Windows Installation

Download the latest release from Releases.

1. **Install .NET 9 Runtime:**
```powershell
   winget install --id Microsoft.DotNet.Runtime.9 -e
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

As mentioned hereabove, upon starting the first time, you can login with the default credentials `admin` / `admin123`. You will be prompted to change them immediately.

---

## How It Works

You can centralize your database credentials easily; create one connection for many profiles. You can tag these profiles by assigning a `Group` to them. Then, create an export definition by creating a `Profile` that'll be assigned a destination:

#### Multiple Destinations
- **Local filesystem**: with date/profile variables
- **FTP/SFTP**: with SSL and passive mode
- **Cloud storage**: AWS S3, Azure Blob
- **HTTP endpoints**: POST to REST APIs
- **SMB shares**: Windows network drives
- **E-mail**: various SMTP providers supported

#### Custom Templates
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

#### Document Generation (PDF, DOCX)

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

#### Job Scheduling

After creating a job, you can schedule it using various methods such as:

- **Cron:** `0 2 * * *` (daily at 02:00)
- **Interval:** Every 15 minutes
- **Webhooks:** Trigger via HTTP POST

## Security

We've made sure to keep your data safe. Here's a glimpse on how we do this:

- **Encrypted credentials**: Sensitive data is stored using RSA+AES hybrid encryption
- **Transient execution**: Exchanged data exists only in memory while being processed, nothing persists *
- **Hash validation**: Detect configuration tampering
- **Audit logging**: Every change is tracked for accountability

Remark: * Outgoing mail temporarily persist recipient information, until approved by the (when using the email approval option).

> [!IMPORTANT] 
> Since this application is built for local data orchistration, make sure to **never expose** Reef outside of your network. If you need to reach it from outside, expose only the `/api` routes and run them through Nginx or whatever reverse proxy you use. There's an IIS example in [web.config.md](web.config.md) if you need a starting point, though you'l likely have to tweak it for your setup.

## Lore

> Reef comes from the idea of a coral reef, something that grows slowly from countless small bits until it becomes its own world. Database exports feel the same way. Each customer, each dump, each migration, another layer added to the structure. It's alive in its own way, even if it's just data.

## License

Free for open source projects and personal use under the **AGPL 3.0** license. For more information, please see the [license](LICENSE) file.

## Contributing

Contributions welcome! Please submit issues and pull requests, using the templates we provided.
