using Packmoji.Core.Building;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Building
{
    /// <summary>A native file is C or C++ by the end of its name, and a file that is neither is not guessed at.</summary>
    public sealed class NativeSourceTests
    {
        [Theory]
        [InlineData("native/net.cpp")]
        [InlineData("native/net.cc")]
        [InlineData("native/net.cxx")]
        [InlineData("a.b.cpp")]
        public void These_are_cpp(string path) => NativeSource.LanguageOf(path).ShouldBe(NativeLanguage.Cpp);

        [Theory]
        [InlineData("native/clock.c")]
        [InlineData("c.cpp.c")]
        public void These_are_c(string path) => NativeSource.LanguageOf(path).ShouldBe(NativeLanguage.C);

        [Theory]
        [InlineData("native/net.h")]
        [InlineData("native/net.hpp")]
        [InlineData("native/net.CPP")]
        [InlineData("native/net.C")]
        [InlineData("native/net.m")]
        [InlineData("native/cpp")]
        [InlineData("native/c")]
        [InlineData("native/net.cpp.txt")]
        [InlineData("src/lib.🍇")]
        public void These_are_neither(string path) => NativeSource.LanguageOf(path).ShouldBeNull();
    }
}
