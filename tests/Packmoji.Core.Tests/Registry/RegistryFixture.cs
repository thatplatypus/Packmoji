namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// The text of a registry's file, built a key at a time, so that a test can change one key, leave
    /// one out or add one, and say nothing of the rest. A value is written as its JSON.
    /// </summary>
    internal sealed class RegistryFixture
    {
        public const string Sha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        public const string OtherSha = "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824";

        private readonly List<(string Key, string Value)> _members;

        private RegistryFixture(IEnumerable<(string Key, string Value)> members) => _members = members.ToList();

        /// <summary>One version, as a package's file holds it: active, with nothing it needs.</summary>
        public static RegistryFixture Version(string version = "1.0.0") => new(
        [
            ("version", $"\"{version}\""),
            ("status", "\"active\""),
            ("source", "\"github.com/thatplatypus/grapevine\""),
            ("sha256", $"\"{Sha}\""),
            ("size", "1024"),
            ("verified", "\"checksum\""),
            ("immutable", "false"),
            ("published", "\"2026-10-11T15:02:11Z\""),
            ("indexed", "\"2026-10-11T15:17:40Z\""),
            ("dependencies", "{}"),
        ]);

        /// <summary>The file of <c>@thatplatypus/crypto</c>, with these versions.</summary>
        public static RegistryFixture Package(params RegistryFixture[] versions) => Package(string.Join(",\n", versions.Select(version => version.ToString())));

        /// <summary>The same, with the text of its versions as it is given.</summary>
        public static RegistryFixture Package(string versions) => new(
        [
            ("version", "1"),
            ("name", "\"@thatplatypus/crypto\""),
            ("versions", $"[\n{versions}\n]"),
        ]);

        /// <summary>One refused release, as a repository's file holds it.</summary>
        public static RegistryFixture Refusal(string tag = "crypto-v0.2.0") => new(
        [
            ("tag", $"\"{tag}\""),
            ("sha256", $"\"{Sha}\""),
            ("seen", "\"2026-10-12T08:17:03Z\""),
            ("code", "\"release.invalid\""),
            ("why", "\"the manifest in its archive gives the version 0.1.0\""),
        ]);

        /// <summary>The file of what was refused in <c>github.com/thatplatypus/grapevine</c>.</summary>
        public static RegistryFixture Refused(params RegistryFixture[] releases) => new(
        [
            ("version", "1"),
            ("repository", "\"github.com/thatplatypus/grapevine\""),
            ("releases", $"[\n{string.Join(",\n", releases.Select(release => release.ToString()))}\n]"),
        ]);

        /// <summary>The same, with one key given another value, or added when it is not there.</summary>
        public RegistryFixture With(string key, string value)
        {
            var members = _members.ToList();
            var index = members.FindIndex(member => member.Key == key);
            if (index < 0)
            {
                members.Add((key, value));
            }
            else
            {
                members[index] = (key, value);
            }

            return new RegistryFixture(members);
        }

        public RegistryFixture Without(string key) => new(_members.Where(member => member.Key != key));

        /// <summary>The same, with a mark before the brace that opens it: where a missing key is reported.</summary>
        public string Marked() => "§" + ToString();

        public override string ToString() => "{\n" + string.Join(",\n", _members.Select(member => $"\"{member.Key}\": {member.Value}")) + "\n}";
    }
}
