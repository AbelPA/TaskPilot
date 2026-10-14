import hashlib
import io
from datetime import datetime, timedelta, timezone
from pathlib import Path
from types import SimpleNamespace

import pytest

from audio_worker.media.ffmpeg_processor import ProcessedAudio
from audio_worker.storage.audio_storage import AudioStorage
from audio_worker.storage.minio import MinioAudioStorage
from audio_worker.storage.models import AudioStorageError


REQUEST_ID = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"


class FakeMinioClient:
    def __init__(self):
        self.objects = {}
        self.uploads = []
        self.deletions = []

    def fput_object(self, bucket, object_name, file_path, content_type, metadata):
        contents = Path(file_path).read_bytes()
        self.objects[(bucket, object_name)] = {
            "contents": contents,
            "content_type": content_type,
            "metadata": {f"x-amz-meta-{key}": value for key, value in metadata.items()},
        }
        self.uploads.append((bucket, object_name, content_type, metadata))

    def stat_object(self, bucket, object_name):
        item = self.objects.get((bucket, object_name))
        if item is None:
            from minio.error import S3Error

            raise S3Error(
                code="NoSuchKey",
                message="not found",
                resource=f"/{bucket}/{object_name}",
                request_id="test",
                host_id="test",
                response=None,
            )
        return SimpleNamespace(
            size=len(item["contents"]),
            metadata=item["metadata"],
            content_type=item["content_type"],
        )

    def get_object(self, bucket, object_name):
        return FakeObjectResponse(self.objects[(bucket, object_name)]["contents"])

    def remove_object(self, bucket, object_name):
        self.deletions.append((bucket, object_name))
        self.objects.pop((bucket, object_name), None)


class FakeObjectResponse(io.BytesIO):
    def release_conn(self):
        pass


@pytest.fixture
def storage(monkeypatch):
    client = FakeMinioClient()
    monkeypatch.setattr("audio_worker.storage.minio.Minio", lambda *args, **kwargs: client)
    return MinioAudioStorage(
        "http://minio:9000",
        "worker",
        "secret",
        "audio-extractions",
    ), client


@pytest.mark.asyncio
async def test_store_uses_private_bucket_and_deterministic_object_key(
    storage,
    tmp_path,
    monkeypatch,
):
    implementation, client = storage
    expiration = "2026-10-09T12:00:00+00:00"
    monkeypatch.setattr(
        "audio_worker.storage.minio.expiration_metadata",
        lambda: expiration,
    )
    path = tmp_path / "audio.mp3"
    path.write_bytes(b"private-result")
    checksum = hashlib.sha256(path.read_bytes()).hexdigest()
    audio = ProcessedAudio(path, 30, path.stat().st_size, checksum)

    stored = await AudioStorage(implementation, 100).store(REQUEST_ID, audio)

    assert stored.object_key == f"audio-extractions/{REQUEST_ID}/audio.mp3"
    assert client.uploads[0][:3] == (
        "audio-extractions",
        stored.object_key,
        "audio/mpeg",
    )
    assert client.uploads[0][3]["expires-at"] == expiration
    assert not any("acl" in key.lower() for key in client.uploads[0][3])
    assert client.objects[("audio-extractions", stored.object_key)]["metadata"][
        "x-amz-meta-sha256"
    ] == checksum


@pytest.mark.asyncio
async def test_store_enforces_the_100_mib_output_limit(storage, tmp_path):
    implementation, _ = storage
    path = tmp_path / "audio.mp3"
    with path.open("wb") as result_file:
        result_file.truncate(104_857_601)
    audio = ProcessedAudio(path, 30, 104_857_601, "a" * 64)

    with pytest.raises(AudioStorageError, match="size limit"):
        await AudioStorage(implementation, 104_857_600).store(REQUEST_ID, audio)


@pytest.mark.asyncio
async def test_existing_object_is_reused_only_after_checksum_verification(storage, tmp_path):
    implementation, client = storage
    path = tmp_path / "audio.mp3"
    path.write_bytes(b"existing-result")
    checksum = hashlib.sha256(path.read_bytes()).hexdigest()
    audio = ProcessedAudio(path, 30, path.stat().st_size, checksum)

    first = await AudioStorage(implementation, 100).store(REQUEST_ID, audio)
    second = await implementation.find_existing(REQUEST_ID, 100)

    assert first == second
    assert len(client.uploads) == 1
    client.objects[("audio-extractions", first.object_key)]["contents"] = b"corrupted"
    assert await implementation.find_existing(REQUEST_ID, 100) is None


def test_expiration_metadata_is_seven_days_after_completion():
    from audio_worker.storage.retention import expiration_metadata

    completed_at = datetime(2026, 10, 2, 12, tzinfo=timezone.utc)

    expires_at = expiration_metadata(completed_at)

    assert datetime.fromisoformat(expires_at) == completed_at + timedelta(days=7)
