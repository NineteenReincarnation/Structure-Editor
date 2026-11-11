namespace StructureEditor;

// Independent document history; never knows about forms, canvas or persistence.
// Ordering and item-budget trimming are identical to the original MainForm stacks.
internal sealed class MP_DocumentHistory
{
    private readonly Stack<StructureDocument> undo = new();
    private readonly Stack<StructureDocument> redo = new();

    internal int UndoCount => undo.Count;
    internal int RedoCount => redo.Count;

    internal void Snapshot(StructureDocument current)
    {
        undo.Push(current.Copy());
        redo.Clear();
        int cost = 0;
        var kept = undo.Take(64)
            .TakeWhile((document, index) =>
            {
                cost += document.Items.Count;
                return index < 2 || cost <= 250000;
            })
            .Reverse().ToList();
        undo.Clear();
        foreach (StructureDocument item in kept) undo.Push(item);
    }

    internal StructureDocument Undo(StructureDocument current)
    {
        if (undo.Count == 0) return null;
        redo.Push(current.Copy());
        return undo.Pop();
    }

    internal StructureDocument Redo(StructureDocument current)
    {
        if (redo.Count == 0) return null;
        undo.Push(current.Copy());
        return redo.Pop();
    }

    // Top-of-stack-first arrays preserve the existing in-memory session format.
    internal StructureDocument[] CaptureUndo() => undo.ToArray();
    internal StructureDocument[] CaptureRedo() => redo.ToArray();

    internal void Restore(StructureDocument[] storedUndo, StructureDocument[] storedRedo)
    {
        undo.Clear();
        foreach (StructureDocument item in storedUndo.Reverse()) undo.Push(item);
        redo.Clear();
        foreach (StructureDocument item in storedRedo.Reverse()) redo.Push(item);
    }

    internal void Clear()
    {
        undo.Clear();
        redo.Clear();
    }
}
