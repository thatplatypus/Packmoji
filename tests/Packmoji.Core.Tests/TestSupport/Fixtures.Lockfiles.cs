namespace Packmoji.Core.Tests.TestSupport
{
    internal static partial class Fixtures
    {
        public static readonly string EmptyLockfile = """
            {
              "version": 1,
              "root": {
                "dependencies": [],
                "devDependencies": []
              },
              "packages": []
            }

            """.ReplaceLineEndings("\n");

        public static readonly string Lockfile = """
            {
              "version": 1,
              "root": {
                "dependencies": [
                  "@thatplatypus/grapevine@0.3"
                ],
                "devDependencies": []
              },
              "packages": [
                {
                  "name": "@thatplatypus/crypto",
                  "version": "1.0.0",
                  "source": "github.com/thatplatypus/grapevine",
                  "releaseTag": "crypto-v1.0.0",
                  "asset": "crypto-1.0.0.pmj.tar.gz",
                  "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
                  "verified": "attestation",
                  "dependencies": []
                },
                {
                  "name": "@thatplatypus/deflate",
                  "version": "0.1.0",
                  "source": "github.com/thatplatypus/grapevine",
                  "releaseTag": "deflate-v0.1.0",
                  "asset": "deflate-0.1.0.pmj.tar.gz",
                  "sha256": "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
                  "verified": "attestation",
                  "dependencies": []
                },
                {
                  "name": "@thatplatypus/grapevine",
                  "version": "0.3.0",
                  "source": "github.com/thatplatypus/grapevine",
                  "releaseTag": "grapevine-v0.3.0",
                  "asset": "grapevine-0.3.0.pmj.tar.gz",
                  "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                  "verified": "checksum",
                  "dependencies": [
                    "@thatplatypus/crypto@1.0.0",
                    "@thatplatypus/deflate@0.1.0"
                  ]
                }
              ]
            }

            """.ReplaceLineEndings("\n");
    }
}
