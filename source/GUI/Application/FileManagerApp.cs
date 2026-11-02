using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;
using Font = Cosmos.Kernel.System.Graphics.Fonts.Font;

using ZonderqOS.SystemCore;
namespace ZonderqOS.GUI.Apps
{
    public class FileManagerApp : Application
    {
        private readonly Action<string> nanoLauncher;
        private readonly FileManagerView view;
        private readonly List<FileEntry> entries = new List<FileEntry>();
        // Sorted directory snapshot: typing in the filename filter must not
        // re-enumerate a potentially slow FAT32 volume on every keystroke.
        private readonly List<FileEntry> cachedEntries = new List<FileEntry>();
        private bool hasLoadedDirectory;
        // The path represented by cachedEntries can differ from currentPath
        // while NavigateTo is validating a new directory.
        private string loadedDirectoryPath;
        private readonly List<SidebarEntry> sidebarEntries = new List<SidebarEntry>();

        private string currentPath = "/root";
        private string searchText = "";
        private string status = "";
        private int selectedIndex = -1;
        private int scrollIndex;
        private int lastClickIndex = -1;
        private long lastClickTimestamp;
        private int lastClickX, lastClickY;
        private bool contextMenuVisible;
        private int contextMenuSelection = -1;
        private int contextMenuX;
        private int contextMenuY;
        private int dialogMode;
        private string dialogName = "";

        internal const int ToolbarTop = 4;
        internal const int ToolbarHeight = 40;
        internal const int ContentTop = 50;
        internal const int FooterHeight = 24;
        internal const int SidebarWidth = 176;
        internal const int TileWidth = 112;
        internal const int TileHeight = 94;
        internal const int TileGap = 6;
        internal const int SidebarSectionHeight = 22;
        internal const int SidebarItemHeight = 36;
        internal const int ContextMenuWidth = 240;
        internal const int ContextMenuItemHeight = 29;
        internal const int ContextMenuItemCount = 7;
        internal const int ContextMenuHeight = ContextMenuItemHeight * ContextMenuItemCount;

        // The menu must remain usable even when the window is resized to its
        // minimum size (320x220, with a 300x170 client view).
        internal int ContextMenuWidthForView => Math.Min(ContextMenuWidth, Math.Max(1, view.Width - 8));
        // A 200px firmware framebuffer can leave a 106px client. Compact
        // rows to a 9px glyph instead of overflowing the footer with a
        // forced 16px minimum. Below that size, do not open an invisible menu.
        internal bool CanShowContextMenu => view.Width >= 32 &&
            view.Height >= FooterHeight + 12 + 9 * ContextMenuItemCount;
        internal int ContextMenuItemHeightForView =>
            Math.Max(9, Math.Min(ContextMenuItemHeight,
                (view.Height - FooterHeight - 12) / ContextMenuItemCount));
        internal int ContextMenuHeightForView => ContextMenuItemHeightForView * ContextMenuItemCount;

        // Shared grid geometry for painting, hit-testing, keyboard navigation
        // and scrolling. A two-pixel disagreement can hide a clickable column.
        // A 240px framebuffer leaves a 220px client. The fixed 176px
        // sidebar used to paint the file grid outside that client, making
        // files inaccessible. Collapse it below 220px, otherwise shrink it
        // just enough to preserve one full 112px file tile.
        internal int SidebarWidthForView => view.Width < 220
            ? 0 : Math.Min(SidebarWidth, view.Width - TileWidth - 12);
        // Four 36px buttons spaced 42px apart; hide any that would
        // cross the client boundary and share this count with hit-testing.
        // Guard the negative range explicitly: C# integer division rounds
        // toward zero, so (43 - 44) / 42 + 1 would incorrectly draw a button.
        internal int ToolbarButtonCountForView => view.Width < 44 || view.Height < 44 ? 0 :
            Math.Min(4, (view.Width - 44) / 42 + 1);
        // A client narrower than the 12px chrome inset has no drawable grid.
        // Returning a fake 1px width would paint outside the framebuffer.
        internal int GridWidthForView => Math.Max(0, view.Width - SidebarWidthForView - 12);
        // Do not invent a 40px grid when the client is shorter than the
        // toolbar + footer. A 200x200 framebuffer can leave only 24px.
        internal int GridHeightForView => Math.Max(0, view.Height - FooterHeight - ContentTop - 8);
        // Use a final partial row only when >=40px is available. Compact
        // tiles fit inside that space; smaller fragments stay noninteractive.
        internal const int MinPartialTileHeight = 40;
        internal int GridRowsForView => GridWidthForView <= 0 || GridHeightForView < MinPartialTileHeight ? 0 :
            Math.Max(1, (GridHeightForView + TileHeight + TileGap - MinPartialTileHeight) /
                (TileHeight + TileGap));

        public FileManagerApp(int x, int y, Action<string> openFile) : base("File Manager")
        {
            nanoLauncher = openFile;
            Window = new Window(x, y, 820, 520, "ZonderqOS File Manager");
            Window.CloseAction = Close;
            // The first frame may be rendered before Update() runs.
            // Initialize the client to the already-constrained Window size,
            // otherwise an 800px view can spill outside a 320px desktop.
            view = new FileManagerView(10, 40,
                Math.Max(1, Window.Width - 20), Math.Max(1, Window.Height - 50), this);
            Window.AddChild(view);
            RebuildSidebar();
            Refresh();
        }

        // Alt+F4 dismisses the File Manager's own modal UI before closing
        // its window. Never lose a pending filename or activate a hidden
        // context-menu action as a side effect of the global shortcut.
        public bool DismissTransientUi()
        {
            if (dialogMode != 0)
            {
                dialogMode = 0;
                dialogName = "";
                status = "Cancelled";
                ResetClickSequence();
                return true;
            }

            if (contextMenuVisible)
            {
                contextMenuVisible = false;
                contextMenuSelection = -1;
                ResetClickSequence();
                return true;
            }

            return false;
        }

        public override void Update()
        {
            // A first click after switching back from another window is not
            // the second click of an old File Manager double-click.
            if (!Window.IsActive)
            {
                ResetClickSequence();
                // A menu from a background File Manager must not linger when
                // the user switches windows with Alt+Tab or the taskbar.
                contextMenuVisible = false;
            }

            SyncViewGeometry();
        }

        private void SyncViewGeometry()
        {
            int previousViewX = view.X;
            int previousViewY = view.Y;
            int previousViewWidth = view.Width;
            int previousViewHeight = view.Height;
            view.X = Window.X + 10;
            view.Y = Window.Y + 40;
            view.Width = Math.Max(1, Window.Width - 20);
            view.Height = Math.Max(1, Window.Height - 50);

            // Moving the window invalidates screen-space double-click anchors.
            // Even a small titlebar drag must not turn a later file click
            // into an accidental open on the same tile index.
            if (view.X != previousViewX || view.Y != previousViewY)
                ResetClickSequence();

            // Recalculate visible rows/columns only when the client size
            // changes. A selected file must not disappear after shrinking,
            // and a larger window must not show an empty scrolled page.
            if (view.Width != previousViewWidth || view.Height != previousViewHeight)
            {
                // A click before a resize must not complete a double-click
                // on a different tile occupying the same index afterward.
                ResetClickSequence();
                EnsureSelectionVisible();
            }

            // The menu may have been opened before a window resize. Refit
            // its origin to the current client area so it remains clickable.
            if (contextMenuVisible && !CanShowContextMenu)
            {
                contextMenuVisible = false;
                contextMenuSelection = -1;
            }
            if (contextMenuVisible)
            {
                contextMenuX = Math.Max(4, Math.Min(contextMenuX,
                    view.Width - ContextMenuWidthForView - 4));
                contextMenuY = Math.Max(4, Math.Min(contextMenuY,
                    view.Height - ContextMenuHeightForView - FooterHeight - 4));
            }
        }

        public override void HandleMouse(int x, int y, bool clicked, bool wasClicked)
        {
            HandleMouse(x, y, clicked, wasClicked, false, false);
        }

