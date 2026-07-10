"""RubricGuardian AI Service.

Endpoints:
  POST /extract-text          multipart file -> {"text": "..."}
  POST /extract-requirements  {"text", "document_type"} -> {"requirements": [...]}
  POST /evaluate              {"requirements", "submission_text"} -> {"evaluations": [...]}

Run:  uvicorn main:app --port 8000
"""
import logging

from fastapi import FastAPI, File, HTTPException, UploadFile

from extraction import extract_text_from_upload
from llm import evaluate_requirements, extract_requirements
from schemas import (
    EvaluateRequest,
    EvaluateResponse,
    ExtractRequirementsRequest,
    ExtractRequirementsResponse,
    ExtractTextResponse,
)

logging.basicConfig(level=logging.INFO)
log = logging.getLogger("rubricguardian.ai")

app = FastAPI(title="RubricGuardian AI Service", version="1.0.0")


@app.get("/health")
def health() -> dict:
    return {"status": "ok"}


@app.post("/extract-text", response_model=ExtractTextResponse)
async def extract_text(file: UploadFile = File(...)) -> ExtractTextResponse:
    """Step 1/4 of the workflow: turn an uploaded document into plain text."""
    try:
        data = await file.read()
        text = extract_text_from_upload(file.filename or "upload", data)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except Exception as exc:  # noqa: BLE001
        log.exception("Text extraction failed")
        raise HTTPException(status_code=500, detail="Could not read the file.") from exc
    return ExtractTextResponse(text=text)


@app.post("/extract-requirements", response_model=ExtractRequirementsResponse)
def extract_requirements_endpoint(req: ExtractRequirementsRequest) -> ExtractRequirementsResponse:
    """Steps 2-3: convert rubric/instructions text into structured requirements.

    The prompt forbids inventing requirements: everything must come from the text.
    """
    if not req.text.strip():
        raise HTTPException(status_code=400, detail="Document text is empty.")
    try:
        requirements = extract_requirements(req.text, req.document_type)
    except Exception as exc:  # noqa: BLE001
        log.exception("Requirement extraction failed")
        raise HTTPException(status_code=502, detail="The AI model call failed.") from exc
    return ExtractRequirementsResponse(requirements=requirements)


@app.post("/evaluate", response_model=EvaluateResponse)
def evaluate_endpoint(req: EvaluateRequest) -> EvaluateResponse:
    """Steps 5-7: match evidence, assign status, generate feedback + fix suggestion."""
    if not req.requirements:
        raise HTTPException(status_code=400, detail="No requirements supplied.")
    if not req.submission_text.strip():
        raise HTTPException(status_code=400, detail="Submission text is empty.")
    try:
        evaluations = evaluate_requirements(req.requirements, req.submission_text)
    except Exception as exc:  # noqa: BLE001
        log.exception("Evaluation failed")
        raise HTTPException(status_code=502, detail="The AI model call failed.") from exc
    return EvaluateResponse(evaluations=evaluations)
