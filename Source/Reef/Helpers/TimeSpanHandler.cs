using Dapper;
using System.Data;

namespace Reef.Helpers;

/// <summary>
/// Custom Dapper type handler for TimeSpan to handle SQLite TEXT storage
/// </summary>
public class TimeSpanHandler : SqlMapper.TypeHandler<TimeSpan>
{
    public override TimeSpan Parse(object value)
    {
        if (value == null || value is DBNull)
        {
            return TimeSpan.Zero;
        }

        if (value is TimeSpan timeSpan)
        {
            return timeSpan;
        }

        if (value is string stringValue)
        {
            if (TimeSpan.TryParse(stringValue, out var result))
            {
                return result;
            }
        }

        throw new InvalidCastException($"Cannot convert '{value}' to TimeSpan");
    }

    public override void SetValue(IDbDataParameter parameter, TimeSpan value)
    {
        parameter.Value = value.ToString(@"hh\:mm\:ss");
        parameter.DbType = DbType.String;
    }
}

/// <summary>
/// Custom Dapper type handler for nullable TimeSpan to handle SQLite TEXT storage
/// </summary>
public class NullableTimeSpanHandler : SqlMapper.TypeHandler<TimeSpan?>
{
    public override TimeSpan? Parse(object value)
    {
        if (value == null || value is DBNull)
        {
            return null;
        }

        if (value is TimeSpan timeSpan)
        {
            return timeSpan;
        }

        if (value is string stringValue)
        {
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                return null;
            }

            if (TimeSpan.TryParse(stringValue, out var result))
            {
                return result;
            }
        }

        throw new InvalidCastException($"Cannot convert '{value}' to TimeSpan?");
    }

    public override void SetValue(IDbDataParameter parameter, TimeSpan? value)
    {
        if (value.HasValue)
        {
            parameter.Value = value.Value.ToString(@"hh\:mm\:ss");
            parameter.DbType = DbType.String;
        }
        else
        {
            parameter.Value = DBNull.Value;
        }
    }
}
