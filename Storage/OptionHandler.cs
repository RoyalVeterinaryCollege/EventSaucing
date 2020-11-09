using Dapper;
using Scalesque;
using System.Data;

namespace EventSaucing.Storage {
    /// <summary>
    /// Tells dapper how to deal with Option long types
    /// </summary>
    public class OptionHandler : SqlMapper.TypeHandler<Option<long>> {
        public override Option<long> Parse(object value) {
            return Option.Some((long)value);
        }

        public override void SetValue(IDbDataParameter parameter, Option<long> value) {
            if (value.HasValue) {
                parameter.Value = value.Get();
            } else {
                parameter.Value = null;
            }
        }
    }
}
