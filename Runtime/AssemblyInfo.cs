using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("com.sharq-it.sus.core.editor.tests")]
[assembly: InternalsVisibleTo("com.sharq-it.sus.core.runtime.tests")]
// SusModalService (SusRouterModal mounter) reads SusOverlayComponent.ResolvedLayer instead of
// duplicating the sealed layer as a literal — ARCH-20260903-OVERLAY-MOUNT §5 decision 4 / T-2826.
[assembly: InternalsVisibleTo("com.sharq-it.sus.router")]
[assembly: InternalsVisibleTo("com.sharq-it.sus.router.runtime.tests")]
