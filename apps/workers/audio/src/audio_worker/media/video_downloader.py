import asyncio
import re
import sys
from pathlib import Path


VIDEO_ID_PATTERN = re.compile(r"^[A-Za-z0-9_-]{11}$")


class VideoDownloadError(Exception):
    pass


class VideoUnavailableError(VideoDownloadError):
    pass


async def download_video(
    video_id: str,
    destination_directory: Path,
    maximum_bytes: int,
    timeout_seconds: int,
) -> Path:
    if not VIDEO_ID_PATTERN.fullmatch(video_id):
        raise VideoDownloadError("A canonical YouTube video ID is required.")
    if maximum_bytes <= 0 or timeout_seconds <= 0:
        raise ValueError("Download limits must be positive.")

    root = destination_directory.resolve(strict=True)
    output_template = str(root / "source.%(ext)s")
    process = await asyncio.create_subprocess_exec(
        sys.executable,
        "-m",
        "yt_dlp",
        "--no-playlist",
        "--no-progress",
        "--no-warnings",
        "--quiet",
        "--format",
        "bestaudio/best",
        "--max-filesize",
        str(maximum_bytes),
        "--socket-timeout",
        "15",
        "--retries",
        "2",
        "--output",
        output_template,
        f"https://www.youtube.com/watch?v={video_id}",
        stdout=asyncio.subprocess.DEVNULL,
        stderr=asyncio.subprocess.PIPE,
    )
    try:
        _, stderr = await asyncio.wait_for(process.communicate(), timeout=timeout_seconds)
    except TimeoutError as error:
        await _stop_process(process)
        raise VideoDownloadError("The video download exceeded its time limit.") from error
    except asyncio.CancelledError:
        await _stop_process(process)
        raise

    if process.returncode != 0:
        if b"video unavailable" in stderr.lower():
            raise VideoUnavailableError("The requested video is unavailable.")
        raise VideoDownloadError("The video could not be downloaded.")

    files = [
        path
        for path in root.glob("source.*")
        if path.is_file() and not path.name.endswith((".part", ".ytdl"))
    ]
    if len(files) != 1 or files[0].stat().st_size <= 0:
        raise VideoDownloadError("The downloader did not produce one valid media file.")
    if files[0].stat().st_size > maximum_bytes:
        files[0].unlink(missing_ok=True)
        raise VideoDownloadError("The downloaded media exceeded its size limit.")
    return files[0]


async def _stop_process(process: asyncio.subprocess.Process) -> None:
    if process.returncode is None:
        process.kill()
        await process.wait()
