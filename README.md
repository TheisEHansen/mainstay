# MainStay

A small Windows desktop tool to pull the latest `main` across a folder of Git repositories.

---

## Why?

If you maintain or contribute to multiple projects, keeping all your local default branches in sync with origin is repetitive. Doing it by hand takes time, and simple shell scripts tend to choke if you have unstaged changes, an active feature branch, or a detached HEAD.

MainStay is a lightweight app that scans a folder for Git repositories and updates them in parallel while keeping your local working state intact.

## How it works

When you run MainStay against a folder, it:

1. **Finds local repos** and fetches remote references from `origin`.
2. **Checks if updates are needed.** If your default branch is already up to date with the remote, it moves on without touching your working tree.
3. **Safely updates if there are new commits:**
   - Stashes any uncommitted changes.
   - Switches to your main branch (e.g. `main` or `master`).
   - Fast-forwards to the latest commit.
   - Switches back to whatever branch you were on before.
   - Restores your stashed changes.
4. **Reports errors cleanly.** If a repository cannot fast-forward cleanly or encounters a conflict, MainStay flags the error in the UI rather than guessing or making destructive changes.

## What it does

- **Parallel updates:** Runs checks concurrently with an adjustable worker limit so your network and disk aren't overwhelmed.
- **Leaves your working branch alone:** Stashes changes and returns you to your active branch when finished (or toggle **Stay on main** if you prefer).
- **Flexible branch names:** Defaults to checking for `main` and `master`, but you can customize the list and priority order.
- **Clear status view:** Shows which repositories updated, commit diffs (`from` -> `to`), durations, and detailed error messages if an operation failed.
- **Exporting:** Option to save run results as CSV or JSON if you want to inspect them later.

## Requirements

- Windows 10 or 11
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Git](https://git-scm.com/) installed and available in your `PATH`

## Running from source

Clone the repository and run the project:

```bash
git clone https://github.com/TheisEHansen/mainstay.git
cd mainstay
dotnet run --project MainStay
```

You can also open `MainStay.slnx` in Visual Studio or JetBrains Rider.

## Configuration

All options can be configured directly in the app:

| Setting | Default | Description |
|---|---|---|
| **Root folder** | Current directory | The directory to scan recursively for Git repositories. |
| **Main branch names** | `main, master` | Comma-separated list of target branch names, checked in order of priority. |
| **Parallel repositories** | `10` | Number of repositories to process concurrently. |
| **Git timeout** | `120s` | Maximum time allowed per repository operation before aborting. |
| **Stay on main** | Unchecked | If checked, keeps repositories on the default branch instead of switching back to your previous branch. |

## Running tests

The test suite runs against temporary, isolated Git repositories created during the test run:

```bash
dotnet test
```

## License

This project is available under the [MIT License](LICENSE).
