# tools

| File | What it does |
| --- | --- |
| `get-antlr.sh` | Downloads the ANTLR 4.13.1 tool jar into `tools/`. Needed only to regenerate the parser. |
| `generate-parser.sh` | Regenerates `src/CalcEngine.Core/Grammar/Generated` from `Formula.g4`. Requires Java. |
| `gui-smoke.js` | Drives the GUI client in a real browser and checks the engine end to end. |

## The GUI smoke test

The unit tests prove the engine; this proves the client actually drives it. It
loads the sample results sheet, edits a mark and checks that the total, the
grade and the class average all move, scans for duplicates, inserts a circular
reference and reads the reported cycle back off the screen, types a malformed
formula and reads the parser's message off the status bar, then replaces text
and undoes it.

```bash
# 1. start the client (serves on http://localhost:5271)
dotnet run --project src/CalcEngine.Gui -c Release

# 2. in another terminal
npm install playwright            # once; PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 if a browser is already present
SHOTS=./shots node tools/gui-smoke.js

# point it somewhere else if you started the client on another port
CALCENGINE_URL=http://localhost:5000/ SHOTS=./shots node tools/gui-smoke.js
```

It prints a JSON report and writes numbered screenshots to `$SHOTS`. An empty
`errors` array means no failed requests and no browser exceptions.

It found two real defects during development: the grid committing an empty edit
buffer over `A1` on the first click, and `Workbook.AutomaticCalculation` being
unable to be switched back on. A third — the selected cell going stale after a
Replace All — got past it and was reported by a user, so §8 now compares every
checked cell's rendered text against its tooltip, which is read straight from
the workbook. Two views of the same cell disagreeing is the failure this script
exists to catch.
