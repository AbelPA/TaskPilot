from unittest.mock import AsyncMock, Mock

import aio_pika
import pytest

from audio_worker.messaging.topology import (
    DEAD_LETTER_EXCHANGE,
    declare_topology,
)


@pytest.mark.asyncio
async def test_declares_and_binds_dead_letter_exchange() -> None:
    channel = Mock()
    dead_letter_exchange = Mock()
    queues = [Mock(bind=AsyncMock()) for _ in range(6)]
    dead_letter_queue = queues[2]
    channel.declare_exchange = AsyncMock(
        side_effect=[Mock(), dead_letter_exchange],
    )
    channel.declare_queue = AsyncMock(side_effect=queues)

    await declare_topology(channel)

    channel.declare_exchange.assert_any_await(
        DEAD_LETTER_EXCHANGE,
        aio_pika.ExchangeType.TOPIC,
        durable=True,
    )
    dead_letter_queue.bind.assert_awaited_once_with(
        dead_letter_exchange,
        routing_key="#",
    )
