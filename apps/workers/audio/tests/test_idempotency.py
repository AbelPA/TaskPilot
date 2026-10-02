import json
from datetime import datetime, timezone
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator, FormatChecker

from audio_worker.config import WorkerSettings
from audio_worker.messaging.consumer import (
    ExtractionRequest,
    TransientProcessingError,
    consume_requests,
)
from audio_worker.messaging.publisher import publish_completed
from audio_worker.processing.audio_extraction_service import AudioExtractionService
from audio_worker.messaging.publisher import publish_failed
from audio_worker.storage.models import StoredAudio


REQUEST_ID = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
REQUEST = ExtractionRequest(
    event_id="f75a33e4-2fab-4b9f-8ae4-75d3462d8624",
    request_id=REQUEST_ID,
    video_id="abc_1234567",
    start_seconds=7,
    end_seconds=19,
)
EVENT = {
    "eventId": REQUEST.event_id,
    "eventType": "audio.extraction.requested",
    "schemaVersion": 1,
    "requestId": REQUEST_ID,
    "occurredAt": datetime.now(timezone.utc).isoformat(),
    "data": {
        "videoId": REQUEST.video_id,
        "startSeconds": REQUEST.start_seconds,
        "endSeconds": REQUEST.end_seconds,
        "outputFormat": "mp3",
    },
}


class FakeExchange:
    def __init__(self):
        self.published = []

    async def publish(self, message, routing_key, mandatory):
        self.published.append((message, routing_key, mandatory))


class FakeQueue:
    async def consume(self, callback, no_ack):
        self.callback = callback
        return "consumer-tag"


class FakeMessage:
    def __init__(self, body, headers=None):
        self.body = body
        self.headers = headers or {}
        self.routing_key = "audio.extraction.requested"
        self.content_type = "application/json"
        self.message_id = EVENT["eventId"]
        self.type = EVENT["eventType"]
        self.acked = False
        self.nacked = False

    async def ack(self):
        self.acked = True

    async def nack(self, requeue):
        self.nacked = requeue


@pytest.mark.asyncio
async def test_existing_result_is_reused_with_stable_terminal_event_id():
    published = []

    class Exchange:
        async def publish(self, message, **kwargs):
            published.append(message)

    audio = StoredAudio(
        f"audio-extractions/{REQUEST_ID}/audio.mp3",
        "audio/mpeg",
        12,
        100,
        "a" * 64,
    )
    await publish_completed(Exchange(), REQUEST, audio)
    await publish_completed(Exchange(), REQUEST, audio)

    assert published[0].message_id == published[1].message_id
    assert json.loads(published[0].body)["eventId"] == json.loads(published[1].body)["eventId"]


@pytest.mark.asyncio
async def test_terminal_publishers_produce_events_matching_versioned_schemas():
    exchange = FakeExchange()
    audio = StoredAudio(
        f"audio-extractions/{REQUEST_ID}/audio.mp3",
        "audio/mpeg",
        REQUEST.duration_seconds,
        100,
        "a" * 64,
    )
    await publish_completed(exchange, REQUEST, audio)
    await publish_failed(exchange, REQUEST, "DOWNLOAD_FAILED")

    root = Path(__file__).resolve().parents[4]
    for message, routing_key, _ in exchange.published:
        schema_name = f"audio-extraction-{routing_key.rsplit('.', 1)[-1]}.schema.json"
        schema = json.loads(
            (
                root
                / "specs/002-youtube-audio-extraction/contracts/events"
                / schema_name
            ).read_text()
        )
        event = json.loads(message.body)
        Draft202012Validator(schema, format_checker=FormatChecker()).validate(event)
        assert message.message_id == event["eventId"]


@pytest.mark.asyncio
async def test_service_reuses_verified_object_after_redelivery_without_new_media_work():
    published = []
    configuration = WorkerSettings(
        rabbitmq_host="rabbitmq",
        rabbitmq_port=5672,
        rabbitmq_username="worker",
        rabbitmq_password="placeholder",
        s3_endpoint="http://minio:9000",
        s3_access_key="worker",
        s3_secret_key="placeholder",
        s3_bucket="audio-extractions",
        maximum_download_bytes=2_147_483_648,
        maximum_output_bytes=104_857_600,
        job_timeout_seconds=900,
    )
    stored = StoredAudio(
        f"audio-extractions/{REQUEST_ID}/audio.mp3",
        "audio/mpeg",
        REQUEST.duration_seconds,
        100,
        "a" * 64,
    )

    class Storage:
        find_calls = 0
        store_calls = 0

        async def find_existing(self, request_id):
            self.find_calls += 1
            return stored

        async def store(self, request_id, audio):
            self.store_calls += 1
            return stored

    class Exchange:
        async def publish(self, message, **kwargs):
            published.append(message)

    storage = Storage()
    service = AudioExtractionService(configuration, storage, Exchange())
    await service.process(REQUEST)
    await service.process(REQUEST)

    assert storage.find_calls == 2
    assert storage.store_calls == 0
    assert len(published) == 2
    assert published[0].message_id == published[1].message_id


