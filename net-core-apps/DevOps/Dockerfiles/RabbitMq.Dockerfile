ARG RABBITMQ_IMAGE=rabbitmq:4.2.4-management@sha256:aeee1db0ff9fdb1347f3585242c14ed10877b56c22eeaa113b21208ef3dd3edf
FROM ${RABBITMQ_IMAGE}

USER root
ADD --checksum=sha256:f168b2c09810cde3726961d31f38e3408e6a7fbff3929908d6f962061d8e70a1 \
    https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v4.2.0/rabbitmq_delayed_message_exchange-4.2.0.ez \
    /plugins/rabbitmq_delayed_message_exchange-4.2.0.ez
RUN rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange
USER rabbitmq
