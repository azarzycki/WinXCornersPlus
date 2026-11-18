using System.Runtime.InteropServices;

// Review S3: every [DllImport] in this assembly (user32, shell32, dwmapi, uxtheme, ...) must be
// resolved from System32 only. Without this attribute the loader also searches the application
// directory, so a planted dwmapi.dll / uxtheme.dll next to the exe would be loaded into a process
// that may be running as administrator.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
