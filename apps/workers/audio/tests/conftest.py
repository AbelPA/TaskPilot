import json
from pathlib import Path

import pytest


REPOSITORY_ROOT = Path(__file__).resolve().parents[4]
SCHEMAS_DIR = REPOSITORY_ROOT / "specs/002-youtube-audio-extraction/contracts/events"
FIXTURES_DIR = REPOSITORY_ROOT / "apps/api/AudioExtractions/Contracts/Fixtures"


@pytest.fixture
def event_schemas() -> dict[str, dict]:
    return {
        path.name.removesuffix(".schema.json"): json.loads(path.read_text())
        for path in sorted(SCHEMAS_DIR.glob("*.schema.json"))
    }


@pytest.fixture
def event_fixtures() -> dict[str, dict]:
    return {
        path.stem: json.loads(path.read_text())
        for path in sorted(FIXTURES_DIR.glob("*.json"))
    }
