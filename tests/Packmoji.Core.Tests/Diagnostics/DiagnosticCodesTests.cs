using System.Reflection;
using System.Text.RegularExpressions;
using Packmoji.Core.Diagnostics;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Diagnostics
{
    public sealed class DiagnosticCodesTests
    {
        private static readonly string[] Codes = typeof(DiagnosticCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        [Fact]
        public void There_are_the_80_codes_of_the_four_designs() => Codes.Length.ShouldBe(80);

        [Fact]
        public void No_two_codes_are_the_same() => Codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(Codes.Length);

        [Fact]
        public void Every_code_is_a_family_and_a_name_in_lowercase()
        {
            var shape = new Regex("^[a-z0-9]+(-[a-z0-9]+)*\\.[a-z0-9]+(-[a-z0-9]+)*$");
            foreach (var code in Codes)
            {
                shape.IsMatch(code).ShouldBeTrue(code);
            }
        }

        // A code is public and tools match on it, so the spelling of each is held here as well as chosen there.
        [Fact]
        public void The_resolver_brought_ten_and_they_are_spelled_as_its_design_spells_them()
        {
            string[] added =
            [
                DiagnosticCodes.FileTooLarge,
                DiagnosticCodes.LockMismatch,
                DiagnosticCodes.ResolveVersionMissing,
                DiagnosticCodes.ResolveYanked,
                DiagnosticCodes.ResolveYankedLocked,
                DiagnosticCodes.ResolveQuarantined,
                DiagnosticCodes.ResolveLineConflict,
                DiagnosticCodes.ResolveNameCollision,
                DiagnosticCodes.ResolveCycle,
                DiagnosticCodes.ResolveGraphTooLarge,
            ];

            added.ShouldBe(
            [
                "file.too-large",
                "lock.mismatch",
                "resolve.version-missing",
                "resolve.yanked",
                "resolve.yanked-locked",
                "resolve.quarantined",
                "resolve.line-conflict",
                "resolve.name-collision",
                "resolve.cycle",
                "resolve.graph-too-large",
            ]);
        }

        [Fact]
        public void The_commands_brought_eighteen_and_they_are_spelled_as_their_design_spells_them()
        {
            string[] added =
            [
                DiagnosticCodes.ProjectNotFound,
                DiagnosticCodes.ProjectExists,
                DiagnosticCodes.ProjectUnreadable,
                DiagnosticCodes.DependencyExists,
                DiagnosticCodes.DependencyNotFound,
                DiagnosticCodes.PackageNotFound,
                DiagnosticCodes.VersionNoneReleased,
                DiagnosticCodes.ReleaseInvalid,
                DiagnosticCodes.ArchiveInvalid,
                DiagnosticCodes.PackNothing,
                DiagnosticCodes.PackUnportable,
                DiagnosticCodes.LockOutOfDate,
                DiagnosticCodes.AttestationUnverifiable,
                DiagnosticCodes.GitHubUnreachable,
                DiagnosticCodes.GitHubRateLimited,
                DiagnosticCodes.CacheUnusable,
                DiagnosticCodes.CacheMismatch,
                DiagnosticCodes.ConfigInvalid,
            ];

            added.ShouldBe(
            [
                "project.not-found",
                "project.exists",
                "project.unreadable",
                "dependency.exists",
                "dependency.not-found",
                "package.not-found",
                "version.none-released",
                "release.invalid",
                "archive.invalid",
                "pack.nothing",
                "pack.unportable",
                "lock.out-of-date",
                "attestation.unverifiable",
                "github.unreachable",
                "github.rate-limited",
                "cache.unusable",
                "cache.mismatch",
                "config.invalid",
            ]);
        }

        [Fact]
        public void The_build_brought_fourteen_and_they_are_spelled_as_its_design_spells_them()
        {
            string[] added =
            [
                DiagnosticCodes.CompilerNotFound,
                DiagnosticCodes.CompilerUnknown,
                DiagnosticCodes.CompilerTooOld,
                DiagnosticCodes.CompilerIncomplete,
                DiagnosticCodes.ToolNotFound,
                DiagnosticCodes.NativeUnsupported,
                DiagnosticCodes.BuildCompileFailed,
                DiagnosticCodes.BuildNativeFailed,
                DiagnosticCodes.BuildArchiveFailed,
                DiagnosticCodes.BuildLinkFailed,
                DiagnosticCodes.BuiltUnusable,
                DiagnosticCodes.PackagesForeign,
                DiagnosticCodes.RunNotAnApp,
                DiagnosticCodes.RunFailed,
            ];

            added.ShouldBe(
            [
                "compiler.not-found",
                "compiler.unknown",
                "compiler.too-old",
                "compiler.incomplete",
                "tool.not-found",
                "native.unsupported",
                "build.compile-failed",
                "build.native-failed",
                "build.archive-failed",
                "build.link-failed",
                "built.unusable",
                "packages.foreign",
                "run.not-an-app",
                "run.failed",
            ]);
        }
    }
}