@pytest.mark.asyncio
async def test_redelivery_after_upload_before_ack_reuses_single_stored_result():
    queue = FakeQueue()
    retry_exchange = FakeExchange()
    dead_exchange = FakeExchange()

    class Service:
        def __init__(self):
            self.calls = 0
            self.stored_objects = set()

        async def process(self, request):
            self.calls += 1
            self.stored_objects.add(request.request_id)
            if self.calls == 1:
                raise TransientProcessingError("simulated worker interruption after upload")

        async def publish_retries_exhausted(self, request):
            raise AssertionError("The bounded retry should not be exhausted.")

    service = Service()
    await consume_requests(queue, retry_exchange, dead_exchange, service)
    original = FakeMessage(json.dumps(EVENT).encode())
    await queue.callback(original)

    retry_message, retry_key, _ = retry_exchange.published[0]
    assert retry_key == "audio.extraction.requested.retry.5000"
    assert original.acked
    assert retry_message.headers["x-audio-extraction-retry"] == 1

    redelivery = FakeMessage(retry_message.body, retry_message.headers)
    await queue.callback(redelivery)
    assert redelivery.acked
    assert service.calls == 2
    assert service.stored_objects == {REQUEST_ID}


@pytest.mark.asyncio
async def test_transient_failures_are_bounded_and_then_terminally_failed():
    queue = FakeQueue()
    retry_exchange = FakeExchange()
    dead_exchange = FakeExchange()

    class Service:
        def __init__(self):
            self.exhausted = 0

        async def process(self, request):
            raise TransientProcessingError("transient")

        async def publish_retries_exhausted(self, request):
            self.exhausted += 1

    service = Service()
    await consume_requests(queue, retry_exchange, dead_exchange, service)
    for attempt in range(4):
        message = FakeMessage(
            json.dumps(EVENT).encode(),
            {} if attempt == 0 else {"x-audio-extraction-retry": attempt},
        )
        await queue.callback(message)
        assert message.acked

    assert len(retry_exchange.published) == 3
    assert service.exhausted == 1


@pytest.mark.asyncio
async def test_malformed_payload_is_redacted_before_dead_lettering():
    queue = FakeQueue()
    retry_exchange = FakeExchange()
    dead_exchange = FakeExchange()

    class Service:
        async def process(self, request):
            raise AssertionError("Malformed messages must not be dispatched.")

    await consume_requests(queue, retry_exchange, dead_exchange, Service())
    secret_payload = b'{"url":"https://example.invalid/private-token"}'
    message = FakeMessage(secret_payload)
    await queue.callback(message)

    dead_body = dead_exchange.published[0][0].body
    assert message.acked
    assert b"private-token" not in dead_body
    assert json.loads(dead_body)["failureCode"] == "MALFORMED_REQUEST"


@pytest.mark.asyncio
async def test_unavailable_retry_or_terminal_publisher_does_not_ack_work():
    queue = FakeQueue()
    dead_exchange = FakeExchange()

    class UnavailableExchange:
        async def publish(self, message, **kwargs):
            raise ConnectionError("broker unavailable")

    class Service:
        async def process(self, request):
            raise TransientProcessingError("transient")

        async def publish_retries_exhausted(self, request):
            raise ConnectionError("broker unavailable")

    await consume_requests(queue, UnavailableExchange(), dead_exchange, Service())
    first = FakeMessage(json.dumps(EVENT).encode())
    await queue.callback(first)
    assert first.nacked
    assert not first.acked

    exhausted = FakeMessage(
        json.dumps(EVENT).encode(),
        {"x-audio-extraction-retry": 3},
    )
    await queue.callback(exhausted)
    assert exhausted.nacked
    assert not exhausted.acked
