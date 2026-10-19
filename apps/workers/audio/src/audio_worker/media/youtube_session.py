import asyncio
import json
import logging
import os
import shutil
import tempfile
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

logger = logging.getLogger(__name__)

LOGIN_URL = (
    "https://accounts.google.com/ServiceLogin?service=youtube"
    "&continue=https%3A%2F%2Fwww.youtube.com%2F"
)
COOKIE_FILE_NAME = "youtube-cookies.txt"
LOGIN_TIMEOUT_SECONDS = 60
COOKIE_DOMAINS = ("youtube.com", "google.com")


class YouTubeLoginError(Exception):
    pass


@dataclass(frozen=True)
class YouTubeAccount:
    username: str
    password: str


@dataclass(frozen=True)
class YouTubeSessionSettings:
    api_base_url: str
    internal_token: str
    cache_directory: Path
    chrome_binary: str | None = None
    chromedriver_path: str | None = None


def fetch_account(settings: YouTubeSessionSettings) -> YouTubeAccount | None:
    request = urllib.request.Request(
        f"{settings.api_base_url.rstrip('/')}/internal/youtube-account",
        headers={"X-Internal-Token": settings.internal_token},
    )
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            if response.status == 204:
                return None
            body = json.loads(response.read())
    except urllib.error.URLError as error:
        raise YouTubeLoginError("The YouTube account could not be fetched from the API.") from error
    username = body.get("username")
    password = body.get("password")
    if not username or not password:
        return None
    return YouTubeAccount(username, password)


def to_netscape(cookies: list[dict[str, Any]]) -> str:
    lines = ["# Netscape HTTP Cookie File"]
    for cookie in cookies:
        domain = str(cookie.get("domain", ""))
        if not any(domain.lstrip(".").endswith(allowed) for allowed in COOKIE_DOMAINS):
            continue
        name = str(cookie.get("name", ""))
        value = str(cookie.get("value", ""))
        if not name or "\t" in value or "\n" in value:
            continue
        prefix = "#HttpOnly_" if cookie.get("httpOnly") else ""
        lines.append(
            "\t".join(
                [
                    f"{prefix}{domain}",
                    "TRUE" if domain.startswith(".") else "FALSE",
                    str(cookie.get("path", "/")),
                    "TRUE" if cookie.get("secure") else "FALSE",
                    str(_expiry(cookie)),
                    name,
                    value,
                ]
            )
        )
    return "\n".join(lines) + "\n"


def _expiry(cookie: dict[str, Any]) -> int:
    # Selenium reports "expiry", CDP reports "expires" (-1 for session cookies).
    value = cookie.get("expiry", cookie.get("expires", 0))
    return max(int(value), 0)


def login_with_chrome(account: YouTubeAccount, settings: YouTubeSessionSettings) -> list[dict[str, Any]]:
    # Selenium is imported lazily so the worker starts without it when the feature is unused.
    from selenium import webdriver
    from selenium.common.exceptions import TimeoutException
    from selenium.webdriver.chrome.service import Service
    from selenium.webdriver.common.by import By
    from selenium.webdriver.common.keys import Keys
    from selenium.webdriver.support import expected_conditions as conditions
    from selenium.webdriver.support.ui import WebDriverWait

    options = webdriver.ChromeOptions()
    if settings.chrome_binary:
        options.binary_location = settings.chrome_binary
    for argument in (
        "--headless=new",
        "--no-sandbox",
        "--disable-dev-shm-usage",
        "--disable-gpu",
        "--window-size=1280,900",
        "--lang=en-US",
        "--disable-blink-features=AutomationControlled",
    ):
        options.add_argument(argument)
    # Google rejects browsers that advertise automation ("This browser or app may not be secure").
    options.add_experimental_option("excludeSwitches", ["enable-automation"])
    options.add_experimental_option("useAutomationExtension", False)
    service = Service(executable_path=settings.chromedriver_path) if settings.chromedriver_path else Service()

    with tempfile.TemporaryDirectory(prefix="chrome-profile-") as profile:
        options.add_argument(f"--user-data-dir={profile}")
        driver = webdriver.Chrome(service=service, options=options)
        try:
            _hide_automation(driver)
            wait = WebDriverWait(driver, LOGIN_TIMEOUT_SECONDS)
            driver.get(LOGIN_URL)
            email = wait.until(conditions.visibility_of_element_located((By.CSS_SELECTOR, "input#identifierId")))
            email.send_keys(account.username, Keys.ENTER)
            password = wait.until(
                conditions.visibility_of_element_located((By.CSS_SELECTOR, "input[name=Passwd]"))
            )
            password.send_keys(account.password, Keys.ENTER)
            try:
                wait.until(lambda current: "youtube.com" in (current.current_url.split("/")[2:3] or [""])[0])
            except TimeoutException as error:
                raise YouTubeLoginError(
                    "Google did not complete the sign-in (wrong password, 2-step verification or a challenge)."
                ) from error
            # robots.txt loads no player page, so YouTube does not rotate the session cookies
            # before they are exported.
            driver.get("https://www.youtube.com/robots.txt")
            # CDP returns the cookies of every domain; get_cookies() only returns the current one.
            return driver.execute_cdp_cmd("Network.getAllCookies", {})["cookies"]
        except TimeoutException as error:
            raise YouTubeLoginError(
                f"The Google sign-in page did not respond as expected ({_describe_page(driver)})."
            ) from error
        finally:
            driver.quit()


