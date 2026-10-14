import aio_pika

EXCHANGE = "media.events"
DEAD_LETTER_EXCHANGE = "media.events.dlx"
WORKER_QUEUE = "audio.extraction.worker"
NOTIFICATION_QUEUE = "audio.extraction.notifications"
DEAD_LETTER_QUEUE = "audio.extraction.dead-letter"
REQUESTED_ROUTING_KEY = "audio.extraction.requested"


async def declare_topology(channel: aio_pika.abc.AbstractChannel) -> None:
    exchange = await channel.declare_exchange(
        EXCHANGE,
        aio_pika.ExchangeType.TOPIC,
        durable=True,
    )
    dead_letter_exchange = await channel.declare_exchange(
        DEAD_LETTER_EXCHANGE,
        aio_pika.ExchangeType.TOPIC,
        durable=True,
    )
    worker_queue = await channel.declare_queue(WORKER_QUEUE, durable=True)
    await worker_queue.bind(exchange, routing_key=REQUESTED_ROUTING_KEY)
    notification_queue = await channel.declare_queue(NOTIFICATION_QUEUE, durable=True)
    await notification_queue.bind(exchange, routing_key="audio.extraction.completed")
    await notification_queue.bind(exchange, routing_key="audio.extraction.failed")
    dead_letter_queue = await channel.declare_queue(DEAD_LETTER_QUEUE, durable=True)
    await dead_letter_queue.bind(dead_letter_exchange, routing_key="#")

    for ttl in (5_000, 30_000, 180_000):
        retry_queue = await channel.declare_queue(
            f"audio.extraction.worker.retry.{ttl // 1000}s",
            durable=True,
            arguments={
                "x-message-ttl": ttl,
                "x-dead-letter-exchange": EXCHANGE,
                "x-dead-letter-routing-key": REQUESTED_ROUTING_KEY,
            },
        )
        await retry_queue.bind(exchange, routing_key=f"{REQUESTED_ROUTING_KEY}.retry.{ttl}")
