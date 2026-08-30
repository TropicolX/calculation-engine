# CalcEngine — a spreadsheet calculation engine for the University results portal

**CSC322 Modern Programming Language Design and Development · Group E**
Assigned additional features: **Find and Replace** · **Duplicate Detection / Removal**

CalcEngine is the machinery behind `=SUM(B2:B45)*0.3`: a formula language, an
expression tree, a dependency graph and a reactive recalculation loop, packaged
as a .NET class library that any client — the results portal, a desktop grid, a
batch importer — can embed.

```
383 tests, all green
single edit through a 500-cell chain in a 100,000-cell workbook   0.32 ms   (target 50 ms)
full recalculation of the same workbook                          79.37 ms   (target 2,000 ms)
```

![The GUI client driving the engine](docs/images/gui-results-sheet.png)

---

## Quick start

```bash
dotnet build          # SDK 8.0 or later.  No Java required.
dotnet test           # 383 tests, about five seconds
dotnet run --project src/CalcEngine.Gui -c Release        # the GUI client
dotnet run -c Release --project benchmarks/CalcEngine.Benchmarks   # the targets
```

Java is needed only when the grammar itself changes:

```bash
tools/generate-parser.sh
```

The generated lexer and parser are committed so the solution builds with the
.NET SDK alone; a CI job re-runs the generator and fails if the committed copy
has drifted from `Formula.g4`.

### Using the API

```csharp
var workbook = new Workbook();
workbook.AddSheet("Marks");

workbook.SetCellContent("Marks", "B2", "72");
workbook.SetCellContent("Marks", "C2", "=ROUND(B2*0.3,1)");

var result = workbook.SetCellContent("Marks", "B2", "85");

result.CellsEvaluated;                       // 1  — only C2 needed recomputing
result.Changes.Count;                        // 2  — B2 itself, then C2
result.Changes[1].Address.ToA1();            // "Marks!C2"
result.Changes[1].NewValue.AsNumber;         // 25.5
workbook.GetCellValue("Marks", "C2");        // 25.5

// Errors are values, never exceptions.
workbook.SetCellContent("Marks", "D2", "=B2/0");
workbook.GetCellValue("Marks", "D2").AsError.Code;      // "#DIV/0!"

// A malformed formula is reported, and the text the user typed is kept.
var bad = workbook.SetCellContent("Marks", "E2", "=SUM(B2:B45");
bad.SyntaxErrors[0].ToString();
// "Column 12: the bracket opened at column 5 was never closed."

// A cycle is reported with its exact path, never as a crash or a hang.
workbook.SetCellContent("Marks", "F2", "=G2+1");
var cycle = workbook.SetCellContent("Marks", "G2", "=F2+1");
cycle.Cycles[0].ToString();                  // "F2 → G2 → F2"

// One undo per operation, however many cells it touched.
workbook.History.Undo();
```

---

## Repository layout

| Path                               | What lives there                                                                                                                     |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------ |
| `src/CalcEngine.Core`              | **The API.** Grammar, parser, expression tree, dependency graph, evaluator, function library, undo/redo, and both assigned features. |
| `src/CalcEngine.Gui`               | GUI client: a scrollable grid driving the API through its public surface only.                                                       |
| `tests/CalcEngine.Core.Tests`      | 383 xUnit tests.                                                                                                                     |
| `benchmarks/CalcEngine.Benchmarks` | Performance harness; exits non-zero if a published target is missed.                                                                 |
| `docs/`                            | Design portfolio, ADT specifications, grammar, benchmarks, AI log, critique, reflection.                                             |
| `tools/`                           | ANTLR download and parser generation; the browser smoke test.                                                                        |

### Inside `CalcEngine.Core`

| Namespace                                     | Responsibility                                                |
| --------------------------------------------- | ------------------------------------------------------------- |
| `Model`                                       | `CellAddress`, `CellRange`, `CellValue`, `ErrorValue`         |
| `Grammar`                                     | `Formula.g4` and the generated ANTLR parser                   |
| `Parsing`                                     | Text → expression tree, or errors with positions              |
| `Expressions`                                 | The tree ADT, printing, reference extraction, literal finding |
| `Evaluation`                                  | Coercions and the evaluation-context seam                     |
| `Functions`                                   | The library and its registry                                  |
| `Dependencies`                                | The graph, the range index, ordering, cycle extraction        |
| `Commands`                                    | Undo/redo                                                     |
| `Features.FindReplace`, `Features.Duplicates` | The two assigned features                                     |

Dependencies point inwards only, and ANTLR is confined to `Grammar` and the
four files of `Parsing` that drive it. Nothing else in the solution mentions
`Antlr4.Runtime`, so the parser generator is replaceable without touching the
expression tree, the evaluator, the graph or the features.

