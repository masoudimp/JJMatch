# JJMatch Project Guidelines

These rules apply to every change in this repository. Their purpose is to keep JJMatch small, readable, and free of unnecessary complexity.

## Core rules

### 1. Minimal changes first

Prefer modifying existing classes and methods over creating new ones.

Minimal changes do not mean forcing unrelated responsibilities into one large class. Introduce a new class when it provides a clear and necessary separation of responsibilities.

### 2. Avoid overengineering

Do not introduce new classes, interfaces, abstractions, or design patterns unless they are genuinely necessary.

### 3. Reuse test fixtures

Use the existing fixtures under `tests/JJMatch.Tests/TestData/`. See the mandatory testing rule below: no other input fixtures may be created.

### 4. Keep tests focused

Follow the existing test conventions. Do not add unnecessary test files or duplicate coverage that already exists.

### 5. Separate responsibilities

The following are separate concerns:

- Structural extraction
- Structural comparison
- Scoring
- Difference reporting

Do not implement a future stage prematurely.

### 6. Keep scoring defined and explainable

The public API completion request explicitly authorized similarity scoring. Preserve the Sørensen–Dice contract in `Docs/Scoring.md`; do not add weights or change the denominator without a documented requirement.

### 7. Preserve compatibility

Avoid unnecessary changes to existing methods, existing tests, and the public NuGet library interface.

### 8. Document decisions

Check the existing documentation before asking questions. Update recorded decisions in `Docs/Decisions.md` when requirements change.

### 9. Keep it simple

Optimize for readability, maintainability, and the smallest reasonable implementation.

## Mandatory testing rule: only use the approved fixtures

This is a permanent project convention. All JSON-based tests in JJMatch must use exclusively the four existing fixture files:

- `tests/JJMatch.Tests/TestData/ComplexJson/Left.json`
- `tests/JJMatch.Tests/TestData/ComplexJson/Right.json`
- `tests/JJMatch.Tests/TestData/Stringified/LeftStringify.txt`
- `tests/JJMatch.Tests/TestData/Stringified/RightStringify.txt`

All test scenarios originate from `Left.json` and `Right.json`. Each file contains `core` (the original complex document), `stress` (the original adversarial document), and `publicApi` (consumer scenarios); `Left.json` also contains `stressChanges`, the 51-entry catalogue. Tests select a section without reconstructing JSON. Load the fixture envelope with `MaxDepth = 128` for the existing 100-level stress array; the depth-limit test reparses the unchanged raw `core` section to retain its original 7/8-level boundary. This test-only setting is not a production depth policy. Each TXT file is the JSON-stringified (escaped string) representation of its corresponding JSON fixture.

Strict requirements:

1. Never write inline, hardcoded, or custom JSON inside test methods.
2. Never create additional JSON or TXT input fixture files.
3. Load all JSON test inputs from the existing `Left.json` and `Right.json` files. Use `LeftStringify.txt` and `RightStringify.txt` only when testing stringified JSON input handling.
4. When a new feature requires additional test scenarios, reuse the existing fixture content first. Extend `Left.json` and `Right.json` only when their existing content cannot cover the scenario.
5. Reuse the same fixtures across all test classes and components.
6. Remove redundant JSON fixtures introduced by other changes.
7. Do not generate alternative JSON documents dynamically inside tests.
8. Tests must still have explicit, meaningful assertions with independently defined expected results.
9. Ensure new scenarios do not accidentally invalidate existing tests. Update expected assertions when a fixture change is intentional.
10. Represent every new test scenario within `Left.json` and `Right.json`, using clearly identifiable sections where appropriate.
11. Migrate existing inline JSON tests to these fixtures to maintain consistency.
12. Tests may select specific sections of the loaded fixtures to isolate individual scenarios, but must not construct or generate additional JSON documents.
13. Fixture modifications must preserve existing scenarios and should not unnecessarily break unrelated tests.
14. Keep both JSON fixtures organized and maintainable: do not duplicate scenarios or add unnecessary properties or nested objects.
15. Keep each TXT file synchronized with its JSON fixture: any change to `Left.json` or `Right.json` is mirrored in the corresponding TXT file in the same change.
16. The Stringified fixtures are test inputs, not expected values.

Before writing any new tests, inspect the two JSON fixtures and determine how they can cover the required scenarios.

## Folder organization

Source files are grouped by role, and internal code by concern. Tests mirror the concern folders:

```text
src/JJMatch/
  JJMatcher.cs
  JJMatchResult.cs
  Options/
  Models/
  Internal/
    JsonStructure/
    Matching/

tests/JJMatch.Tests/
  JsonStructure/
  Matching/
```

- `Options/` holds the public enums a caller passes in, `Models/` the public types a result is made of, and `Internal/` everything that is not public.
- Folders do not change namespaces: every source file stays in the `JJMatch` namespace.
- Do not create or split classes just to satisfy the folder structure.
- `src/` holds code only. Each internal function has a concise document in `Docs/`, named after the function (`Traverse.md`, `Match.md`, `MatchValues.md`), covering purpose, inputs and outputs, behavior and rules, edge cases and limitations, and test coverage.
- Test folders do not get their own document; the function's document points to its tests.
- Update the document in the same change as the function it describes.

## Before implementing a change

- Review the existing code, tests, and documentation.
- Identify what can be reused.
- Avoid adding files unless necessary.
- Ask for approval before introducing a significant architectural change unless the current request already authorizes it. The public API completion request authorizes its necessary architectural and scoring decisions.
