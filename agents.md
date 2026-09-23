# Stanza Agent Guidelines (`agents.md`)

Welcome, AI Agent! This document contains instructions, operational rules, and project-specific conventions for working on the **Stanza** repository. Follow these guidelines carefully to maintain code quality, repository cleanliness, and development velocity.

---

## 1. Git Worktree Workflow (Mandatory)

To keep the primary repository tree pristine and avoid interference across parallel or isolated tasks, **all development, testing, and modifications must occur within a Git worktree inside the `.worktree` directory**.

### 1.1 Directory Initialization
Before starting work on a feature, bugfix, or refactor, verify if the `.worktree` directory exists at the root of the repository. If it does not exist, create it:

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

*(Note: `.worktree/` is ignored by Git in `.gitignore`.)*

### 1.2 Creating a Worktree
Always create an isolated branch and worktree under `.worktree/<branch-name>`:

- **New branch from current `HEAD` (or `main`)**:
  ```bash
  git worktree add .worktree/<branch-name> -b <branch-name>
  ```
- **Existing branch**:
  ```bash
  git worktree add .worktree/<branch-name> <branch-name>
  ```

### 1.3 Working in the Worktree
Once the worktree is created, switch your working directory to the newly created worktree path:
```bash
cd .worktree/<branch-name>
```
Perform all code modifications, builds, tests, and Git operations (stage, commit, push) **exclusively within that directory**.

### 1.4 Pull Requests & Post-Task Cleanup
When your task is complete, committed, and ready:
1. Push your branch and open a Pull Request (PR) on GitHub. **Pull Requests via GitHub are mandatory for merging code into `main`**; direct pushes or merges to `main` are strictly prohibited.
2. Once the PR is merged, navigate back to the repository root.
3. Remove the worktree when it is no longer needed:
   ```bash
   git worktree remove .worktree/<branch-name>
   ```
4. Prune dead worktree references if necessary:
   ```bash
   git worktree prune
   ```

---

## 2. Project Overview & Architecture

**Stanza** is a cross-platform XMPP client built on **.NET 10** and **Avalonia UI 11**.

```
Stanza/
├── Stanza.slnx                    # Solution file
├── src/
│   ├── Stanza.Core/               # RFC 6120/6121 XMPP engine, RFC 7622 JID parser, System.IO.Pipelines transport, XML streaming
│   ├── Stanza.Protocol.Xeps/      # XEP implementations (XEP-0198, MAM, Carbons, MUC, HTTP Upload, OMEMO XEP-0384)
│   ├── Stanza.Storage/            # SQLite repositories (messages, roster, accounts, OMEMO keys/sessions)
│   └── Stanza.Gui/                # Avalonia UI 11 desktop app (MVVM, Views, ViewModels, native platform interop)
└── tests/
    ├── Stanza.Core.Tests/         # Protocol, parsing, and pipeline tests
    ├── Stanza.Gui.Tests/          # ViewModel and UI helper tests
    ├── Stanza.Storage.Tests/      # SQLite persistence tests
    ├── Stanza.Xeps.Tests/         # XEP feature suite & cryptographic tests
    └── Stanza.MockServer/         # In-memory loopback XMPP test server harness
```

---

## 3. Build & Test Commands

Always verify your changes by building the solution and executing tests before marking any task as complete:

### 3.1 Build Solution
```bash
dotnet build Stanza.slnx
```

### 3.2 Run Test Suite
```bash
dotnet test Stanza.slnx
```
*(Tests utilize `Stanza.MockServer`, an in-memory loopback server, requiring no external network dependencies.)*

### 3.3 Run Code Coverage (Optional / Verification)
- **PowerShell**:
  ```powershell
  pwsh ./scripts/coverage.ps1
  ```
- **Bash**:
  ```bash
  ./scripts/coverage.sh
  ```

### 3.4 Running the Application
```bash
dotnet run --project src/Stanza.Gui/Stanza.Gui.csproj
```

---

## 4. Coding Standards & Guidelines

1. **Target Runtime & Language**:
   - Target **.NET 10** (`net10.0`) and C# 13.
   - Nullable reference types are enabled (`<Nullable>enable</Nullable>`). Ensure no warnings (`CS8600`, `CS8602`, etc.) are introduced.
   - Use file-scoped namespaces (`namespace Stanza.Core;`).
   - Use primary constructors, pattern matching, and target-typed `new()` where it improves clarity.

2. **Asynchronous & Resource Patterns**:
   - Always accept and thread through `CancellationToken` for asynchronous I/O and network operations.
   - In library code (`Stanza.Core`, `Stanza.Protocol.Xeps`, `Stanza.Storage`), use `.ConfigureAwait(false)` on awaited tasks.
   - Properly dispose of unmanaged resources and streams using `await using` or `using`.

3. **MVVM & UI Thread Safety**:
   - `Stanza.Gui` uses `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
   - All network stanzas, cryptographic computations, and disk I/O **must remain off the UI thread**.
   - Dispatch to UI thread only when mutating observable collections or properties bound to the UI (e.g., via `Dispatcher.UIThread`).

4. **Security & Cryptography**:
   - Never log or persist plaintext passwords, pre-shared keys, or private cryptographic keys.
   - All OMEMO sessions and PEP bundles must follow XEP-0384 & XEP-0420 specifications.

---

## 5. Git & Commit Conventions

- Use **Conventional Commits** format:
  - `feat: <description>` for new features
  - `fix: <description>` for bug fixes
  - `refactor: <description>` for code refactoring
  - `test: <description>` for adding or updating tests
  - `docs: <description>` for documentation changes
- Keep commits atomic and focused.
- Ensure all tests pass prior to committing.
- **Pull Requests (PRs)**: Pull Requests via GitHub are **mandatory** for merging any code into `main`. Direct pushes or merges into `main` are strictly prohibited.
