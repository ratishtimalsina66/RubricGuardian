import pytest

from extraction import MAX_CHARS, extract_text_from_upload


def test_unsupported_extension_raises():
    with pytest.raises(ValueError, match="Unsupported file type"):
        extract_text_from_upload("submission.zip", b"anything")


def test_empty_text_raises():
    with pytest.raises(ValueError, match="No readable text"):
        extract_text_from_upload("submission.txt", b"   \n\n  ")


def test_oversized_text_is_truncated():
    huge = ("line of text\n" * ((MAX_CHARS // len("line of text\n")) + 100)).encode("utf-8")
    text = extract_text_from_upload("submission.txt", huge)
    assert len(text) <= MAX_CHARS


def test_txt_round_trips_plain_content():
    text = extract_text_from_upload("submission.txt", b"Hello world\n\nSecond paragraph.")
    assert "Hello world" in text
    assert "Second paragraph." in text


def test_invalid_utf8_bytes_do_not_crash():
    text = extract_text_from_upload("submission.txt", b"valid text \xff\xfe more text")
    assert "valid text" in text
