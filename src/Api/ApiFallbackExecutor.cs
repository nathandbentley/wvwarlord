using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace WvWarlord.Api
{
    /// <summary>
    /// Executes an API action with the mandated two-route architecture:
    ///   Route 1 - the internal Gw2ApiManager pipeline (typed Gw2Sharp client).
    ///   Route 2 - a direct string query against https://api.guildwars2.com/v2/.
    /// Route 1 failures/timeouts are caught and logged, then Route 2 runs.
    /// <see cref="SkipRoute1Globally"/> lets a developer bypass Route 1 entirely
    /// for every call in the module, forcing everything onto the direct path.
    /// </summary>
    public static class ApiFallbackExecutor
    {
        /// <summary>Global developer switch: when true, Route 1 is never attempted.</summary>
        public static bool SkipRoute1Globally { get; set; } = false;

        private static readonly HttpClient _httpClient = BuildHttpClient();

        private static HttpClient BuildHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BlishHUD-WvwWarlord");
            return client;
        }

        /// <summary>
        /// Runs Route 1 then Route 2 fallback, returning Route 1's typed result
        /// or the raw JSON string from Route 2, whichever succeeded.
        /// Caller distinguishes by checking which out-value is non-null, or by
        /// using the typed overload below.
        /// </summary>
        public static async Task<T> ExecuteAsync<T>(
            string actionName,
            Func<Task<T>> route1,
            Func<Task<string>> route2RawJsonFactory,
            Func<string, T> route2Parser)
        {
            // NOTE: successful calls are intentionally NOT logged (steady-state
            // noise). Only catches and the initial startup sequence get logged.
            if (!SkipRoute1Globally && route1 != null)
            {
                try
                {
                    T result = await route1();
                    ApiCallTracker.RecordBlishCall();
                    return result;
                }
                catch (Exception ex)
                {
                    ApiCallTracker.RecordCatch();
                    ApiCallTracker.Log($"{actionName}: Route1 failed ({ex.Message}). Falling back to Route2.");
                }
            }

            string json = await route2RawJsonFactory();
            ApiCallTracker.RecordDirectCall();
            return route2Parser(json);
        }

        /// <summary>Performs the raw GET used by every Route 2 fallback, appending the API key when supplied.</summary>
        public static async Task<string> GetRawAsync(string relativePath, string apiKey = null)
        {
            string url = $"https://api.guildwars2.com/v2/{relativePath}";
            if (!string.IsNullOrEmpty(apiKey))
            {
                url += (url.Contains("?") ? "&" : "?") + $"access_token={apiKey}";
            }

            using (var response = await _httpClient.GetAsync(url))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }
    }
}
