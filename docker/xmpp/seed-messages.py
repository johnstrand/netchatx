#!/usr/bin/env python3
import socket
import base64
import time
import sys

def seed():
    host = "127.0.0.1"
    port = 5222

    # Wait for prosody port to be ready
    s = None
    for i in range(30):
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.connect((host, port))
            break
        except Exception:
            time.sleep(0.5)
            s = None

    if not s:
        print("[seed-messages] Failed to connect to XMPP server on port 5222", file=sys.stderr)
        return

    s.settimeout(5.0)

    try:
        # Step 1: Open stream
        s.sendall(b"<stream:stream to='localhost' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams' version='1.0'>")
        resp = s.recv(4096).decode('utf-8', errors='ignore')

        # Step 2: SASL PLAIN auth as userb@localhost
        # PLAIN payload: \0userb\0password
        auth_bytes = b"\x00userb\x00password"
        auth_b64 = base64.b64encode(auth_bytes).decode('ascii')
        auth_stanza = f"<auth xmlns='urn:ietf:params:xml:ns:xmpp-sasl' mechanism='PLAIN'>{auth_b64}</auth>"
        s.sendall(auth_stanza.encode('utf-8'))

        resp = s.recv(4096).decode('utf-8', errors='ignore')
        if "success" not in resp:
            print(f"[seed-messages] Auth failed: {resp}", file=sys.stderr)
            return

        # Step 3: Re-open stream
        s.sendall(b"<stream:stream to='localhost' xmlns='jabber:client' xmlns:stream='http://etherx.jabber.org/streams' version='1.0'>")
        resp = s.recv(4096).decode('utf-8', errors='ignore')

        # Step 4: Resource bind
        bind_iq = "<iq type='set' id='bind-1'><bind xmlns='urn:ietf:params:xml:ns:xmpp-bind'><resource>seed-bot</resource></bind></iq>"
        s.sendall(bind_iq.encode('utf-8'))
        resp = s.recv(4096).decode('utf-8', errors='ignore')

        # Step 5: Send sample messages from userb to usera
        messages = [
            "Hey User A! Welcome to Stanza XMPP Client.",
            "Here is a code snippet to test code block formatting and syntax highlighting:\n```csharp\npublic sealed class XmppClient\n{\n    public async Task ConnectAsync() => Console.WriteLine(\"Connected!\");\n}\n```",
            "Local Docker XMPP infrastructure is operational! 🚀 Everything is pre-configured and ready for screenshots."
        ]

        for idx, body in enumerate(messages):
            time.sleep(0.3)
            # Escape xml
            escaped_body = body.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
            msg_stanza = f"<message to='usera@localhost' from='userb@localhost/seed-bot' type='chat' id='seed-msg-{idx+1}'><body>{escaped_body}</body></message>"
            s.sendall(msg_stanza.encode('utf-8'))

        time.sleep(0.5)
        # Close stream
        s.sendall(b"</stream:stream>")
        print("[seed-messages] Sample messages successfully seeded to usera@localhost")
    except Exception as e:
        print(f"[seed-messages] Error seeding messages: {e}", file=sys.stderr)
    finally:
        s.close()

if __name__ == "__main__":
    seed()
