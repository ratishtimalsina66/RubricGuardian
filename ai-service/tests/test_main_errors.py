"""Verify that each LLM failure mode maps to a distinct HTTP status/detail,
instead of collapsing into a single generic 502 "The AI model call failed."
"""
import json

import httpx
import openai
import pytest
from fastapi.testclient import TestClient

import main

client = TestClient(main.app)

_REQUEST = httpx.Request("POST", "https://api.openai.com/v1/chat/completions")


def _auth_error():
    return openai.AuthenticationError("bad key", response=httpx.Response(401, request=_REQUEST), body=None)


def _rate_limit_error():
    return openai.RateLimitError("rate limited", response=httpx.Response(429, request=_REQUEST), body=None)


def _insufficient_quota_error():
    return openai.RateLimitError(
        "quota exceeded",
        response=httpx.Response(429, request=_REQUEST),
        body={"message": "quota exceeded", "type": "insufficient_quota", "param": None, "code": "insufficient_quota"},
    )


def _timeout_error():
    return openai.APITimeoutError(request=_REQUEST)


def _connection_error():
    return openai.APIConnectionError(message="connection error", request=_REQUEST)


def _json_error():
    return json.JSONDecodeError("Expecting value", "not json", 0)


def _missing_credentials_error():
    # What the OpenAI SDK actually raises when constructing the client with an empty/unset
    # api_key - a client-construction-time OpenAIError, not an HTTP-level AuthenticationError.
    return openai.OpenAIError(
        "Missing credentials. Please pass an `api_key` ... or set the `OPENAI_API_KEY` "
        "environment variable."
    )


CASES = [
    (_auth_error, 502, "OPENAI_API_KEY"),
    (_rate_limit_error, 429, "rate-limited"),
    (_insufficient_quota_error, 429, "quota/credits"),
    (_timeout_error, 504, "did not respond in time"),
    (_connection_error, 502, "Could not reach the AI provider"),
    (_json_error, 502, "could not be parsed as JSON"),
    (_missing_credentials_error, 502, "OPENAI_API_KEY"),
    (lambda: RuntimeError("boom"), 500, "unexpected error"),
]


@pytest.fixture(autouse=True)
def _no_auth_required(monkeypatch):
    monkeypatch.delenv("AI_SERVICE_API_KEY", raising=False)


@pytest.mark.parametrize("make_exc,expected_status,detail_substring", CASES)
def test_extract_requirements_error_mapping(monkeypatch, make_exc, expected_status, detail_substring):
    def raiser(text, document_type):
        raise make_exc()

    monkeypatch.setattr(main, "extract_requirements", raiser)
    resp = client.post(
        "/extract-requirements", json={"text": "some document text", "document_type": "Instructions"}
    )
    assert resp.status_code == expected_status
    assert detail_substring in resp.json()["detail"]


@pytest.mark.parametrize("make_exc,expected_status,detail_substring", CASES)
def test_evaluate_error_mapping(monkeypatch, make_exc, expected_status, detail_substring):
    def raiser(requirements, submission_text):
        raise make_exc()

    monkeypatch.setattr(main, "evaluate_requirements", raiser)
    resp = client.post(
        "/evaluate",
        json={
            "requirements": [{"requirement_id": 1, "requirement_text": "Has a title"}],
            "submission_text": "My submission text",
        },
    )
    assert resp.status_code == expected_status
    assert detail_substring in resp.json()["detail"]


def test_extract_requirements_empty_text_is_400(monkeypatch):
    resp = client.post("/extract-requirements", json={"text": "   ", "document_type": "Instructions"})
    assert resp.status_code == 400


def test_evaluate_success_still_returns_200(monkeypatch):
    monkeypatch.setattr(main, "evaluate_requirements", lambda requirements, submission_text: [])
    resp = client.post(
        "/evaluate",
        json={
            "requirements": [{"requirement_id": 1, "requirement_text": "Has a title"}],
            "submission_text": "My submission text",
        },
    )
    assert resp.status_code == 200
    assert resp.json() == {"evaluations": []}
