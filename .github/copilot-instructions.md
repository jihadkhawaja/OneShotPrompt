# Project Guidelines

## Build and Test

- Use the .NET SDK pinned in `global.json`.
- Restore or build with `dotnet restore OneShotPrompt.slnx` and `dotnet build OneShotPrompt.slnx`.
- Run tests with `dotnet test tests/OneShotPrompt.Tests/OneShotPrompt.Tests.csproj`.
- Run the console app with `dotnet run --project src/OneShotPrompt.Console -- <command> --config <path>`.
- Common commands are `validate`, `jobs`, and `run`. Running with no arguments defaults to `run --config config.yaml`.

## Architecture

- Keep the existing layer boundaries intact: `OneShotPrompt.Core` holds domain models and enums, `OneShotPrompt.Application` holds use cases and abstractions, `OneShotPrompt.Infrastructure` holds YAML loading, providers, tools, and persistence, and `OneShotPrompt.Console` is the CLI entrypoint.
- Wire concrete implementations in the console layer. Keep domain and orchestration logic out of `Program.cs`.
- Preserve the tool-selection flow: the runtime narrows the tool catalog before the execution agent runs.
- Treat the config directory as the runtime root for adjacent `skills/` content and `.oneshotprompt/memory/` persistence.

## Code Style

- Follow `.editorconfig`: use file-scoped namespaces, four-space indentation in C# files, braces on new lines, and existing C# collection and object initializer style.
- Prefer the existing concise C# style used in the repo, including primary constructors where they fit naturally.
- Keep project file indentation at two spaces.
- Add new NuGet package versions in `Directory.Packages.props`; keep `PackageReference` items versionless in project files unless a project-specific override is required.

## Conventions

- Keep changes focused and avoid introducing abstractions unless they isolate a real variability point such as providers, persistence, or tool surfaces.
- When behavior changes, update or add xUnit coverage in `tests/OneShotPrompt.Tests`.
- If you change CLI behavior, configuration shape or validation, scheduling behavior, or bundled skills under `src/OneShotPrompt.Console/skills`, update the relevant docs in `README.md`, `docs/`, or `.github/CONTRIBUTING.md`.
- Use `config.example.yaml` as the source for safe configuration examples; never commit real secrets.
- Respect the safety model: `AutoApprove: false` means read-only execution, and mutation-capable examples or code paths should not bypass that boundary.

## Important Files

- Start with `README.md` for the product overview and operator commands.
- Use `docs/configuration.md` for config semantics, tool access rules, and skill-loading behavior.
- Use `src/OneShotPrompt.Application/Services/JobRunner.cs` to understand execution flow and user-visible output.
- Use `src/OneShotPrompt.Console/Program.cs` and `src/OneShotPrompt.Console/Cli/CommandLineArguments.cs` for CLI behavior.