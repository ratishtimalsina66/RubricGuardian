from typing import Literal, Optional

from pydantic import BaseModel, Field


class ExtractTextResponse(BaseModel):
    text: str


class ExtractRequirementsRequest(BaseModel):
    text: str
    document_type: str = "Instructions"  # "Instructions" or "Rubric"


class ExtractedRequirement(BaseModel):
    requirement_text: str
    category: str = "General"
    points: Optional[float] = None
    is_required: bool = True


class ExtractRequirementsResponse(BaseModel):
    requirements: list[ExtractedRequirement]


class RequirementInput(BaseModel):
    requirement_id: int
    requirement_text: str
    category: str = "General"
    is_required: bool = True


class EvaluateRequest(BaseModel):
    requirements: list[RequirementInput]
    submission_text: str


class EvaluationResult(BaseModel):
    requirement_id: int
    status: Literal["Complete", "Partial", "Missing", "Unclear"]
    evidence_text: Optional[str] = None
    confidence_score: float = Field(ge=0.0, le=1.0)
    risk_level: Literal["Low", "Medium", "High"]
    feedback: str
    fix_suggestion: str


class EvaluateResponse(BaseModel):
    evaluations: list[EvaluationResult]
