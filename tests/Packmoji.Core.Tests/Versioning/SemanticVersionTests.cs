using Packmoji.Core.Diagnostics;
using Packmoji.Core.Tests.TestSupport;
using Packmoji.Core.Versioning;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Versioning
{
    public sealed class SemanticVersionTests
    {
        // From the valid examples published with the SemVer 2.0.0 specification, those without build metadata.
        [Theory]
        [InlineData("0.0.4", 0, 0, 4, "")]
        [InlineData("1.2.3", 1, 2, 3, "")]
        [InlineData("10.20.30", 10, 20, 30, "")]
        [InlineData("1.0.0-alpha", 1, 0, 0, "alpha")]
        [InlineData("1.0.0-alpha.beta.1", 1, 0, 0, "alpha.beta.1")]
        [InlineData("1.0.0-alpha0.valid", 1, 0, 0, "alpha0.valid")]
        [InlineData("1.0.0-alpha.0valid", 1, 0, 0, "alpha.0valid")]
        [InlineData("10.2.3-DEV-SNAPSHOT", 10, 2, 3, "DEV-SNAPSHOT")]
        [InlineData("1.2.3-SNAPSHOT-123", 1, 2, 3, "SNAPSHOT-123")]
        [InlineData("2.0.1-alpha.1227", 2, 0, 1, "alpha.1227")]
        [InlineData("1.2.3----RC-SNAPSHOT.12.9.1--.12", 1, 2, 3, "---RC-SNAPSHOT.12.9.1--.12")]
        [InlineData("1.0.0-0A.is.legal", 1, 0, 0, "0A.is.legal")]
        [InlineData("1.0.0-0", 1, 0, 0, "0")]
        [InlineData("2147483647.2147483647.2147483647", int.MaxValue, int.MaxValue, int.MaxValue, "")]
        public void A_valid_version_is_parsed(string text, int major, int minor, int patch, string prerelease)
        {
            SemanticVersion.TryParse(text, out var version, out var error).ShouldBeTrue();

            error.ShouldBeNull();
            version.ShouldNotBeNull();
            version.Major.ShouldBe(major);
            version.Minor.ShouldBe(minor);
            version.Patch.ShouldBe(patch);
            version.Prerelease.ShouldBe(prerelease);
            version.IsPrerelease.ShouldBe(prerelease.Length > 0);
            version.ToString().ShouldBe(text);
        }

        // From the invalid examples published with the specification, and a few of our own.
        [Theory]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("1.2")]
        [InlineData("1.2.3.4")]
        [InlineData("1.2.3-0123")]
        [InlineData("1.2.3-0123.0123")]
        [InlineData("+invalid")]
        [InlineData("-invalid")]
        [InlineData("alpha")]
        [InlineData("alpha.beta")]
        [InlineData("alpha.1")]
        [InlineData("1.0.0-alpha_beta")]
        [InlineData("1.0.0-alpha..")]
        [InlineData("1.0.0-alpha..1")]
        [InlineData("1.0.0-alpha.")]
        [InlineData("1.0.0-")]
        [InlineData("01.1.1")]
        [InlineData("1.01.1")]
        [InlineData("1.1.01")]
        [InlineData("1.2.3.DEV")]
        [InlineData("1.2-SNAPSHOT")]
        [InlineData("-1.0.3-gamma")]
        [InlineData("1..3")]
        [InlineData("1.2.x")]
        [InlineData("v1.2.3")]
        [InlineData("1.2.3 ")]
        [InlineData(" 1.2.3")]
        [InlineData("1.0.0-béta")]
        [InlineData("2147483648.0.0")]
        [InlineData("99999999999999999999999.999999999999999999.99999999999999999")]
        [InlineData("١.٢.٣")] // Arabic-Indic digits, which char.IsDigit accepts
        [InlineData("１.２.３")] // fullwidth digits
        public void An_invalid_version_is_refused(string text)
        {
            SemanticVersion.TryParse(text, out var version, out var error).ShouldBeFalse();

            version.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.VersionInvalid);
        }

        [Theory]
        [InlineData("1.1.2+meta", "\"1.1.2\"")]
        [InlineData("1.1.2-prerelease+meta", "\"1.1.2-prerelease\"")]
        [InlineData("1.0.0-alpha+beta", "\"1.0.0-alpha\"")]
        [InlineData("2.0.0+build.1848", "\"2.0.0\"")]
        [InlineData("1.0.0+0.build.1-rc.10000aaa-kk-0.1", "\"1.0.0\"")]
        public void Build_metadata_is_refused_under_its_own_code(string text, string expectedInFix)
        {
            SemanticVersion.TryParse(text, out var version, out var error).ShouldBeFalse();

            version.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.VersionBuildMetadata);
            error!.Fix.ShouldContain(expectedInFix);
        }

        [Theory]
        [InlineData("v1.2.3", "\"1.2.3\"")]
        [InlineData("1.2", "\"1.2.0\"")]
        public void The_fix_names_the_version_that_was_meant(string text, string expectedInFix)
        {
            SemanticVersion.TryParse(text, out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Fix.ShouldContain(expectedInFix);
        }

        [Fact]
        public void A_version_may_be_64_characters_and_no_more()
        {
            SemanticVersion.TryParse("1.0.0-" + new string('a', 58), out _, out _).ShouldBeTrue();
            SemanticVersion.TryParse("1.0.0-" + new string('a', 59), out _, out var error).ShouldBeFalse();
            error.ShouldBeComplete().Reason.ShouldContain("64");
        }

        [Fact]
        public void Versions_are_ordered_as_the_specification_orders_them()
        {
            string[] ascending =
            [
                "0.0.1", "0.1.0", "0.1.1",
                "1.0.0-0", "1.0.0-1", "1.0.0-2", "1.0.0-10",
                "1.0.0-Beta", "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1",
                "1.0.0", "1.0.1", "1.1.0", "2.0.0", "2.1.0", "2.1.1", "10.0.0",
            ];
            var versions = ascending.Select(Sample.Version).ToList();

            for (var i = 0; i < versions.Count; i++)
            {
                for (var j = 0; j < versions.Count; j++)
                {
                    Math.Sign(versions[i].CompareTo(versions[j])).ShouldBe(Math.Sign(i.CompareTo(j)), $"{versions[i]} against {versions[j]}");
                }
            }
        }

        [Fact]
        public void A_long_numeric_identifier_is_compared_by_value_without_overflow()
        {
            var smaller = Sample.Version("1.0.0-99999999999999999999");
            var larger = Sample.Version("1.0.0-100000000000000000000");

            (smaller < larger).ShouldBeTrue();
            (larger > smaller).ShouldBeTrue();
        }

        [Fact]
        public void The_operators_agree_with_the_order()
        {
            var one = Sample.Version("1.0.0");
            var two = Sample.Version("2.0.0");

            (one < two).ShouldBeTrue();
            (one <= two).ShouldBeTrue();
            (two > one).ShouldBeTrue();
            (two >= one).ShouldBeTrue();
            (one >= Sample.Version("1.0.0")).ShouldBeTrue();
            (one <= Sample.Version("1.0.0")).ShouldBeTrue();
            (one > two).ShouldBeFalse();
        }

        [Fact]
        public void Two_parses_of_one_text_are_equal_and_compare_as_zero()
        {
            var left = Sample.Version("1.2.3-beta.1");
            var right = Sample.Version("1.2.3-beta.1");

            left.ShouldBe(right);
            left.GetHashCode().ShouldBe(right.GetHashCode());
            left.CompareTo(right).ShouldBe(0);
            left.ShouldNotBe(Sample.Version("1.2.3-beta.2"));
            left.CompareTo(null).ShouldBeGreaterThan(0);
        }
    }
}
