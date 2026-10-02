from dataclasses import dataclass
import os


class ConfigurationError(ValueError):
    """Raised when required worker settings are missing or invalid."""


@dataclass(frozen=True)
class WorkerSettings:
    rabbitmq_host: str
    rabbitmq_port: int
    rabbitmq_username: str
    rabbitmq_password: str
    s3_endpoint: str
    s3_access_key: str
    s3_secret_key: str
    s3_bucket: str


def load_settings() -> WorkerSettings:
    required = {
        "RABBITMQ_HOST": os.getenv("RABBITMQ_HOST", ""),
        "RABBITMQ_USERNAME": os.getenv("RABBITMQ_USERNAME", ""),
        "RABBITMQ_PASSWORD": os.getenv("RABBITMQ_PASSWORD", ""),
        "S3_ENDPOINT": os.getenv("S3_ENDPOINT", ""),
        "S3_ACCESS_KEY": os.getenv("S3_ACCESS_KEY", ""),
        "S3_SECRET_KEY": os.getenv("S3_SECRET_KEY", ""),
        "S3_BUCKET": os.getenv("S3_BUCKET", ""),
    }
    missing = [name for name, value in required.items() if not value.strip()]
    if missing:
        raise ConfigurationError(f"Required worker settings are missing: {', '.join(missing)}")

    try:
        rabbitmq_port = int(os.getenv("RABBITMQ_PORT", "5672"))
    except ValueError as error:
        raise ConfigurationError("RABBITMQ_PORT must be an integer.") from error
    if not 1 <= rabbitmq_port <= 65_535:
        raise ConfigurationError("RABBITMQ_PORT must be between 1 and 65535.")

    return WorkerSettings(
        rabbitmq_host=required["RABBITMQ_HOST"],
        rabbitmq_port=rabbitmq_port,
        rabbitmq_username=required["RABBITMQ_USERNAME"],
        rabbitmq_password=required["RABBITMQ_PASSWORD"],
        s3_endpoint=required["S3_ENDPOINT"],
        s3_access_key=required["S3_ACCESS_KEY"],
        s3_secret_key=required["S3_SECRET_KEY"],
        s3_bucket=required["S3_BUCKET"],
    )
