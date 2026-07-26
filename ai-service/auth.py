"""Shared-secret auth between the ASP.NET Core app and this service.

Enforced only when AI_SERVICE_API_KEY is set server-side; if it's unset, requests
are allowed through unauthenticated (see the startup warning in main.py). This lets
local dev boot without any keys configured, matching the OPENAI_API_KEY behavior.
"""
import os

from fastapi import Header, HTTPException


def verify_api_key(x_api_key: str | None = Header(default=None)) -> None:
    expected = os.getenv("AI_SERVICE_API_KEY")
    if not expected:
        return
    if x_api_key != expected:
        raise HTTPException(status_code=401, detail="Missing or invalid X-API-Key header.")
