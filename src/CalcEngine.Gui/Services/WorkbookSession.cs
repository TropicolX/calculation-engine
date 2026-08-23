using CalcEngine.Core;
using CalcEngine.Core.Features.Duplicates;
using CalcEngine.Core.Model;

namespace CalcEngine.Gui.Services;

/// <summary>
/// One browser session's workbook and the presentation state around it.
/// </summary>
/// <remarks>
/// <para>Registered per-circuit, so two people driving the demo never see each
/// other's edits. Everything the grid needs beyond the engine's own API lives
/// here — selection, viewport size, which cells were touched by the last
/// recalculation — and none of it leaks back into
/// <c>CalcEngine.Core</c>.</para>
///
/// <para>The class is deliberately thin. It subscribes to the workbook through
/// the same public <see cref="ICellChangeObserver"/> any client would use, which
/// is the point of the demonstration: the grid is not privileged.</para>
/// </remarks>
public sealed class WorkbookSession : ICellChangeObserver, IDisposable
{
    private readonly IDisposable _subscription;

    public WorkbookSession()
    {
        Workbook = new Workbook();
        _subscription = Workbook.Subscribe(this);
        ActiveSheet = Workbook.Sheets[0].Name;
        Selected = new SheetCellAddress(ActiveSheet, CellAddress.Parse("A1"));
        LoadSampleResultsSheet();
    }

    /// <summary>The engine under demonstration.</summary>
    public Workbook Workbook { get; }

    /// <summary>The sheet currently shown.</summary>
    public string ActiveSheet { get; private set; }

    /// <summary>The selected cell.</summary>
    public SheetCellAddress Selected { get; private set; }

    /// <summary>How many rows the grid shows.</summary>
    public int VisibleRows { get; private set; } = 40;

    /// <summary>How many columns the grid shows.</summary>
    public int VisibleColumns { get; private set; } = 12;

    /// <summary>What the last recalculation did; drives the status bar.</summary>
    public CalculationResult? LastResult { get; private set; }

    /// <summary>Cells changed by the last recalculation; the grid flashes them.</summary>
    public HashSet<SheetCellAddress> RecentlyChanged { get; } = [];

    /// <summary>The most recent duplicate scan, if any; the grid shades it.</summary>
    public DuplicateReport? Duplicates { get; set; }

    /// <summary>A message for the status bar.</summary>
    public string? Notice { get; set; }

    /// <summary>Raised whenever anything the UI shows has changed.</summary>
    public event Action? Changed;

    /// <inheritdoc />
    public void OnCellsChanged(CalculationResult result)
    {
        LastResult = result;
        RecentlyChanged.Clear();
        foreach (var change in result.Changes)
        {
            RecentlyChanged.Add(change.Address);
        }
    }

    public void Select(CellAddress address)
    {
        Selected = new SheetCellAddress(ActiveSheet, address);
        Changed?.Invoke();
    }

    public void SelectSheet(string sheetName)
    {
        ActiveSheet = sheetName;
        Selected = new SheetCellAddress(sheetName, Selected.Address);
        Changed?.Invoke();
    }

    public void MoveSelection(int columnDelta, int rowDelta)
    {
        if (Selected.Address.TryOffset(columnDelta, rowDelta, out var moved))
        {
            Selected = new SheetCellAddress(ActiveSheet, moved);
            GrowToFit(moved);
            Changed?.Invoke();
        }
    }

    /// <summary>The raw content of the selected cell, for the formula bar.</summary>
    public string SelectedContent => Workbook.GetCellContent(Selected) ?? string.Empty;

    /// <summary>Commits an edit to a cell and reports what happened.</summary>
    public CalculationResult Commit(CellAddress address, string? content)
    {
        var result = Workbook.SetCellContent(ActiveSheet, address, content);
        Notice = result.HasSyntaxErrors
            ? $"{address.ToA1()}: {result.SyntaxErrors[0]}"
            : result.HasCycles
                ? $"Circular reference: {result.Cycles[0]}"
                : null;
        Changed?.Invoke();
        return result;
    }

    public void Undo()
    {
        Workbook.History.Undo();
        Notice = null;
        Changed?.Invoke();
    }

    public void Redo()
    {
        Workbook.History.Redo();
        Notice = null;
        Changed?.Invoke();
    }

    public void RecalculateAll()
    {
        var result = Workbook.RecalculateAll();
        Notice = $"Recalculated {result.CellsEvaluated:N0} formula cells in {result.Duration.TotalMilliseconds:F2} ms.";
        Changed?.Invoke();
    }

    public void ClearDuplicates()
    {
        Duplicates = null;
        Changed?.Invoke();
    }

    public void Refresh() => Changed?.Invoke();

    public void AddRows(int count)
    {
        VisibleRows = Math.Min(CellAddress.MaxRow, VisibleRows + count);
        Changed?.Invoke();
    }

