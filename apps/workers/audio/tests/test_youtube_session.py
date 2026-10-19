import asyncio
import stat

import pytest

from audio_worker.media import video_downloader
from audio_worker.media.video_downloader import VideoAuthenticationRequiredError, download_video
from audio_worker.media.youtube_session import (
    YouTubeAccount,
    YouTubeSession,
    YouTubeSessionSettings,
    to_netscape,
)

VIDEO_ID = "8t11Lxbm8LA"


def _settings(tmp_path):
    return YouTubeSessionSettings("http://api:8080", "token", tmp_path / "cache")


def test_netscape_export_keeps_only_google_domains_and_marks_http_only():
    content = to_netscape(
        [
            {"domain": ".youtube.com", "name": "SID", "value": "a", "secure": True, "httpOnly": True, "expiry": 10},
            {"domain": "accounts.google.com", "name": "G", "value": "b"},
            {"domain": ".example.com", "name": "X", "value": "c"},
        ]
    )

    lines = content.splitlines()
    assert lines[0] == "# Netscape HTTP Cookie File"
    assert lines[1] == "#HttpOnly_.youtube.com\tTRUE\t/\tTRUE\t10\tSID\ta"
    assert lines[2] == "accounts.google.com\tFALSE\t/\tFALSE\t0\tG\tb"
    assert len(lines) == 3


def test_netscape_export_reads_cdp_expiry_and_session_cookies():
    content = to_netscape(
        [
            {"domain": ".google.com", "name": "SAPISID", "value": "a", "expires": 1900000000.5},
            {"domain": ".youtube.com", "name": "S", "value": "b", "expires": -1},
        ]
    )

    lines = content.splitlines()
    assert lines[1].split("\t")[4] == "1900000000"
    assert lines[2].split("\t")[4] == "0"


@pytest.mark.asyncio
async def test_session_logs_in_once_caches_and_copies_privately(tmp_path):
    logins = []

    def login(account, settings):
        logins.append(account.username)
        return [{"domain": ".youtube.com", "name": "SID", "value": "v"}]

    session = YouTubeSession(
        _settings(tmp_path),
        account_loader=lambda settings: YouTubeAccount("user@example.com", "pw"),
        browser_login=login,
    )
    first = tmp_path / "job1"
    second = tmp_path / "job2"
    first.mkdir()
    second.mkdir()

    path = await session.prepare_cookie_file(first)
    await session.prepare_cookie_file(second)
    await session.prepare_cookie_file(second, refresh=True)

    assert logins == ["user@example.com", "user@example.com"]
    assert path.parent == first
    assert "SID\tv" in path.read_text()
    assert stat.S_IMODE(path.stat().st_mode) == 0o600
    assert stat.S_IMODE((tmp_path / "cache" / "youtube-cookies.txt").stat().st_mode) == 0o600


@pytest.mark.asyncio
async def test_session_returns_none_without_configured_account(tmp_path, caplog):
    session = YouTubeSession(
        _settings(tmp_path),
        account_loader=lambda settings: None,
        browser_login=lambda account, settings: pytest.fail("must not sign in"),
    )

    assert await session.prepare_cookie_file(tmp_path) is None
    assert "No YouTube account is configured" in caplog.text


@pytest.mark.asyncio
async def test_downloader_passes_cookies_and_maps_bot_check(tmp_path, monkeypatch):
    cookies = tmp_path / "cookies.txt"
    cookies.write_text("# Netscape HTTP Cookie File\n")
    captured = {}

    class FakeProcess:
        returncode = 1

        async def communicate(self):
            return b"", b"ERROR: [youtube] x: The page needs to be reloaded."

    async def fake_exec(*arguments, **kwargs):
        captured["arguments"] = arguments
        return FakeProcess()

    monkeypatch.setattr(asyncio, "create_subprocess_exec", fake_exec)

    with pytest.raises(VideoAuthenticationRequiredError):
        await download_video(VIDEO_ID, tmp_path, 10_000, 15, cookies)

    arguments = captured["arguments"]
    assert arguments[arguments.index("--cookies") + 1] == str(cookies)
    assert video_downloader.VideoDownloadError in VideoAuthenticationRequiredError.__mro__


@pytest.mark.asyncio
async def test_service_refreshes_cookies_and_retries_once_after_bot_check(tmp_path, monkeypatch):
    from types import SimpleNamespace

    from audio_worker.processing.audio_extraction_service import AudioExtractionService

    calls = []
    refreshes = []

    class FakeSession:
        async def prepare_cookie_file(self, root, refresh=False):
            refreshes.append(refresh)
            return root / "cookies.txt"

    async def fake_download(video_id, root, maximum, timeout, cookies=None):
        calls.append(cookies)
        if len(calls) == 1:
            raise VideoAuthenticationRequiredError("bot")
        return root / "source.webm"

    monkeypatch.setattr(
        "audio_worker.processing.audio_extraction_service.download_video", fake_download
    )
    settings = SimpleNamespace(maximum_download_bytes=1, job_timeout_seconds=1)
    service = AudioExtractionService(settings, None, None, FakeSession())

    path = await service._download(VIDEO_ID, tmp_path)

    assert path == tmp_path / "source.webm"
    assert refreshes == [False, True]
    assert len(calls) == 2
