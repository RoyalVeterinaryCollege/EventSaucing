using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventSaucing.Reactors {
    public abstract class ReactorReadModel {
        public long Id { get; set; }
        public int VersionNumber { get; set; }
        public bool IsError { get; set; } 
    }
}
