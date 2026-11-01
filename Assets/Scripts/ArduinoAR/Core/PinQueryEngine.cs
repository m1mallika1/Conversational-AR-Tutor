using System.Collections.Generic;

namespace ArduinoAR
{
    /// <summary>Result of a query: the retrieved records drive both the answer text and the highlighting.</summary>
    public sealed class PinQueryResult
    {
        public string Query;
        public string IntentTitle;
        public readonly List<PinRecord> Records = new();
        public string Answer;
        public BoardProfile Profile;
        /// <summary>Full structured response from <see cref="BoardAssistant"/>.</summary>
        public AssistantResponse Response;
        public bool Matched => Records.Count > 0;
    }

    /// <summary>
    /// Backwards-compatible, context-free entry point. Delegates to the <see cref="BoardAssistant"/>
    /// pipeline (entities → intent → retrieval → grounded answer); kept so existing callers and tests work.
    /// </summary>
    public static class PinQueryEngine
    {
        /// <summary>Query against the reference Arduino Uno R3 profile (virtual demo board).</summary>
        public static PinQueryResult Query(string query) => Query(query, ArduinoUnoProfile.Profile);

        public static PinQueryResult Query(string query, BoardProfile profile)
        {
            var response = BoardAssistant.Process(query, profile);
            var result = new PinQueryResult
            {
                Query = query ?? "",
                Profile = profile,
                IntentTitle = response.IntentTitle,
                Answer = response.ToDisplayText(),
                Response = response,
            };
            result.Records.AddRange(response.HighlightPins);
            return result;
        }
    }
}
