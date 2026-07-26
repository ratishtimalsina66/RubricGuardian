"""RubricGuardian AI Service.

Endpoints:
  POST /extract-text          multipart file -> {"text": "..."}
  POST /extract-requirements  {"text", "document_type"} -> {"requirements": [...]}
  POST /evaluate              {"requirements", "submission_text"} -> {"evaluations": [...]}

Run:  uvicorn main:app --port 8000
"""
import logging
import os
from contextlib import asynccontextmanager

from dotenv import load_dotenv

load_dotenv()  # populate os.environ from ai-service/.env before any project module reads env vars

from fastapi import Depends, FastAPI, File, HTTPException, UploadFile

from auth import verify_api_key
from extraction import extract_text_from_upload
from llm import evaluate_requirements, extract_requirements, translate_llm_error
from schemas import (
    EvaluateRequest,
    EvaluateResponse,
    ExtractRequirementsRequest,
    ExtractRequirementsResponse,
    ExtractTextResponse,
)

logging.basicConfig(level=logging.INFO)
log = logging.getLogger("rubricguardian.ai")


@asynccontextmanager
async def lifespan(app: FastAPI):
    key = os.getenv("OPENAI_API_KEY", "not-set")
    if not key or key == "not-set":
        log.warning(
            "OPENAI_API_KEY is not set (or is the placeholder 'not-set'). Calls to "
            "/extract-requirements and /evaluate will fail with a 502 until a valid key "
            "is configured - see ai-service/.env.example."
        )
    if not os.getenv("AI_SERVICE_API_KEY"):
        log.warning(
            "AI_SERVICE_API_KEY is not set. /extract-text, /extract-requirements, and "
            "/evaluate are UNAUTHENTICATED. Do not expose port 8000 beyond localhost "
            "until this is configured."
        )
    yield


app = FastAPI(title="RubricGuardian AI Service", version="1.0.0", lifespan=lifespan)


@app.get("/health")
def health() -> dict:
    return {"status": "ok"}


@app.post("/extract-text", response_model=ExtractTextResponse, dependencies=[Depends(verify_api_key)])
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


@app.post(
    "/extract-requirements",
    response_model=ExtractRequirementsResponse,
    dependencies=[Depends(verify_api_key)],
)
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
        status, detail = translate_llm_error(exc)
        raise HTTPException(status_code=status, detail=detail) from exc
    return ExtractRequirementsResponse(requirements=requirements)


@app.post("/evaluate", response_model=EvaluateResponse, dependencies=[Depends(verify_api_key)])
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
        status, detail = translate_llm_error(exc)
        raise HTTPException(status_code=status, detail=detail) from exc
    return EvaluateResponse(evaluations=evaluations)
