using System.Runtime.InteropServices;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.Infrastructure;

/// <summary>
/// Guards the precondition the whole sqlite-vec test surface rests on: the native extension has to
/// reach the build output, or every one of those tests skips.
/// </summary>
/// <remarks>
/// These are deliberately not <c>SkippableFact</c>. A skip here would hide exactly the failure mode
/// being guarded — 62 call sites once skipped on every CI run because availability detection was
/// short-circuited, and skips keep a build green, so nothing reported it for as long as it lasted.
/// </remarks>
public class SqliteVecAvailabilityTests
{
    [Fact]
    public void NativeExtension_IsPresentInOutput_ForTheCurrentPlatform()
    {
        var detected = CITestHelper.ShouldSkipSqliteVec();

        Assert.False(detected,
            "sqlite-vec was not detected in the build output. Either the package failed to restore " +
            "its native asset, or ENABLE_SQLITEVEC_TESTS is set to false in this environment. " +
            "Every sqlite-vec test skips while this holds, and skipping does not fail a build.");
    }

    [Fact]
    public void NativeExtension_IsPresentInOutput_ForLinux()
    {
        // CI runs on ubuntu, so the Linux asset must be in the output regardless of which platform
        // produced the build. A RID-agnostic build copies every runtime; if this file goes missing
        // the sqlite-vec surface silently stops running on CI while still passing locally.
        var linuxNative = Path.Combine(
            AppContext.BaseDirectory, "runtimes", "linux-x64", "native", "vec0.so");

        Assert.True(File.Exists(linuxNative),
            $"Expected the Linux sqlite-vec native at '{linuxNative}'. Its absence means CI would " +
            "skip the sqlite-vec tests even though this machine runs them.");
    }

    [Fact]
    public void Detection_DoesNotConsultCiEnvironmentVariables()
    {
        // Availability is a property of the filesystem, not of who is running the build. The
        // removed CI branch made this false: it returned "unavailable" whenever CI/GITHUB_ACTIONS
        // was set, so the detection below never executed there.
        var helper = typeof(CITestHelper);
        var source = helper.GetMethods()
            .Select(m => m.Name)
            .ToList();

        Assert.DoesNotContain("IsCIEnvironment", source);
        Assert.DoesNotContain("SkipIfCIEnvironment", source);
    }

    [Fact]
    public void WindowsNative_IsPresentInOutput_WhenRunningOnWindows()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Windows-specific asset check");

        var windowsNative = Path.Combine(
            AppContext.BaseDirectory, "runtimes", "win-x64", "native", "vec0.dll");

        Assert.True(File.Exists(windowsNative),
            $"Expected the Windows sqlite-vec native at '{windowsNative}'.");
    }
}
