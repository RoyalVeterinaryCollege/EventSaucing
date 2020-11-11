using System;

namespace EventSaucing {
    /// <summary>
    /// Exception occured when validating an EventSaucingReactor
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
