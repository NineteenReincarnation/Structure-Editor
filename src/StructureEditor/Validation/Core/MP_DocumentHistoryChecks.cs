namespace StructureEditor;

// Offline contract: exact undo/redo order, session restoration and stack budget.
// These tests never initialize a Form or depend on RimWorld installation.
internal static class MP_DocumentHistoryChecks
{
    internal static void Run()
    {
        var initial = new StructureDocument
        {
            Items = new() { new() { Kind = "Building", DefName = "Wall", X = -4 } }
        };
        var history = new MP_DocumentHistory();
        var live = initial.Copy();
        history.Snapshot(live);
        live.Items[0].X = 10;
        history.Snapshot(live);
        live.Items[0].X = 20;

        StructureDocument first = history.Undo(live);
        StructureDocument second = history.Undo(first);
        if (first.Items[0].X != 10 || second.Items[0].X != -4
            || history.UndoCount != 0 || history.RedoCount != 2)
            throw new InvalidOperationException("Document undo stack order changed.");

        StructureDocument third = history.Redo(second);
        if (third.Items[0].X != 10)
            throw new InvalidOperationException("Document redo order changed.");

        // Capture/Restore are top-first arrays used by WorkspaceDocument.
        var restored = new MP_DocumentHistory();
        restored.Restore(history.CaptureUndo(), history.CaptureRedo());
        StructureDocument fourth = restored.Redo(third);
        if (fourth.Items[0].X != 20 || restored.RedoCount != 0)
            throw new InvalidOperationException("Session history restoration changed.");

        // A new edit must invalidate redo, just as the previous stacks did.
        history.Snapshot(third);
        if (history.RedoCount != 0 || history.UndoCount != 2)
            throw new InvalidOperationException("New edit did not invalidate redo.");

        // Old history could hold at most 64 snapshots, newest first.
        var bounded = new MP_DocumentHistory();
        for (int i = 0; i < 70; i++)
        {
            live.Items[0].X = i;
            bounded.Snapshot(live);
        }
        if (bounded.UndoCount != 64
            || bounded.CaptureUndo()[0].Items[0].X != 69
            || bounded.CaptureUndo()[63].Items[0].X != 6)
            throw new InvalidOperationException("Document history limit changed.");

        bounded.Clear();
        if (bounded.UndoCount != 0 || bounded.RedoCount != 0)
            throw new InvalidOperationException("History clear kept stale sessions.");
    }
}
