import asyncio
import json
from datetime import datetime, timezone
from pathlib import Path

import pytest

from audio_worker.config import WorkerSettings
from audio_worker.media.ffmpeg_processor import ProcessedAudio, extract_audio
from audio_worker.media.video_downloader import VideoDownloadError, download_video
from audio_worker.media.ffmpeg_processor import AudioProcessingError
from audio_worker.messaging.consumer import (
    ExtractionRequest,
    InvalidRequestMessage,
    validate_requested_event,
)
from audio_worker.processing.audio_extraction_service import AudioExtractionService
from audio_worker.storage.models import AudioStorageError, StoredAudio


VIDEO_ID = "abc_1234567"
REQUEST_ID = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"


def request_event(**overrides):
    event = {
        "eventId": "f75a33e4-2fab-4b9f-8ae4-75d3462d8624",
        "eventType": "audio.extraction.requested",
        "schemaVersion": 1,
        "requestId": REQUEST_ID,
        "occurredAt": datetime.now(timezone.utc).isoformat(),
        "data": {
            "videoId": VIDEO_ID,
            "startSeconds": 7,
            "endSeconds": 19,
            "outputFormat": "mp3",
        },
    }
    event.update(overrides)
    return event


def settings():
    return WorkerSettings(
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


def test_request_event_validation_accepts_canonical_id_and_integer_interval():
    event = request_event()

    extraction = validate_requested_event(json.dumps(event).encode())

    assert extraction == ExtractionRequest(
        event_id=event["eventId"],
        request_id=REQUEST_ID,
        video_id=VIDEO_ID,
        start_seconds=7,
        end_seconds=19,
    )


@pytest.mark.parametrize(
    "change",
    [
        lambda event: event["data"].update(videoId="https://youtube.com/watch?v=abc_1234567"),
        lambda event: event["data"].update(startSeconds=True),
        lambda event: event["data"].update(endSeconds=7),
        lambda event: event.update(schemaVersion=2),
        lambda event: event.update(requestId="invalid"),
    ],
)
def test_request_event_validation_rejects_noncanonical_or_invalid_values(change):
    event = request_event()
    change(event)

    with pytest.raises(InvalidRequestMessage):
        validate_requested_event(event)


@pytest.mark.asyncio
async def test_video_downloader_uses_only_generated_youtube_url_and_controlled_paths(
    tmp_path,
    monkeypatch,
):
    invocation = {}

    class FakeProcess:
        returncode = 0

        async def communicate(self):
            return b"", b""

    async def fake_exec(*arguments, **kwargs):
        invocation["arguments"] = arguments
        invocation["kwargs"] = kwargs
        template = Path(arguments[arguments.index("--output") + 1])
        (template.parent / "source.webm").write_bytes(b"source")
        return FakeProcess()

    monkeypatch.setattr(asyncio, "create_subprocess_exec", fake_exec)
    path = await download_video(VIDEO_ID, tmp_path, 10_000, 15)

    assert path == tmp_path / "source.webm"
    assert f"https://www.youtube.com/watch?v={VIDEO_ID}" in invocation["arguments"]
    assert "--max-filesize" in invocation["arguments"]
    assert "shell" not in invocation["kwargs"]
    assert path.read_bytes() == b"source"


@pytest.mark.asyncio
async def test_ffmpeg_uses_fixed_argument_vector_and_exact_requested_interval(
    tmp_path,
    monkeypatch,
):
    source = tmp_path / "source.webm"
    output = tmp_path / "audio.mp3"
    source.write_bytes(b"source")
    invocation = []

    class FakeProcess:
        def __init__(self, command):
            self.command = command
            self.returncode = 0

        async def communicate(self):
            if self.command[0] == "ffmpeg":
                Path(self.command[-1]).write_bytes(b"mp3-data")
                return b"", b""
            return b'{"format":{"duration":"12.0"}}', b""

    async def fake_exec(*arguments, **kwargs):
        invocation.append((arguments, kwargs))
        return FakeProcess(arguments)

    monkeypatch.setattr(asyncio, "create_subprocess_exec", fake_exec)
    processed = await extract_audio(source, output, 7, 19, 30, 1_000)

    ffmpeg_args, ffmpeg_kwargs = invocation[0]
    assert ffmpeg_args[0] == "ffmpeg"
    assert ffmpeg_args[ffmpeg_args.index("-ss") + 1] == "7"
    assert ffmpeg_args[ffmpeg_args.index("-t") + 1] == "12"
    assert "-vn" in ffmpeg_args and ffmpeg_args[ffmpeg_args.index("-map") + 1] == "0:a:0"
    assert "shell" not in ffmpeg_kwargs
    assert processed.size_bytes == len(b"mp3-data")
    assert len(processed.checksum_sha256) == 64


@pytest.mark.asyncio
async def test_service_passes_exact_offsets_and_publishes_only_safe_failure_payloads(
    monkeypatch,
    tmp_path,
):
    request = validate_requested_event(request_event())
    published = []

    class Exchange:
        async def publish(self, message, **kwargs):
            published.append(json.loads(message.body))

    class Storage:
        async def find_existing(self, request_id):
            return None

        async def store(self, request_id, audio):
            assert audio.path.is_file()
            return StoredAudio(
                f"audio-extractions/{request_id}/audio.mp3",
                "audio/mpeg",
                12,
                audio.size_bytes,
                audio.checksum_sha256,
            )

    service = AudioExtractionService(settings(), Storage(), Exchange())
    calls = {}

    async def fake_download(video_id, directory, maximum_bytes, timeout):
        calls["video_id"] = video_id
        calls["directory"] = directory
        source = directory / "source.webm"
        source.write_bytes(b"source")
        return source

    async def fake_extract(source, output, start, end, timeout, maximum_bytes):
        calls["interval"] = (start, end)
        output.write_bytes(b"audio")
        return ProcessedAudio(output, end - start, 5, "a" * 64)

    monkeypatch.setattr(
        "audio_worker.processing.audio_extraction_service.download_video",
        fake_download,
    )
    monkeypatch.setattr(
        "audio_worker.processing.audio_extraction_service.extract_audio",
        fake_extract,
    )
    await service.process(request)

    assert calls["video_id"] == VIDEO_ID
    assert calls["interval"] == (7, 19)
    assert published[0]["eventType"] == "audio.extraction.completed"
    assert published[0]["data"]["objectKey"] == f"audio-extractions/{REQUEST_ID}/audio.mp3"

    class UnavailableStorage(Storage):
        pass

    service = AudioExtractionService(settings(), UnavailableStorage(), Exchange())

    async def unavailable(*args, **kwargs):
        from audio_worker.media.video_downloader import VideoUnavailableError

        raise VideoUnavailableError("upstream text must not be forwarded")

    monkeypatch.setattr(
        "audio_worker.processing.audio_extraction_service.download_video",
        unavailable,
    )
    await service.process(request)
    assert published[-1]["data"]["message"] == "O vídeo não está disponível para processamento."
    assert "upstream text" not in json.dumps(published[-1])


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("download_error", "extract_error", "storage_error"),
    [
        (VideoDownloadError("download transport error"), None, None),
        (None, AudioProcessingError("ffmpeg diagnostic with private file path"), None),
        (None, None, AudioStorageError("storage endpoint and credentials")),
    ],
)
async def test_worker_maps_media_and_storage_failures_without_exposing_details(
    monkeypatch,
    tmp_path,
    download_error,
    extract_error,
    storage_error,
):
    from audio_worker.messaging.consumer import TransientProcessingError

    request = validate_requested_event(request_event())
    published = []

    class Exchange:
        async def publish(self, message, **kwargs):
            published.append(json.loads(message.body))

    class Storage:
        async def find_existing(self, request_id):
            return None

        async def store(self, request_id, audio):
            if storage_error:
                raise storage_error
            return StoredAudio(
                f"audio-extractions/{request_id}/audio.mp3",
                "audio/mpeg",
                12,
                audio.size_bytes,
                audio.checksum_sha256,
            )

    async def fake_download(*args):
        if download_error:
            raise download_error
        path = args[1] / "source.webm"
        path.write_bytes(b"source")
        return path

    async def fake_extract(*args):
        if extract_error:
            raise extract_error
        output = args[1]
        output.write_bytes(b"audio")
        return ProcessedAudio(output, 12, 5, "a" * 64)

    monkeypatch.setattr(
        "audio_worker.processing.audio_extraction_service.download_video",
        fake_download,
    )
    monkeypatch.setattr(
        "audio_worker.processing.audio_extraction_service.extract_audio",
        fake_extract,
    )
    service = AudioExtractionService(settings(), Storage(), Exchange())

    if download_error or storage_error:
        with pytest.raises(TransientProcessingError):
            await service.process(request)
        assert published == []
    else:
        await service.process(request)
        assert published[0]["eventType"] == "audio.extraction.failed"
        serialized = json.dumps(published[0])
        assert "private file path" not in serialized
        assert "credentials" not in serialized