        public override void HandleMouse(int mouseX, int mouseY, bool left, bool oldLeft, bool right, bool oldRight)
        {
            // A mode change or an external move may precede this input frame.
            // Refit modal menus before interpreting their screen-space hitboxes.
            SyncViewGeometry();
            int x = mouseX - view.X;
            int y = mouseY - view.Y;

            // The context menu owns the next press, even outside its bounds.
            // Consume dismissal before Window.HandleMouse: otherwise a click
            // on the titlebar can close the window, or a click on the sidebar
            // can mount/unmount a volume hidden beneath the menu.
            // Right-button ownership wins over any fresh left press, even
            // if right was already held on a previous frame. A chord must
            // never execute Copy/Cut/Paste/New from the context menu.
            if (right && dialogMode == 0 && contextMenuVisible)
            {
                if (!oldRight || (left && !oldLeft))
                {
                    contextMenuVisible = false;
                    contextMenuSelection = -1;
                    ResetClickSequence();
                }
                return;
            }
            if (left && !oldLeft && dialogMode == 0 && contextMenuVisible)
            {
                if (ContextClick(x, y))
                    return;
                contextMenuVisible = false;
                contextMenuSelection = -1;
                ResetClickSequence();
                return;
            }
            // Do not forward a held left button to Window.HandleMouse while
            // the context menu is open. An interrupted title-bar drag or
            // resize must not resume behind Copy/Cut/Paste.
            if (left && dialogMode == 0 && contextMenuVisible)
                return;

            // The create-file/folder dialog is modal. In particular, a
            // click on the caption close/minimize controls must not dismiss
            // the owning window while the filename editor is active.
            if (dialogMode != 0)
            {
                if ((left && !oldLeft) || (right && !oldRight))
                    ResetClickSequence();
                return;
            }

            Window.HandleMouse(mouseX, mouseY, left, oldLeft);
            if (!Window.Visible || Window.IsMinimized || !IsRunning)
                return;

            // Window.HandleMouse can move or resize the window immediately.
            // Never dispatch a file/sidebar/toolbar click using the previous
            // frame's client origin or dimensions.
            SyncViewGeometry();
            x = mouseX - view.X;
            y = mouseY - view.Y;
            if (x < 0 || y < 0 || x >= view.Width || y >= view.Height)
                return;

            int contentBottom = view.Height - FooterHeight - 8;

            if (right && !oldRight)
            {
                // A context click is not part of a left-button double-click.
                ResetClickSequence();
                if (dialogMode != 0)
                    return;

                if (SidebarWidthForView > 0 && x < SidebarWidthForView + 4 && y >= ContentTop && y < contentBottom)
                {
                    SidebarRightClick(x, y);
                    return;
                }

                if (x >= SidebarWidthForView + 6 && y >= ContentTop && y < contentBottom)
                {
                    if (!CanShowContextMenu)
                    {
                        status = "Enlarge window for context menu";
                        return;
                    }
                    selectedIndex = HitEntryIndex(x, y);
                    // A fixed sidebar offset used to push the 240px menu off the
                    // right edge of narrow windows. Prefer viewport bounds.
                    contextMenuX = Math.Max(4, Math.Min(x, view.Width - ContextMenuWidthForView - 4));
                    contextMenuY = Math.Max(4, Math.Min(y,
                        view.Height - ContextMenuHeightForView - FooterHeight - 4));
                    contextMenuSelection = -1;
                    contextMenuVisible = true;
                    return;
                }
            }

            if (!left || oldLeft || dialogMode != 0)
                return;

            if (y >= ToolbarTop && y < ToolbarTop + ToolbarHeight)
            {
                // The toolbar background extends beyond the painted 30px
                // buttons. Only their actual Y+7..Y+37 rectangles may
                // navigate or refresh a filesystem; blank chrome is inert.
                if (y >= 7 && y < 37)
                    Toolbar(x);
                return;
            }

            if (y < ContentTop || y >= contentBottom)
                return;

            if (SidebarWidthForView > 0 && x < SidebarWidthForView + 4)
            {
                SidebarClick(x, y);
                return;
            }

            int index = HitEntryIndex(x, y);
            if (index < 0 || index >= entries.Count)
            {
                selectedIndex = -1;
                ResetClickSequence();
                return;
            }

            long now = Stopwatch.GetTimestamp();
            long elapsed = now - lastClickTimestamp;
            if (selectedIndex == index && lastClickIndex == index &&
                lastClickTimestamp > 0 && elapsed >= 0 &&
                elapsed <= Stopwatch.Frequency / 2 &&
                System.Math.Abs(mouseX - lastClickX) <= 12 &&
                System.Math.Abs(mouseY - lastClickY) <= 12)
            {
                // Clear the old click before opening: navigating to a new
                // directory must not turn the next single click on the same
                // tile index into a second double-click.
                ResetClickSequence();
                OpenEntry(index);
            }
            else
            {
                selectedIndex = index;
                lastClickIndex = index;
                lastClickTimestamp = now;
                lastClickX = mouseX;
                lastClickY = mouseY;
            }

            EnsureSelectionVisible();
        }

        private void ResetClickSequence()
        {
            lastClickIndex = -1;
            lastClickTimestamp = 0;
        }

        private int HitEntryIndex(int x, int y)
        {
            int contentBottom = view.Height - FooterHeight - 8;
            if (GridRowsForView == 0 ||
                x < SidebarWidthForView + 8 || y < ContentTop || y >= contentBottom)
                return -1;

            int gridLeft = SidebarWidthForView + 8;
            int columns = Columns();
            int relativeX = x - gridLeft;
            int relativeY = y - ContentTop;
            int strideX = TileWidth + TileGap;
            int strideY = TileHeight + TileGap;
            // On very small firmware framebuffers, the grid may be shorter
            // than a full tile. On larger windows its last partial row may
            // be deliberately omitted by Grid(). A broad content-area test
            // alone must not make those unpainted pixels clickable.
            if (relativeX < 0 || relativeX >= GridWidthForView ||
                relativeY < 0 || relativeY >= GridHeightForView)
                return -1;
            int col = relativeX / strideX;
            int row = relativeY / strideY;
            // Grid() paints only rows that have >=40px of space. Any
            // smaller trailing fragment is deliberately noninteractive.
            int visibleRows = GridRowsForView;

            if (col < 0 || col >= columns || row < 0 || row >= visibleRows)
                return -1;
            if (relativeX % strideX >= TileWidth || relativeY % strideY >= TileHeight)
                return -1;

            int index = scrollIndex + row * columns + col;
            return index >= 0 && index < entries.Count ? index : -1;
        }

        private bool ContextClick(int x, int y)
        {
            if (x < contextMenuX || x >= contextMenuX + ContextMenuWidthForView ||
                y < contextMenuY || y >= contextMenuY + ContextMenuHeightForView)
                return false;

            int item = (y - contextMenuY) / ContextMenuItemHeightForView;
            ExecuteContextAction(item);
            return true;
        }

        private bool ContextActionEnabled(int item)
        {
            if (item == 0 || item == 1)
                return selectedIndex >= 0 && selectedIndex < entries.Count;
            if (item == 2)
                return FileClipboard.HasItem;
            return item >= 3 && item < ContextMenuItemCount;
        }

        private void ExecuteContextAction(int item)
        {
            contextMenuVisible = false;
            contextMenuSelection = -1;
            // A visually disabled menu item must never perform an action.
            if (!ContextActionEnabled(item))
                return;

            if (item == 0)
                CopySelected(false);
            else if (item == 1)
                CopySelected(true);
            else if (item == 2)
                PasteClipboard();
            else if (item == 3)
                BeginCreate(1);
            else if (item == 4)
                BeginCreate(2);
            else if (item == 5)
                RefreshAll();
            else if (item == 6)
                GoUp();
        }

        private void Toolbar(int x)
        {
            ResetClickSequence();
            // Match the painted 36px controls at x=8,50,92,134 exactly.
            // The old broad x<42/84/... hit boxes also activated controls
            // from their unpainted margins, including hidden buttons on a
            // narrow framebuffer.
            if (x < 8)
                return;
            int offset = x - 8;
            int button = offset / 42;
            if (button >= ToolbarButtonCountForView || offset % 42 >= 36)
                return;

            if (button == 0)
                GoUp();
            else if (button == 1)
                NavigateTo("/root");
            else if (button == 2)
                RefreshAll();
            else if (button == 3)
            {
                searchText = "";
                ApplySearchFilter();
                status = "Search cleared";
            }
        }

