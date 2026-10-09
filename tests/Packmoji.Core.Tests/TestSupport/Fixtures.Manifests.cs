namespace Packmoji.Core.Tests.TestSupport
{
    /// <summary>Files in their canonical form: what the writer must produce, byte for byte.</summary>
    internal static partial class Fixtures
    {
        public static readonly string MinimalManifest = """
            {
              "package": {
                "name": "@thatplatypus/crypto",
                "version": "1.0.0",
                "kind": "library",
                "emojicode": ">=1.0.0-beta.2"
              }
            }

            """.ReplaceLineEndings("\n");

        public static readonly string FullManifest = """
            {
              "package": {
                "name": "@thatplatypus/grapevine",
                "version": "0.3.0",
                "kind": "library",
                "emojicode": ">=1.0.0-beta.2",
                "description": "HTTP framework for Emojicode",
                "license": "MIT",
                "repository": "github.com/thatplatypus/grapevine"
              },
              "dependencies": {
                "@thatplatypus/crypto": "1.0",
                "@thatplatypus/deflate": "0.1"
              },
              "devDependencies": {
                "@thatplatypus/testkit": "0.1"
              },
              "build": {
                "entry": "src/lib.🍇",
                "sources": [
                  "src/**/*.emojic",
                  "src/**/*.🍇"
                ]
              },
              "native": {
                "sources": [
                  "native/*.cpp"
                ],
                "includeDirs": [
                  "native/include"
                ],
                "link": [
                  "pthread"
                ]
              },
              "policy": {
                "requireAttestation": false
              }
            }

            """.ReplaceLineEndings("\n");

        // Laid out as Grapevine is today: sources beside the manifest, and an app-less package with a native shim.
        public static readonly string FlatManifest = """
            {
              "package": {
                "name": "@thatplatypus/grapevine",
                "version": "0.3.0",
                "kind": "library",
                "emojicode": ">=1.0.0-beta.2"
              },
              "dependencies": {
                "@thatplatypus/crypto": "1.0",
                "@thatplatypus/deflate": "0.1"
              },
              "build": {
                "entry": "grapevine.🍇",
                "sources": [
                  "*.🍇"
                ]
              },
              "native": {
                "sources": [
                  "native/*.cpp"
                ],
                "link": [
                  "pthread"
                ]
              }
            }

            """.ReplaceLineEndings("\n");
    }
}
