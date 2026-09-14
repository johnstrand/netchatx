# NetChatx

**NetChatx** is a modern, cross-platform XMPP desktop client written in C# targeting **.NET 10** and powered by **Avalonia UI 11**. It is engineered for responsiveness, low memory usage, extensive protocol compliance (Modern Client Compliance Suite / XEP-0459), and **OMEMO (XEP-0384)** end-to-end encryption.

---

## Highlights & Features

- **Modern Cross-Platform GUI**: Built with **Avalonia UI 11** and Fluent Design, offering dark and light theme adaptability, crisp high-DPI scaling, and native feel across Windows, Linux, and macOS.
- **Deep Customization & Settings**:
  - **Typography**: Custom font family and configurable font size.
  - **Chat Bubble Styling**: Customizable background colors for incoming and outgoing chat bubbles.
  - **Message Merging**: Configurable time window for merging consecutive messages from the same sender into clean conversational bubbles.
  - **Dynamic Chat Box**: Text wrapping with auto-expanding input field up to a configurable maximum line count before scrolling begins.
- **Native System Notifications & Flashing**:
  - **Platform-Specific Notifications**: Hooks into native OS notification subsystems (Windows Runtime / PowerShell toasts, Linux D-Bus / `notify-send`, and macOS AppleScript notifications).
  - **Window & Taskbar Flashing**: Flashes window and taskbar / dock icon on new messages when the window is inactive or running in the background.
  - **Configurable Preferences**: Independent settings toggles for system popups and window flashing.
- **Rich Media & In-Chat Previews**:
  - **Clipboard Image Pasting**: Directly paste screenshots or copied images (`Ctrl+V` / `Cmd+V`) using native Windows clipboard interop (`PNG` & `CF_DIB`) and cross-platform clipboard providers.
  - **File Attachments**: Pick and send images via the 📎 attachment button.
  - **XEP-0363 HTTP File Upload**: Automatically uploads media to server-hosted HTTP upload slots; seamlessly falls back to local media caching (`%AppData%/NetChatx/media/`) when offline or on servers without HTTP upload.
  - **Asynchronous Thumbnail Rendering**: Non-blocking image loading and caching with loading indicators; automatically hides raw URLs for image-only messages and opens images in the system's default viewer on click.
- **Robust Message Archive & History**:
  - **XEP-0313 MAM v2**: Automatic bidirectional history synchronization and RSM pagination.
  - **Intelligent Deduplication**: Eliminates duplicate messages from server synchronization and live incoming streams using stanza IDs and fuzzy timestamp matching.
  - **Date Headers & Autoscroll**: Sticky date separators ("Today", "Yesterday", full date) and smart scrolling that keeps you locked to the newest messages while preserving view position when loading older history.
- **End-to-End Encryption (OMEMO)**:
  - **XEP-0384 & XEP-0420**: Multi-device Signal Double Ratchet encryption with Stanza Content Encryption (SCE) envelopes.
  - Atomic local key management and PEP bundle synchronization.
- **High-Performance Architecture**:
  - Built on `System.IO.Pipelines` and low-allocation streaming XML.
  - Fully decoupled UI thread using `CommunityToolkit.Mvvm`, keeping networking, cryptography, disk I/O, and image decoding completely off the main render loop.
- **Embedded SQLite Storage**: Built on `Microsoft.Data.Sqlite` in WAL mode for lightning-fast message archives, full-text search, contact roster caching, and account profiles.

---

## Supported Specifications & XEP Matrix

NetChatx complies with the **Modern Client Compliance Suite (XEP-0459)**:

| Category | Specification | Description |
| :--- | :--- | :--- |
| **Core & Transport** | **RFC 6120 / 6121** | XMPP Core & Instant Messaging (Streams, TLS, Bind, Session, Roster) |
| | **RFC 7622** | JID format parsing and PRECIS stringprep validation |
| | **RFC 5802 / 7677** | SASL SCRAM-SHA-256 / SCRAM-SHA-1 and PLAIN authentication |
| | **XEP-0198** | Stream Management (stanza ack counters, seamless network resumption) |
| | **XEP-0199** | XMPP Ping & keepalive latency monitoring |
| | **XEP-0352** | Client State Indication (Active / Inactive battery & bandwidth optimization) |
| **Discovery & Caps** | **XEP-0030** | Service Discovery (`disco#info`, `disco#items`) |
| | **XEP-0115** | Entity Capabilities (caps hash caching) |
| **Messaging & Chat** | **XEP-0280** | Message Carbons (real-time sync across all user devices) |
| | **XEP-0313** | Message Archive Management (MAM v2 archive sync & RSM pagination) |
| | **XEP-0359** | Unique and Stable Stanza IDs (`origin-id` and `stanza-id`) |
| | **XEP-0308** | Last Message Correction (message editing) |
| | **XEP-0184** | Message Delivery Receipts (`✓` delivered, `✓✓` read) |
| | **XEP-0333** | Chat Markers (`received`, `displayed`, `acknowledged`) |
| | **XEP-0085** | Chat State Notifications (`composing`, `paused`, `active`, `inactive`, `gone`) |
| **Group Chat (MUC)** | **XEP-0045** | Multi-User Chat (rooms, nicknames, subjects, occupant roster) |
| | **XEP-0249** | Direct MUC Invitations |
| **File Sharing** | **XEP-0363** | HTTP File Upload (slot discovery, PUT upload, media preview links) |
| **End-to-End Encryption** | **XEP-0384** | OMEMO Encryption (Curve25519 Double Ratchet & PEP bundles) |
| | **XEP-0420** | Stanza Content Encryption (SCE payload envelopes) |