    public void AddColumns(int count)
    {
        VisibleColumns = Math.Min(CellAddress.MaxColumn, VisibleColumns + count);
        Changed?.Invoke();
    }

    private void GrowToFit(CellAddress address)
    {
        VisibleRows = Math.Max(VisibleRows, address.Row);
        VisibleColumns = Math.Max(VisibleColumns, address.Column);
    }

    /// <summary>
    /// Demonstrates a deliberate three-cell cycle: L2 → L3 → L4 → L2.
    /// </summary>
    public void InsertCircularReference()
    {
        Workbook.SetCellContents(
            [
                new CellEdit(new SheetCellAddress(ActiveSheet, CellAddress.Parse("L2")), "=L3+1"),
                new CellEdit(new SheetCellAddress(ActiveSheet, CellAddress.Parse("L3")), "=L4+1"),
                new CellEdit(new SheetCellAddress(ActiveSheet, CellAddress.Parse("L4")), "=L2+1"),
            ],
            "insert a circular reference");

        Notice = LastResult?.HasCycles == true
            ? $"Circular reference detected: {LastResult.Cycles[0]}"
            : null;
        Changed?.Invoke();
    }

    /// <summary>
    /// A CSC322 results sheet: raw marks, a weighted total, a grade looked up
    /// from a band table, a duplicate flag, and summary statistics — enough that
    /// changing one mark visibly moves seven other cells.
    /// </summary>
    public void LoadSampleResultsSheet()
    {
        var edits = new List<CellEdit>();

        void Set(string address, string content) =>
            edits.Add(new CellEdit(new SheetCellAddress(ActiveSheet, CellAddress.Parse(address)), content));

        Set("A1", "Matric No");
        Set("B1", "Name");
        Set("C1", "CA (30%)");
        Set("D1", "Exam (70%)");
        Set("E1", "Total");
        Set("F1", "Grade");
        Set("G1", "Check");

        var students = new (string Matric, string Name, int Ca, int Exam)[]
        {
            ("CSC/21/0431", "Okafor, Ngozi", 26, 71),
            ("CSC/21/0432", "Adeyemi, Bola", 19, 48),
            ("CSC/21/0433", "Eze, Chidi", 28, 84),
            ("CSC/21/0434", "Ibrahim, Aisha", 12, 33),
            ("CSC/21/0435", "Nwosu, Emeka", 22, 57),
            ("CSC/21/0433", "Eze, Chidi", 28, 84),
            ("CSC/21/0436", "Balogun, Tunde", 25, 62),
            ("CSC/21/0437", "Ogundipe, Femi", 17, 44),
            ("CSC/21/0438", "Musa, Halima", 29, 90),
            ("CSC/21/0432", "Adeyemi, Bola", 19, 48),
            ("CSC/21/0439", "Chukwu, Amara", 24, 66),
            ("CSC/21/0440", "Bello, Sadiq", 20, 51),
        };

        for (var i = 0; i < students.Length; i++)
        {
            var row = i + 2;
            var (matric, name, ca, exam) = students[i];
            Set($"A{row}", matric);
            Set($"B{row}", name);
            Set($"C{row}", ca.ToString());
            Set($"D{row}", exam.ToString());
            Set($"E{row}", $"=ROUND(C{row}*0.3+D{row}*0.7,1)");
            Set($"F{row}", $"=LOOKUP(E{row},$I$3:$I$8,$J$3:$J$8)");
            Set($"G{row}", $"=IF(COUNTIF($A$2:$A$13,A{row})>1,\"DUPLICATE\",\"\")");
        }

        Set("I2", "From");
        Set("J2", "Grade");
        var bands = new (int Mark, string Grade)[] { (0, "F"), (40, "E"), (45, "D"), (50, "C"), (60, "B"), (70, "A") };
        for (var i = 0; i < bands.Length; i++)
        {
            Set($"I{i + 3}", bands[i].Mark.ToString());
            Set($"J{i + 3}", bands[i].Grade);
        }

        Set("A16", "Class average");
        Set("B16", "=ROUND(AVERAGE(E2:E13),2)");
        Set("A17", "Highest");
        Set("B17", "=MAX(E2:E13)");
        Set("A18", "Lowest");
        Set("B18", "=MIN(E2:E13)");
        Set("A19", "Passed");
        Set("B19", "=COUNTIF(F2:F13,\"<>F\")");
        Set("A20", "Entries");
        Set("B20", "=COUNTA(A2:A13)");

        using (Workbook.History.Suspend())
        {
            Workbook.SetCellContents(edits, "load the sample results sheet");
        }

        RecentlyChanged.Clear();
        Notice = "Sample results sheet loaded. Two students are entered twice — try Find duplicates.";
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void Dispose() => _subscription.Dispose();
}
