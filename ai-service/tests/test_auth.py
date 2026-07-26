"""Verify the shared-secret X-API-Key check: enforced only when AI_SERVICE_API_KEY
is configured, and never required for /health.
"""
from fastapi.testclient import TestClient

import main

client = TestClient(main.app)


def _stub_llm(monkeypatch):
    monkeypatch.setattr(main, "extract_requirements", lambda text, document_type: [])
    monkeypatch.setattr(main, "evaluate_requirements", lambda requirements, submission_text: [])


def test_health_never_requires_auth(monkeypatch):
    monkeypatch.setenv("AI_SERVICE_API_KEY", "secret")
    resp = client.get("/health")
    assert resp.status_code == 200


def test_protected_endpoints_reject_missing_header_when_key_set(monkeypatch):
    monkeypatch.setenv("AI_SERVICE_API_KEY", "secret")
    _stub_llm(monkeypatch)

    resp = client.post("/extract-requirements", json={"text": "some text", "document_type": "Instructions"})
    assert resp.status_code == 401

    resp = client.post(
        "/evaluate",
        json={"requirements": [{"requirement_id": 1, "requirement_text": "x"}], "submission_text": "y"},
    )
    assert resp.status_code == 401

    resp = client.post("/extract-text", files={"file": ("doc.txt", b"hello world", "text/plain")})
    assert resp.status_code == 401


def test_protected_endpoints_reject_wrong_header_when_key_set(monkeypatch):
    monkeypatch.setenv("AI_SERVICE_API_KEY", "secret")
    _stub_llm(monkeypatch)

    resp = client.post(
        "/extract-requirements",
        json={"text": "some text", "document_type": "Instructions"},
        headers={"X-API-Key": "wrong"},
    )
    assert resp.status_code == 401


def test_protected_endpoints_accept_correct_header_when_key_set(monkeypatch):
    monkeypatch.setenv("AI_SERVICE_API_KEY", "secret")
    _stub_llm(monkeypatch)

    resp = client.post(
        "/extract-requirements",
        json={"text": "some text", "document_type": "Instructions"},
        headers={"X-API-Key": "secret"},
    )
    assert resp.status_code == 200


def test_protected_endpoints_allow_unauthenticated_when_key_unset(monkeypatch):
    monkeypatch.delenv("AI_SERVICE_API_KEY", raising=False)
    _stub_llm(monkeypatch)

    resp = client.post("/extract-requirements", json={"text": "some text", "document_type": "Instructions"})
    assert resp.status_code == 200
