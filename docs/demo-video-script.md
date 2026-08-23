# Demonstration video — script

**CSC322 Group E — CalcEngine.** Target length 5:00. The GUI drives the API
throughout; nothing is staged in code behind the scenes.

Setup: `dotnet run --project src/CalcEngine.Gui -c Release`, browser at
`http://localhost:5000`, terminal open in a second window.

---

**0:00 — 0:25 · What this is**

> "This is CalcEngine — a calculation engine for the university results portal.
> The library has no user interface of its own; what you are looking at is a
> client, and everything it does goes through the same public API the portal
> would use. On screen is a CSC322 results sheet: matriculation numbers, names,
> continuous assessment, exam marks, and three computed columns."

Point at the formula bar showing `=ROUND(C2*0.3+D2*0.7,1)`.

---

**0:25 — 1:15 · Reactive recalculation**

Click `D2`, type `35`, press Enter.

> "One mark changes. The total recomputes, the grade recomputes because it looks
> the total up in the band table over here, and the class average, the highest,
> the lowest and the pass count all move. The cells flashing green are exactly
> the cells the engine decided were affected."

Point at the diagnostics panel.

> "Six cells evaluated, in a fifth of a millisecond. Not six hundred — the
> engine walks the dependency graph from the cell that changed and stops."

Click `F2` and show `=LOOKUP(E2,$I$3:$I$8,$J$3:$J$8)` in the formula bar.

> "Grades come from `LOOKUP` over a band table, so the pass mark is data rather
> than something buried in nested `IF`s."

---

**1:15 — 1:50 · Circular references**

Click **Insert a circular reference**.

> "L2 refers to L3, L3 to L4, and L4 back to L2. The engine does not hang and it
> does not throw: the three cells show `#CIRC!`, and the banner names the exact
> ring — L2, L3, L4, back to L2 — in the order the formulas refer to each other."

Click one of the cells and hover to show the tooltip.

> "The same path is on the error value itself, so a client that has no banner
> can still tell the user which cells to look at."

Fix `L4` by typing `10`.

> "Break the ring and every cell in it recovers in the same pass."

---

**1:50 — 2:20 · Errors are values, not exceptions**

Click an empty cell, type `=SUM(B2:B13` (no closing bracket), Enter.

> "A malformed formula is normal input. The status bar says: column 12, the
> bracket opened at column 5 was never closed. It names the bracket you have to
> go and fix, not the end of the line — and the cell keeps the text you typed so
> you can correct it."

Type `=B2/0`.

> "Division by zero is `#DIV/0!` — a value in a cell, not an exception escaping
> to the client. The whole API works this way."

---

**2:20 — 3:10 · Assigned feature 1: Find and Replace**

In the panel, find `CSC/21/0433`, replace with `CSC/21/0533`, **Replace all**.

> "Two cells changed, one undoable operation."

Now the interesting case. Set **Formulas** to *Replace anywhere*, find `B2`,
replace with `B3`, and point at what it would do to a formula containing
`SUM(B2:B9)`.

> "This is the mode Excel gives you, and it rewrites the range inside the
> formula. That is usually what you want when you are renaming something — and
> it is a silent corruption when you are not."

Switch to *Only inside quoted text*, run it again.

> "Same replacement, but now the engine parses the formula and rewrites only the
> text literals. The range is untouched. And whichever mode you use, the
> rewritten formula is re-parsed before it is stored — if the replacement would
> break it, the cell is left alone and reported instead."

Press **Undo**.

> "One press. Four hundred replacements would also be one press."

---

**3:10 — 4:00 · Assigned feature 2: Duplicate detection**

Click **Find duplicates** over `A1:G13`.

> "Two students are entered twice. The engine highlights the repeats, not the
> first occurrences, because the first is the one a removal keeps."

Change **Key columns** to `0` and rescan.

> "On matriculation number alone rather than the whole row, we catch the student
> entered twice with different marks — which is the case that actually corrupts
> a CGPA."

Set removal to **Compact the survivors upwards** and click **Remove** — with a
formula inside the range.

> "It refuses, and says why: compacting would move a formula, and a formula's
> relative references do not follow it. It names the cell. You can override it,
> or use the default mode that clears the repeats in place and moves nothing.
> What it will not do is move it silently."

---

**4:00 — 4:35 · The performance targets**

Switch to the terminal.

```bash
dotnet run -c Release --project benchmarks/CalcEngine.Benchmarks
```

> "A hundred thousand cells with a five-hundred-cell dependency chain — the exact
> workbook the brief describes. A single edit propagates through the whole chain
> in a third of a millisecond against a fifty-millisecond target, and a full
> recalculation of all hundred thousand cells takes eighty milliseconds against
> two seconds. Those are worst-case figures over thirty runs, not medians."

---

**4:35 — 5:00 · Close**

> "Under all of that: an ANTLR grammar, an expression tree that is a Composite
> with an Interpreter for evaluation and a Visitor for everything else, a
> dependency graph on dense integer identifiers with a spatial index for ranges,
> the Command pattern for undo, and the Observer pattern for change
> notification. Three hundred and seventy-five tests, committed before the code
> that makes them pass. Thank you."
