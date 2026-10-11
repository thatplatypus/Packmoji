using System.Net;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Registry;

namespace Packmoji.GitHub
{
    /// <summary>
    /// A registry at an address: each of its files is asked for by its own address, as a browser
    /// would ask for it, and nothing else is ever asked.
    /// </summary>
    /// <remarks>
    /// No token is sent. A registry is read by anyone, and a token that is never sent cannot reach
    /// a registry that is not the one it was meant for.
    /// </remarks>
    public sealed class HttpRegistryFiles : IRegistryFiles
    {
        private const string AskGitHubItself = "to ask GitHub itself in the meantime, run the command again with --direct";

        private readonly HttpClient _http;
        private readonly string _userAgent;
        private readonly TimeSpan _quiet;

        /// <param name="address">The address the registry's <c>v1</c> is under. One that <see cref="GitHubReleaseHost.CanBeAddress"/> refuses is an <see cref="ArgumentException"/>.</param>
        /// <param name="userAgent">What pmj calls itself.</param>
        /// <param name="quiet">How long an answer may send nothing before it is given up. A minute, when it is not said.</param>
        public HttpRegistryFiles(HttpClient http, Uri address, string userAgent = "pmj", TimeSpan? quiet = null)
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentNullException.ThrowIfNull(address);

            // What was given is not in the message: an address that was mistyped may hold a password.
            if (!GitHubReleaseHost.CanBeAddress(address))
            {
                throw new ArgumentException("An address for a registry begins with http:// or https://, and holds no name, password, query or fragment.", nameof(address));
            }

            _http = http;
            Address = address.AbsoluteUri.TrimEnd('/');
            _userAgent = userAgent;
            _quiet = quiet ?? TimeSpan.FromMinutes(1);
        }

        public string Address { get; }

        public async Task<ReadOnlyMemory<byte>?> ReadAsync(string path, int maxBytes, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(path);
            var file = $"{Address}/{path}";
            using var response = await Answers.SendAsync(_http, new HttpRequestMessage(HttpMethod.Get, file), _userAgent, why => Failed(file, why), cancellationToken);

            // Only this says that the registry has no such file. Any other answer is the registry
            // not having been read, which must never be taken for a package that is not in it.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw Unreachable($"the request for {file} was answered with status {(int)response.StatusCode}");
            }

            if (response.Content.Headers.ContentLength > maxBytes || await Answers.ReadAsync(response, maxBytes, _quiet, why => Failed(file, why), cancellationToken) is not { } bytes)
            {
                throw new PackageSourceException(new Diagnostic(
                    DiagnosticCodes.RegistryInvalid,
                    "A file of the registry is too large.",
                    $"{file} is more than {maxBytes} bytes, which is the most a file of a registry may be",
                    $"tell whoever keeps the registry; {AskGitHubItself}"));
            }

            return bytes;
        }

        private static PackageSourceException Failed(string file, string why) => Unreachable($"the request for {file} failed: {why}");

        private static PackageSourceException Unreachable(string reason) =>
            new(new Diagnostic(
                DiagnosticCodes.RegistryUnreachable,
                "The registry could not be read.",
                reason,
                $"check the network and the address of the registry, and try again; {AskGitHubItself}"));
    }
}
