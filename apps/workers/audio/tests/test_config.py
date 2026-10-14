import pytest

from audio_worker.config import ConfigurationError, load_settings


def test_load_settings_requires_broker_and_private_storage(monkeypatch):
    for name in (
        "RABBITMQ_HOST",
        "RABBITMQ_USERNAME",
        "RABBITMQ_PASSWORD",
        "S3_ENDPOINT",
        "S3_ACCESS_KEY",
        "S3_SECRET_KEY",
        "S3_BUCKET",
    ):
        monkeypatch.delenv(name, raising=False)

    with pytest.raises(ConfigurationError, match="RABBITMQ_HOST"):
        load_settings()


def test_load_settings_rejects_invalid_broker_port(monkeypatch):
    for name, value in {
        "RABBITMQ_HOST": "rabbitmq",
        "RABBITMQ_USERNAME": "worker",
        "RABBITMQ_PASSWORD": "local-placeholder",
        "S3_ENDPOINT": "http://minio:9000",
        "S3_ACCESS_KEY": "worker",
        "S3_SECRET_KEY": "local-placeholder",
        "S3_BUCKET": "audio-extractions",
        "RABBITMQ_PORT": "not-a-port",
    }.items():
        monkeypatch.setenv(name, value)

    with pytest.raises(ConfigurationError, match="RABBITMQ_PORT"):
        load_settings()
