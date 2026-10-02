import asyncio
import hashlib
import re
from pathlib import Path
from urllib.parse import urlparse

from minio import Minio
from minio.error import S3Error

from audio_worker.media.ffmpeg_processor import ProcessedAudio
from audio_worker.storage.models import AudioStorageError, StoredAudio
from audio_worker.storage.retention import expiration_metadata


class MinioAudioStorage:
    def __init__(
        self,
        endpoint: str,
        access_key: str,
        secret_key: str,
        bucket: str,
    ) -> None:
        parsed = urlparse(endpoint)
        if (
            parsed.scheme not in {"http", "https"}
            or not parsed.hostname
            or parsed.path not in {"", "/"}
            or parsed.query
            or parsed.fragment
            or parsed.username
            or parsed.password
        ):
            raise ValueError("The S3 endpoint must be an absolute http(s) URL.")
        self._client = Minio(
            parsed.netloc,
            access_key=access_key,
            secret_key=secret_key,
            secure=parsed.scheme == "https",
        )
        self._bucket = bucket

    def ensure_bucket(self) -> None:
        if not self._client.bucket_exists(self._bucket):
            self._client.make_bucket(self._bucket)

    async def find_existing(
        self,
        request_id: str,
        maximum_bytes: int,
    ) -> StoredAudio | None:
        return await asyncio.to_thread(
            self._find_existing,
            request_id,
            maximum_bytes,
        )

    async def store(
        self,
        request_id: str,
        audio: ProcessedAudio,
        maximum_bytes: int,
    ) -> StoredAudio:
        return await asyncio.to_thread(
            self._store,
            request_id,
            audio,
            maximum_bytes,
        )

    def _find_existing(self, request_id: str, maximum_bytes: int) -> StoredAudio | None:
        object_key = _object_key(request_id)
        try:
            stat = self._client.stat_object(self._bucket, object_key)
        except S3Error as error:
            if error.code in {"NoSuchKey", "NoSuchObject", "NotFound"}:
                return None
            raise AudioStorageError("The existing audio result could not be checked.") from error

        if stat.size <= 0 or stat.size > maximum_bytes:
            return None
        metadata = {
            key.lower(): value
            for key, value in (stat.metadata or {}).items()
        }
        expected_checksum = metadata.get("x-amz-meta-sha256")
        content_type = stat.content_type or metadata.get("content-type")
        try:
            duration = int(metadata["x-amz-meta-duration-seconds"])
        except (KeyError, TypeError, ValueError):
            return None
        if (
            content_type != "audio/mpeg"
            or duration <= 0
            or duration > 1_800
            or not expected_checksum
        ):
            return None

        existing_digest = self._hash_existing_object(object_key, maximum_bytes)
        if existing_digest is None:
            return None
        actual_checksum, actual_size = existing_digest
        if actual_checksum != expected_checksum or actual_size != stat.size:
            return None
        return StoredAudio(
            object_key,
            "audio/mpeg",
            duration,
            stat.size,
            actual_checksum,
        )

    def _store(
        self,
        request_id: str,
        audio: ProcessedAudio,
        maximum_bytes: int,
    ) -> StoredAudio:
        path = audio.path
        if audio.size_bytes <= 0 or audio.size_bytes > maximum_bytes:
            raise AudioStorageError("The audio result exceeds its configured size limit.")
        if path.stat().st_size != audio.size_bytes:
            raise AudioStorageError("The audio result changed before upload.")

        object_key = _object_key(request_id)
        existing = self._find_existing(request_id, maximum_bytes)
        if existing is not None and abs(existing.duration_seconds - audio.duration_seconds) <= 2:
            return existing

        expires_at = expiration_metadata()
        try:
            self._client.fput_object(
                self._bucket,
                object_key,
                str(path),
                content_type="audio/mpeg",
                metadata={
                    "sha256": audio.checksum_sha256,
                    "duration-seconds": str(audio.duration_seconds),
                    "expires-at": expires_at,
                },
            )
        except Exception as error:
            raise AudioStorageError("The audio result could not be stored.") from error
        stored = self._find_existing(request_id, maximum_bytes)
        if (
            stored is None
            or stored.checksum_sha256 != audio.checksum_sha256
            or abs(stored.duration_seconds - audio.duration_seconds) > 2
        ):
            raise AudioStorageError("The uploaded audio result failed integrity verification.")
        return stored

    def _hash_existing_object(
        self,
        object_key: str,
        maximum_bytes: int,
    ) -> tuple[str, int] | None:
        try:
            response = self._client.get_object(self._bucket, object_key)
        except Exception as error:
            raise AudioStorageError("The existing audio result could not be read.") from error

        digest = hashlib.sha256()
        total = 0
        try:
            for block in iter(lambda: response.read(1024 * 1024), b""):
                total += len(block)
                if total > maximum_bytes:
                    return None
                digest.update(block)
        finally:
            response.close()
            response.release_conn()
        return digest.hexdigest(), total


def _object_key(request_id: str) -> str:
    if not re.fullmatch(r"[A-Za-z0-9_-]{43}", request_id):
        raise ValueError("A valid request capability is required for object storage.")
    return f"audio-extractions/{request_id}/audio.mp3"
