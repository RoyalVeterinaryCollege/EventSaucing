using System;

namespace EventSaucing.Reactors {
    /// <summary>
    /// Exception occured when validating an EventSaucing Reactor
    /// </summary>
    public class ReactorValidationException : Exception {
        public ReactorValidationException() {
        }

        public ReactorValidationException(string message)
            : base(message) {
        }

        public ReactorValidationException(string message, Exception inner)
            : base(message, inner) {
        }
    }
}
