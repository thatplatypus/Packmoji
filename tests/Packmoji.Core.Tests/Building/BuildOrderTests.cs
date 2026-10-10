using CsCheck;
using Packmoji.Core.Building;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Building
{
    /// <summary>
    /// The compiler reads the interface of every package that a package imports, so a package can be
    /// built only when all it depends on has been. The order has to be the same on every machine, or
    /// two builds of one lockfile would say different things.
    /// </summary>
    public sealed class BuildOrderTests
    {
        private static string[] Names(IEnumerable<LockedPackage> packages) => packages.Select(package => package.Name.ToString()).ToArray();

        [Fact]
        public void A_package_comes_after_everything_it_depends_on()
        {
            var lockfile = Locked.Of(
                "@thatplatypus/grapevine 0.3.0 > @thatplatypus/crypto 1.0.0, @thatplatypus/deflate 0.1.0",
                "@thatplatypus/crypto 1.0.0",
                "@thatplatypus/deflate 0.1.0");

            Names(BuildOrder.Of(lockfile)).ShouldBe(["@thatplatypus/crypto", "@thatplatypus/deflate", "@thatplatypus/grapevine"]);
        }

        [Fact]
        public void What_a_package_depends_on_comes_first_whatever_the_two_are_called()
        {
            var lockfile = Locked.Of("@a/first 1.0.0 > @z/last 1.0.0", "@z/last 1.0.0");

            Names(BuildOrder.Of(lockfile)).ShouldBe(["@z/last", "@a/first"]);
        }

        [Fact]
        public void Among_packages_that_are_ready_the_order_is_by_full_name()
        {
            var lockfile = Locked.Of("@b/x 1.0.0", "@a/z 1.0.0", "@a/y 1.0.0 > @c/w 1.0.0", "@c/w 1.0.0");

            // @a/y waits for @c/w, and is then the first by name of what is left.
            Names(BuildOrder.Of(lockfile)).ShouldBe(["@a/z", "@b/x", "@c/w", "@a/y"]);
        }

        [Fact]
        public void A_lockfile_that_holds_nothing_has_nothing_to_build() => BuildOrder.Of(Locked.Of()).ShouldBeEmpty();

        [Fact]
        public void What_a_package_needs_is_all_it_depends_on_directly_or_through_another_in_order_of_name()
        {
            var lockfile = Locked.Of(
                "@t/top 1.0.0 > @t/mid 1.0.0",
                "@t/mid 1.0.0 > @t/side 1.0.0, @t/low 1.0.0",
                "@t/low 1.0.0",
                "@t/side 1.0.0",
                "@t/other 1.0.0");
            LockedPackage Named(string name) => lockfile.Packages.Single(package => package.Name.Name == name);

            Names(BuildOrder.Needs(lockfile, Named("top"))).ShouldBe(["@t/low", "@t/mid", "@t/side"]);
            Names(BuildOrder.Needs(lockfile, Named("mid"))).ShouldBe(["@t/low", "@t/side"]);
            BuildOrder.Needs(lockfile, Named("low")).ShouldBeEmpty();
        }

        [Fact]
        public void What_two_packages_share_is_needed_once()
        {
            var lockfile = Locked.Of("@t/a 1.0.0 > @t/b 1.0.0, @t/c 1.0.0", "@t/b 1.0.0 > @t/d 1.0.0", "@t/c 1.0.0 > @t/d 1.0.0", "@t/d 1.0.0");

            Names(BuildOrder.Of(lockfile)).ShouldBe(["@t/d", "@t/b", "@t/c", "@t/a"]);
            Names(BuildOrder.Needs(lockfile, lockfile.Packages[0])).ShouldBe(["@t/b", "@t/c", "@t/d"]);
        }

        [Fact]
        public void A_chain_as_long_as_a_lockfile_may_be_is_ordered_without_a_stack_that_grows_with_it()
        {
            const int Length = 10_000;
            var lockfile = Locked.Of(Enumerable.Range(0, Length).Select(index => index == Length - 1 ? $"@t/p{index} 1.0.0" : $"@t/p{index} 1.0.0 > @t/p{index + 1} 1.0.0").ToArray());

            var order = BuildOrder.Of(lockfile);

            order.Count.ShouldBe(Length);
            order[0].Name.Name.ShouldBe("p9999");
            order[^1].Name.Name.ShouldBe("p0");
            BuildOrder.Needs(lockfile, lockfile.Packages[0]).Count.ShouldBe(Length - 1);
        }

        [Fact]
        public void A_lockfile_that_no_reader_would_give_is_a_fault_and_not_a_build_that_never_ends()
        {
            // A reader refuses both, so neither can come from a file: a circle, and a package that is not held.
            Should.Throw<InvalidOperationException>(() => BuildOrder.Of(Locked.Of("@t/a 1.0.0 > @t/b 1.0.0", "@t/b 1.0.0 > @t/a 1.0.0")));
            Should.Throw<InvalidOperationException>(() => BuildOrder.Of(Locked.Of("@t/a 1.0.0 > @t/gone 1.0.0")));
        }

        // Up to eight packages. Each may depend only on packages that come after it here, so there is
        // no circle, and each is given a name by lot, so that the order of names says nothing of who
        // depends on whom.
        private static readonly Gen<(int[] Masks, int Seed)> Graphs = Gen.Select(Gen.Int[0, 255].Array[1, 8], Gen.Int[0, 1_000_000]);

        private static Lockfile Made((int[] Masks, int Seed) graph, bool shuffled)
        {
            var count = graph.Masks.Length;
            var names = Enumerable.Range(0, count).Select(index => $"@g/p{index}").ToArray();
            new Random(graph.Seed).Shuffle(names);
            var lines = new List<string>();
            for (var index = 0; index < count; index++)
            {
                var needs = Enumerable.Range(index + 1, count - index - 1).Where(later => (graph.Masks[index] >> (later - index - 1) & 1) == 1).Select(later => $"{names[later]} 1.0.0").ToArray();
                if (shuffled)
                {
                    new Random(graph.Seed + index).Shuffle(needs);
                }

                lines.Add(needs.Length == 0 ? $"{names[index]} 1.0.0" : $"{names[index]} 1.0.0 > {string.Join(", ", needs)}");
            }

            var written = lines.ToArray();
            if (shuffled)
            {
                new Random(graph.Seed + 99).Shuffle(written);
            }

            return Locked.Of(written);
        }

        [Fact]
        public void In_any_lockfile_every_package_comes_after_all_it_depends_on_and_none_is_left_out()
        {
            Graphs.Sample(
                graph =>
                {
                    var lockfile = Made(graph, shuffled: false);
                    var order = Names(BuildOrder.Of(lockfile));

                    order.Order(StringComparer.Ordinal).ShouldBe(Names(lockfile.Packages).Order(StringComparer.Ordinal));
                    foreach (var package in lockfile.Packages)
                    {
                        foreach (var needed in package.Dependencies)
                        {
                            Array.IndexOf(order, needed.Name.ToString()).ShouldBeLessThan(Array.IndexOf(order, package.Name.ToString()));
                        }
                    }
                },
                iter: 1_000);
        }

        [Fact]
        public void The_order_and_what_each_package_needs_do_not_depend_on_how_the_lockfile_was_written()
        {
            Graphs.Sample(
                graph =>
                {
                    var written = Made(graph, shuffled: false);
                    var reordered = Made(graph, shuffled: true);

                    Names(BuildOrder.Of(reordered)).ShouldBe(Names(BuildOrder.Of(written)));
                    foreach (var package in written.Packages)
                    {
                        var same = reordered.Packages.Single(other => other.Name == package.Name);
                        Names(BuildOrder.Needs(reordered, same)).ShouldBe(Names(BuildOrder.Needs(written, package)));
                    }
                },
                iter: 1_000);
        }

        [Fact]
        public void What_a_package_needs_is_exactly_what_can_be_reached_from_it()
        {
            Graphs.Sample(
                graph =>
                {
                    var lockfile = Made(graph, shuffled: false);
                    foreach (var package in lockfile.Packages)
                    {
                        // Worked out another way: add what is depended on until nothing is added.
                        var reached = new SortedSet<string>(package.Dependencies.Select(needed => needed.Name.ToString()), StringComparer.Ordinal);
                        for (var before = -1; before != reached.Count;)
                        {
                            before = reached.Count;
                            foreach (var other in lockfile.Packages.Where(other => reached.Contains(other.Name.ToString())).ToList())
                            {
                                reached.UnionWith(other.Dependencies.Select(needed => needed.Name.ToString()));
                            }
                        }

                        Names(BuildOrder.Needs(lockfile, package)).ShouldBe(reached.ToArray());
                    }
                },
                iter: 1_000);
        }
    }
}
