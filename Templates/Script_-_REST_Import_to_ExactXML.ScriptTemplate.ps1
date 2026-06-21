# Post-process script for an Import Profile whose Source is a REST API
# (Reef's built-in Http source type) and whose Target is a staging table in
# a SQL Server database. After Reef finishes the import, this script:
#   1. Reads the staged rows that haven't been pushed to Exact yet
#   2. Writes them out as an Exact-compatible import XML file
#   3. Calls ExactXML.exe (Exact.XML.Launcher.exe) to import that XML into
#      Exact Globe+ / Synergy
#   4. Marks the staged rows as exported on success
#
# IMPORTANT: ExactXML.exe's exact command-line flags and the XML schema it
# expects depend on the Exact XML import/export *definition* configured on
# your Exact side (created with Exact.XML.Launcher.Tools.exe) - they are not
# publicly documented and aren't guessed here. Confirm both against:
#   - `ExactXML.exe /?` on the machine where it's installed
#   - The XML definition exported from Exact.XML.Launcher.Tools.exe for your
#     specific import scenario
# Env Allowlist for this step: STAGING_DB_CONNSTRING, EXACTXML_EXE_PATH,
# EXACTXML_USERNAME, EXACTXML_PASSWORD

$ErrorActionPreference = "Stop"

$context = [Console]::In.ReadToEnd() | ConvertFrom-Json

$connString = $env:STAGING_DB_CONNSTRING
$exactXmlExe = $env:EXACTXML_EXE_PATH
if (-not $connString -or -not $exactXmlExe) {
    Write-Error "STAGING_DB_CONNSTRING and EXACTXML_EXE_PATH must be set"
    exit 1
}

$workDir = Join-Path $env:TEMP "reef-exactxml-$($context.ExecutionId)"
New-Item -ItemType Directory -Force -Path $workDir | Out-Null
$xmlPath = Join-Path $workDir "import.xml"
$logPath = Join-Path $workDir "exactxml.log"

# 1. Pull staged, not-yet-exported rows for this run
Add-Type -AssemblyName "System.Data"
$conn = New-Object System.Data.SqlClient.SqlConnection $connString
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT * FROM StagingItems WHERE Exported = 0"
$reader = $cmd.ExecuteReader()

# 2. Build the Exact import XML.
# Replace <Item>/<Code>/<Description> below with the schema your Exact XML
# import definition actually expects - this is a placeholder shape.
$xmlWriter = New-Object System.Xml.XmlTextWriter($xmlPath, [System.Text.Encoding]::UTF8)
$xmlWriter.WriteStartDocument()
$xmlWriter.WriteStartElement("eExact")
$xmlWriter.WriteStartElement("Items")
$rowCount = 0
while ($reader.Read()) {
    $xmlWriter.WriteStartElement("Item")
    $xmlWriter.WriteElementString("Code", [string]$reader["Code"])
    $xmlWriter.WriteElementString("Description", [string]$reader["Description"])
    $xmlWriter.WriteEndElement()
    $rowCount++
}
$xmlWriter.WriteEndElement()
$xmlWriter.WriteEndElement()
$xmlWriter.WriteEndDocument()
$xmlWriter.Close()
$reader.Close()

if ($rowCount -eq 0) {
    Write-Output "No pending rows to push to Exact"
    $conn.Close()
    exit 0
}

# 3. Call ExactXML.exe. Flags below are illustrative only - confirm against
# your installation (`ExactXML.exe /?`) before relying on them.
$exactArgs = @(
    "/S", $xmlPath,
    "/L", $logPath,
    "/U", $env:EXACTXML_USERNAME,
    "/P", $env:EXACTXML_PASSWORD,
    "/Silent"
)
$proc = Start-Process -FilePath $exactXmlExe -ArgumentList $exactArgs -Wait -PassThru -NoNewWindow
if (Test-Path $logPath) { Get-Content $logPath | Write-Output }

if ($proc.ExitCode -ne 0) {
    Write-Error "ExactXML.exe exited with code $($proc.ExitCode) - see log above"
    $conn.Close()
    exit $proc.ExitCode
}

# 4. Mark rows exported only after ExactXML.exe confirms success
$update = $conn.CreateCommand()
$update.CommandText = "UPDATE StagingItems SET Exported = 1 WHERE Exported = 0"
$update.ExecuteNonQuery() | Out-Null
$conn.Close()

Write-Output "Pushed $rowCount row(s) to Exact via ExactXML.exe"
