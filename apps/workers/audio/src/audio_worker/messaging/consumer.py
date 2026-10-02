import hashlib
import json
import logging
import os
import re
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path
from typing import Any

import aio_pika
from jsonschema import Draft202012Validator, FormatChecker, ValidationError


REQUEST_ID_PATTERN = re.compile(r"^[A-Za-z0-9_-]{43}$")
VIDEO_ID_PATTERN = re.compile(r"^[A-Za-z0-9_-]{11}$")
logger = logging.getLogger(__name__)


class InvalidRequestMessage(ValueError):
    pass


class TransientProcessingError(Exception):
    pass


@dataclass(frozen=True)
class ExtractionRequest:
    event_id: str
    request_id: str
    video_id: str
    start_seconds: int
    end_seconds: int

    @property
    def duration_seconds(self) -> int:
        return self.end_seconds - self.start_seconds


@lru_cache(maxsize=1)
def _request_schema() -> dict[str, Any]:
    configured = os.getenv("AUDIO_EXTRACTION_SCHEMA_DIR")
    candidates = (
        [Path(configured) / "audio-extraction-requested.schema.json"]
        if configured
        else [
            Path(__file__).resolve().parents[6]
            / "specs/002-youtube-audio-extraction/contracts/events/audio-extraction-requested.schema.json",
            Path("/app/contracts/events/audio-extraction-requested.schema.json"),
        ]
    )
    for candidate in candidates:
        if candidate.is_file():
            return json.loads(candidate.read_text(encoding="utf-8"))
    raise RuntimeError("The audio extraction request schema is unavailable.")


def validate_requested_event(payload: bytes | str | dict[str, Any]) -> ExtractionRequest:
    try:
        if isinstance(payload, bytes):
            event = json.loads(payload, object_pairs_hook=_unique_object)
        elif isinstance(payload, str):
            event = json.loads(payload, object_pairs_hook=_unique_object)
        else:
            event = payload
        Draft202012Validator(
            _request_schema(),
            format_checker=FormatChecker(),
        ).validate(event)
    except (json.JSONDecodeError, UnicodeDecodeError, ValidationError, TypeError, ValueError) as error:
        raise InvalidRequestMessage("The request event does not match the contract.") from error

    data = event["data"]
    start_seconds = data["startSeconds"]
    end_seconds = data["endSeconds"]
    video_id = data["videoId"]
    request_id = event["requestId"]
    if (
        not REQUEST_ID_PATTERN.fullmatch(request_id)
        or not VIDEO_ID_PATTERN.fullmatch(video_id)
        or type(start_seconds) is not int
        or type(end_seconds) is not int
        or start_seconds < 0
        or end_seconds <= start_seconds
        or end_seconds > 21_600
        or end_seconds - start_seconds > 1_800
    ):
        raise InvalidRequestMessage("The request event contains invalid extraction bounds.")

    return ExtractionRequest(
        event_id=event["eventId"],
        request_id=request_id,
        video_id=video_id,
        start_seconds=start_seconds,
        end_seconds=end_seconds,
    )


def _unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in pairs:
        if key in value:
            raise ValueError("Duplicate JSON object key.")
        value[key] = item
    return value


async def consume_requests(
    queue: aio_pika.abc.AbstractQueue,
    exchange: aio_pika.abc.AbstractExchange,
    dead_letter_exchange: aio_pika.abc.AbstractExchange,
    service: Any,
) -> None:
    async def handle(message: aio_pika.IncomingMessage) -> None:
        try:
            request = validate_requested_event(message.body)
        except InvalidRequestMessage:
            digest = hashlib.sha256(message.body).hexdigest()
            poison = {
                "failureCode": "MALFORMED_REQUEST",
                "bodySha256": digest,
                "routingKey": message.routing_key,
            }
            await dead_letter_exchange.publish(
                aio_pika.Message(
                    body=json.dumps(poison, separators=(",", ":")).encode(),
                    content_type="application/json",
                    delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
                ),
                routing_key="audio.extraction.requested.malformed",
                mandatory=True,
            )
            await message.ack()
            logger.warning("Malformed extraction event sent to the dead-letter queue.")
            return

        retry_count = _retry_count(message.headers)
        try:
            await service.process(request)
        except TransientProcessingError:
            await _retry_or_fail(
                message,
                request,
                retry_count,
                exchange,
                service,
            )
            return
        except Exception as error:
            logger.error(
                "Unexpected extraction failure (%s); applying bounded retry policy.",
                type(error).__name__,
            )
            await _retry_or_fail(
                message,
                request,
                retry_count,
                exchange,
                service,
            )
            return

        await message.ack()

    await queue.consume(handle, no_ack=False)


def _retry_count(headers: dict[str, Any] | None) -> int:
    value = (headers or {}).get("x-audio-extraction-retry", 0)
    return value if type(value) is int and value >= 0 else 0


async def _retry_or_fail(
    message: aio_pika.IncomingMessage,
    request: ExtractionRequest,
    retry_count: int,
    exchange: aio_pika.abc.AbstractExchange,
    service: Any,
) -> None:
    if retry_count < 3:
        delay_ms = (5_000, 30_000, 180_000)[retry_count]
        headers = dict(message.headers or {})
        headers["x-audio-extraction-retry"] = retry_count + 1
        try:
            await exchange.publish(
                aio_pika.Message(
                    body=message.body,
                    headers=headers,
                    content_type=message.content_type or "application/json",
                    delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
                    message_id=message.message_id,
                    type=message.type,
                ),
                routing_key=f"audio.extraction.requested.retry.{delay_ms}",
                mandatory=True,
            )
        except Exception:
            logger.error("Worker retry could not be scheduled; delivery remains unacknowledged.")
            await message.nack(requeue=True)
            return
        await message.ack()
        logger.warning("Transient extraction failure scheduled for bounded retry.")
        return

    try:
        await service.publish_retries_exhausted(request)
    except Exception:
        logger.error("Terminal failure could not be published; delivery remains unacknowledged.")
        await message.nack(requeue=True)
        return
    await message.ack()
    logger.error("Extraction retries exhausted; a terminal failure was published.")
