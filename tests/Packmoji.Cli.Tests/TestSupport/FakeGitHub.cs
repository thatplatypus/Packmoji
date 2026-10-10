using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Packmoji.Cli.Tests.TestSupport
{
    /// <summary>
    /// GitHub as pmj sees it over HTTP, held in memory: releases that can be downloaded by their
    /// address, and the list of a repository's releases. It answers as GitHub was seen to answer on
    /// 2026-10-09, and it keeps every request, so that a test can say what was asked and with what.
    /// </summary>
    internal sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly List<Release> _releases = [];
        private readonly HashSet<string> _repositories = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every request made, in order.</summary>
        public List<Request> Requests { get; } = [];

        /// <summary>The downloads asked for, each as the path of its address.</summary>
        public IEnumerable<string> Downloads => Requests.Where(request => request.Uri.Host == "github.com").Select(request => request.Uri.AbsolutePath);

        /// <summary>The lists of releases asked for, each as the path of its address.</summary>
        public IEnumerable<string> Listings => Requests.Where(request => request.Uri.Host != "github.com").Select(request => request.Uri.AbsolutePath);

        /// <summary>When set, the API refuses as it does when its limit on requests is reached.</summary>
        public bool RateLimited { get; set; }

        /// <summary>When set, every request fails before any answer, as when there is no network.</summary>
        public bool Unreachable { get; set; }

        /// <summary>When set, every request is answered with this status and nothing else.</summary>
        public HttpStatusCode? Answer { get; set; }

        /// <summary>The headers that go with <see cref="Answer"/>.</summary>
        public Dictionary<string, string> AnswerHeaders { get; } = [];

        /// <summary>When set, the API answers a list of releases with this text.</summary>
        public string? ListBody { get; set; }

        /// <summary>Whether a download says how long it is before it is sent.</summary>
        public bool SaysLength { get; set; } = true;

        /// <summary>When set, a download sends the first half of a file and then nothing more, as a connection that has gone quiet does.</summary>
        public bool Stalls { get; set; }

        /// <summary>A repository that is there and has released nothing.</summary>
        public FakeGitHub Create(string repository)
        {
            _repositories.Add(repository);
            return this;
        }

        /// <summary>A release with one file. The repository is written <c>owner/repo</c>.</summary>
        public FakeGitHub Upload(string repository, string tag, string asset, byte[] bytes, bool prerelease = false, bool draft = false)
        {
            _repositories.Add(repository);
            var release = _releases.FirstOrDefault(existing => existing.Repository == repository && existing.Tag == tag);
            if (release is null)
            {
                _releases.Add(release = new Release(repository, tag, prerelease, draft, []));
            }

            release.Assets[asset] = bytes;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            Requests.Add(new Request(uri, request.Headers.Authorization, request.Headers.UserAgent.ToString(), request.Headers.Accept.ToString()));
            if (Unreachable)
            {
                throw new HttpRequestException("Connection refused (" + uri.Host + ":443)");
            }

            if (Answer is { } status)
            {
                var answer = new HttpResponseMessage(status);
                foreach (var header in AnswerHeaders)
                {
                    answer.Headers.Add(header.Key, header.Value);
                }

                return Task.FromResult(answer);
            }

            var parts = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
            return Task.FromResult(uri.Host.StartsWith("api.", StringComparison.Ordinal) ? List(uri, parts) : Download(parts));
        }

        // github.com/<owner>/<repo>/releases/download/<tag>/<asset>, with the owner and the repository in any case.
        private HttpResponseMessage Download(string[] parts)
        {
            if (parts.Length != 6 || parts[2] != "releases" || parts[3] != "download")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var release = _releases.FirstOrDefault(existing =>
                string.Equals(existing.Repository, $"{parts[0]}/{parts[1]}", StringComparison.OrdinalIgnoreCase) && existing.Tag == parts[4] && !existing.Draft);
            if (release is null || !release.Assets.TryGetValue(parts[5], out var bytes))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            HttpContent content = Stalls ? new StreamContent(new StalledStream(bytes)) : SaysLength ? new ByteArrayContent(bytes) : new StreamContent(new UnseekableStream(bytes));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }

        // api.github.com/repos/<owner>/<repo>/releases?per_page=100&page=N
        private HttpResponseMessage List(Uri uri, string[] parts)
        {
            if (RateLimited)
            {
                var refused = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"message\":\"API rate limit exceeded\"}") };
                refused.Headers.Add("x-ratelimit-limit", "60");
                refused.Headers.Add("x-ratelimit-remaining", "0");
                refused.Headers.Add("x-ratelimit-reset", "1791599224");
                return refused;
            }

            if (parts.Length != 4 || parts[0] != "repos" || parts[3] != "releases")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (ListBody is not null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListBody, Encoding.UTF8, "application/json") };
            }

            var repository = $"{parts[1]}/{parts[2]}";
            if (!_repositories.Contains(repository))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var releases = _releases.Where(existing => string.Equals(existing.Repository, repository, StringComparison.OrdinalIgnoreCase)).ToList();

            var query = uri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=')).ToDictionary(pair => pair[0], pair => pair[1]);
            var perPage = int.Parse(query.GetValueOrDefault("per_page", "30"));
            var page = int.Parse(query.GetValueOrDefault("page", "1"));

            using var body = new MemoryStream();
            using (var json = new Utf8JsonWriter(body))
            {
                json.WriteStartArray();
                foreach (var release in releases.Skip((page - 1) * perPage).Take(perPage))
                {
                    json.WriteStartObject();
                    json.WriteString("tag_name", release.Tag);
                    json.WriteBoolean("draft", release.Draft);
                    json.WriteBoolean("prerelease", release.Prerelease);
                    json.WriteBoolean("immutable", false);
                    json.WriteStartArray("assets");
                    foreach (var asset in release.Assets)
                    {
                        json.WriteStartObject();
                        json.WriteString("name", asset.Key);
                        json.WriteNumber("size", asset.Value.Length);
                        json.WriteEndObject();
                    }

                    json.WriteEndArray();
                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body.ToArray()) };
        }

        internal sealed record Request(Uri Uri, AuthenticationHeaderValue? Authorization, string UserAgent, string Accept);

        private sealed record Release(string Repository, string Tag, bool Prerelease, bool Draft, Dictionary<string, byte[]> Assets);

        // A body that gives half of itself and then waits until whoever reads it gives up.
        private sealed class StalledStream(byte[] bytes) : MemoryStream(bytes[..(bytes.Length / 2)])
        {
            public override bool CanSeek => false;

            public override long Length => throw new NotSupportedException();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                var read = await base.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return read;
            }
        }

        // A body whose length is not known until it has been read, as a download sent in chunks is.
        private sealed class UnseekableStream(byte[] bytes) : MemoryStream(bytes)
        {
            public override bool CanSeek => false;

            public override long Length => throw new NotSupportedException();
        }
    }
}
