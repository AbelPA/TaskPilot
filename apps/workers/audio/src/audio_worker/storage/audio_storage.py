from audio_worker.media.ffmpeg_processor import ProcessedAudio
from audio_worker.storage.minio import MinioAudioStorage
from audio_worker.storage.models import AudioStorageError, StoredAudio


class AudioStorage:
    def __init__(self, implementation: MinioAudioStorage, maximum_output_bytes: int) -> None:
        self._implementation = implementation
        self._maximum_output_bytes = maximum_output_bytes

    async def find_existing(self, request_id: str) -> StoredAudio | None:
        return await self._implementation.find_existing(
            request_id,
            self._maximum_output_bytes,
        )

    async def store(
        self,
        request_id: str,
        audio: ProcessedAudio,
    ) -> StoredAudio:
        if audio.size_bytes <= 0 or audio.size_bytes > self._maximum_output_bytes:
            raise AudioStorageError("The audio result exceeds its configured size limit.")
        if not audio.path.is_file() or audio.path.stat().st_size != audio.size_bytes:
            raise AudioStorageError("The audio result is not a complete local file.")
        return await self._implementation.store(
            request_id,
            audio,
            self._maximum_output_bytes,
        )
