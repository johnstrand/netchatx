# NetChatx

**NetChatx** is a terminal-based (TUI) XMPP client written in C# targeting **.NET 10** with **Native AOT** readiness. It is engineered for responsiveness, low memory usage, and extensive protocol compliance—featuring the modern client compliance suite alongside **OMEMO (XEP-0384)** end-to-end encryption.

---

## Key Features

- **Blazing Fast & Responsive**: Built on `System.IO.Pipelines` and low-allocation streaming XML. The UI thread (`Terminal.Gui v2`) is fully decoupled from networking, encryption, and disk I/O via asynchronous queues and worker channels.
- **Native AOT Compatible**: Zero dynamic code generation or runtime reflection in the core path. Instant startup with minimal footprint.
- **Embedded SQLite Storage**: Built on `Microsoft.Data.Sqlite` with WAL mode for message archives, full-text search, contact rosters, and atomic OMEMO ratchet session management.
- **Hybrid Terminal UI**: IRC/Profanity-style slash commands (`/join`, `/msg`, `/omemo`, `/theme`) with tab-completion and hotkey navigation (`Alt+1..9`), paired with Terminal.Gui's mouse support, split panes, and dialogs.
- **Multi-Account Concurrent Support**: Run multiple XMPP accounts simultaneously with buffer tabs.

---

## Supported Specifications & XEP Matrix

NetChatx supports the complete **Modern Client Compliance Suite (XEP-0459)**:

| Category | Specification | Description |
| :--- | :--- | :--- |
| **Core & Transport** | **RFC 6120 / 6121** | XMPP Core & Instant Messaging (Streams, TLS, Bind, Session, Roster) |
| | **RFC 7622** | JID format parsing and PRECIS stringprep validation |
| | **RFC 5802 / 7677** | SASL SCRAM-SHA-256 / SCRAM-SHA-1 and PLAIN authentication |
| | **XEP-0198** | Stream Management (stanza ack counters, seamless network reconnection) |
| | **XEP-0199** | XMPP Ping & keepalive latency monitoring |
| | **XEP-0352** | Client State Indication (Active / Inactive bandwidth & battery saving) |
| **Discovery & Caps** | **XEP-0030** | Service Discovery (`disco#info`, `disco#items`) |
| | **XEP-0115** | Entity Capabilities (caps hash caching) |
| **Messaging & Chat** | **XEP-0280** | Message Carbons (instant synchronization across all user devices) |
| | **XEP-0313** | Message Archive Management (MAM v2 historical archive sync & RSM paging) |
| | **XEP-0359** | Unique and Stable Stanza IDs (`origin-id` and `stanza-id`) |
| | **XEP-0308** | Last Message Correction (edit sent messages with `/edit` / `<replace>`) |
| | **XEP-0184** | Message Delivery Receipts (`<request>` and `<received>`) |
| | **XEP-0333** | Chat Markers (`received`, `displayed`, `acknowledged`) |
| | **XEP-0085** | Chat State Notifications (`composing`, `paused`, `active`, `inactive`, `gone`) |
| **Group Chat (MUC)** | **XEP-0045** | Multi-User Chat (rooms, nicknames, subject, occupant tracking, roles) |
| | **XEP-0249** | Direct MUC Invitations |
| **File Sharing** | **XEP-0363** | HTTP File Upload (request upload slot, PUT upload with progress, media links) |
| **End-to-End Encryption** | **XEP-0384** | OMEMO Multi-End Encryption (Curve25519 Double Ratchet & PEP bundles) |
| | **XEP-0420** | Stanza Content Encryption (SCE payload envelopes) |

---

## Project Structure

```
NetChatx/
├── NetChatx.slnx
├── src/
│   ├── NetChatx.Core/              # Protocol engine, RFC 7622 JID, streaming XML lexer, SASL, Pipelines transport
│   ├── NetChatx.Protocol.Xeps/     # Modular XEP features (XEP-0198, MAM, Carbons, MUC, OMEMO Double Ratchet)
│   ├── NetChatx.Storage/           # SQLite repositories for messages, roster, accounts, and OMEMO sessions
│   ├── NetChatx.Tui/               # Terminal.Gui v2 views, split panes, command processor, themes
│   └── NetChatx.App/               # CLI entrypoint, Native AOT configuration, app bootstrap
│
└── tests/
    ├── NetChatx.Core.Tests/        # Core protocol, JID, XML streaming, SASL unit tests
    ├── NetChatx.Storage.Tests/     # SQLite repository and persistence tests
    ├── NetChatx.Xeps.Tests/        # XEP feature suite & OMEMO encryption tests
    └── NetChatx.MockServer/        # In-memory loopback XMPP test server harness
```

---

## TUI Keyboard Shortcuts & Commands

### Slash Commands
| Command | Description |
| :--- | :--- |
| `/connect <jid> <password> [host] [port]` | Connect to an XMPP account |
| `/disconnect` | Disconnect the active connection |
| `/join <room@muc> [nickname] [password]` | Join a Multi-User Chat room |
| `/leave [room@muc]` | Leave the active or specified room |
| `/msg <jid> <text>` | Send a direct 1-on-1 message |
| `/query <jid>` | Open a dedicated chat buffer without sending text |
| `/close` | Close the active chat buffer |
| `/clear` | Clear message history in the active buffer view |
| `/status <available\|away\|dnd\|xa> [text]` | Update presence status |
| `/theme <Catppuccin\|Gruvbox\|Nord\|Classic>`| Switch TUI color theme |
| `/help` | Display command reference |
| `/quit` | Exit NetChatx |

### Hotkeys
- **`Alt+1` .. `Alt+9`**: Switch directly to buffer 1 .. 9.
- **`Enter`**: Submit command or send message in active buffer.
- **`Tab`**: Auto-complete command names, contact JIDs, or room nicknames.

---

## Building & Testing

### Requirements
- **.NET 10 SDK** (or later)

### Run Tests
NetChatx includes a built-in in-memory loopback XMPP test server (`NetChatx.MockServer`), allowing full protocol validation without requiring an external server daemon:

```bash
dotnet test NetChatx.slnx
```

### Run the TUI Client
```bash
dotnet run --project src/NetChatx.App/NetChatx.App.csproj
```

### Publish Native AOT Executable
```bash
dotnet publish src/NetChatx.App/NetChatx.App.csproj -c Release
```
This produces a standalone, single-file native executable (`netchatx.exe` on Windows or `netchatx` on Linux/macOS) with instant startup and no .NET runtime prerequisites.
