/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the conditions in LICENSE.txt
 * are met.
 */

using System.Data.SQLite;
using OpenSim.Framework;

namespace OpenSim.Data.SQLite
{
    /// <summary>
    /// SQLite Offline IM V2 provider ported from Gunthar's standalone-HG
    /// implementation.
    /// </summary>
    public class SQLiteOfflineIMData :
        SQLiteGenericTableHandler<OfflineIMData>, IOfflineIMData
    {
        public SQLiteOfflineIMData(string connectionString, string realm)
            : base(connectionString, realm, "IM_Store")
        {
        }

        public void DeleteOld()
        {
            using SQLiteCommand command = new SQLiteCommand();
            command.CommandText =
                $"delete from {m_Realm} where TMStamp < :tstamp";
            command.Parameters.Add(new SQLiteParameter(
                ":tstamp", Util.UnixTimeSinceEpoch() - (14 * 24 * 60 * 60)));
            ExecuteNonQuery(command, m_Connection);
        }
    }
}
