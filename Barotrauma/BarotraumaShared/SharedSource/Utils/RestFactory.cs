using RestSharp;

namespace Barotrauma
{
    /// <summary>
    /// Factory methods for creating RestSharp clients and requests with default timeout
    /// settings, to avoid unforeseen connectivity issues hanging the game.
    /// The timeout needs to be added to both the client and the request, due to known
    /// issues with RestSharp 106.x that we use: https://github.com/restsharp/RestSharp/issues/1900
    /// </summary>
    public static class RestFactory
    {
        /// <summary>
        /// Creates a RestClient with <see cref="GameSettings.Config.RemoteContentTimeoutMs"/> applied.
        /// </summary>
        public static RestClient CreateClient(string baseUrl)
        {
            return new RestClient(baseUrl)
            {
                Timeout = GameSettings.CurrentConfig.RemoteContentTimeoutMs
            };
        }

        /// <summary>
        /// Creates a RestRequest with <see cref="GameSettings.Config.RemoteContentTimeoutMs"/> applied.
        /// </summary>
        public static RestRequest CreateRequest(string resource, Method method = Method.GET)
        {
            return new RestRequest(resource, method)
            {
                Timeout = GameSettings.CurrentConfig.RemoteContentTimeoutMs
            };
        }
    }
}