        private void SidebarClick(int x, int y)
        {
            ResetClickSequence();
            SidebarEntry entry = GetSidebarEntryAt(y);
            if (entry == null || entry.IsSection)
                return;

            if (entry.IsVolume)
            {
                // Only the painted 20x22 M/U button may change mount state.
                // Clicking its label or the neighboring padding must never
                // detach a FAT32 volume (or implicitly mount a new one).
                if (entry.PartitionIndex > 0 && IsVolumeActionHit(entry, x, y))
                {
                    if (entry.IsMounted)
                        UnmountVolume(entry);
                    else
                        MountVolume(entry, false);
                    return;
                }

                if (entry.IsMounted && !string.IsNullOrEmpty(entry.Path))
                {
                    NavigateTo(entry.Path);
                    return;
                }

                if (entry.PartitionIndex > 0)
                {
                    status = "Use the M button to mount " + entry.Label;
                    return;
                }
            }

            if (entry.Navigable && !string.IsNullOrEmpty(entry.Path))
            {
                NavigateTo(entry.Path);
                return;
            }

            status = string.IsNullOrEmpty(entry.Detail)
                ? entry.Label
                : entry.Label + ": " + entry.Detail;
        }

        private bool IsVolumeActionHit(SidebarEntry target, int x, int y)
        {
            // Match RenderSidebar: sidebar origin X+4, action at width-27,
            // button Y+7, 20x22. Reject padding and clipped rows.
            int actionLeft = 4 + SidebarWidthForView - 27;
            if (x < actionLeft || x >= actionLeft + 20)
                return false;

            int rowY = ContentTop + 8;
            int sidebarBottom = ContentTop + GridHeightForView;
            for (int i = 0; i < sidebarEntries.Count; i++)
            {
                SidebarEntry entry = sidebarEntries[i];
                int rowHeight = entry.IsSection ? SidebarSectionHeight : SidebarItemHeight;
                if (rowY + rowHeight > sidebarBottom)
                    break;
                if (entry == target)
                    return entry.IsVolume && entry.PartitionIndex > 0 &&
                        y >= rowY + 7 && y < rowY + 29;
                rowY += rowHeight;
            }
            return false;
        }

        private void SidebarRightClick(int x, int y)
        {
            SidebarEntry entry = GetSidebarEntryAt(y);
            if (entry == null || !entry.IsVolume || entry.PartitionIndex <= 0)
                return;

            // Right-click is informational, not a destructive mount toggle.
            // A stray context click must never unmount an open FAT32 volume.
            // Explicit left-click on the M/U control remains available.
            status = entry.IsMounted
                ? "Mounted: " + (string.IsNullOrEmpty(entry.Path) ? entry.Label : entry.Path) +
                    " (use U button to unmount)"
                : "Not mounted: " + entry.Label + " (use M button to mount)";
        }

        private SidebarEntry GetSidebarEntryAt(int y)
        {
            // When the sidebar is collapsed, no invisible volume action
            // may be hit-tested from the blank left client margin.
            if (SidebarWidthForView <= 0)
                return null;

            // Match FileManagerView.RenderSidebar's fully-visible row limit.
            // A partially clipped volume row must not be clickable when the
            // window is shrunk, especially for mount/unmount actions.
            int sidebarBottom = ContentTop + GridHeightForView;
            int itemY = ContentTop + 8;
            for (int i = 0; i < sidebarEntries.Count; i++)
            {
                SidebarEntry entry = sidebarEntries[i];
                int height = entry.IsSection ? SidebarSectionHeight : SidebarItemHeight;
                if (itemY + height > sidebarBottom)
                    break;
                if (y >= itemY && y < itemY + height)
                    return entry;

                itemY += height;
            }

            return null;
        }

        private void MountVolume(SidebarEntry entry, bool openAfterMount)
        {
            if (entry == null || !entry.IsVolume || entry.PartitionIndex <= 0)
                return;

            string mountPoint;
            string error;
            if (!StorageMountManager.TryMount(entry.PartitionIndex, out mountPoint, out error))
            {
                status = "Mount error: " + (string.IsNullOrEmpty(error) ? "unknown error" : error);
                RebuildSidebar();
                return;
            }

            status = "Mounted Partition " + entry.PartitionIndex + " at " + mountPoint;
            RebuildSidebar();

            if (openAfterMount)
                NavigateTo(mountPoint);
            else
                RefreshKeepStatus();
        }

        private void UnmountVolume(SidebarEntry entry)
        {
            if (entry == null || !entry.IsVolume || entry.PartitionIndex <= 0)
                return;

            string mountPoint = StorageMountManager.GetMountPoint(entry.PartitionIndex);
            bool viewingVolume = !string.IsNullOrEmpty(mountPoint) &&
                IsInsidePath(currentPath, mountPoint);

            string error;
            if (!StorageMountManager.TryUnmount(entry.PartitionIndex, out error))
            {
                status = "Unmount error: " + (string.IsNullOrEmpty(error) ? "unknown error" : error);
                RebuildSidebar();
                RefreshKeepStatus();
                return;
            }

            // The detach has succeeded. A stale cached listing must never
            // remain interactive if BOTH the home and root fallback scans fail
            // (for example, during a simultaneous storage error). Clear only
            // snapshots of the detached volume; unrelated directories retain
            // their selection and cached view on a transient refresh failure.
            if (viewingVolume)
            {
                entries.Clear();
                cachedEntries.Clear();
                hasLoadedDirectory = false;
                loadedDirectoryPath = null;
                selectedIndex = -1;
                scrollIndex = 0;
                contextMenuVisible = false;
                contextMenuSelection = -1;
                ResetClickSequence();
                // /root is not guaranteed to exist on every disk-backed boot.
                currentPath = Directory.Exists("/root") ? "/root" : "/";
                searchText = "";
            }

            string unmountStatus = "Unmounted Partition " + entry.PartitionIndex;
            status = unmountStatus;
            RebuildSidebar();
            if (!RefreshInternal(unmountStatus) && viewingVolume && currentPath != "/")
            {
                // A disappearing home directory must not strand the file
                // manager on a detached volume. Root is the safe fallback;
                // never attempt to remount or write to the removed device.
                currentPath = "/";
                RefreshInternal(unmountStatus);
            }
        }

        private static bool IsInsidePath(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
                return false;
            if (path == root)
                return true;
            if (root == "/")
                return false;
            return path.StartsWith(root + "/", StringComparison.Ordinal);
        }

        private void NavigateTo(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                {
                    status = "Location unavailable";
                    return;
                }

                // Do not strand the user in an unreadable directory. A failed
                // scan must leave both the old path and its cached listing intact.
                string previousPath = currentPath;
                string previousSearch = searchText;
                currentPath = path.Replace('\\', '/');
                searchText = "";
                if (!RefreshInternal(""))
                {
                    currentPath = previousPath;
                    searchText = previousSearch;
                }
            }
            catch (Exception ex)
            {
                status = "Navigation error: " + ex.Message;
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            // A display-mode change or resize may precede the next Update().
            // Refit the client before keyboard selection, Enter and clipboard
            // shortcuts so hidden tiles cannot be acted on through stale bounds.
            SyncViewGeometry();
            // A modified Enter must not create files; modifier shortcuts must
            // never leak into the filename editor or a modal context menu.
            bool modified = (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0;
            if (dialogMode != 0)
            {
                if (key.Key == ConsoleKeyEx.Escape)
                {
                    dialogMode = 0;
                    dialogName = "";
                    status = "Cancelled";
                    return;
                }

                if (modified)
                    return;

                if ((key.Key == ConsoleKeyEx.Enter || key.Key == ConsoleKeyEx.NumEnter) &&
                    key.Modifiers == 0)
                {
                    FinishCreate();
                    return;
                }

                if (key.Key == ConsoleKeyEx.Backspace)
                {
                    if (dialogName.Length > 0)
                        dialogName = dialogName.Substring(0, dialogName.Length - 1);
                    return;
                }

                if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar) &&
                    (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0 &&
                    dialogName.Length < 64)
                    dialogName += key.KeyChar;
                return;
            }

            // Navigate the open context menu with the keyboard, skipping
            // disabled Copy/Cut/Paste rows. Enter uses the same action path
            // as a mouse click; Escape still dismisses without side effects.
            if (contextMenuVisible)
            {
                if (key.Key == ConsoleKeyEx.Escape)
                {
                    contextMenuVisible = false;
                    contextMenuSelection = -1;
                    return;
                }

                // A context menu is modal: no typing into the search filter,
                // navigation, refresh or clipboard writes behind the overlay.
                if (modified)
                    return;

                if (key.Key == ConsoleKeyEx.UpArrow || key.Key == ConsoleKeyEx.DownArrow)
                {
                    int direction = key.Key == ConsoleKeyEx.UpArrow ? -1 : 1;
                    int candidate = contextMenuSelection;
                    for (int attempt = 0; attempt < ContextMenuItemCount; attempt++)
                    {
                        candidate = candidate < 0
                            ? (direction > 0 ? 0 : ContextMenuItemCount - 1)
                            : (candidate + direction + ContextMenuItemCount) % ContextMenuItemCount;
                        if (ContextActionEnabled(candidate))
                        {
                            contextMenuSelection = candidate;
                            break;
                        }
                    }
                    return;
                }

                if ((key.Key == ConsoleKeyEx.Enter || key.Key == ConsoleKeyEx.NumEnter) &&
                    key.Modifiers == 0)
                {
                    if (contextMenuSelection >= 0)
                        ExecuteContextAction(contextMenuSelection);
                    return;
                }

                return;
            }

            // Ctrl+C/X/V are file actions, but Ctrl+Alt combinations
            // belong to the global desktop shortcut layer.
            bool control = (key.Modifiers & ConsoleModifiers.Control) != 0 &&
                (key.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Shift)) == 0;
            if (control)
            {
                if (key.Key == ConsoleKeyEx.C)
                {
                    CopySelected(false);
                    return;
                }
                if (key.Key == ConsoleKeyEx.X)
                {
                    CopySelected(true);
                    return;
                }
                if (key.Key == ConsoleKeyEx.V)
                {
                    PasteClipboard();
                    return;
                }
            }

