using CsCheck;
using Packmoji.Core.Graphs;
using Packmoji.Core.Identity;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Graphs
{
    public sealed class CycleFinderTests
    {
        private static PackageName Package(string bare) => Sample.Name("@thatplatypus/" + bare);

        /// <summary>
        /// Finds the circles of a graph written one package to a string, as in <c>"a: b c"</c> for an
        /// <c>a</c> that depends on <c>b</c> and on <c>c</c>, and gives each circle as its bare names.
        /// </summary>
        private static string[] Circles(params string[] packages)
        {
            var graph = packages
                .Select(package => package.Split(':'))
                .ToDictionary(
                    parts => Package(parts[0]),
                    parts => (IReadOnlyList<PackageName>)parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Package).ToList());

            return CycleFinder.Find(graph).Select(circle => string.Join(" ", circle.Select(name => name.Name))).ToArray();
        }

        [Fact]
        public void Packages_that_depend_on_nothing_have_no_circle()
        {
            Circles().ShouldBeEmpty();
            Circles("a:", "b:").ShouldBeEmpty();
        }

        [Fact]
        public void A_line_of_packages_and_two_that_share_a_dependency_have_none()
        {
            Circles("a: b", "b: c", "c:").ShouldBeEmpty();
            Circles("a: b c", "b: d", "c: d", "d:").ShouldBeEmpty();
        }

        [Fact]
        public void Two_packages_that_need_each_other_are_a_circle() => Circles("a: b", "b: a").ShouldBe(["a b"]);

        [Fact]
        public void A_package_that_needs_itself_is_a_circle_of_one() => Circles("a: a", "b: a").ShouldBe(["a"]);

        [Fact]
        public void A_circle_begins_at_its_first_package_by_name_however_the_graph_was_given()
        {
            Circles("a: b", "b: c", "c: a").ShouldBe(["a b c"]);
            Circles("c: a", "b: c", "a: b").ShouldBe(["a b c"]);
            Circles("b: c", "a: b", "c: a").ShouldBe(["a b c"]);
        }

        [Fact]
        public void Two_circles_that_share_nothing_are_both_found_in_the_order_of_their_names()
        {
            Circles("x: y", "y: x", "b: a", "a: b").ShouldBe(["a b", "x y"]);
        }

        [Fact]
        public void Packages_that_all_lead_to_one_another_are_shown_once_by_their_shortest_circle()
        {
            Circles("a: b", "b: c a", "c: a").ShouldBe(["a b"]);
        }

        [Fact]
        public void Of_two_circles_equally_short_the_one_that_sorts_first_is_shown()
        {
            Circles("a: c b", "b: a", "c: a").ShouldBe(["a b"]);
            Circles("a: d", "b: a", "c: a", "d: c b").ShouldBe(["a d b"]);
        }

        [Fact]
        public void What_only_leads_into_a_circle_or_out_of_it_is_no_part_of_it()
        {
            Circles("top: a", "a: b", "b: a leaf", "leaf:").ShouldBe(["a b"]);
        }

        [Fact]
        public void A_name_that_is_not_a_package_of_the_graph_is_passed_over()
        {
            Circles("a: ghost b", "b: ghost").ShouldBeEmpty();
            Circles("a: ghost b", "b: a ghost").ShouldBe(["a b"]);
        }

        [Fact]
        public void A_package_named_twice_by_another_is_one_dependency() => Circles("a: b b", "b: a a").ShouldBe(["a b"]);

        [Fact]
        public void A_circle_of_fifty_thousand_packages_is_found_and_the_stack_does_not_run_out()
        {
            const int count = 50_000;
            var names = Enumerable.Range(0, count).Select(number => Package($"p{number}")).ToList();
            var graph = Enumerable.Range(0, count).ToDictionary(
                number => names[number],
                number => (IReadOnlyList<PackageName>)[names[(number + 1) % count]]);

            var circle = CycleFinder.Find(graph).ShouldHaveSingleItem();

            circle.Count.ShouldBe(count);
            circle[0].ShouldBe(names[0]);
            circle[1].ShouldBe(names[1]);
            circle[^1].ShouldBe(names[count - 1]);
        }

        [Fact]
        public void A_line_of_fifty_thousand_packages_has_no_circle_and_the_stack_does_not_run_out()
        {
            const int count = 50_000;
            var names = Enumerable.Range(0, count).Select(number => Package($"p{number}")).ToList();
            var graph = Enumerable.Range(0, count).ToDictionary(
                number => names[number],
                number => (IReadOnlyList<PackageName>)(number + 1 < count ? [names[number + 1]] : []));

            CycleFinder.Find(graph).ShouldBeEmpty();
        }

        private sealed record Edge(int From, int To);

        // Up to six packages named p0 to p5, so that their order by name is their order by number.
        private static readonly Gen<(int Count, Edge[] Edges)> Graphs =
            from count in Gen.Int[1, 6]
            from edges in Gen.Select(Gen.Int[0, count - 1], Gen.Int[0, count - 1], (tail, head) => new Edge(tail, head)).Array[0, 12]
            select (count, edges);

        /// <summary>
        /// Holds the finder to an answer worked out the slow and obvious way: which packages can reach
        /// which, by trying every package as a step between every two.
        /// </summary>
        [Fact]
        public void Whatever_the_graph_each_group_of_packages_that_reach_one_another_gives_its_one_shortest_circle()
        {
            Graphs.Sample(
                sample =>
                {
                    var (count, edges) = sample;
                    var names = Enumerable.Range(0, count).Select(number => Package($"p{number}")).ToList();
                    var graph = Enumerable.Range(0, count).ToDictionary(
                        number => names[number],
                        number => (IReadOnlyList<PackageName>)edges.Where(edge => edge.From == number).Select(edge => names[edge.To]).ToList());

                    // steps[i, j] is the fewest dependencies that lead from i to j, taking at least one.
                    const int none = 1_000;
                    var steps = new int[count, count];
                    for (var i = 0; i < count; i++)
                    {
                        for (var j = 0; j < count; j++)
                        {
                            steps[i, j] = edges.Any(edge => edge.From == i && edge.To == j) ? 1 : none;
                        }
                    }

                    for (var through = 0; through < count; through++)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            for (var j = 0; j < count; j++)
                            {
                                steps[i, j] = Math.Min(steps[i, j], steps[i, through] + steps[through, j]);
                            }
                        }
                    }

                    // A package begins a circle when it reaches itself and no package before it shares its group.
                    var expected = Enumerable.Range(0, count)
                        .Where(i => steps[i, i] < none && !Enumerable.Range(0, i).Any(j => steps[i, j] < none && steps[j, i] < none))
                        .ToList();

                    var circles = CycleFinder.Find(graph);

                    circles.Select(circle => names.IndexOf(circle[0])).ShouldBe(expected);
                    foreach (var circle in circles)
                    {
                        var numbers = circle.Select(name => names.IndexOf(name)).ToList();
                        numbers.Distinct().Count().ShouldBe(numbers.Count);
                        numbers.Count.ShouldBe(steps[numbers[0], numbers[0]]);
                        for (var at = 0; at < numbers.Count; at++)
                        {
                            var next = numbers[(at + 1) % numbers.Count];
                            edges.ShouldContain(edge => edge.From == numbers[at] && edge.To == next);
                        }
                    }
                },
                iter: 2_000);
        }
    }
}
