FROM debian:bookworm-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
    prosody \
    prosody-modules \
    lua-sec \
    lua-event \
    lua-unbound \
    openssl \
    ca-certificates \
    python3 \
    && rm -rf /var/lib/apt/lists/*

COPY prosody.cfg.lua /etc/prosody/prosody.cfg.lua
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
COPY seed-messages.py /usr/local/bin/seed-messages.py

RUN chmod +x /usr/local/bin/entrypoint.sh /usr/local/bin/seed-messages.py && \
    mkdir -p /var/run/prosody /var/lib/prosody /etc/prosody && \
    chown -R prosody:prosody /var/run/prosody /var/lib/prosody /etc/prosody

EXPOSE 5222 5223 5280

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
