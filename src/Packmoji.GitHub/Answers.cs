namespace Packmoji.GitHub
{
    /// <summary>
    /// Asking over HTTP and reading what is answered, as everything that reaches the network does
    /// it: pmj says who it is, an answer is read no further than a limit, and one that goes quiet
    /// is given up.
    /// </summary>
    /// <remarks>
    /// Who is asked decides what not being answered is called, so each caller gives a way to make
    /// its own problem out of why.
    /// </remarks>
    internal static class Answers
    {
        /// <summary>Sends a request and gives the answer once its start has arrived. The request is disposed either way.</summary>
        /// <param name="unreachable">Makes the problem of not being answered, from why.</param>
        public static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request, string userAgent, Func<string, Exception> unreachable, CancellationToken cancellationToken)
        {
            request.Headers.UserAgent.ParseAdd(userAgent);
            try
            {
                return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException failure)
            {
                throw unreachable(failure.Message);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw unreachable("there was no answer in time");
            }
            finally
            {
                request.Dispose();
            }
        }

        /// <summary>The body of an answer, or null when it is longer than <paramref name="maxBytes"/>.</summary>
        /// <param name="quiet">How long the answer may send nothing before it is given up.</param>
        /// <param name="unreachable">Makes the problem of an answer that stopped, from why.</param>
        public static async Task<byte[]?> ReadAsync(HttpResponseMessage response, int maxBytes, TimeSpan quiet, Func<string, Exception> unreachable, CancellationToken cancellationToken)
        {
            // HttpClient's own limit on time ends when the start of an answer arrives. An answer that
            // then goes quiet would be waited on for ever, so each read is given a limit of its own.
            using var patience = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var read = new MemoryStream();
                var buffer = new byte[81_920];
                while (true)
                {
                    patience.CancelAfter(quiet);
                    var count = await body.ReadAsync(buffer, patience.Token);
                    if (count == 0)
                    {
                        return read.ToArray();
                    }

                    if (read.Length + count > maxBytes)
                    {
                        return null;
                    }

                    read.Write(buffer, 0, count);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw unreachable("the answer stopped coming");
            }
            catch (Exception failure) when (failure is HttpRequestException or IOException)
            {
                throw unreachable(failure.Message);
            }
        }
    }
}
