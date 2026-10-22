// Post-process script for a Profile. Reads the execution context from
// stdin and POSTs it to a REST ERP endpoint (e.g. to confirm an export
// landed, or push a summary row into the ERP). Set Env Allowlist to
// ERP_API_URL and ERP_API_KEY when configuring this as a post-process
// Script step.

const ERP_API_URL = process.env.ERP_API_URL;
const ERP_API_KEY = process.env.ERP_API_KEY;

if (!ERP_API_URL || !ERP_API_KEY) {
  console.error("ERP_API_URL and ERP_API_KEY must be set");
  process.exit(1);
}

let raw = "";
process.stdin.on("data", chunk => raw += chunk);
process.stdin.on("end", async () => {
  const context = JSON.parse(raw);

  const body = {
    externalReference: `reef-execution-${context.ExecutionId}`,
    status: context.Status,
    rowCount: context.RowCount,
    outputPath: context.OutputPath,
    completedAt: context.CompletedAt
  };

  const response = await fetch(ERP_API_URL, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Authorization": `Bearer ${ERP_API_KEY}`
    },
    body: JSON.stringify(body)
  });

  if (!response.ok) {
    console.error(`ERP call failed: ${response.status} ${await response.text()}`);
    process.exit(1);
  }

  console.log(`ERP call succeeded: ${response.status}`);
});
