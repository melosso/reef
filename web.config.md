## Using IIS As Proxy (to Reef)

As mentioned in the README.md, Reef isn't secure enough (yet) to expose the application directly to the internet. This document demonstrates how to set up an IIS reverse proxy for Reef using ARR and URL Rewrite. It assumes IIS is installed and the script is run from the proxy folder.

---

### Prerequisites

* IIS installed with URL Rewrite + Static Content features.
* Administrator privileges.
* ARR 3.0 (will be installed by script if missing).
* `web.config` file from the git repository.

Please make sure to create a Reverse Proxy folder (e.g. C:\Apps\Reef Proxy) and run these scripts in that location.

### 1. Install ARR & Enable Proxy

```powershell
# Download and install ARR 3.0 if not present
$arrUrl = "https://download.microsoft.com/download/E/9/8/E9849D6A-020E-47E4-9FD0-A023E99B54EB/requestRouter_amd64.msi"
$arrInstaller = "$env:TEMP\arr_installer.msi"

Invoke-WebRequest -Uri $arrUrl -OutFile $arrInstaller
Start-Process msiexec.exe -ArgumentList "/i", $arrInstaller, "/quiet", "/norestart" -Wait
iisreset
Write-Host "✓ ARR installation complete"
```

```powershell
# Enable ARR proxy functionality and configure server variables
Import-Module WebAdministration
Set-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter "system.webServer/proxy" -name "enabled" -value "True"

$serverVars = @('HTTP_X_ORIGINAL_HOST','HTTP_X_FORWARDED_FOR','HTTP_X_FORWARDED_PROTO')
foreach ($var in $serverVars) {
    $exists = Get-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' \
        -filter "system.webServer/rewrite/allowedServerVariables" -name "." | Where-Object { $_.name -eq $var }
    if (-not $exists) {
        Add-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' \
            -filter "system.webServer/rewrite/allowedServerVariables" -name "." -value @{name=$var}
        Write-Host "✓ Added server variable: $var"
    }
}
```

---

### 2. Create JSON Fallback

```powershell
# Create error401.json fallback if missing
$jsonFile = Join-Path -Path (Get-Location) -ChildPath "error401.json"
if (-not (Test-Path $jsonFile)) {
    '{"error":"Authentication required","success":false}' | Out-File -Encoding UTF8 $jsonFile
    Write-Host "✓ Created error401.json fallback file"
} else {
    Write-Host "error401.json already exists" -ForegroundColor Gray
}
```

---

### 3. `web.config` Example

Place this `web.config` in the same folder:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>

    <!-- Enable ARR proxy -->
    <proxy enabled="true" preserveHostHeader="true" reverseRewriteHostInResponseHeaders="false" />

    <rewrite>
      <rules>

        <!-- Optional: redirect /api to /api/ -->
        <rule name="ForceAPITrailingSlash" stopProcessing="true">
          <match url="^api$" />
          <action type="Redirect" url="/api/" redirectType="Permanent" />
        </rule>

        <!-- Proxy /health -->
        <rule name="ProxyHealth" stopProcessing="true">
          <match url="^health$" />
          <action type="Rewrite" url="http://localhost:8085/health" />
          <serverVariables>
            <set name="HTTP_X_ORIGINAL_HOST" value="{HTTP_HOST}" />
            <set name="HTTP_X_FORWARDED_FOR" value="{REMOTE_ADDR}" />
            <set name="HTTP_X_FORWARDED_PROTO" value="http" />
          </serverVariables>
        </rule>

        <!-- Proxy /api and /api/... -->
        <rule name="ProxyAPI" stopProcessing="true">
          <match url="^api(/.*)?$" />
          <action type="Rewrite" url="http://localhost:8085/api{R:1}" />
          <serverVariables>
            <set name="HTTP_X_ORIGINAL_HOST" value="{HTTP_HOST}" />
            <set name="HTTP_X_FORWARDED_FOR" value="{REMOTE_ADDR}" />
            <set name="HTTP_X_FORWARDED_PROTO" value="http" />
          </serverVariables>
        </rule>

        <!-- Block all other requests → serve JSON -->
        <rule name="BlockOther" stopProcessing="true">
          <match url=".*" />
          <conditions>
            <add input="{REQUEST_URI}" pattern="^/health" negate="true" />
            <add input="{REQUEST_URI}" pattern="^/api" negate="true" />
          </conditions>
          <action type="Rewrite" url="/error401.json" />
        </rule>

      </rules>
    </rewrite>

    <!-- Security headers -->
    <httpProtocol>
      <customHeaders>
        <remove name="X-Powered-By" />
        <remove name="X-Content-Type-Options" />
        <remove name="X-Frame-Options" />
        <remove name="Strict-Transport-Security" />
        <remove name="Referrer-Policy" />
        <remove name="Permissions-Policy" />
        <remove name="Content-Security-Policy" />

        <add name="X-Content-Type-Options" value="nosniff" />
        <add name="X-Frame-Options" value="DENY" />
        <add name="Strict-Transport-Security" value="max-age=31536000; includeSubDomains; preload" />
        <add name="Referrer-Policy" value="strict-origin-when-cross-origin" />
        <add name="Permissions-Policy" value="geolocation=(), camera=(), microphone=(), payment=()" />
        <add name="Content-Security-Policy" 
             value="default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; img-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self' http://localhost:8085; manifest-src 'self'; font-src 'self';" />
      </customHeaders>
    </httpProtocol>

  </system.webServer>
</configuration>
```

---

### 4. Testing

```powershell
# Test health endpoint
Invoke-WebRequest http://localhost:8084/health

# Test API
Invoke-WebRequest http://localhost:8084/api/...

# Test blocked path
Invoke-WebRequest http://localhost:8084/anythingelse
```

* `/health` → proxied to backend
* `/api/...` → proxied to backend
* Anything else → returns `error401.json`

---

### ARR Reference

[https://www.iis.net/downloads/microsoft/application-request-routing](https://www.iis.net/downloads/microsoft/application-request-routing)