def _hide_automation(driver: Any) -> None:
    driver.execute_cdp_cmd(
        "Page.addScriptToEvaluateOnNewDocument",
        {"source": "Object.defineProperty(navigator, 'webdriver', {get: () => undefined})"},
    )
    user_agent = driver.execute_script("return navigator.userAgent").replace("HeadlessChrome", "Chrome")
    driver.execute_cdp_cmd("Network.setUserAgentOverride", {"userAgent": user_agent})


def _describe_page(driver: Any) -> str:
    # Only the path and title are reported; queries and page text may carry account data.
    try:
        url = driver.current_url.split("?")[0]
        return f"url={url}, title={driver.title!r}"
    except Exception:
        return "page state unavailable"


class YouTubeSession:
    """Signs in with the configured account and exposes the cookies to yt-dlp."""

    def __init__(
        self,
        settings: YouTubeSessionSettings,
        account_loader: Callable[[YouTubeSessionSettings], YouTubeAccount | None] = fetch_account,
        browser_login: Callable[[YouTubeAccount, YouTubeSessionSettings], list[dict[str, Any]]] = login_with_chrome,
    ) -> None:
        self._settings = settings
        self._account_loader = account_loader
        self._browser_login = browser_login
        self._lock = asyncio.Lock()

    @property
    def _cache_file(self) -> Path:
        return self._settings.cache_directory / COOKIE_FILE_NAME

    async def prepare_cookie_file(self, job_directory: Path, refresh: bool = False) -> Path | None:
        """Returns a job-local copy of the cookies, or None when no account is configured."""
        async with self._lock:
            if refresh or not self._cache_file.is_file():
                if not await self._refresh():
                    return None
            return self._copy_to(job_directory)

    async def _refresh(self) -> bool:
        account = await asyncio.to_thread(self._account_loader, self._settings)
        if account is None:
            logger.warning(
                "No YouTube account is configured; downloading without authentication. "
                "Set the account on the Settings screen (/settings)."
            )
            self._cache_file.unlink(missing_ok=True)
            return False
        logger.info("Signing in to YouTube with the configured account.")
        cookies = await asyncio.to_thread(self._browser_login, account, self._settings)
        content = to_netscape(cookies)
        self._settings.cache_directory.mkdir(parents=True, exist_ok=True)
        _write_private(self._cache_file, content)
        return True

    def _copy_to(self, job_directory: Path) -> Path:
        # yt-dlp rewrites the cookie file, so each job works on its own private copy.
        destination = job_directory / COOKIE_FILE_NAME
        shutil.copyfile(self._cache_file, destination)
        destination.chmod(0o600)
        return destination


def _write_private(path: Path, content: str) -> None:
    temporary = path.with_suffix(".tmp")
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as handle:
        handle.write(content)
    os.replace(temporary, path)
