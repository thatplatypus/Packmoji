using Packmoji.Core.Building;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Building
{
    /// <summary>
    /// A built package is kept once for the whole machine and used by every project whose key for it
    /// is the same. So the key has to change with everything that changes what is built, or a project
    /// is given something that was built for another.
    /// </summary>
    public sealed class BuildKeyTests
    {
        private static readonly Sha256Digest Compiler = Sample.Sha('c');
        private static readonly Sha256Digest Archive = Sample.Sha('a');

        private static Dictionary<PackageName, Sha256Digest> Needs(params (string Name, char Key)[] dependencies) =>
            dependencies.ToDictionary(dependency => Sample.Name(dependency.Name), dependency => Sample.Sha(dependency.Key));

        private static Dictionary<NativeLanguage, Sha256Digest> Native(params (NativeLanguage Language, char Says)[] compilers) =>
            compilers.ToDictionary(compiler => compiler.Language, compiler => Sample.Sha(compiler.Says));

        private static Sha256Digest Plain() => BuildKey.Of(Compiler, optimized: false, Archive, Needs(), Native());

        [Fact]
        public void The_key_of_a_package_that_stands_alone_is_the_digest_of_four_lines()
        {
            var lines =
                "packmoji build 1\n" +
                $"compiler {new string('c', 64)}\n" +
                "optimized no\n" +
                $"package {new string('a', 64)}\n";

            Plain().ShouldBe(Sample.DigestOf(lines));
        }

        [Fact]
        public void What_it_depends_on_and_what_compiles_its_native_code_each_add_a_line_in_one_order()
        {
            var lines =
                "packmoji build 1\n" +
                $"compiler {new string('c', 64)}\n" +
                "optimized yes\n" +
                $"package {new string('a', 64)}\n" +
                $"dependency @one/zebra {new string('1', 64)}\n" +
                $"dependency @two/apple {new string('2', 64)}\n" +
                $"native c {new string('3', 64)}\n" +
                $"native c++ {new string('4', 64)}\n";

            var key = BuildKey.Of(
                Compiler,
                optimized: true,
                Archive,
                Needs(("@two/apple", '2'), ("@one/zebra", '1')),
                Native((NativeLanguage.Cpp, '4'), (NativeLanguage.C, '3')));

            key.ShouldBe(Sample.DigestOf(lines));
        }

        [Fact]
        public void Another_compiler_is_another_key() =>
            BuildKey.Of(Sample.Sha('d'), optimized: false, Archive, Needs(), Native()).ShouldNotBe(Plain());

        [Fact]
        public void Optimizing_is_another_key() =>
            BuildKey.Of(Compiler, optimized: true, Archive, Needs(), Native()).ShouldNotBe(Plain());

        [Fact]
        public void Another_archive_is_another_key() =>
            BuildKey.Of(Compiler, optimized: false, Sample.Sha('b'), Needs(), Native()).ShouldNotBe(Plain());

        [Fact]
        public void A_dependency_that_was_built_another_way_is_another_key()
        {
            var one = BuildKey.Of(Compiler, optimized: false, Archive, Needs(("@thatplatypus/crypto", '1')), Native());
            var other = BuildKey.Of(Compiler, optimized: false, Archive, Needs(("@thatplatypus/crypto", '2')), Native());

            other.ShouldNotBe(one);
            one.ShouldNotBe(Plain());
        }

        [Fact]
        public void The_same_key_under_another_dependencys_name_is_another_key()
        {
            var one = BuildKey.Of(Compiler, optimized: false, Archive, Needs(("@thatplatypus/crypto", '1')), Native());
            var other = BuildKey.Of(Compiler, optimized: false, Archive, Needs(("@thatplatypus/deflate", '1')), Native());

            other.ShouldNotBe(one);
        }

        [Fact]
        public void Another_c_or_cpp_compiler_is_another_key_and_so_is_the_same_one_for_the_other_language()
        {
            var cpp = BuildKey.Of(Compiler, optimized: false, Archive, Needs(), Native((NativeLanguage.Cpp, '1')));

            cpp.ShouldNotBe(Plain());
            BuildKey.Of(Compiler, optimized: false, Archive, Needs(), Native((NativeLanguage.Cpp, '2'))).ShouldNotBe(cpp);
            BuildKey.Of(Compiler, optimized: false, Archive, Needs(), Native((NativeLanguage.C, '1'))).ShouldNotBe(cpp);
        }
    }
}
