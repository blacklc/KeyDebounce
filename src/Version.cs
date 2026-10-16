// Single source of truth for the version string.
// Keep it in sync with: VERSION, CHANGELOG.md, and the git tag (v<version>).
using System.Reflection;

[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: AssemblyInformationalVersion("1.0.1")]
[assembly: AssemblyProduct("KeyDebounce")]
[assembly: AssemblyTitle("KeyDebounce")]
[assembly: AssemblyCompany("KeyDebounce contributors")]
[assembly: AssemblyCopyright("MIT licensed")]

static class AppInfo
{
    /// Shown in the window title, in KeyDebounce.status, and in the tray tooltip.
    public const string Version = "1.0.1";
}
