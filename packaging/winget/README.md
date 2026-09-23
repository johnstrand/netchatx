# WinGet Package Infrastructure for Stanza (`JohnStrand.Stanza`)

This directory contains the manifest templates and specifications for publishing the Windows version of **Stanza** to the official **Windows Package Manager (WinGet)** repository ([`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs)).

---

## 📦 Package Details

- **Package Identifier**: `JohnStrand.Stanza`
- **Moniker**: `stanza`
- **Installer Type**: Inno Setup (`.exe`)
- **Architecture**: `x64`
- **Installation Scope**: Machine (`Program Files`)
- **Product Code**: `{D37E84B1-0268-4C9C-A20D-261BAE24523F}_is1`

---

## 🚀 Installation via WinGet

Once published to `microsoft/winget-pkgs`, users can install Stanza with:

```powershell
winget install JohnStrand.Stanza
```

Or by moniker:

```powershell
winget install stanza
```

To upgrade:

```powershell
winget upgrade JohnStrand.Stanza
```

---

## 🤖 Automated CI/CD Publishing

When a new release is published via `.github/workflows/release.yml`:

1. The Windows installer `Stanza-v<version>-win-x64-installer.exe` is built and attached to the GitHub Release.
2. The `publish-winget` job triggers:
   - Evaluates if `publish_to_winget` is enabled (default: `true`).
   - Requests manual approval through the GitHub Environment `winget`.
   - Executes `scripts/publish-winget.ps1` with the release version and installer URL.
   - If `WINGET_TOKEN` is present in repository secrets, `wingetcreate` submits a pull request to `microsoft/winget-pkgs`.
   - If `WINGET_TOKEN` is unset, the job logs helpful setup instructions and exits cleanly.

### Setting up the `WINGET_TOKEN` Secret

To enable automated pull requests to `microsoft/winget-pkgs`:

1. Create a GitHub Personal Access Token (Classic) with `public_repo` scope at [GitHub Tokens](https://github.com/settings/tokens).
   *(Alternatively, use a Fine-Grained Personal Access Token with access to public repositories and pull requests).*
2. In this repository, go to **Settings** $\rightarrow$ **Secrets and variables** $\rightarrow$ **Actions**.
3. Create a new repository secret named `WINGET_TOKEN` and paste your token.
4. (Optional) In **Settings** $\rightarrow$ **Environments**, configure the `winget` environment with required reviewers to enforce manual approval before publishing.

---

## 🛠️ Local Manifest Generation & Verification

You can generate manifests locally or verify them without submitting a PR:

### 1. Dry-run / Generate Manifests Locally
```powershell
pwsh ./scripts/publish-winget.ps1 -Version "0.1.0" -DryRun
```

### 2. Specify a Local Installer File
```powershell
pwsh ./scripts/publish-winget.ps1 -Version "0.1.0" -InstallerFile "dist/Stanza-v0.1.0-win-x64-installer.exe" -DryRun
```

### 3. Submit PR Locally with a Token
```powershell
pwsh ./scripts/publish-winget.ps1 -Version "0.1.0" -Token "<YOUR_GITHUB_PAT>"
```
