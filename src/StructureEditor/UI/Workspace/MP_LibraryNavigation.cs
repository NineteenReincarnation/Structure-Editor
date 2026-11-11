namespace StructureEditor;

// One navigation policy for three separate WPF library lists.
// W/S select rows, A/D move left/right between the library pages.
// Unselected lists start at the top on S and at the bottom on W.
internal static class MP_LibraryNavigation
{
    internal static int Step(int current, int direction, int count)
    {
        if (count <= 0) return -1;
        if (current < 0) return direction < 0 ? count - 1 : 0;
        return Math.Clamp(current + Math.Sign(direction), 0, count - 1);
    }

    internal static int StepPage(int current, int direction, int count)
    {
        if (count <= 0) return -1;
        return Math.Clamp(current + Math.Sign(direction), 0, count - 1);
    }
}
