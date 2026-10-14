import asyncio
import hashlib
import json
from dataclasses import dataclass
from pathlib import Path


class AudioProcessingError(Exception):
    pass


class AudioProcessingTimeoutError(AudioProcessingError):
    pass


@dataclass(frozen=True)
class ProcessedAudio:
    path: Path
    duration_seconds: int
    size_bytes: int
    checksum_sha256: str


async def extract_audio(
    source_path: Path,
    output_path: Path,
    start_seconds: int,
    end_seconds: int,
    timeout_seconds: int,
    maximum_output_bytes: int,
) -> ProcessedAudio:
    if (
        type(start_seconds) is not int
        or type(end_seconds) is not int
        or start_seconds < 0
        or end_seconds <= start_seconds
        or end_seconds - start_seconds > 1_800
        or timeout_seconds <= 0
        or maximum_output_bytes <= 0
    ):
        raise ValueError("Invalid audio extraction bounds or limits.")
    source = source_path.resolve(strict=True)
    output_directory = output_path.parent.resolve(strict=True)
    if (
        not source.is_file()
        or source.parent != output_directory
        or output_path.name != "audio.mp3"
        or not source.name.startswith("source.")
    ):
        raise ValueError("Audio input and output paths must be generated files.")

    duration = end_seconds - start_seconds
    process = await asyncio.create_subprocess_exec(
        "ffmpeg",
        "-nostdin",
        "-hide_banner",
        "-v",
        "error",
        "-ss",
        str(start_seconds),
        "-i",
        str(source),
        "-t",
        str(duration),
        "-vn",
        "-map",
        "0:a:0",
        "-c:a",
        "libmp3lame",
        "-q:a",
        "2",
        "-y",
        str(output_path),
        stdout=asyncio.subprocess.DEVNULL,
        stderr=asyncio.subprocess.PIPE,
    )
    try:
        _, stderr = await asyncio.wait_for(process.communicate(), timeout=timeout_seconds)
    except TimeoutError as error:
        await _stop_process(process)
        raise AudioProcessingTimeoutError("Audio processing exceeded its time limit.") from error
    except asyncio.CancelledError:
        await _stop_process(process)
        raise

    if process.returncode != 0 or not output_path.is_file():
        raise AudioProcessingError("The requested audio interval could not be processed.")

    actual_duration = await _probe_duration(output_path)
    if abs(actual_duration - duration) > 2:
        raise AudioProcessingError("The generated audio duration does not match the requested interval.")

    size_bytes = output_path.stat().st_size
    if size_bytes <= 0 or size_bytes > maximum_output_bytes:
        output_path.unlink(missing_ok=True)
        raise AudioProcessingError("The generated audio exceeded its size limit.")

    checksum = await asyncio.to_thread(_file_sha256, output_path)
    return ProcessedAudio(output_path, actual_duration, size_bytes, checksum)


async def _probe_duration(path: Path) -> int:
    process = await asyncio.create_subprocess_exec(
        "ffprobe",
        "-v",
        "error",
        "-show_entries",
        "format=duration",
        "-of",
        "json",
        str(path),
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.DEVNULL,
    )
    try:
        stdout, _ = await asyncio.wait_for(process.communicate(), timeout=10)
    except TimeoutError as error:
        await _stop_process(process)
        raise AudioProcessingTimeoutError("Audio verification exceeded its time limit.") from error
    except asyncio.CancelledError:
        await _stop_process(process)
        raise
    if process.returncode != 0:
        raise AudioProcessingError("The generated audio could not be verified.")
    try:
        seconds = float(json.loads(stdout)["format"]["duration"])
    except (KeyError, TypeError, ValueError, json.JSONDecodeError) as error:
        raise AudioProcessingError("The generated audio duration is invalid.") from error
    if seconds <= 0:
        raise AudioProcessingError("The generated audio duration is invalid.")
    return round(seconds)


def _file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as audio:
        for block in iter(lambda: audio.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


async def _stop_process(process: asyncio.subprocess.Process) -> None:
    if process.returncode is None:
        process.kill()
        await process.wait()
