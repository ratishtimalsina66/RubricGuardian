# RubricGuardian — AI Prompts

Both prompts live in `ai-service/llm.py`. This file explains the intent behind each one
so you can tune them without breaking the guarantees the app depends on.

---

## Prompt 1: Requirement extraction

**Goal:** Turn a rubric or instruction document into structured requirements —
without inventing anything.

**System prompt (from `llm.py`):**

```
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
```

**User message template:**

```
Document type: {Instructions | Rubric}

Document text:
---
{extracted document text}
---

Extract the requirements as JSON.
```

**Why it's shaped this way:**
- Rule 1 + 2 enforce the app's core promise: every requirement traces to the uploaded document.
- Rule 4 prevents hallucinated point values (a common failure mode).
- Temperature is set to 0.1 for consistent extraction across re-runs.
- The response is parsed defensively (`_parse_json`) in case a model wraps output in ``` fences.

---

## Prompt 2: Submission evaluation

**Goal:** For each requirement, find real evidence in the submission, assign a status,
and produce one practical fix.

**System prompt (from `llm.py`):**

```
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
```

**User message template:**

```
Requirements to check (JSON):
[{"requirement_id": 12, "requirement_text": "...", "category": "...", "is_required": true}, ...]

Student submission text:
---
{extracted submission text}
---

Evaluate every requirement and respond as JSON.
```

**Why it's shaped this way:**
- "Never guess Complete" + the Unclear category implement the app rule: weak evidence
  must never inflate readiness.
- Requirements are sent in batches of 8 (`EVAL_BATCH_SIZE`) so long rubrics don't cause
  dropped items or context overflow; missing/malformed items get a safe "Unclear" fallback.
- `requirement_id` round-trips through the model so results map back to database rows —
  the app discards any IDs the model invents.
- Evidence must be quoted from the submission, which powers the traceability column
  in the readiness report.

---

## Tuning tips

- **Stricter grading:** add "When a requirement demands a specific count or number
  (e.g. 'at least 5 sources'), verify the exact count before marking Complete."
- **Different course styles:** extend the category list in Prompt 1 (e.g. "Lab Safety",
  "Methodology") for science courses.
- **Cheaper runs:** raise `EVAL_BATCH_SIZE`, or point `OPENAI_BASE_URL` at a local
  model via Ollama/LM Studio.
