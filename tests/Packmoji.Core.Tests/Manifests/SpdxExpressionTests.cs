using Packmoji.Core.Diagnostics;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Manifests
{
    public sealed class SpdxExpressionTests
    {
        [Theory]
        [InlineData("MIT")]
        [InlineData("Apache-2.0")]
        [InlineData("GPL-2.0+")]
        [InlineData("MIT OR Apache-2.0")]
        [InlineData("MIT AND Apache-2.0")]
        [InlineData("MIT AND Apache-2.0 OR BSD-3-Clause")]
        [InlineData("GPL-2.0-only WITH Classpath-exception-2.0")]
        [InlineData("(MIT OR Apache-2.0) AND BSD-3-Clause")]
        [InlineData("MIT OR (Apache-2.0 AND BSD-3-Clause)")]
        [InlineData("((MIT))")]
        [InlineData("LicenseRef-Proprietary")]
        [InlineData("DocumentRef-spdx-tool-1.2:LicenseRef-MIT-Style-2")]
        public void An_spdx_expression_is_accepted(string text)
        {
            SpdxExpression.TryParse(text, out var expression, out var error).ShouldBeTrue();

            error.ShouldBeNull();
            expression.ShouldNotBeNull().Text.ShouldBe(text);
            expression.ToString().ShouldBe(text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("see LICENSE file")]
        [InlineData("MIT or Apache-2.0")]
        [InlineData("MIT OR")]
        [InlineData("OR MIT")]
        [InlineData("MIT AND")]
        [InlineData("AND")]
        [InlineData("(MIT")]
        [InlineData("MIT)")]
        [InlineData("()")]
        [InlineData("MIT  OR Apache-2.0")]
        [InlineData(" MIT")]
        [InlineData("MIT ")]
        [InlineData("MIT, Apache-2.0")]
        [InlineData("MIT/Apache-2.0")]
        [InlineData("MIT WITH")]
        [InlineData("WITH Classpath-exception-2.0")]
        [InlineData("(MIT OR Apache-2.0) WITH Classpath-exception-2.0")]
        [InlineData("MIT+ +")]
        [InlineData("DocumentRef-x:MIT")]
        [InlineData("MIT\nApache-2.0")]
        public void Anything_else_is_refused(string text)
        {
            SpdxExpression.TryParse(text, out var expression, out var error).ShouldBeFalse();

            expression.ShouldBeNull();
            error.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.LicenseInvalid);
        }

        [Fact]
        public void An_expression_may_be_100_characters_and_no_more()
        {
            SpdxExpression.TryParse(new string('A', 100), out _, out _).ShouldBeTrue();
            SpdxExpression.TryParse(new string('A', 101), out _, out _).ShouldBeFalse();
        }

        [Fact]
        public void Operators_in_lowercase_get_a_fix_that_says_to_use_capitals()
        {
            SpdxExpression.TryParse("MIT or Apache-2.0", out _, out var error).ShouldBeFalse();

            error.ShouldBeComplete().Fix.ShouldContain("capitals");
        }

        [Fact]
        public void Two_expressions_of_one_text_are_equal() => Sample.License("MIT").ShouldBe(Sample.License("MIT"));
    }
}