---

## Project Structure

```
NetChatx/
├── NetChatx.slnx
├── src/
│   ├── NetChatx.Core/              # XMPP protocol engine, RFC 7622 JID, streaming XML lexer, SASL, Pipelines transport
│   ├── NetChatx.Protocol.Xeps/     # Modular XEP features (XEP-0198, MAM, Carbons, MUC, HTTP Upload, OMEMO)
│   ├── NetChatx.Storage/           # SQLite repositories for messages, roster, accounts, and OMEMO sessions
│   └── NetChatx.Gui/               # Avalonia UI 11 desktop application (MVVM, Views, ViewModels, Clipboard & Media helpers)
│
└── tests/
    ├── NetChatx.Core.Tests/        # Core protocol, JID parsing, XML streaming, SASL unit tests
    ├── NetChatx.Gui.Tests/         # ViewModels, message paging, image extraction, and clipboard unit tests
    ├── NetChatx.Storage.Tests/     # SQLite repository and persistence tests
    ├── NetChatx.Xeps.Tests/        # XEP feature suite & OMEMO encryption tests
    └── NetChatx.MockServer/        # In-memory loopback XMPP test server harness
```

---

## Getting Started

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or later)

### Run the GUI Application
To start NetChatx:
```bash
dotnet run --project src/NetChatx.Gui/NetChatx.Gui.csproj
```

### Running Tests & Code Coverage
NetChatx includes an in-memory loopback XMPP mock server (`NetChatx.MockServer`), allowing end-to-end protocol and UI logic testing without an external XMPP server:

```bash
dotnet test NetChatx.slnx
```

To run tests with code coverage and generate HTML & Cobertura reports locally:
```bash
# Using PowerShell:
pwsh ./scripts/coverage.ps1

# Using Bash:
./scripts/coverage.sh
```

Or directly via the .NET CLI:
```bash
dotnet test NetChatx.slnx --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory ./TestResults
reportgenerator -reports:TestResults/**/coverage.cobertura.xml -targetdir:coveragereport "-reporttypes:MarkdownSummaryGithub;Html;Cobertura;Badges"
```
The resulting reports will be generated in `coveragereport/`:
- `SummaryGithub.md`: Formatted for GitHub Step Summary and PR comments.
- `index.html`: Interactive browsable HTML coverage report.
- `Cobertura.xml`: Unified Cobertura XML report for CI systems and third-party tools.

### Building for Release
```bash
dotnet build NetChatx.slnx -c Release
```

---

## Releases & Installation Packages

NetChatx features an automated, run-on-demand GitHub Actions release pipeline ([`.github/workflows/release.yml`](.github/workflows/release.yml)) that produces self-contained, native installation packages for 64-bit systems:

### Platform Packages
- **Windows (`win-x64`)**:
  - **Inno Setup Installer (`.exe`)**: `NetChatx-v{version}-win-x64-installer.exe` featuring Start Menu & Desktop shortcuts, uninstaller, and icon associations.
  - **Portable Archive (`.zip`)**: `NetChatx-v{version}-win-x64.zip` for instant portable execution.
- **Linux (`linux-x64`)**:
  - **Debian Package (`.deb`)**: `NetChatx-v{version}-linux-x64.deb` installing to `/usr/lib/netchatx` with `/usr/bin/netchatx` launcher, desktop launcher entry, and hi-res hicolor app icon.
  - **Tarball Archive (`.tar.gz`)**: `NetChatx-v{version}-linux-x64.tar.gz` for portable installation across all Linux distributions.
- **macOS / OSX (`osx-arm64` & `osx-x64`)**:
  - **DMG Disk Image (`.dmg`)**: `NetChatx-v{version}-osx-{arch}.dmg` with drag-to-Applications installer, retina `.icns` bundle icons, and ad-hoc code signing for Apple Silicon and Intel Macs.
  - **App Bundle ZIP (`.zip`)**: `NetChatx-v{version}-osx-{arch}.zip` containing the signed `NetChatx.app`.

### Versioning & Git Labels
- **Rolling Minor/Major Versioning**: The release workflow automatically increments the minor version (`0.1.0` $\rightarrow$ `0.2.0` $\dots \rightarrow$ `0.9.0`). When the minor version reaches `10`, it resets to `0` and increments the major version (`0.9.0` $\rightarrow$ `1.0.0`, `1.9.0` $\rightarrow$ `2.0.0`).
- **Git Tags & Labels**: Each release automatically creates an annotated git release tag (`vX.Y.0`) and creates/updates a matching GitHub repository label (`vX.Y.0`).
- **On-Demand Dispatch**:
  ```bash
  # Trigger automated release bump
  gh workflow run release.yml

  # Test build matrix with dry-run mode
  gh workflow run release.yml -f dry_run=true

  # Trigger release with explicit version override
  gh workflow run release.yml -f version_override="1.0.0"
  ```

---

## Keyboard Shortcuts & Interaction

- **`Enter`**: Send message.
- **`Shift + Enter`**: Insert new line in the text field.
- **`Ctrl + V` / `Cmd + V`**: Paste image directly from clipboard as a chat image.
- **`📎` Button**: Browse and attach image files (`.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.bmp`).
- **Click Thumbnail**: Open full image in system default viewer.
- **Search Bar**: Quick-filter through messages and roster contacts.
