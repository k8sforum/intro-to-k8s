#!/usr/bin/env python3
import json
import os

# Read from environment variables
session_id = os.getenv("CLAUDE_SESSION_ID", "unknown")
hook_event_name = os.getenv("CLAUDE_EVENT_NAME", "PostToolUse")
tool_name = os.getenv("CLAUDE_TOOL_NAME", "unknown")
tool_input_str = os.getenv("CLAUDE_TOOL_INPUT", "{}")
tool_response_str = os.getenv("CLAUDE_TOOL_RESPONSE", "{}")

try:
    tool_input = json.loads(tool_input_str)
except json.JSONDecodeError:
    tool_input = {}

try:
    tool_response = json.loads(tool_response_str)
except json.JSONDecodeError:
    tool_response = {}

log_entry = {
    "session_id": session_id,
    "hook_event_name": hook_event_name,
    "tool_name": tool_name,
    "tool_input": tool_input,
    "tool_response": tool_response
}

with open("tool_use_log.json", "a") as f:
    f.write(json.dumps(log_entry) + "\n")