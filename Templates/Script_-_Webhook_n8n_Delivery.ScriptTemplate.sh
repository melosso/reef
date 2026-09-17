#!/bin/sh
# Post-process script for a Profile. Reef writes the execution context as
# JSON on stdin; this script reads it and forwards it to an n8n/webhook
# endpoint. Set Env Allowlist to N8N_WEBHOOK_URL when configuring this as a
# post-process Script step.

set -eu

CONTEXT=$(cat)
URL="${N8N_WEBHOOK_URL:?N8N_WEBHOOK_URL is not set - add it to the Script step's Env Allowlist}"

curl -sS -f -X POST "$URL" \
  -H "Content-Type: application/json" \
  -d "$CONTEXT"