---

## The formula language

Numbers, text, booleans, error literals, cell references (`B2`, `$B$2`),
ranges (`B2:B45`), sheet-qualified references (`Marks!B2`, `'CSC 322'!B2`),
the arithmetic, comparison and concatenation operators with Excel's precedence,
parentheses, and a function library.

Required: **SUM, AVERAGE, MIN, MAX, COUNT, IF, ROUND, LOOKUP**.
Also provided: COUNTA, COUNTIF, ABS, INT, SQRT, MOD, POWER, ROUNDUP, ROUNDDOWN,
AND, OR, NOT, IFERROR, ISERROR, ISBLANK, ISNUMBER, ISTEXT, LEN, UPPER, LOWER,
TRIM, VALUE, LEFT, RIGHT, MID, EXACT, CONCAT, SUBSTITUTE — and clients may
register their own without forking the engine.

Full specification, precedence table and error-message catalogue:
[`docs/grammar.md`](docs/grammar.md).

---

## What makes it fast

Three decisions, measured in [`docs/benchmarks.md`](docs/benchmarks.md):

1. **Dense integer cell identifiers.** Adjacency is array indexing, not string
   hashing, and one dependency graph spans every sheet.
2. **Generation-stamped traversal marks.** The recalculation never clears its
   mark arrays; clearing 100,000 flags per keystroke would cost more than the
   propagation itself.
3. **A spatial index for range dependencies.** `=SUM(B2:B45)` is one index entry
   rather than 44 edges — and, decisively, it still fires when a user types into
   a cell that was *empty when the formula was written*, which an expanded edge
   list cannot do.

---

## Documentation

| Document                                                                                   | Contents                                                                 |
| ------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------ |
| [Design portfolio](docs/design-portfolio.md)                                               | Architecture, class diagrams, patterns, and the alternatives we rejected |
| [ADT specifications](docs/adt-specifications.md)                                           | Abstraction function and representation invariant for every type         |
| [Formal grammar](docs/grammar.md)                                                          | EBNF, lexical conventions, precedence, error reporting                   |
| [Benchmarks](docs/benchmarks.md)                                                           | The published targets, how to run them, and the results                  |
| [Test suite](docs/testing.md)                                                              | How the suite is organised, and what got past it                         |
| [AI collaboration log](docs/ai-collaboration-log.md)                                       | Twenty entries, including what we did not ship                           |
| [Critique exercise](docs/critique/critique.md) · [transcript](docs/critique/transcript.md) | A senior review of an AI-written dependency graph                        |
| [Reflection](docs/reflection.md)                                                           | What we designed, what we would change, what the tools got wrong         |
| [Demo script](docs/demo-video-script.md)                                                   | The five-minute demonstration, beat by beat                              |

---

## Design patterns, and where to find them

| Pattern            | Where                                                                                | Why                                                                |
| ------------------ | ------------------------------------------------------------------------------------ | ------------------------------------------------------------------ |
| Composite          | `Expressions/Expression.cs` and its nine subclasses                                  | The expression tree                                                |
| Interpreter        | `Expression.Evaluate(IEvaluationContext)`                                            | Evaluation is the hot path; one virtual call beats double dispatch |
| Visitor            | `IExpressionVisitor<T>`, `FormulaPrinter`, `ReferenceCollector`, `TextLiteralFinder` | Open-ended traversals cost a class, not a method on nine types     |
| Observer           | `ICellChangeObserver`, `Workbook.CellsChanged`                                       | Change propagation to clients                                      |
| Command            | `IUndoableCommand`, `SetCellsCommand`, `CompositeCommand`, `CommandStack`            | Undo/redo of at least 100 operations                               |
| Strategy           | `IFunction` and the library                                                          | A pluggable function set                                           |
| Factory / Registry | `FunctionRegistry.CreateDefault`, `Register`                                         | Clients extend the library without a fork                          |
| Adapter            | `PointwiseFunction`                                                                  | Two dozen functions that differ only by a lambda                   |

---

## Known limits

Stated here rather than discovered later.

* **No structural edits.** Insert/delete row or column, with the reference
  rewriting they imply, is not implemented. This is why `RemoveDuplicates` in
  `ShiftUp` mode *refuses* when compacting would relocate a formula instead of
  moving it silently — see [design portfolio §7.2](docs/design-portfolio.md).
* **No persistence.** The engine has no file format; a `Workbook` is
  reconstructible from a list of `CellEdit`s.
* **Single-threaded.** `Workbook` is not thread-safe, by design.
* **No whole-column ranges** (`A:A`), no array formulas, no defined names. The
  first is a deliberate exclusion — one edge standing for 1,048,576 cells would
  defeat the range index.
