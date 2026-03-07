# Contributing

## Before You Start

- Check existing issues and pull requests before starting new work.
- Keep changes focused. Large mixed-purpose pull requests are harder to review.
- If you plan to make a behavioral change, describe the problem and expected
  outcome clearly in the issue or pull request.

## Development Setup

1. Install the .NET SDK version pinned in `global.json`.
2. Clone the repository.
3. Restore and build the solution.
4. Use the shared package versions from `Directory.Packages.props` for any new
  NuGet dependencies.
5. Follow the repo-wide formatting and style guidance in `.editorconfig`.

```powershell
dotnet restore OneShotPrompt.slnx
dotnet build OneShotPrompt.slnx
```

## Running Tests

Run the test project before opening a pull request.

```powershell
dotnet test tests/OneShotPrompt.Tests/OneShotPrompt.Tests.csproj
```

If you change CLI behavior, configuration handling, scheduling docs, or bundled
skills, update the related documentation in `README.md` or `docs/`.

## Dependency Management

- This repository uses NuGet Central Package Management.
- Add new package versions in `Directory.Packages.props` instead of individual
  project files.
- Keep `PackageReference` items in project files versionless unless a
  project-specific override is intentionally required.

## Configuration and Safety

- Do not commit real API keys, tokens, or private configuration.
- Use `config.yaml.example` as the starting point for examples.
- Prefer examples that are safe to run in a local test workspace.

## Pull Request Guidelines

- Include a concise summary of the change and the reason for it.
- Call out any configuration, provider, or documentation impact.
- Add or update tests when behavior changes.
- Keep formatting-only changes separate from logic changes when practical.

## Review Expectations

Maintainers may ask for changes before merging. Reviews generally focus on:

- Behavior and correctness.
- Test coverage for changed behavior.
- Backward compatibility of configuration and CLI flows.
- Documentation accuracy.

## Reporting Problems

- For bugs and feature requests, use the issue templates.
- For security-sensitive reports, follow [SECURITY.md](SECURITY.md) instead of
  opening a public issue.