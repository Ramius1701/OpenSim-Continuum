using System;
using System.Globalization;
using MySql.Data.MySqlClient;

namespace OpenSim.Continuum.Economy
{
    internal static class MySqlValue
    {
        internal static void Open(MySqlConnection connection)
        {
            connection.Open();
            using MySqlCommand command = connection.CreateCommand();
            command.CommandText = "SET time_zone = '+00:00'";
            command.ExecuteNonQuery();
        }

        internal static Guid Guid(MySqlDataReader reader, int ordinal)
        {
            object value = reader.GetValue(ordinal);
            if (value is Guid guid)
                return guid;
            return System.Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
    }
}
