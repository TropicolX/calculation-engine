using CalcEngine.Core.Evaluation;
using CalcEngine.Core.Model;

namespace CalcEngine.Core.Functions;

/// <summary>
/// LOOKUP: finds a value in an ascending vector and returns the matching entry
/// of a result vector.
/// </summary>
/// <remarks>
/// <para>Two forms, both Excel's:</para>
/// <list type="bullet">
///   <item><description><c>LOOKUP(value, lookupVector, resultVector)</c></description></item>
///   <item><description><c>LOOKUP(value, block)</c> — the first column (or row)
///   of the block is searched and the last is returned.</description></item>
/// </list>
///
/// <para>The match is "the largest entry that does not exceed the value", with
/// an exact match preferred. That is what makes it the natural way to express a
/// grade band: <c>=LOOKUP(B2,$E$2:$E$7,$F$2:$F$7)</c> over a table of
/// 0/40/45/50/60/70 turns a mark into F/E/D/C/B/A with no nested IFs.</para>
///
/// <para>Excel binary-searches and gives undefined results on unsorted data;
/// this implementation scans linearly instead. It agrees with Excel on every
/// sorted vector — the only input either specification defines — and on
/// unsorted data it degrades to something explainable ("the last entry that
/// does not exceed the value") rather than to whatever a binary search happens
/// to land on. The vectors in a grade table are short, so the cost is
/// irrelevant.</para>
/// </remarks>
public sealed class LookupFunction()
    : FunctionBase("LOOKUP", 2, 3, "Finds a value in a sorted vector and returns the matching result.")
{
    /// <inheritdoc />
    public override CellValue Invoke(FunctionArguments arguments)
    {
        var key = arguments.Value(0);
        if (key.IsError)
        {
            return key;
        }

        var searchArea = arguments.AsRange(1);
        if (searchArea is null)
        {
            return arguments.Invalid("the second argument must be a range");
        }

        Func<int, CellValue> lookupAt;
        Func<int, CellValue> resultAt;
        int length;

        if (arguments.Count == 3)
        {
            var resultArea = arguments.AsRange(2);
            if (resultArea is null)
            {
                return arguments.Invalid("the third argument must be a range");
            }

            var (search, searchLength) = Vector(searchArea);
            var (result, resultLength) = Vector(resultArea);
            lookupAt = search;
            resultAt = result;
            length = Math.Min(searchLength, resultLength);
        }
        else if (searchArea.RowCount > 1 && searchArea.ColumnCount > 1)
        {
            // Block form: search the first column, return the last.
            var lastColumn = searchArea.ColumnCount - 1;
            lookupAt = i => searchArea.At(i, 0);
            resultAt = i => searchArea.At(i, lastColumn);
            length = searchArea.RowCount;
        }
        else
        {
            var (vector, vectorLength) = Vector(searchArea);
            lookupAt = vector;
            resultAt = vector;
            length = vectorLength;
        }

        var best = -1;
        for (var i = 0; i < length; i++)
        {
            var candidate = lookupAt(i);
            if (candidate.IsBlank)
            {
                continue;
            }

            if (candidate.IsError)
            {
                return candidate;
            }

            var order = ValueCoercion.Compare(candidate, key);
            if (order == 0)
            {
                best = i;
                break;
            }

            if (order < 0)
            {
                best = i;
            }
        }

        if (best < 0)
        {
            return CellValue.FromError(
                ErrorValue.NotAvailable.WithDetail("no entry in the lookup vector is small enough"));
        }

        return resultAt(best);
    }

    /// <summary>Reads a one-row or one-column range as a vector.</summary>
    private static (Func<int, CellValue> At, int Length) Vector(IRangeView view) =>
        view.RowCount == 1
            ? (i => view.At(0, i), view.ColumnCount)
            : (i => view.At(i, 0), view.RowCount);
}
