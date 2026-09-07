using System.Runtime.CompilerServices;

// The registry sets SusStoryEntry.Weight right after it builds an entry, so the setter is
// internal: a story declares its weight, nobody else assigns it. The Editor test assembly needs
// the same door to build a heavy entry WITHOUT adding a heavy story to the shared fixtures — the
// registry tests assert the exact list of groups and slugs, and a new fixture story would move
// figures that have nothing to do with the matrix (card T-3038).
[assembly: InternalsVisibleTo("com.sharq-it.sus.core.editor.tests")]
