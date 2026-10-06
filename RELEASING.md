# Releasing StoneForge

For StoneForgeTeam maintainers who publish releases. To build and test StoneForge yourself, see the [README](README.md).

Releases are built and published by the [Release workflow](.github/workflows/release.yml) when a version tag is pushed. The API build needs Stoneshard's own game data, so the workflow runs on a self-hosted Windows runner labelled `stoneshard` rather than on GitHub's runners.

To publish a release:

1. Set `<Version>` in `Directory.Build.props`, for example `0.2.0`.
2. Add a `## 0.2.0 — <title>` section to [CHANGELOG.md](CHANGELOG.md). Its text becomes the release notes.
3. Merge to `main`, then tag the merged commit and push the tag:

   ```powershell
   git fetch origin
   git tag v0.2.0 origin/main
   git push origin v0.2.0
   ```

The workflow checks that the tag matches `Directory.Build.props`, builds, runs both test projects (failing if the integration tests have no game data), and publishes `StoneForge-<version>.zip` with the changelog notes. Versions with a suffix such as `0.2.0-beta.1` are published as pre-releases. Follow the run under **Actions**; it stays queued until the runner is online.

To fix a failed release, use **Re-run jobs**. If the fix needs a new commit, merge it, then move the tag to it and push it again:

```powershell
git fetch origin
git tag -f v0.2.0 origin/main
git push origin :refs/tags/v0.2.0
git push origin v0.2.0
```

## Release runner

On a Windows PC with Stoneshard (StoneForge installed; either branch - on the native one the API is generated from the last VM build's data dump, kept in `%LOCALAPPDATA%\StoneForge\GameData`, so build once on the VM branch first), Visual Studio's C++ tools (`v145`) and the .NET 10 SDK:

1. In this repository's **Settings → Actions → Runners**, choose **New self-hosted runner**, **Windows**, **x64**, and run the commands shown in PowerShell.
2. When `config.cmd` asks, press Enter for the **Default** runner group and the default name. At **additional labels**, type `stoneshard`. `self-hosted`, `Windows` and `X64` are added automatically. Answer **N** to running as a service.
3. Allow PowerShell scripts for your account (no administrator needed):

   ```powershell
   Set-ExecutionPolicy RemoteSigned -Scope CurrentUser
   ```

4. Start the runner with `.\run.cmd` and keep the window open. At `Listening for Jobs` it picks up queued releases.

A runner installed as a Windows service runs under another account: it needs `Set-ExecutionPolicy RemoteSigned -Scope LocalMachine`, and `STONESHARD_DIR` and `STONEFORGE_TEST_DATA` set as system environment variables unless the game is in Steam's default folder. A self-hosted runner executes workflow code on that PC, so require approval for fork pull request workflows (**Settings → Actions → General**). Full details are in [Releasing](https://github.com/StoneForgeTeam/StoneForgeDocs/blob/main/docs/development/releasing.md).
