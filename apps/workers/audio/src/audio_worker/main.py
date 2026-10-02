import asyncio
import logging

import asyncio
import logging

import aio_pika

from audio_worker.config import load_settings
from audio_worker.messaging.consumer import consume_requests
from audio_worker.messaging.topology import (
    DEAD_LETTER_EXCHANGE,
    EXCHANGE,
    WORKER_QUEUE,
    declare_topology,
)
from audio_worker.processing.audio_extraction_service import AudioExtractionService
from audio_worker.storage.audio_storage import AudioStorage
from audio_worker.storage.minio import MinioAudioStorage


async def run() -> None:
    settings = load_settings()
    connection = await aio_pika.connect_robust(
        host=settings.rabbitmq_host,
        port=settings.rabbitmq_port,
        login=settings.rabbitmq_username,
        password=settings.rabbitmq_password,
    )
    async with connection:
        channel = await connection.channel(publisher_confirms=True, on_return_raises=True)
        await channel.set_qos(prefetch_count=1)
        await declare_topology(channel)
        exchange = await channel.get_exchange(EXCHANGE, ensure=True)
        dead_letter_exchange = await channel.get_exchange(DEAD_LETTER_EXCHANGE, ensure=True)
        queue = await channel.get_queue(WORKER_QUEUE, ensure=True)
        storage = AudioStorage(
            MinioAudioStorage(
                settings.s3_endpoint,
                settings.s3_access_key,
                settings.s3_secret_key,
                settings.s3_bucket,
            ),
            settings.maximum_output_bytes,
        )
        service = AudioExtractionService(settings, storage, exchange)
        await consume_requests(queue, exchange, dead_letter_exchange, service)
        logging.getLogger(__name__).info("Audio extraction worker is consuming requests.")
        await asyncio.Event().wait()


def main() -> None:
    logging.basicConfig(level=logging.INFO)
    logging.basicConfig(level=logging.INFO)
    asyncio.run(run())


if __name__ == "__main__":
    main()
