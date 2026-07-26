"""Regression test: an empty-but-set OPENAI_BASE_URL (as `.env`'s OPENAI_BASE_URL= line
produces via python-dotenv) must resolve to the official OpenAI endpoint, not an empty
string. The openai SDK re-reads os.environ["OPENAI_BASE_URL"] itself when base_url=None is
passed, so `"" or None` in our own code isn't enough to avoid it - a real request would fail
with a confusing "missing scheme" connection error otherwise.
"""
import llm


def test_empty_base_url_env_var_resolves_to_default(monkeypatch):
    monkeypatch.setenv("OPENAI_BASE_URL", "")
    monkeypatch.setenv("OPENAI_API_KEY", "sk-test")
    monkeypatch.setattr(llm, "_client", None)

    client = llm._get_client()

    assert str(client.base_url) == llm.DEFAULT_BASE_URL + "/"


def test_unset_base_url_env_var_resolves_to_default(monkeypatch):
    monkeypatch.delenv("OPENAI_BASE_URL", raising=False)
    monkeypatch.setenv("OPENAI_API_KEY", "sk-test")
    monkeypatch.setattr(llm, "_client", None)

    client = llm._get_client()

    assert str(client.base_url) == llm.DEFAULT_BASE_URL + "/"


def test_custom_base_url_env_var_is_respected(monkeypatch):
    monkeypatch.setenv("OPENAI_BASE_URL", "http://localhost:11434/v1")
    monkeypatch.setenv("OPENAI_API_KEY", "not-needed")
    monkeypatch.setattr(llm, "_client", None)

    client = llm._get_client()

    assert str(client.base_url) == "http://localhost:11434/v1/"
