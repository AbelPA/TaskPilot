import logging
import os
import time
from urllib.parse import urlparse

from urllib3.exceptions import HTTPError

from audio_worker.storage.minio import MinioAudioStorage

logger = logging.getLogger(__name__)


def ensure_bucket(storage: MinioAudioStorage, timeout_seconds: int = 120) -> None:
    deadline = time.monotonic() + timeout_seconds
    while True:
        try:
            storage.ensure_bucket()
            return
        except (HTTPError, OSError) as error:
            if time.monotonic() >= deadline:
                raise RuntimeError(
                    "S3 storage did not become available before the startup timeout."
                ) from error
            logger.info("Waiting for S3 storage to become available.")
            time.sleep(2)


def main() -> None:
    endpoint = urlparse(os.environ["S3_ENDPOINT"])
    if endpoint.scheme not in {"http", "https"} or not endpoint.netloc:
        raise ValueError("S3_ENDPOINT must be an absolute http(s) URL.")

    storage = MinioAudioStorage(
        os.environ["S3_ENDPOINT"],
        os.environ["S3_ACCESS_KEY"],
        os.environ["S3_SECRET_KEY"],
        os.environ["S3_BUCKET"],
    )
    ensure_bucket(storage)
    logger.info("S3 bucket %s is ready.", os.environ["S3_BUCKET"])


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    main()
