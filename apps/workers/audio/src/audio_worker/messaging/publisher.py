import json
import uuid
from datetime import datetime, timezone
from typing import Any

import aio_pika
from opentelemetry.propagate import inject

from audio_worker.messaging.consumer import ExtractionRequest
from audio_worker.storage.audio_storage import StoredAudio


TERMINAL_NAMESPACE = uuid.UUID("dc3e24af-8904-5f5e-92b7-98f5af8987de")
COMPLETED_MESSAGE = "A extração de áudio solicitada foi concluída."
FAILED_MESSAGES = {
    "VIDEO_UNAVAILABLE": "O vídeo não está disponível para processamento.",
    "DOWNLOAD_FAILED": "Não foi possível obter o áudio do vídeo.",
    "EXTRACTION_FAILED": "Não foi possível extrair o intervalo solicitado.",
    "STORAGE_FAILED": "Não foi possível armazenar o áudio extraído.",
    "RETRIES_EXHAUSTED": "A extração não foi concluída após novas tentativas.",
}


async def publish_completed(
    exchange: aio_pika.abc.AbstractExchange,
    request: ExtractionRequest,
    audio: StoredAudio,
) -> None:
    await _publish(
        exchange,
        request,
        "audio.extraction.completed",
        {
            "objectKey": audio.object_key,
            "contentType": audio.content_type,
            "durationSeconds": audio.duration_seconds,
            "sizeBytes": audio.size_bytes,
            "checksumSha256": audio.checksum_sha256,
        },
    )


async def publish_failed(
    exchange: aio_pika.abc.AbstractExchange,
    request: ExtractionRequest,
    code: str,
) -> None:
    if code not in FAILED_MESSAGES:
        raise ValueError("The extraction failure code is not part of the public contract.")
    await _publish(
        exchange,
        request,
        "audio.extraction.failed",
        {
            "code": code,
            "message": FAILED_MESSAGES[code],
            "retryable": False,
        },
    )


async def _publish(
    exchange: aio_pika.abc.AbstractExchange,
    request: ExtractionRequest,
    event_type: str,
    data: dict[str, Any],
) -> None:
    event_id = str(uuid.uuid5(TERMINAL_NAMESPACE, f"{request.request_id}:{event_type}"))
    payload = {
        "eventId": event_id,
        "eventType": event_type,
        "schemaVersion": 1,
        "requestId": request.request_id,
        "occurredAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "data": data,
    }
    headers: dict[str, str] = {}
    inject(headers)
    await exchange.publish(
        aio_pika.Message(
            body=json.dumps(payload, separators=(",", ":")).encode(),
            headers=headers or None,
            content_type="application/json",
            delivery_mode=aio_pika.DeliveryMode.PERSISTENT,
            message_id=event_id,
            type=event_type,
        ),
        routing_key=event_type,
        mandatory=True,
    )
