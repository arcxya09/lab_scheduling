using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LabSchedule;

/// <summary>Win32 helpers: DPI, the common file dialogs, and "show in folder".</summary>
internal static partial class NativeMethods
{
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_HIDEREADONLY = 0x00000004;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_EXPLORER = 0x00080000;
    private const int MaxPath = 32768;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName(ref OpenFileName ofn);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Shows the Open dialog; returns null when cancelled.</summary>
    public static string? ShowOpenDialog(IntPtr owner, string title, string filterName, string extension) =>
        ShowDialog(owner, title, filterName, extension, initialPath: null, save: false);

    /// <summary>Shows the Save As dialog starting at <paramref name="defaultPath"/>; returns null when cancelled.</summary>
    public static string? ShowSaveDialog(IntPtr owner, string title, string filterName, string extension, string defaultPath) =>
        ShowDialog(owner, title, filterName, extension, defaultPath, save: true);

    private static string? ShowDialog(IntPtr owner, string title, string filterName, string extension, string? initialPath, bool save)
    {
        var allocations = new List<IntPtr>();
        IntPtr Alloc(string? text)
        {
            if (text is null) return IntPtr.Zero;
            var pointer = Marshal.StringToHGlobalUni(text);
            allocations.Add(pointer);
            return pointer;
        }

        var fileBuffer = Marshal.AllocHGlobal(MaxPath * sizeof(char));
        allocations.Add(fileBuffer);
        try
        {
            var initialName = initialPath is null ? "" : Path.GetFileName(initialPath);
            var initialChars = (initialName + "\0").ToCharArray();
            Marshal.Copy(initialChars, 0, fileBuffer, Math.Min(initialChars.Length, MaxPath));
            var initialDir = initialPath is null ? null : Path.GetDirectoryName(initialPath);

            var ofn = new OpenFileName
            {
                lStructSize = Marshal.SizeOf<OpenFileName>(),
                hwndOwner = owner,
                lpstrFilter = Alloc($"{filterName}\0*.{extension}\0\0"),
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = MaxPath,
                lpstrInitialDir = Alloc(initialDir),
                lpstrTitle = Alloc(title),
                lpstrDefExt = Alloc(extension),
                Flags = OFN_EXPLORER | OFN_NOCHANGEDIR | OFN_HIDEREADONLY | OFN_PATHMUSTEXIST |
                        (save ? OFN_OVERWRITEPROMPT : OFN_FILEMUSTEXIST)
            };
            var ok = save ? GetSaveFileName(ref ofn) : GetOpenFileName(ref ofn);
            return ok ? Marshal.PtrToStringUni(fileBuffer) : null;
        }
        finally
        {
            allocations.ForEach(Marshal.FreeHGlobal);
        }
    }

    /// <summary>Opens Explorer with the file selected (or the folder, if the file is missing).</summary>
    public static bool ShowInFolder(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"") { UseShellExecute = true });
            return true;
        }
        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory)) return false;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
        return true;
    }
}
