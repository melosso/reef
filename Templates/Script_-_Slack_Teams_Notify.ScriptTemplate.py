#!/usr/bin/env python3
"""
Post-process script for a Profile. Reads the execution context from stdin
and posts a short notification to Slack or Microsoft Teams via an incoming
webhook URL. Set Env Allowlist to SLACK_WEBHOOK_URL (or TEAMS_WEBHOOK_URL)
when configuring this as a post-process Script step.
"""
import json
import os
import sys
import urllib.request

context = json.load(sys.stdin)

webhook_url = os.environ.get("SLACK_WEBHOOK_URL") or os.environ.get("TEAMS_WEBHOOK_URL")
if not webhook_url:
    print("SLACK_WEBHOOK_URL or TEAMS_WEBHOOK_URL is not set", file=sys.stderr)
    sys.exit(1)

status = context.get("Status", "Unknown")
row_count = context.get("RowCount", 0)
profile_id = context.get("ProfileId")
output_path = context.get("OutputPath") or "(no file)"

text = f"Reef export {status}: profile {profile_id}, {row_count} rows -> {output_path}"

# Slack and Teams incoming webhooks both accept {"text": "..."}
payload = json.dumps({"text": text}).encode("utf-8")
req = urllib.request.Request(webhook_url, data=payload, headers={"Content-Type": "application/json"})

with urllib.request.urlopen(req, timeout=10) as resp:
    print(f"Notification sent, webhook responded {resp.status}")
