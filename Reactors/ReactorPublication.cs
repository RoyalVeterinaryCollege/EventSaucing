using Scalesque;
using System;

namespace EventSaucing.Reactors {
    public class ReactorPublication {
        /// <summary>
        /// The unique id of the publication
        /// </summary>
        public Option<long> Id { get; set; } = Option.None();

        /// <summary>
        /// Gets or sets the version of the publication.
        /// </summary>
        public int VersionNumber { get; set; }

        private string name;
        private object article;

        public static void GuardPublicationName(string name) { 
            if (string.IsNullOrWhiteSpace(name)) throw new Exception("Publication name missing");
            if (name.Length > 2048) throw new Exception("Publication name too long"); 
        }
        /// <summary>
        /// The name of the publication
        /// </summary>
        public string Name {
            get => name;
            set {
                GuardPublicationName(value);
                name = value;
            }
        }

        /// <summary>
        /// A hash for the name to speed up db searches
        /// </summary>
        public int NameHash { get => name.GetHashCode(); }
        public object Article {
            get => article; 
            set {
                article = value;
                LastPublishedDate = DateTime.Now;
            }
        }

        public DateTime? LastPublishedDate { get; set; }
        /// <summary>
        /// Method use to set article on deserialisation
        /// </summary>
        /// <param name="article"></param>
        public void RestoreArticle(object article) {
            this.article = article;
        }
    }
}
