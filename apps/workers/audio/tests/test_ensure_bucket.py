from unittest.mock import Mock

import pytest
from urllib3.exceptions import HTTPError

from audio_worker.storage import minio as minio_module
from audio_worker.storage.ensure_bucket import ensure_bucket
from audio_worker.storage.minio import MinioAudioStorage


class BucketStorage:
    def __init__(self, failures: int = 0) -> None:
        self.failures = failures
        self.calls = 0

    def ensure_bucket(self) -> None:
        self.calls += 1
        if self.failures:
            self.failures -= 1
            raise HTTPError("S3 endpoint is not ready")


def test_storage_creates_bucket_when_missing(monkeypatch) -> None:
    client = Mock()
    client.bucket_exists.return_value = False
    monkeypatch.setattr(minio_module, "Minio", lambda *_args, **_kwargs: client)
    storage = MinioAudioStorage("http://seaweedfs:8333", "access", "secret", "audio")

    storage.ensure_bucket()

    client.bucket_exists.assert_called_once_with("audio")
    client.make_bucket.assert_called_once_with("audio")


def test_storage_keeps_existing_bucket(monkeypatch) -> None:
    client = Mock()
    client.bucket_exists.return_value = True
    monkeypatch.setattr(minio_module, "Minio", lambda *_args, **_kwargs: client)
    storage = MinioAudioStorage("http://seaweedfs:8333", "access", "secret", "audio")

    storage.ensure_bucket()

    client.bucket_exists.assert_called_once_with("audio")
    client.make_bucket.assert_not_called()


def test_ensure_bucket_retries_until_storage_is_available(monkeypatch) -> None:
    storage = BucketStorage(failures=2)
    monkeypatch.setattr("audio_worker.storage.ensure_bucket.time.sleep", lambda _: None)

    ensure_bucket(storage)

    assert storage.calls == 3


def test_ensure_bucket_fails_after_timeout(monkeypatch) -> None:
    storage = BucketStorage(failures=1)
    monotonic_values = iter((0, 2))
    monkeypatch.setattr(
        "audio_worker.storage.ensure_bucket.time.monotonic",
        lambda: next(monotonic_values),
    )
    monkeypatch.setattr("audio_worker.storage.ensure_bucket.time.sleep", lambda _: None)

    with pytest.raises(RuntimeError) as error:
        ensure_bucket(storage, timeout_seconds=1)

    assert (
        str(error.value)
        == "S3 storage did not become available before the startup timeout."
    )
    assert isinstance(error.value.__cause__, HTTPError)