            if (modified)
                return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                if (contextMenuVisible)
                {
                    contextMenuVisible = false;
                    contextMenuSelection = -1;
                    return;
                }

                // Escape clears an active filename filter before closing the
                // entire file manager. Keep the user in the current folder.
                if (searchText.Length > 0)
                {
                    searchText = "";
                    ApplySearchFilter();
                    return;
                }

                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                // Backspace edits the live filename filter first. Without this,
                // a typo unexpectedly navigated to the parent directory.
                if (searchText.Length > 0)
                {
                    searchText = searchText.Substring(0, searchText.Length - 1);
                    ApplySearchFilter();
                }
                else
                {
                    GoUp();
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                Select(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.RightArrow)
            {
                Select(1);
                return;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                Select(-Columns());
                return;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                Select(Columns());
                return;
            }

            if (key.Key == ConsoleKeyEx.Home)
            {
                SelectBoundary(false);
                return;
            }

            if (key.Key == ConsoleKeyEx.End)
            {
                SelectBoundary(true);
                return;
            }

            if (key.Key == ConsoleKeyEx.PageUp)
            {
                SelectPage(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.PageDown)
            {
                SelectPage(1);
                return;
            }

            if ((key.Key == ConsoleKeyEx.Enter || key.Key == ConsoleKeyEx.NumEnter) &&
                    key.Modifiers == 0)
            {
                ResetClickSequence();
                if (selectedIndex >= 0)
                    OpenEntry(selectedIndex);
                return;
            }

            if (key.Key == ConsoleKeyEx.F5)
            {
                RefreshAll();
                return;
            }

            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar) &&
                (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0)
            {
                if (searchText.Length < 48)
                {
                    searchText += key.KeyChar;
                    ApplySearchFilter();
                }
            }
        }

        private void CopySelected(bool cut)
        {
            if (GridRowsForView == 0)
            {
                status = "Enlarge window to select files";
                return;
            }
            if (selectedIndex < 0 || selectedIndex >= entries.Count)
            {
                status = cut ? "Select an item to cut" : "Select an item to copy";
                return;
            }

            FileEntry entry = entries[selectedIndex];
            FileClipboard.Set(entry.FullPath, entry.IsDirectory, cut);
            status = (cut ? "Cut: " : "Copied: ") + entry.Name + " - choose destination and paste";
        }

        private void PasteClipboard()
        {
            string destinationPath;
            string error;
            bool wasCut = FileClipboard.IsCut;
            string sourceName = FileClipboard.DisplayName;

            if (!FileClipboard.TryPaste(currentPath, out destinationPath, out error))
            {
                status = "Paste error: " + (string.IsNullOrEmpty(error) ? "unknown error" : error);
                return;
            }

            string targetName = Path.GetFileName((destinationPath ?? "").TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(targetName))
                targetName = sourceName;

            status = (wasCut ? "Moved: " : "Pasted: ") + targetName;
            RefreshKeepStatus();
        }

        private int Columns()
        {
            return Math.Max(1, (GridWidthForView + TileGap) / (TileWidth + TileGap));
        }

        private void SelectBoundary(bool last)
        {
            if (entries.Count == 0 || GridRowsForView == 0)
                return;

            ResetClickSequence();
            contextMenuVisible = false;
            selectedIndex = last ? entries.Count - 1 : 0;
            EnsureSelectionVisible();
        }

        private void SelectPage(int direction)
        {
            // Page navigation advances by exactly the number of tiles
            // painted in the current viewport, not a fixed row count.
            int rows = GridRowsForView;
            if (rows == 0)
                return;
            Select(direction * Columns() * rows);
        }

        private void Select(int delta)
        {
            if (entries.Count == 0 || GridRowsForView == 0)
                return;

            // Keyboard selection must not complete a pending mouse double-click.
            ResetClickSequence();
            contextMenuVisible = false;
            if (selectedIndex < 0)
                selectedIndex = delta >= 0 ? 0 : entries.Count - 1;
            else
                selectedIndex = Math.Max(0, Math.Min(entries.Count - 1, selectedIndex + delta));

            EnsureSelectionVisible();
        }

        private void EnsureSelectionVisible()
        {
            int cols = Columns();
            int rows = GridRowsForView;
            if (rows == 0)
            {
                // A tiny framebuffer can leave no painted file tiles at all.
                // Never retain an invisible selection: Enter or Ctrl+X/C
                // must not act on a file that the user cannot see.
                selectedIndex = -1;
                scrollIndex = 0;
                contextMenuVisible = false;
                contextMenuSelection = -1;
                ResetClickSequence();
                return;
            }
            int visible = cols * rows;

            if (selectedIndex >= 0)
            {
                if (selectedIndex < scrollIndex)
                    scrollIndex = selectedIndex;
                else if (selectedIndex >= scrollIndex + visible)
                    scrollIndex = selectedIndex - visible + cols;
            }

            scrollIndex -= scrollIndex % cols;
            if (scrollIndex < 0)
                scrollIndex = 0;

            // A partially filled last row still needs a full row-aligned
            // scroll position. For 10 files, 3 columns and 3 visible rows,
            // the old (count - visible) clamp gave 1, rounded down to 0,
            // leaving file #10 unreachable even when selected.
            int lastRow = entries.Count > 0 ? (entries.Count - 1) / cols : 0;
            int maxScroll = Math.Max(0, (lastRow - rows + 1) * cols);
            if (scrollIndex > maxScroll)
                scrollIndex = maxScroll;
        }

        private void GoUp()
        {
            if (currentPath == "/")
            {
                status = "Already at root";
                return;
            }

            try
            {
                string parent = Directory.GetParent(currentPath)?.FullName;
                NavigateTo(string.IsNullOrEmpty(parent) ? "/" : parent);
            }
            catch (Exception ex)
            {
                status = "Parent error: " + ex.Message;
            }
        }

        private void OpenEntry(int index)
        {
            if (GridRowsForView == 0 || index < 0 || index >= entries.Count)
                return;

            FileEntry entry = entries[index];
            if (entry.IsDirectory)
            {
                NavigateTo(entry.FullPath);
            }
            else if (nanoLauncher != null)
            {
                nanoLauncher(entry.FullPath);
                status = "Opened " + entry.Name;
            }
        }

        private void BeginCreate(int mode)
        {
            dialogMode = mode;
            dialogName = "";
            status = mode == 1 ? "New file: type a name" : "New folder: type a name";
        }

        private void FinishCreate()
        {
            string name = (dialogName ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                status = "Name cannot be empty";
                return;
            }

            if (name == "." || name == ".." || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0)
            {
                status = "Invalid name";
                return;
            }

            string path = Path.Combine(currentPath, name).Replace('\\', '/');
            if (VirtualFs.IsReadOnlyPath(path))
            {
                status = "Read-only virtual filesystem";
                dialogMode = 0;
                dialogName = "";
                return;
            }

            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    status = "Already exists";
                    return;
                }

