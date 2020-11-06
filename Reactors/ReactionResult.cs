using System.Collections.Generic;

namespace EventSaucing.Reactors {
    /// <summary>
    /// A class determining what should happen after reacting to a message
    /// </summary>
    public class ReactionResult {
        /// <summary>
        /// Gets or sets if the reactor wants to persist itself due to reacting to a message
        /// </summary>
        public bool PersistState { get; set; }

        /// <summary>
        /// A list of publication that should be published due to reacting to a message
        /// </summary>
        public List<ReactorPublication> PublishArticles { get; set; } = new List<ReactorPublication>();
    }
}
