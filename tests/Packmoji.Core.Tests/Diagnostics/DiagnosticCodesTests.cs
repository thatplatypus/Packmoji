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
        public void There_are_the_38_codes_of_the_design() => Codes.Length.ShouldBe(38);

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
    }
}
