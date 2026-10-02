import asyncio
import tempfile
from pathlib import Path
from typing import Any

from audio_worker.config import WorkerSettings
from audio_worker.media.ffmpeg_processor import (
    AudioProcessingError,
    AudioProcessingTimeoutError,
    extract_audio,
)
from audio_worker.media.video_downloader import (
    VideoDownloadError,
    VideoUnavailableError,
    download_video,
)
from audio_worker.messaging.consumer import ExtractionRequest, TransientProcessingError
from audio_worker.messaging.publisher import publish_completed, publish_failed
from audio_worker.storage.audio_storage import AudioStorage
from audio_worker.storage.models import AudioStorageError, StoredAudio


class AudioExtractionService:
    def __init__(
        self,
        settings: WorkerSettings,
        storage: AudioStorage,
        outcome_exchange: Any,
    ) -> None:
        self._settings = settings
        self._storage = storage
        self._outcome_exchange = outcome_exchange

    async def process(self, request: ExtractionRequest) -> None:
        failure_code: str | None = None
        try:
            async with asyncio.timeout(self._settings.job_timeout_seconds):
                existing = await self._storage.find_existing(request.request_id)
                if existing is not None and _duration_matches(existing, request):
                    await self._publish_completed(request, existing)
                    return
                stored = await self._extract_and_store(request)
        except VideoUnavailableError:
            failure_code = "VIDEO_UNAVAILABLE"
        except VideoDownloadError as error:
            raise TransientProcessingError("The source download failed.") from error
        except AudioProcessingTimeoutError as error:
            raise TransientProcessingError("Audio processing exceeded its time limit.") from error
        except AudioProcessingError:
            failure_code = "EXTRACTION_FAILED"
        except AudioStorageError as error:
            raise TransientProcessingError("The result storage operation failed.") from error
        except TimeoutError as error:
            raise TransientProcessingError("The extraction job exceeded its time limit.") from error
        if failure_code is not None:
            await self._publish_failed(request, failure_code)
            return
        await self._publish_completed(request, stored)

    async def publish_retries_exhausted(self, request: ExtractionRequest) -> None:
        await self._publish_failed(request, "RETRIES_EXHAUSTED")

    async def _extract_and_store(self, request: ExtractionRequest) -> StoredAudio:
        with tempfile.TemporaryDirectory(prefix="audio-extraction-") as directory:
            root = Path(directory).resolve(strict=True)
            source_path = await download_video(
                request.video_id,
                root,
                self._settings.maximum_download_bytes,
                self._settings.job_timeout_seconds,
            )
            output_path = root / "audio.mp3"
            audio = await extract_audio(
                source_path,
                output_path,
                request.start_seconds,
                request.end_seconds,
                self._settings.job_timeout_seconds,
                self._settings.maximum_output_bytes,
            )
            return await self._storage.store(request.request_id, audio)

    async def _publish_completed(
        self,
        request: ExtractionRequest,
        audio: StoredAudio,
    ) -> None:
        try:
            await publish_completed(self._outcome_exchange, request, audio)
        except Exception as error:
            raise TransientProcessingError("The completion event could not be confirmed.") from error

    async def _publish_failed(self, request: ExtractionRequest, code: str) -> None:
        try:
            await publish_failed(self._outcome_exchange, request, code)
        except Exception as error:
            raise TransientProcessingError("The failure event could not be confirmed.") from error


def _duration_matches(audio: StoredAudio, request: ExtractionRequest) -> bool:
    return abs(audio.duration_seconds - request.duration_seconds) <= 2
