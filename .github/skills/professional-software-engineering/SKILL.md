---
name: professional-software-engineering
description: Internal project-agent engineering workflow for implementing features and fixes with unit tests, clean abstractions, extensible design, repeatable setup, cross-platform execution, and Docker-ready delivery. Use when the agent is building, refactoring, hardening, testing, packaging, or operationalizing software professionally.
argument-hint: What feature, refactor, service, or project setup should be engineered?
user-invocable: false
metadata:
  author: oneshotprompt
  version: "1.0"
---

Use this skill as internal guidance for the project agent. The goal is to produce software that is testable, maintainable, portable, and straightforward to restore, build, run, test, and containerize across environments.

Workflow:
1. Restate the target behavior, constraints, and acceptance criteria before changing code.
2. Identify where variability belongs and introduce abstractions only at those seams, such as provider boundaries, persistence, transport, or environment-specific behavior.
3. Prefer small contracts with clear responsibilities over broad utility layers or speculative frameworks.
4. Implement the narrowest design that satisfies the current requirement while keeping extension points explicit.
5. Add or update unit tests for the expected behavior, edge cases, and the regression being fixed. If the change is risky, write the failing test first.
6. Verify developer ergonomics: the project should be easy to restore, build, run, test, and configure on a clean machine.
7. Add or refine container support when the software benefits from a reproducible runtime, isolated dependencies, or consistent deployment behavior.
8. Document any required setup, environment variables, runtime assumptions, and operational commands close to the code or in the repo docs.
9. Validate the result with the available build, test, and run paths before declaring the work complete.

Decision points:
- If the task changes observable behavior, add a regression or behavior test.
- If the code must support multiple providers, storage backends, transports, or environments, isolate that variability behind a focused interface.
- If an abstraction only serves one call site and does not reduce coupling or improve testability, keep the code concrete.
- If local setup is fragile or slow, improve the bootstrap path with scripts, defaults, or clearer docs before adding more features.
- If runtime parity matters across developer machines, CI, or deployment targets, prefer a Dockerfile or container workflow with explicit inputs.

Quality criteria:
- The implementation is readable without tracing through unnecessary layers.
- Tests cover the changed behavior and fail for the right reasons when the behavior breaks.
- Setup and run paths are explicit, repeatable, and not dependent on hidden machine state.
- Cross-platform assumptions are either removed or documented.
- Docker support, when added, is minimal, reproducible, and aligned with the normal local run path.
- Documentation matches the actual commands and configuration required to operate the software.
- The agent leaves the repository in a state that another engineer can build and validate without tribal knowledge.

Guardrails:
- Do not introduce abstraction for its own sake.
- Do not skip tests for behavior changes unless the environment truly prevents them, and then state that limitation explicitly.
- Do not add Docker complexity when a simpler run model is enough.
- Do not leave setup knowledge implicit in chat messages, terminal history, or personal machine state.
- Do not treat passing compilation alone as sufficient validation.