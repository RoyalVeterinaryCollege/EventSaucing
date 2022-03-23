using Akka.Actor;
using Dapper;
using EventSaucing.Storage;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {

    public interface IReactorPublicationFinder {
        /// <summary>
        /// Searches the publications list for publications of that name
        /// </summary>
        /// <param name="name"></param>
        /// <returns>IEnumerable ReactorPublicationSearchResult  multiple reactors can publish to the same publication</returns>
        Task<IEnumerable<ReactorPublicationSearchResult>> FindPublications(string name);
    }

    public class ReactorPublicationFinder : IReactorPublicationFinder {
        private readonly IDbService dbservice;

        public ReactorPublicationFinder(IDbService dbservice) {
            this.dbservice = dbservice;
        }
        public async Task<IEnumerable<ReactorPublicationSearchResult>> FindPublications(string name) {
            using(var con = dbservice.GetReadmodel()) {
                await con.OpenAsync();
                return await con.QueryAsync<ReactorPublicationSearchResult>("SELECT * FROM dbo.ReactorPublications RP WHERE NameHash=@NameHash AND Name=@Name", new { NameHash = name.GetHashCode(), name });
            }
        }
    }

    public class ReactorPublicationSearchResult {
        /// <summary>
        /// The unique id of the publication
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// The id of the reactor who publishes
        /// </summary>
        public long PublishingReactorId { get; set; }

        /// <summary>
        /// The name of the publicaiton
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A hash for the name to speed up db searches
        /// </summary>
        public string NameHash { get; set; }

        public string ArticleSerialisationType { get; set; }

        public string ArticleSerialisation { get; set; }
        public object Article { get => JsonConvert.DeserializeObject(ArticleSerialisation, Type.GetType(ArticleSerialisationType, throwOnError: true)); }

        public int VersionNumber { get; set; }
    }
}
