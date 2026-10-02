# Contributing to Stanza

Thank you for your interest in contributing to **Stanza**! We are building a modern, cross-platform XMPP desktop client engineered for high performance, deep customization, Native AOT compilation, and state-of-the-art end-to-end encryption.

This guide provides instructions, operational rules, and project-specific conventions to help you set up your development environment, understand the repository structure, follow our development workflow, and submit pull requests.

---

## Table of Contents

- [Project Overview](#project-overview)
- [Prerequisites & Development Environment](#prerequisites--development-environment)
- [Git Worktree Development Workflow](#git-worktree-development-workflow)
  - [1. Ensure `.worktree` Directory Exists](#1-ensure-worktree-directory-exists)
  - [2. Create a Worktree](#2-create-a-worktree)
  - [3. Work Inside the Worktree](#3-work-inside-the-worktree)
  - [4. Post-Task Cleanup](#4-post-task-cleanup)
- [Coding Guidelines & Standards](#coding-guidelines--standards)
  - [Target Runtime & C# Version](#target-runtime--c-version)
  - [File-Scoped Namespaces](#file-scoped-namespaces)
  - [Nullable Reference Types](#nullable-reference-types)
  - [Asynchronous Patterns & ConfigureAwait](#asynchronous-patterns--configureawait)
  - [Threading & CancellationTokens](#threading--cancellationtokens)
  - [MVVM & UI Thread Isolation](#mvvm--ui-thread-isolation)
  - [Native AOT & Trimming Compatibility](#native-aot--trimming-compatibility)
  - [Security & Cryptography](#security--cryptography)
- [Building & Testing](#building--testing)
  - [Build Solution](#build-solution)
  - [Run Tests](#run-tests)
  - [Code Coverage](#code-coverage)
  - [Run GUI Application](#run-gui-application)
  - [Native AOT Publish](#native-aot-publish)
- [Git Commit & Branch Conventions](#git-commit--branch-conventions)
  - [Branch Naming](#branch-naming)
  - [Conventional Commits](#conventional-commits)
- [Pull Request Submission Process](#pull-request-submission-process)

---

## Project Overview

Stanza is built on a modern .NET stack:

- **.NET 10** (`net10.0`) and C# 13.
- **Avalonia UI 11**: Cross-platform desktop interface (Windows, Linux, macOS) using Fluent Design.
- **`System.IO.Pipelines` & Low-Allocation Streaming XML**: High-throughput non-blocking network I/O.
- **CommunityToolkit.Mvvm**: Clean MVVM architecture with compiled bindings and decoupled background tasks.
- **SQLite (WAL mode)** via `Microsoft.Data.Sqlite`: Fast local embedded storage for history, roster, accounts, and keys.
- **OMEMO (XEP-0384 / XEP-0420)**: Signal Double Ratchet end-to-end encryption with Curve25519, AES-128-GCM, and HKDF.
- **Native AOT (`PublishAot=true`)**: Direct compilation to native machine code with zero JIT overhead.

---

## Prerequisites & Development Environment

Before contributing, ensure you have the following installed:

1. **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** (version 10.0.100 or later).
   ```bash
   dotnet --version
   ```
2. **[Git](https://git-scm.com/)** (v2.30 or later recommended).
3. **[GitHub CLI (`gh`)](https://cli.github.com/)** (recommended for opening and managing pull requests).
4. **Supported IDEs / Editors**:
   - Visual Studio 2026 / Visual Studio Code (with C# Dev Kit extension) / JetBrains Rider.
   - Avalonia IDE extension (for XAML preview and IntelliSense).

---

## Git Worktree Development Workflow

To keep the primary repository tree pristine, prevent unintentional file tracking, and isolate parallel development tasks, **all development, testing, and modifications must occur within a Git worktree inside the `.worktree` directory**.

This workflow is mandatory for both human contributors and AI agents (as documented in `AGENTS.md`).

### 1. Ensure `.worktree` Directory Exists

From the repository root (`netchatx`):

- **PowerShell**:
  ```powershell
  if (!(Test-Path -Path ".worktree")) {
      New-Item -ItemType Directory -Path ".worktree" | Out-Null
  }
  ```
- **Bash**:
  ```bash
  mkdir -p .worktree
  ```
  _(Note: `.worktree/` is ignored by Git in `.gitignore`.)_

### 2. Create a Worktree

Always create an isolated branch and worktree under `.worktree/<branch-name>`:

- **Create new branch from `main`**:
  ```bash
  git worktree add .worktree/<branch-name> -b <branch-name>
  ```
- **Work on an existing branch**:
  ```bash
  git worktree add .worktree/<branch-name> <branch-name>
  ```

### 3. Work Inside the Worktree

Navigate to your newly created worktree directory:

```bash
cd .worktree/<branch-name>
```

Perform all code modifications, builds, tests, and Git commits **exclusively within this directory**. Do **not** modify files in the main repository root while working on a branch.

Ensure that `dotnet format` is run before committing to maintain consistent code style.

### 4. Post-Task Cleanup

Once your pull request is merged:

1. Return to the repository root:
   ```bash
   cd ../..
   ```
2. Remove the worktree:
   ```bash
   git worktree remove .worktree/<branch-name>
   ```
3. Prune obsolete worktree tracking entries:
   ```bash
   git worktree prune
   ```
4. Alternatively, use the automated workspace cleanup script:
   ```powershell
   pwsh ./scripts/cleanup-workspaces.ps1
   ```

---

## Coding Guidelines & Standards

### Target Runtime & C# Version

- All projects target **.NET 10** (`net10.0`) and C# 13.
- Modern C# features are encouraged where they enhance clarity and performance:
  - Primary constructors
  - Collection expressions (`[elem1, elem2]`)
  - Pattern matching (`is`, `switch` expressions)
  - Target-typed `new()`

### File-Scoped Namespaces

Always use **file-scoped namespaces** (`namespace Stanza.Core;`). Do **not** use block-scoped namespaces with braces (`namespace Stanza.Core { ... }`).

```csharp
// Correct:
namespace Stanza.Core.Transport;

public sealed class TcpTlsTransport : IXmppTransport
{
    // ...
}
```

### Nullable Reference Types

Nullable reference types are enabled across all projects (`<Nullable>enable</Nullable>`).

- Code must compile with **zero warnings** (`CS8600`, `CS8602`, `CS8603`, `CS8618`, etc.).
- Annotate nullable parameters and return types appropriately (`string?`, `XmppElement?`).
- Use null-forgiving operators (`!`) sparingly and only when accompanied by sound invariant justification.

### Asynchronous Patterns & ConfigureAwait

- In library code (**`Stanza.Core`**, **`Stanza.Protocol.Xeps`**, **`Stanza.Storage`**), always append `.ConfigureAwait(false)` to awaited tasks:
  ```csharp
  var buffer = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
  ```
- In GUI code (**`Stanza.Gui`**), omit `.ConfigureAwait(false)` when execution must resume on the Avalonia UI synchronization context.

### Threading & CancellationTokens

- Always accept a `CancellationToken` for asynchronous I/O and network operations, with default value `cancellationToken = default`.
- Thread `cancellationToken` down through all asynchronous call chains.
- Ensure proper resource cleanup by using `await using` or `using` on disposables (`IAsyncDisposable` / `IDisposable`).

### MVVM & UI Thread Isolation

- `Stanza.Gui` uses `CommunityToolkit.Mvvm`:
  - ViewModels inherit from `ViewModelBase` / `ObservableObject`.
  - Use `[ObservableProperty]` and `[RelayCommand]` attributes.
- **Never perform network I/O, cryptographic computations, SQLite database access, or image decoding on the UI thread.**
- When background operations complete, marshal state updates to the UI thread using Avalonia's dispatcher:
  ```csharp
  Dispatcher.UIThread.Post(() =>
  {
      Messages.Add(newBubble);
  });
  ```

### Native AOT & Trimming Compatibility

Stanza compiles as a self-contained Native Ahead-Of-Time application (`PublishAot=true`).

- All libraries must maintain trim and AOT compatibility (`<IsAotCompatible>true</IsAotCompatible>`, `<IsTrimmable>true</IsTrimmable>`).
- **Forbidden**:
  - Runtime reflection and unconstrained generic type instantiation (`Type.GetType`, `MakeGenericType`, `Activator.CreateInstance`).
  - Runtime code generation (`System.Reflection.Emit`).
  - Serializers relying on runtime reflection.
- **Required**:
  - Compiled XAML bindings (`AvaloniaUseCompiledBindingsByDefault = true`).
  - Compile-time Roslyn source generators (such as `CommunityToolkit.Mvvm`).

### Security & Cryptography

- **Never log, persist, or expose** sensitive credentials, including plaintext passwords, pre-shared secrets, or private cryptographic keys.
- Cryptographic keys must be securely stored in SQLite or zeroed out from memory when no longer needed.
- Follow XEP-0384 and XEP-0420 strictly for OMEMO encryption and SCE envelope construction.

---

## Building & Testing

Always verify your changes by building the entire solution and running all tests prior to committing.

### Build Solution

Build the solution from within your worktree directory:

```bash
dotnet build Stanza.slnx
```

### Run Tests

Execute the full automated test suite:

```bash
dotnet test Stanza.slnx
```

> [!NOTE]
> Tests run against `Stanza.MockServer`, an in-memory loopback mock server based on `System.IO.Pipelines`. No network connection or external XMPP server is required to run the full test suite.

### Code Coverage

Generate HTML and Cobertura code coverage reports:

- **PowerShell**:
  ```powershell
  pwsh ./scripts/coverage.ps1
  ```
- **Bash**:
  ```bash
  ./scripts/coverage.sh
  ```
  Reports are written to `coveragereport/index.html`.

### Run GUI Application

Launch Stanza locally:

```bash
dotnet run --project src/Stanza.Gui/Stanza.Gui.csproj
```

### Native AOT Publish

Test Native AOT publishing for your platform:

```bash
# Windows
dotnet publish src/Stanza.Gui/Stanza.Gui.csproj -c Release -r win-x64

# Linux
dotnet publish src/Stanza.Gui/Stanza.Gui.csproj -c Release -r linux-x64

# macOS Apple Silicon
dotnet publish src/Stanza.Gui/Stanza.Gui.csproj -c Release -r osx-arm64
```

---

## Git Commit & Branch Conventions

### Branch Naming

Create descriptive branch names following the format:

- `feat/<short-description>`: New feature
- `fix/<short-description>`: Bug fix
- `refactor/<short-description>`: Code refactoring
- `test/<short-description>`: Test suite updates
- `docs/<short-description>`: Documentation changes

### Conventional Commits

All commits must follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

```
<type>(<optional-scope>): <description>

[optional body]

[optional footer(s)]
```

#### Allowed Types:

- `feat`: A new feature or capability.
- `fix`: A bug fix.
- `refactor`: Code changes that neither fix a bug nor add a feature.
- `test`: Adding missing tests or correcting existing tests.
- `docs`: Documentation updates.
- `ci`: Changes to CI/CD workflows and scripts.
- `chore`: Maintenance, build dependencies, or auxiliary tool updates.

#### Examples:

- `feat(omemo): add support for prekey bundle rotation`
- `fix(transport): handle reconnection backoff on socket disconnect (#112)`
- `docs: add CONTRIBUTING.md and architecture guide`
- `refactor(gui): extract message bubble rendering into dedicated control`

---

## Pull Request Submission Process

1. **Verify Build and Tests**:
   Ensure the solution builds cleanly with zero warnings/errors and all tests pass:
   ```bash
   dotnet build Stanza.slnx
   dotnet test Stanza.slnx
   ```
2. **Commit Changes**:
   Stage your changes and commit with a conventional commit message:
   ```bash
   git add -A
   git commit -m "feat(module): descriptive commit message"
   ```
3. **Push to Remote**:
   Push your worktree branch to origin:
   ```bash
   git push -u origin <branch-name>
   ```
4. **Create Pull Request**:
   Use the GitHub CLI (`gh`) or GitHub web interface:
   ```bash
   gh pr create --title "feat(module): descriptive title" --body "Closes #<issue-number>\n\nDetailed explanation of changes." --base main
   ```
   > [!IMPORTANT]
   > Direct pushes and merges to `main` are strictly prohibited. All changes must be submitted via Pull Request and pass continuous integration checks.
5. **Review & Iterate**:
   CI will automatically run builds, tests, and formatting checks. Address any feedback or requested changes by pushing additional commits to your branch.
