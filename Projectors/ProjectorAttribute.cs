using System;

namespace EventSaucing.Projectors {
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ProjectorAttribute : Attribute {
        /// <summary>
        /// Gets the unique id for the projector
        /// </summary>
        public int ProjectorId { get; }

        public ProjectorAttribute(int projectorId) {
            ProjectorId = projectorId;
        }
    }
}
