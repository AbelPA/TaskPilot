import asyncio
import logging

import aio_pika

from audio_worker.config import load_settings
from audio_worker.messaging.topology import declare_topology


async def run() -> None:
    settings = load_settings()
    connection = await aio_pika.connect_robust(
        host=settings.rabbitmq_host,
        port=settings.rabbitmq_port,
        login=settings.rabbitmq_username,
        password=settings.rabbitmq_password,
    )
    async with connection:
        channel = await connection.channel()
        await declare_topology(channel)
        logging.getLogger(__name__).info(
            "Worker connected and declared durable queues; extraction consumption is pending User Story 2."
        )
        await asyncio.Event().wait()


def main() -> None:
    logging.basicConfig(level=logging.INFO)
    asyncio.run(run())


if __name__ == "__main__":
    main()
