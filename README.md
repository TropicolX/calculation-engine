# CalcEngine — a spreadsheet calculation engine for the University results portal

**CSC322 Modern Programming Language Design — Group E**
Assigned additional features: **Find and Replace** and **Duplicate Detection / Removal**.

`CalcEngine` is the machinery behind `=SUM(B2:B45)*0.3`: a formula language, an
expression tree, a dependency graph and a reactive recalculation loop, packaged
as a .NET class library that any client (the results portal, a desktop grid, a
batch importer) can embed.

## Repository layout

| Path | What lives there |
| --- | --- |
| `src/CalcEngine.Core` | The API. Grammar, parser, expression tree, dependency graph, evaluator, function library, undo/redo, Find & Replace, duplicate detection. |
| `src/CalcEngine.Gui` | GUI client: a scrollable grid that drives the API. |
| `tests/CalcEngine.Core.Tests` | xUnit test suite. |
| `benchmarks/CalcEngine.Benchmarks` | Performance harness for the published targets. |
| `docs/` | Design portfolio, grammar, ADT specifications, AI collaboration log, critique, reflection. |
| `tools/` | ANTLR download and parser-generation scripts. |

## Building

```bash
dotnet build            # SDK 8.0 or later; no Java required
dotnet test             # run the suite
```

Java is needed only when `Formula.g4` changes:

```bash
tools/generate-parser.sh
```

The generated lexer/parser are committed so the solution builds with the .NET
SDK alone.
