"""LLM calls for requirement extraction and submission evaluation.

Works with any OpenAI-compatible API (OpenAI, Azure OpenAI, Ollama, LM Studio, etc.)
via three environment variables:

    OPENAI_API_KEY    - API key (use "not-needed" for local servers)
    OPENAI_BASE_URL   - optional; defaults to the official OpenAI endpoint
    OPENAI_MODEL      - model name; defaults to gpt-4o-mini
"""
import json
import logging
import os
import re

from openai import OpenAI

from schemas import EvaluationResult, ExtractedRequirement, RequirementInput

log = logging.getLogger("rubricguardian.llm")

MODEL = os.getenv("OPENAI_MODEL", "gpt-4o-mini")
SUBMISSION_CHAR_LIMIT = 60_000   # per evaluation call; long docs are truncated for the MVP
EVAL_BATCH_SIZE = 8              # requirements evaluated per LLM call

_client: OpenAI | None = None


def _get_client() -> OpenAI:
    global _client
    if _client is None:
        _client = OpenAI(
            api_key=os.getenv("OPENAI_API_KEY", "not-set"),
            base_url=os.getenv("OPENAI_BASE_URL") or None,
        )
    return _client


# ---------------------------------------------------------------------------
# Prompt 1: requirement extraction
# ---------------------------------------------------------------------------
EXTRACTION_SYSTEM_PROMPT = """\
You extract gradeable requirements from assignment instructions or grading rubrics.

Hard rules:
1. NEVER invent requirements. Every requirement must be directly stated in the document text.
2. If the document contains no gradeable requirements, return an empty list.
3. Split compound requirements ("must have a title AND a date") into separate items when they
   would be graded separately; keep them together when the rubric grades them as one item.
4. Copy point values only if the document states them; otherwise use null.
5. Mark is_required = false only for items the document labels optional, bonus, or extra credit.
6. Choose a short category for each item, e.g. Content, Formatting, Functionality,
   Code Quality, Documentation, Citations, Length, Submission Logistics, Bonus.

Respond with ONLY valid JSON in this exact shape, no markdown fences, no commentary:
{"requirements": [{"requirement_text": "...", "category": "...", "points": 10 or null, "is_required": true}]}
"""

EXTRACTION_USER_TEMPLATE = """\
Document type: {document_type}

Document text:
---
{text}
---

Extract the requirements as JSON."""


def extract_requirements(text: str, document_type: str) -> list[ExtractedRequirement]:
    raw = _chat_json(
        system=EXTRACTION_SYSTEM_PROMPT,
        user=EXTRACTION_USER_TEMPLATE.format(document_type=document_type, text=text),
    )
    items = raw.get("requirements", [])
    results: list[ExtractedRequirement] = []
    for item in items:
        try:
            results.append(ExtractedRequirement(**item))
        except Exception:  # noqa: BLE001
            log.warning("Skipping malformed requirement item: %r", item)
    return results


# ---------------------------------------------------------------------------
# Prompt 2: submission evaluation
# ---------------------------------------------------------------------------
EVALUATION_SYSTEM_PROMPT = """\
You check a student's submission against a list of assignment requirements before they submit.

For EACH requirement, decide one status:
- "Complete": clear, direct evidence in the submission fully satisfies the requirement.
- "Partial": some evidence exists but the requirement is only partly satisfied.
- "Missing": no evidence of the requirement was found in the submission.
- "Unclear": the submission text is ambiguous, or the requirement cannot be verified from
  text alone (e.g. "presentation must last 5 minutes"). When evidence is weak, prefer
  Partial or Unclear over Complete. Never guess Complete.

For each requirement also produce:
- evidence_text: a short quote (max ~40 words) copied from the submission that supports your
  status. Use null when status is Missing or nothing relevant exists. Never fabricate quotes.
- confidence_score: 0.0-1.0, how confident you are in the status.
- risk_level: how much this item threatens the grade if left as-is.
  "High" = required item that is Missing, or Partial on a core deliverable.
  "Medium" = Partial/Unclear items that likely cost points.
  "Low" = Complete items, optional/bonus items, or minor polish.
- feedback: one or two concise sentences explaining the status. Practical, not padded.
- fix_suggestion: one concrete action the student can take. For Complete items,
  say "No change needed." or suggest a quick verification.

Respond with ONLY valid JSON, no markdown fences, no commentary:
{"evaluations": [{"requirement_id": 1, "status": "Complete", "evidence_text": "..." or null,
"confidence_score": 0.9, "risk_level": "Low", "feedback": "...", "fix_suggestion": "..."}]}

Include every requirement_id you were given exactly once.
"""

EVALUATION_USER_TEMPLATE = """\
Requirements to check (JSON):
{requirements_json}

Student submission text:
---
{submission_text}
---

Evaluate every requirement and respond as JSON."""


def evaluate_requirements(
    requirements: list[RequirementInput], submission_text: str
) -> list[EvaluationResult]:
    submission_text = submission_text[:SUBMISSION_CHAR_LIMIT]
    results: list[EvaluationResult] = []

    for start in range(0, len(requirements), EVAL_BATCH_SIZE):
        batch = requirements[start : start + EVAL_BATCH_SIZE]
        payload = [
            {
                "requirement_id": r.requirement_id,
                "requirement_text": r.requirement_text,
                "category": r.category,
                "is_required": r.is_required,
            }
            for r in batch
        ]
        raw = _chat_json(
            system=EVALUATION_SYSTEM_PROMPT,
            user=EVALUATION_USER_TEMPLATE.format(
                requirements_json=json.dumps(payload, indent=2),
                submission_text=submission_text,
            ),
        )
        returned = {item.get("requirement_id"): item for item in raw.get("evaluations", [])}

        for r in batch:
            item = returned.get(r.requirement_id)
            if item is None:
                results.append(_fallback_result(r, "The model did not return a result for this item."))
                continue
            try:
                results.append(EvaluationResult(**item))
            except Exception:  # noqa: BLE001
                log.warning("Malformed evaluation for requirement %s: %r", r.requirement_id, item)
                results.append(_fallback_result(r, "The model returned an unreadable result for this item."))

    return results


def _fallback_result(r: RequirementInput, reason: str) -> EvaluationResult:
    return EvaluationResult(
        requirement_id=r.requirement_id,
        status="Unclear",
        evidence_text=None,
        confidence_score=0.0,
        risk_level="Medium" if r.is_required else "Low",
        feedback=reason,
        fix_suggestion="Re-run the check, or verify this requirement manually.",
    )


# ---------------------------------------------------------------------------
# Shared plumbing
# ---------------------------------------------------------------------------
def _chat_json(system: str, user: str) -> dict:
    response = _get_client().chat.completions.create(
        model=MODEL,
        temperature=0.1,
        messages=[
            {"role": "system", "content": system},
            {"role": "user", "content": user},
        ],
    )
    content = response.choices[0].message.content or ""
    return _parse_json(content)


def _parse_json(content: str) -> dict:
    """Parse the model output as JSON, tolerating markdown fences."""
    content = content.strip()
    content = re.sub(r"^```(?:json)?\s*", "", content)
    content = re.sub(r"\s*```$", "", content)
    try:
        return json.loads(content)
    except json.JSONDecodeError:
        match = re.search(r"\{.*\}", content, re.DOTALL)
        if match:
            return json.loads(match.group(0))
        raise
