from dataclasses import dataclass


class AudioStorageError(Exception):
    pass


@dataclass(frozen=True)
class StoredAudio:
    object_key: str
    content_type: str
    duration_seconds: int
    size_bytes: int
    checksum_sha256: str