                if (dialogMode == 1)
                {
                    File.WriteAllText(path, "");
                    status = "Created file: " + name;
                }
                else
                {
                    Directory.CreateDirectory(path);
                    status = "Created folder: " + name;
                }

                dialogMode = 0;
                dialogName = "";
                RefreshKeepStatus();
            }
            catch (Exception ex)
            {
                status = "Create error: " + ex.Message;
                dialogMode = 0;
                dialogName = "";
            }
        }

        private void RefreshAll()
        {
            RebuildSidebar();
            Refresh();
        }

        private void Refresh()
        {
            RefreshInternal("");
        }

        private void RefreshKeepStatus()
        {
            RefreshInternal(status);
        }

        private bool RefreshInternal(string keep)
        {
            try
            {
                string targetPath = currentPath;
                // The first boot may have no /root yet. Only that initial
                // scan may fall back to /. A removed FAT32 volume or a later
                // unreadable folder must NOT silently replace the user's
                // current directory and cached listing with root.
                if (!hasLoadedDirectory && targetPath == "/root" &&
                    !Directory.Exists(targetPath))
                    targetPath = "/";
                // Directory.GetFiles can fail after GetDirectories succeeded
                // (e.g. FAT32 media removed or an I/O error). Scan and sort
                // into a temporary snapshot; never erase the last usable view
                // until the complete read has succeeded.
                List<FileEntry> scanned = new List<FileEntry>();
                Add(scanned, Directory.GetDirectories(targetPath), true);
                Add(scanned, Directory.GetFiles(targetPath), false);
                Sort(scanned);

                // An explicit F5 or a successful paste should not jump back
                // to the first tile in a large directory. Compare the actual
                // cached path, not currentPath: NavigateTo changes currentPath
                // before it asks us to scan the new location.
                bool sameDirectory = hasLoadedDirectory &&
                    string.Equals(loadedDirectoryPath, targetPath, StringComparison.Ordinal);
                string selectedPath = sameDirectory && selectedIndex >= 0 && selectedIndex < entries.Count
                    ? entries[selectedIndex].FullPath : null;
                int previousScroll = sameDirectory ? scrollIndex : 0;
                // If new names sort before the old viewport, keeping only
                // the numeric scroll offset unexpectedly jumps to other files.
                string firstVisiblePath = sameDirectory && scrollIndex >= 0 && scrollIndex < entries.Count
                    ? entries[scrollIndex].FullPath : null;

                currentPath = targetPath;
                loadedDirectoryPath = targetPath;
                hasLoadedDirectory = true;
                entries.Clear();
                entries.AddRange(scanned);
                cachedEntries.Clear();
                cachedEntries.AddRange(scanned);
                selectedIndex = -1;
                ResetClickSequence();
                scrollIndex = previousScroll;
                contextMenuVisible = false;
                contextMenuSelection = -1;

                if (!string.IsNullOrEmpty(searchText))
                    Filter();

                // Entries are reconstructed on every scan, so reference
                // equality cannot restore the old selection. A removed file
                // remains unselected; never select a different tile merely
                // because it now occupies the same index.
                if (!string.IsNullOrEmpty(selectedPath) || !string.IsNullOrEmpty(firstVisiblePath))
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (string.Equals(entries[i].FullPath, selectedPath, StringComparison.Ordinal))
                            selectedIndex = i;
                        if (string.Equals(entries[i].FullPath, firstVisiblePath, StringComparison.Ordinal))
                            scrollIndex = i;
                    }
                }
                // The viewport is row-aligned and clamped to the actual
                // number of tiles. A removed anchor falls back to the old
                // numeric position; a surviving selection stays visible.
                EnsureSelectionVisible();

                status = string.IsNullOrEmpty(keep)
                    ? (string.IsNullOrEmpty(searchText)
                        ? entries.Count + " item(s)"
                        : "Search: " + searchText + " (" + entries.Count + " item(s))")
                    : keep;
                return true;
            }
            catch (Exception ex)
            {
                // Retain the old path, selection, scroll and cached snapshot
                // on transient storage failure. Navigation rolls back its path.
                status = "Read error: " + ex.Message;
                return false;
            }
        }

        private void ApplySearchFilter()
        {
            // Rebuild only the visible list. Explicit F5, navigation and
            // successful filesystem changes still refresh the disk snapshot.
            // Keep the selected FileEntry object when it survives filtering;
            // otherwise select the first search match so Enter works directly.
            FileEntry previousSelection = selectedIndex >= 0 && selectedIndex < entries.Count
                ? entries[selectedIndex] : null;
            scrollIndex = 0;
            contextMenuVisible = false;
            ResetClickSequence();
            entries.Clear();
            entries.AddRange(cachedEntries);
            if (!string.IsNullOrEmpty(searchText))
                Filter();
            selectedIndex = previousSelection == null ? -1 : entries.IndexOf(previousSelection);
            if (selectedIndex < 0 && searchText.Length > 0 && entries.Count > 0)
                selectedIndex = 0;
            EnsureSelectionVisible();
            status = string.IsNullOrEmpty(searchText)
                ? entries.Count + " item(s)"
                : "Search: " + searchText + " (" + entries.Count + " item(s))";
        }

        private void RebuildSidebar()
        {
            sidebarEntries.Clear();
            sidebarEntries.Add(SidebarEntry.Section("PLACES"));
            sidebarEntries.Add(SidebarEntry.Location("Home", "/root", IconType.Start, "USER FILES"));
            sidebarEntries.Add(SidebarEntry.Location("Root", "/", IconType.Folder, "FILESYSTEM"));

            if (Directory.Exists("/home"))
                sidebarEntries.Add(SidebarEntry.Location("Users", "/home", IconType.Folder, "/HOME"));
            if (Directory.Exists("/mnt"))
                sidebarEntries.Add(SidebarEntry.Location("Mounts", "/mnt", IconType.Folder, "/MNT"));

            sidebarEntries.Add(SidebarEntry.Section("VOLUMES"));

            try
            {
                int partitionCount = StorageManager.Partitions.Count;
                if (partitionCount > 0)
                {
                    for (int i = 0; i < partitionCount && i < 5; i++)
                    {
                        var partition = StorageManager.Partitions[i];
                        ulong bytes = (ulong)partition.BlockCount * (ulong)partition.BlockSize;
                        string mountPoint = StorageMountManager.GetMountPoint(i);
                        bool mounted = i == 0 || !string.IsNullOrEmpty(mountPoint);
                        string label = i == 0 ? "System" : "Partition " + i;
                        string detail = FormatCapacity(bytes) + (mounted ? "  MOUNTED" : "  READY");

                        sidebarEntries.Add(SidebarEntry.Volume(label, i, mountPoint, IconType.HardDisk, detail, mounted));
                    }
                }
                else
                {
                    sidebarEntries.Add(SidebarEntry.Device("No volumes", IconType.HardDisk, "NONE DETECTED"));
                }
            }
            catch
            {
                sidebarEntries.Add(SidebarEntry.Device("System", IconType.HardDisk, "STORAGE READY"));
            }

            sidebarEntries.Add(SidebarEntry.Section("DEVICES"));

            try
            {
                int deviceCount = StorageManager.DeviceCount;
                for (int i = 0; i < deviceCount && i < 5; i++)
                {
                    var device = StorageManager.GetDevice(i);
                    ulong bytes = (ulong)device.BlockCount * (ulong)device.BlockSize;
                    sidebarEntries.Add(SidebarEntry.Device("Disk " + i, IconType.HardDisk,
                        FormatCapacity(bytes) + "  DEVICE"));
                }

                if (deviceCount == 0)
                    sidebarEntries.Add(SidebarEntry.Device("No disks", IconType.HardDisk, "NONE DETECTED"));
            }
            catch
            {
                sidebarEntries.Add(SidebarEntry.Device("Storage", IconType.HardDisk, "UNAVAILABLE"));
            }
        }

        private static string FormatCapacity(ulong bytes)
        {
            ulong megabytes = bytes / (1024UL * 1024UL);
            if (megabytes >= 1024)
            {
                ulong wholeGb = megabytes / 1024;
                ulong tenths = (megabytes % 1024) * 10 / 1024;
                return wholeGb + "." + tenths + " GB";
            }

            return megabytes + " MB";
        }

        private void Add(List<FileEntry> destination, string[] paths, bool directory)
        {
            if (paths == null)
                return;

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                string name = Path.GetFileName(path.TrimEnd('/', '\\'));
                if (string.IsNullOrEmpty(name))
                    name = path;

                destination.Add(new FileEntry(name, path.Replace('\\', '/'), directory));
            }
        }

        private void Sort(List<FileEntry> destination)
        {
            // A large directory used to freeze the GUI with insertion sort
            // (quadratic comparisons). Stable bottom-up merge sort avoids
            // recursion and does not depend on Cosmos List<T>.Sort support.
            int count = destination.Count;
            if (count < 2)
                return;

            FileEntry[] scratch = new FileEntry[count];
            for (int run = 1; run < count; run = run > count / 2 ? count : run * 2)
            {
                int start = 0;
                while (start < count)
                {
                    int middle = Math.Min(count, start + run);
                    int end = Math.Min(count, middle + run);
                    int left = start;
                    int right = middle;

                    for (int write = start; write < end; write++)
                    {
                        // Equal names retain their original order.
                        if (left < middle && (right >= end ||
                            Compare(destination[left], destination[right]) <= 0))
                            scratch[write] = destination[left++];
                        else
                            scratch[write] = destination[right++];
                    }

                    start = end;
                }

                for (int i = 0; i < count; i++)
                    destination[i] = scratch[i];
            }
        }

        private int Compare(FileEntry a, FileEntry b)
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private void Filter()
        {
            // Compact matching entries in place. Repeated RemoveAt(i) shifts
            // the remaining list on each miss (quadratic for large folders).
            // IndexOf avoids allocating lowercase copies of every filename.
            int write = 0;
            for (int read = 0; read < entries.Count; read++)
            {
                FileEntry entry = entries[read];
                if (entry.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    entries[write++] = entry;
            }
            if (write < entries.Count)
                entries.RemoveRange(write, entries.Count - write);
        }

        public string CurrentPath { get { return currentPath; } }
        public string SearchText { get { return searchText; } }
        public string Status { get { return status; } }
        public int SelectedIndex { get { return selectedIndex; } }
        public int ScrollIndex { get { return scrollIndex; } }
        public List<FileEntry> Entries { get { return entries; } }
        public List<SidebarEntry> SidebarEntries { get { return sidebarEntries; } }
        public bool ContextMenuVisible { get { return contextMenuVisible; } }
        public int ContextMenuSelection { get { return contextMenuSelection; } }
        public int ContextMenuX { get { return contextMenuX; } }
        public int ContextMenuY { get { return contextMenuY; } }
        public int DialogMode { get { return dialogMode; } }
        public string DialogName { get { return dialogName; } }
    }

    public sealed class FileEntry
    {
        public readonly string Name;
        public readonly string FullPath;
        public readonly bool IsDirectory;

        public FileEntry(string name, string fullPath, bool isDirectory)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
        }
    }

    public sealed class SidebarEntry
    {
        public readonly string Label;
        public readonly string Path;
        public readonly string Detail;
        public readonly IconType Icon;
        public readonly bool Navigable;
        public readonly bool IsSection;
        public readonly bool IsVolume;
        public readonly int PartitionIndex;
        public readonly bool IsMounted;

        private SidebarEntry(string label, string path, IconType icon, string detail, bool navigable,
            bool isSection, bool isVolume, int partitionIndex, bool isMounted)
        {
            Label = label;
            Path = path;
            Icon = icon;
            Detail = detail;
            Navigable = navigable;
            IsSection = isSection;
            IsVolume = isVolume;
            PartitionIndex = partitionIndex;
            IsMounted = isMounted;
        }

        public static SidebarEntry Section(string label)
        {
            return new SidebarEntry(label, null, IconType.List, "", false, true, false, -1, false);
        }

        public static SidebarEntry Location(string label, string path, IconType icon, string detail)
        {
            return new SidebarEntry(label, path, icon, detail, true, false, false, -1, false);
        }

        public static SidebarEntry Device(string label, IconType icon, string detail)
        {
            return new SidebarEntry(label, null, icon, detail, false, false, false, -1, false);
        }

        public static SidebarEntry Volume(string label, int partitionIndex, string path, IconType icon,
            string detail, bool mounted)
        {
            return new SidebarEntry(label, path, icon, detail, mounted, false, true, partitionIndex, mounted);
        }
    }

    internal sealed class FileManagerView : Widget
    {
        private readonly FileManagerApp app;
        private readonly Font font = PCScreenFont.DefaultFont;

        private static readonly Color Chrome = Color.FromArgb(30, 35, 41);
        private static readonly Color ChromeRaised = Color.FromArgb(40, 47, 55);
        private static readonly Color ChromeBorder = Color.FromArgb(68, 78, 90);
        private static readonly Color Accent = Color.FromArgb(65, 140, 200);
        private static readonly Color ContentBackground = Color.FromArgb(28, 33, 39);
        private static readonly Color SidebarBackground = Color.FromArgb(24, 29, 35);
        private static readonly Color FooterText = Color.FromArgb(190, 199, 208);
        private static readonly Color MainText = Color.FromArgb(224, 230, 236);
        private static readonly Color SecondaryText = Color.FromArgb(142, 156, 170);

        public FileManagerView(int x, int y, int width, int height, FileManagerApp owner) : base(x, y, width, height)
        {
            app = owner;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            canvas.DrawFilledRectangle(Chrome, X, Y, Width, Height);
            canvas.DrawRectangle(ChromeBorder, X, Y, Width, Height);

            RenderToolbar(canvas);

            int footerY = Y + Height - FileManagerApp.FooterHeight - 4;
            int contentY = Y + FileManagerApp.ContentTop;
            int contentH = app.GridHeightForView;
            int sidebarX = X + 4;
            int sidebarW = app.SidebarWidthForView;
            int gridX = X + sidebarW + 8;
            int gridW = app.GridWidthForView;

            if (sidebarW > 0 && contentH > 0)
            {
                canvas.DrawFilledRectangle(SidebarBackground, sidebarX, contentY, sidebarW, contentH);
                canvas.DrawRectangle(Color.FromArgb(53, 62, 72), sidebarX, contentY, sidebarW, contentH);
                RenderSidebar(canvas, sidebarX, contentY, sidebarW, contentH);
            }

            if (contentH > 0 && gridW > 0)
            {
                canvas.DrawFilledRectangle(ContentBackground, gridX, contentY, gridW, contentH);
                canvas.DrawRectangle(Color.FromArgb(53, 62, 72), gridX, contentY, gridW, contentH);
                Grid(canvas, gridX, contentY, gridW, contentH);
            }

            RenderFooter(canvas, footerY);

            if (app.ContextMenuVisible)
                Menu(canvas);
            if (app.DialogMode != 0)
                Dialog(canvas);
        }

        private void RenderToolbar(Canvas canvas)
        {
            // A firmware mode can be shorter than the normal 40px toolbar.
            // Never submit a negative rectangle or paint controls outside
            // the actual client. The same height gate applies to hit-testing.
            if (Width <= 8 || Height <= 4)
                return;
            canvas.DrawFilledRectangle(ChromeRaised, X + 4, Y + 4,
                Width - 8, Math.Min(40, Height - 4));
            if (app.ToolbarButtonCountForView > 0)
                ToolbarButton(canvas, IconType.ArrowUp, 8);
            if (app.ToolbarButtonCountForView > 1)
                ToolbarButton(canvas, IconType.Start, 50);
            if (app.ToolbarButtonCountForView > 2)
                ToolbarButton(canvas, IconType.Refresh, 92);
            if (app.ToolbarButtonCountForView > 3)
                ToolbarButton(canvas, IconType.Search, 134);

            // The location bar begins after four 36px buttons. Hide it
            // instead of drawing 120px past the right edge on tiny clients.
            if (Width >= 308 && Height >= 44)
            {
                int pathX = X + 180;
                // Reserve the same 8px right inset as the toolbar background.
                // At 300-307px a forced 120px bar overran the client edge.
                int pathW = Width - 188;
                UiPainter.RoundedPanel(canvas, pathX, Y + 9, pathW, 30,
                    UiMetrics.ButtonRadius, UiPalette.Surface, UiPalette.AccentBorder);

                SmallTextRenderer.DrawClipped(canvas, app.CurrentPath, pathX + 10, Y + 20,
                    Math.Max(20, pathW - 20), MainText);

                if (!string.IsNullOrEmpty(app.SearchText) && pathW >= 250)
                {
                    int searchWidth = Math.Min(170, pathW / 3);
                    int searchX = pathX + pathW - searchWidth - 8;
                    canvas.DrawFilledRectangle(Color.FromArgb(35, 47, 58), searchX, Y + 14, searchWidth, 20);
                    IconManager.DrawScaled(canvas, IconType.Search, searchX + 4, Y + 16, 14, 14);
                    SmallTextRenderer.DrawClipped(canvas, app.SearchText, searchX + 22, Y + 21,
                        searchWidth - 27, Color.FromArgb(166, 202, 231));
                }
            }
        }

        private void RenderSidebar(Canvas canvas, int x, int y, int width, int height)
        {
            int itemY = y + 8;
            List<SidebarEntry> items = app.SidebarEntries;

            for (int i = 0; i < items.Count; i++)
            {
                SidebarEntry entry = items[i];
                int rowHeight = entry.IsSection ? FileManagerApp.SidebarSectionHeight : FileManagerApp.SidebarItemHeight;
                if (itemY + rowHeight > y + height)
                    break;

                if (entry.IsSection)
                {
                    IconManager.DrawScaled(canvas, entry.Icon, x + 10, itemY + 4, 14, 14);
                    SmallTextRenderer.DrawClipped(canvas, entry.Label, x + 30, itemY + 7,
                        width - 40, Color.FromArgb(104, 130, 151));
                    itemY += rowHeight;
                    continue;
                }

                bool active = IsEntryActive(entry);
                if (active)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(37, 62, 83), x + 4, itemY + 1, width - 8, rowHeight - 2);
                    canvas.DrawFilledRectangle(Accent, x + 4, itemY + 1, 3, rowHeight - 2);
                }

                int iconY = itemY + 8;
                canvas.DrawFilledRectangle(Color.FromArgb(31, 38, 45), x + 10, iconY - 3, 26, 26);
                IconManager.DrawScaled(canvas, entry.Icon, x + 14, iconY + 1, 18, 18);

                int actionReserve = entry.IsVolume && entry.PartitionIndex > 0 ? 30 : 0;
                // At 96-139px the icon and volume M/U control remain
                // available; text would overlap the mount control.
                if (width >= 140)
                {
                    SmallTextRenderer.DrawClipped(canvas, entry.Label, x + 44, itemY + 8,
                        Math.Max(20, width - 52 - actionReserve), active ? Color.WhiteSmoke : MainText);
                    SmallTextRenderer.DrawClipped(canvas, entry.Detail, x + 44, itemY + 21,
                        Math.Max(20, width - 52 - actionReserve), active ? Color.FromArgb(143, 190, 226) : SecondaryText);
                }

                if (entry.IsVolume && entry.PartitionIndex > 0)
                {
                    int actionX = x + width - 27;
                    Color actionBackground = entry.IsMounted
                        ? Color.FromArgb(69, 48, 52)
                        : Color.FromArgb(35, 64, 87);
                    Color actionBorder = entry.IsMounted
                        ? Color.FromArgb(121, 70, 76)
                        : Color.FromArgb(67, 126, 171);
                    canvas.DrawFilledRectangle(actionBackground, actionX, itemY + 7, 20, 22);
                    canvas.DrawRectangle(actionBorder, actionX, itemY + 7, 20, 22);
                    SmallTextRenderer.DrawCentered(canvas, entry.IsMounted ? "U" : "M",
                        actionX, itemY + 15, 20, Color.WhiteSmoke);
                }

                itemY += rowHeight;
            }
        }

        private bool IsEntryActive(SidebarEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Path))
                return false;
            if (entry.Path == "/")
                return app.CurrentPath == "/";
            if (app.CurrentPath == entry.Path)
                return true;
            return app.CurrentPath.StartsWith(entry.Path + "/", StringComparison.Ordinal);
        }

        private void RenderFooter(Canvas canvas, int footerY)
        {
            // The footer is optional on tiny firmware framebuffers. Its
            // 24px background must not be drawn with a negative width or
            // above the top of the view.
            if (Width <= 8 || Height < FileManagerApp.FooterHeight + 4)
                return;
            int footerX = X + 4;
            int footerW = Width - 8;

            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), footerX, footerY, footerW, FileManagerApp.FooterHeight);
            canvas.DrawLine(Color.FromArgb(72, 84, 96), footerX, footerY, footerX + footerW, footerY);

            string hint = Width >= 760
                ? "CTRL+C COPY   CTRL+X CUT   CTRL+V PASTE   F5 REFRESH"
                : "COPY CUT PASTE: RIGHT CLICK";

            int hintWidth = SmallTextRenderer.Width(hint);
            int hintX = Math.Max(X + Width / 2, X + Width - 10 - hintWidth);
            int hintMaxWidth = Math.Max(0, X + Width - 10 - hintX);
            int statusX = X + 10;
            // A forced 18px label overran the right edge on very narrow
            // clients. Never request text beyond the actual footer bounds.
            int statusMaxWidth = Math.Max(0, Math.Min(hintX - statusX - 12,
                X + Width - 10 - statusX));
            int textY = footerY + 8;

            if (statusMaxWidth > 0)
                SmallTextRenderer.DrawClipped(canvas, app.Status ?? "",
                    statusX, textY, statusMaxWidth, FooterText);
            if (hintMaxWidth > 0)
                SmallTextRenderer.DrawClipped(canvas, hint,
                    hintX, textY, hintMaxWidth, SecondaryText);
        }

        private void Grid(Canvas canvas, int x, int y, int width, int height)
        {
            // The remaining strip below the toolbar is too short for even
            // the compact 40px tile; keep it noninteractive and unpainted.
            if (height < FileManagerApp.MinPartialTileHeight)
                return;
            int iconSize = 42;
            int tileWidth = Math.Min(FileManagerApp.TileWidth, width);
            int labelWidth = Math.Min(88, Math.Max(1, tileWidth - 12));
            int labelHeight = 18;
            int cols = Math.Max(1, (width + FileManagerApp.TileGap) /
                (FileManagerApp.TileWidth + FileManagerApp.TileGap));
            int rows = app.GridRowsForView;
            int count = cols * rows;

            for (int i = 0; i < count; i++)
            {
                int index = app.ScrollIndex + i;
                if (index >= app.Entries.Count)
                    break;

                FileEntry entry = app.Entries[index];
                int tileX = x + i % cols * (FileManagerApp.TileWidth + FileManagerApp.TileGap);
                int tileY = y + i / cols * (FileManagerApp.TileHeight + FileManagerApp.TileGap);
                // The minimum 320x220 window leaves only 88px for the 94px
                // tile. Never paint its selection into the footer below.
                int visibleTileHeight = Math.Min(FileManagerApp.TileHeight, y + height - tileY);
                if (visibleTileHeight <= 0)
                    break;

                if (index == app.SelectedIndex)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(41, 66, 87), tileX, tileY,
                        tileWidth, visibleTileHeight);
                    canvas.DrawRectangle(Accent, tileX, tileY,
                        tileWidth, visibleTileHeight);
                    canvas.DrawFilledRectangle(Accent, tileX, tileY, Math.Min(3, tileWidth), visibleTileHeight);
                }

                if (visibleTileHeight < iconSize + 33 || tileWidth < 88)
                {
                    // A short grid or narrow framebuffer cannot contain the
                    // full icon and label. Clip both to the painted tile.
                    if (visibleTileHeight >= 32 && tileWidth >= 32)
                    {
                        int compactIconSize = Math.Min(24, tileWidth - 8);
                        IconManager.DrawScaled(canvas, ResolveEntryIcon(entry),
                            tileX + 6, tileY + 7, compactIconSize, compactIconSize);
                        if (tileWidth >= 48)
                            SmallTextRenderer.DrawClipped(canvas, entry.Name ?? "",
                                tileX + 36, tileY + 17, tileWidth - 42,
                                index == app.SelectedIndex ? Color.WhiteSmoke : MainText);
                    }
                }
                else
                {
                    int iconX = tileX + (tileWidth - iconSize) / 2;
                    canvas.DrawFilledRectangle(Color.FromArgb(34, 40, 47), iconX - 5, tileY + 3, iconSize + 10, iconSize + 8);
                    IconManager.DrawScaled(canvas, ResolveEntryIcon(entry),
                        iconX, tileY + 7, iconSize, iconSize);

                    DrawWrappedName(canvas, entry.Name ?? "",
                        tileX + (tileWidth - labelWidth) / 2,
                        tileY + iconSize + 15,
                        labelWidth,
                        labelHeight,
                        index == app.SelectedIndex ? Color.WhiteSmoke : MainText);
                }
            }
        }

        private static IconType ResolveEntryIcon(FileEntry entry)
        {
            if (entry == null)
                return IconType.File;
            if (entry.IsDirectory)
                return IconType.Folder;

            string extension;
            try
            {
                extension = Path.GetExtension(entry.Name ?? "").ToLowerInvariant();
            }
            catch
            {
                return IconType.File;
            }

            switch (extension)
            {
                case ".bmp": return IconType.FileBmp;
                case ".doc": return IconType.FileDoc;
                case ".docx": return IconType.FileDocx;
                case ".jpg":
                case ".jpeg": return IconType.FileJpg;
                case ".pdf": return IconType.FilePdf;
                case ".png": return IconType.FilePng;
                case ".svg": return IconType.FileSvg;
                case ".txt":
                case ".log":
                case ".md":
                case ".cfg":
                case ".conf":
                case ".ini": return IconType.FileTxt;
                default: return IconType.File;
            }
        }

        private void DrawWrappedName(Canvas canvas, string name, int x, int y, int width, int height, Color color)
        {
            const int glyphWidth = 5;
            const int spacing = 1;
            const int lineHeight = 9;
            int maxChars = Math.Max(1, (width + spacing) / (glyphWidth + spacing));

            if (string.IsNullOrEmpty(name))
                return;

            int position = 0;
            int maxLines = Math.Max(1, height / lineHeight);

            for (int line = 0; line < maxLines && position < name.Length; line++)
            {
                int take = Math.Min(maxChars, name.Length - position);
                if (position + take < name.Length)
                {
                    int breakAt = name.LastIndexOf(' ', position + take - 1, take);
                    if (breakAt >= position)
                        take = breakAt - position;
                }

                if (take <= 0)
                    take = Math.Min(maxChars, name.Length - position);

                bool truncated = position + take < name.Length && line == maxLines - 1;
                int drawTake = truncated && take >= 3 ? take - 3 : take;
                int textWidth = drawTake * 6 - (drawTake > 0 ? 1 : 0);
                if (truncated)
                    textWidth += 18;

                int textX = x + Math.Max(0, (width - textWidth) / 2);
                SmallTextRenderer.DrawRange(canvas, name, position, drawTake, textX, y + line * lineHeight, color);
                if (truncated)
                    SmallTextRenderer.Draw(canvas, "...", textX + drawTake * 6, y + line * lineHeight, color);

                position += take;
                while (position < name.Length && name[position] == ' ')
                    position++;
            }
        }

        private void ToolbarButton(Canvas canvas, IconType type, int offset)
        {
            int buttonX = X + offset;
            UiPainter.RoundedPanel(canvas, buttonX, Y + 7, 36, 30,
                UiMetrics.TileRadius, UiPalette.SurfaceRaised, UiPalette.Border);
            IconManager.Draw(canvas, type, buttonX + 9, Y + 11, Color.WhiteSmoke);
        }

        private void Menu(Canvas canvas)
        {
            if (!app.CanShowContextMenu)
                return;
            int x = X + app.ContextMenuX;
            int y = Y + app.ContextMenuY;

            int menuWidth = app.ContextMenuWidthForView;
            int menuHeight = app.ContextMenuHeightForView;
            int rowHeight = app.ContextMenuItemHeightForView;

            UiPainter.Shadow(canvas, x, y, menuWidth, menuHeight, UiMetrics.MenuRadius);
            UiPainter.RoundedPanel(canvas, x, y, menuWidth, menuHeight, UiMetrics.MenuRadius,
                UiPalette.SurfaceRaised, UiPalette.Border);

            bool selected = app.SelectedIndex >= 0 && app.SelectedIndex < app.Entries.Count;
            DrawMenuItem(canvas, "Copy", "Ctrl+C", x, y + 0 * rowHeight, selected, 0);
            DrawMenuItem(canvas, "Cut", "Ctrl+X", x, y + 1 * rowHeight, selected, 1);
            DrawMenuItem(canvas, "Paste", "Ctrl+V", x, y + 2 * rowHeight, FileClipboard.HasItem, 2);
            canvas.DrawLine(Color.FromArgb(63, 73, 84), x + 8, y + 3 * rowHeight,
                x + menuWidth - 8, y + 3 * rowHeight);
            DrawMenuItem(canvas, "New file", "", x, y + 3 * rowHeight, true, 3);
            DrawMenuItem(canvas, "New folder", "", x, y + 4 * rowHeight, true, 4);
            DrawMenuItem(canvas, "Refresh", "F5", x, y + 5 * rowHeight, true, 5);
            DrawMenuItem(canvas, "Go up", "Backspace", x, y + 6 * rowHeight, true, 6);
        }

        private void DrawMenuItem(Canvas canvas, string text, string shortcut, int x, int y, bool enabled, int index)
        {
            if (enabled && app.ContextMenuSelection == index)
                canvas.DrawFilledRectangle(Color.FromArgb(53, 78, 99),
                    x + 4, y + 1, Math.Max(1, app.ContextMenuWidthForView - 8),
                    Math.Max(1, app.ContextMenuItemHeightForView - 2));
            Color color = enabled ? MainText : Color.FromArgb(91, 103, 114);
            int menuWidth = app.ContextMenuWidthForView;
            int textOffset = Math.Max(0, (app.ContextMenuItemHeightForView - 9) / 2);
            // At 24-159px there is no room for shortcut labels. Clip the
            // action name and never draw a shortcut left of the menu.
            bool showShortcut = !string.IsNullOrEmpty(shortcut) && menuWidth >= 160;
            int shortcutWidth = showShortcut ? SmallTextRenderer.Width(shortcut) : 0;
            int labelWidth = Math.Max(0, menuWidth - 20 -
                (showShortcut ? shortcutWidth + 10 : 0));
            if (labelWidth > 0)
                SmallTextRenderer.DrawClipped(canvas, text,
                    x + 10, y + textOffset, labelWidth, color);

            if (showShortcut)
                SmallTextRenderer.Draw(canvas, shortcut,
                    x + menuWidth - shortcutWidth - 10, y + textOffset,
                    enabled ? SecondaryText : Color.FromArgb(75, 86, 96));
        }

        private void Dialog(Canvas canvas)
        {
            // The old 280x126 minimum escaped the client on 240px displays
            // and when an existing window was shrunk after a mode change.
            // Keep even the 4px shadow inside the actual view.
            int width = Math.Min(430, Math.Max(0, Width - 16));
            int height = Math.Min(126, Math.Max(0, Height - 16));
            if (width < 32 || height < 28)
                return;
            int x = X + (Width - width) / 2;
            int y = Y + (Height - height) / 2;

            canvas.DrawFilledRectangle(Color.FromArgb(20, 24, 29), x + 4, y + 4, width, height);
            canvas.DrawFilledRectangle(Color.FromArgb(38, 44, 51), x, y, width, height);
            canvas.DrawRectangle(Accent, x, y, width, height);

            string title = app.DialogMode == 1 ? "New file" : "New folder";
            SmallTextRenderer.DrawClipped(canvas, title, x + 12, y + 12,
                width - 24, Color.FromArgb(235, 239, 243));

            // Compact the input field vertically; omit it rather than draw
            // beyond the client when the framebuffer is extremely short.
            int fieldY = y + Math.Min(42, Math.Max(24, height - 40));
            int fieldHeight = Math.Min(30, Math.Max(0, height - (fieldY - y) - 8));
            if (width >= 48 && fieldHeight >= 18)
            {
                canvas.DrawFilledRectangle(Color.FromArgb(26, 31, 37),
                    x + 12, fieldY, width - 24, fieldHeight);
                canvas.DrawRectangle(Color.FromArgb(83, 96, 110),
                    x + 12, fieldY, width - 24, fieldHeight);
                SmallTextRenderer.DrawClipped(canvas, app.DialogName + "_",
                    x + 18, fieldY + Math.Min(12, fieldHeight - 9),
                    width - 36, Color.FromArgb(226, 232, 238));
            }

            if (height >= 108 && width >= 200)
                SmallTextRenderer.DrawClipped(canvas, "ENTER CREATE   ESC CANCEL",
                    x + 12, y + 94, width - 24, SecondaryText);
        }
    }
}
