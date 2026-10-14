from datetime import datetime, timedelta, timezone


RETENTION_DAYS = 7


def expiration_metadata(completed_at: datetime | None = None) -> str:
    completed = completed_at or datetime.now(timezone.utc)
    if completed.tzinfo is None or completed.utcoffset() is None:
        raise ValueError("The completion timestamp must include a timezone.")
    return (completed.astimezone(timezone.utc) + timedelta(days=RETENTION_DAYS)).isoformat()
