# CLAUDE.md

Before making any architectural change, read `Docs/ProjectGuidelines.md` and `Docs/Decisions.md` and follow them.

Mandatory testing rule: all JSON-based tests must load their input only from `tests/JJMatch.Tests/TestData/ComplexJson/Left.json` and `Right.json`, or, only when testing stringified JSON input handling, from `tests/JJMatch.Tests/TestData/Stringified/LeftStringify.txt` and `RightStringify.txt`. The TXT files are the JSON-stringified form of the two JSON fixtures, and must be kept in sync with them. Never write inline JSON in tests, generate JSON dynamically, or add other JSON or TXT fixture files; extend `Left.json` and `Right.json` instead. See `Docs/ProjectGuidelines.md` for the full rule.
