/*
 * Copyright (c) Contributors, http://opensimulator.org/, http://www.nsl.tuis.ac.jp/ See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using log4net;
using MySql.Data.MySqlClient;
using OpenMetaverse;
using OpenSim.Data.MySQL.MySQLMoneyDataWrapper;
using OpenSim.Modules.Currency;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace OpenSim.Grid.MoneyServer
{
    class MoneyDBService : IMoneyDBService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private string m_connect;
        private long TicksToEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

        // DB manager pool
        protected Dictionary<int, MySQLSuperManager> m_dbconnections = new Dictionary<int, MySQLSuperManager>();	// with Lock
        private int m_maxConnections;

        public int m_lastConnect = 0;

        public MoneyDBService(string connect)
        {
            m_connect = connect;
            Initialise(m_connect, 10);
        }

        public MoneyDBService()
        {
        }

        public readonly object connectionLock = new object();

        //public void Initialise(string connectionString, int maxDBConnections)
        //{
        //    lock (connectionLock)
        //    {
        //        if (maxDBConnections <= 0)
        //        {
        //            throw new ArgumentException("maxDBConnections must be greater than zero", nameof(maxDBConnections));
        //        }

        //        //m_log.InfoFormat("[Initialise]: Setting m_maxConnections to {0}", maxDBConnections);
        //        m_connect = connectionString;
        //        m_maxConnections = maxDBConnections;

        //        //m_log.InfoFormat("[Initialise]: m_maxConnections is now {0}", m_maxConnections);

        //        if (connectionString != string.Empty)
        //        {
        //            for (int i = 0; i < m_maxConnections; i++)
        //            {
        //                //m_log.Info("Connecting to DB... [" + i + "]");
        //                MySQLSuperManager msm = new MySQLSuperManager(connectionString);
        //                m_dbconnections.Add(i, msm);
        //            }
        //        }
        //        else
        //        {
        //            m_log.Error("[MONEY DB]: Connection string is null, initialise database failed");
        //            throw new Exception("Failed to initialise MySql database");
        //        }
        //    }
        //}

        private const int MAX_ALLOWED_CONNECTIONS = 100;

        // Ersetze die Methode Initialise, damit die Verbindungen im Pool korrekt angelegt werden.
        // Füge außerdem eine Schutzmaßnahme in GetLockedConnection hinzu, damit der Index nie außerhalb des Bereichs liegt.

        public void Initialise(string connectionString, int maxDBConnections)
        {
            lock (connectionLock)
            {
                if (maxDBConnections <= 0)
                {
                    throw new ArgumentException("maxDBConnections must be greater than zero", nameof(maxDBConnections));
                }
                if (maxDBConnections > MAX_ALLOWED_CONNECTIONS)
                {
                    throw new ArgumentException($"maxDBConnections must not exceed {MAX_ALLOWED_CONNECTIONS}", nameof(maxDBConnections));
                }
                m_connect = connectionString;
                m_maxConnections = maxDBConnections;

                if (connectionString == string.Empty)
                {
                    m_log.Error("[MONEY DB]: Connection string is null, initialise database failed");
                    throw new Exception("Failed to initialise MySql database");
                }

                // Verbindungen im Pool anlegen!
                m_dbconnections.Clear();
                for (int i = 0; i < m_maxConnections; i++)
                {
                    MySQLSuperManager msm = new MySQLSuperManager(connectionString);
                    m_dbconnections.Add(i, msm);
                }
            }
        }

        // Optional: Zusätzlicher Schutz in GetLockedConnection
        public MySQLSuperManager GetLockedConnection()
        {
            lock (connectionLock)
            {
                if (m_maxConnections == 0)
                {
                    throw new InvalidOperationException("m_maxConnections cannot be zero.");
                }

                int lockedCons = 0;
                while (true)
                {
                    m_lastConnect++;
                    if (m_lastConnect == int.MaxValue)
                    {
                        m_lastConnect = 0;
                    }

                    int index = m_lastConnect % m_maxConnections;
                    // Schutz: Index muss im Bereich liegen
                    if (index < 0 || index >= m_maxConnections)
                    {
                        m_log.ErrorFormat("GetLockedConnection: Index {0} out of range (0..{1})", index, m_maxConnections - 1);
                        index = 0;
                    }

                    if (!m_dbconnections.ContainsKey(index))
                    {
                        m_log.ErrorFormat("GetLockedConnection: Invalid connection index {0}", index);
                        throw new KeyNotFoundException($"The given key '{index}' was not present in the dictionary");
                    }

                    MySQLSuperManager msm = m_dbconnections[index];
                    if (!msm.Locked)
                    {
                        msm.GetLock();
                        return msm;
                    }

                    lockedCons++;
                    if (lockedCons > m_maxConnections)
                    {
                        lockedCons = 0;
                        System.Threading.Thread.Sleep(2000);
                        m_log.Warn("GetLockedConnection: All connections are in use. Probable cause: Something didn't release a mutex properly, or high volume of requests inbound.");
                    }
                }
            }
        }

        public void Reconnect()
        {
            //m_log.Debug("Reconnect attempt started.");

            if (m_maxConnections <= 0)
            {
                throw new InvalidOperationException("m_maxConnections must be greater than zero.");
            }

            for (int i = 0; i < m_maxConnections; i++)
            {
                MySQLSuperManager msm = m_dbconnections[i];
                try
                {
                    msm.Manager.Reconnect();
                    //m_log.DebugFormat("Reconnected to database connection {0} successfully.", i);
                }
                catch (Exception ex)
                {
                    m_log.ErrorFormat("Failed to reconnect to database connection {0}: {1}", i, ex.Message);
                }
            }
            //m_log.Debug("Reconnect attempt completed.");
        }



        // Plan:
        // 1. Stelle sicher, dass alle Datenbankoperationen ausschließlich über MySQLSuperManager und dessen Manager laufen.
        // 2. Entferne alle direkten MySqlConnection-Zugriffe (z.B. in giveMoney und getBalance), die nicht den Pool nutzen.
        // 3. Implementiere die Methoden so, dass sie immer dbm.Manager verwenden und die Verbindung korrekt freigeben.

        public int getBalance(string userID)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.getBalance(userID);
            }
            catch (MySqlException e)
            {
                m_log.Error(e);
                dbm.Manager.Reconnect();
                return dbm.Manager.getBalance(userID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return 0;
            }
            finally
            {
                dbm.Release();
            }
        }

        public int CheckMaximumMoney(string userID, int m_CurrencyMaximum)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            // Beispielwert, sollte aus der Konfigurationsdatei MoneyServer.ini geladen werden
            m_log.InfoFormat("[CHECK MAXIMUM MONEY]: Currency Maximum: {0}", m_CurrencyMaximum);
            if (m_CurrencyMaximum <= 0)
            {
                m_CurrencyMaximum = 1000;
            }

            try
            {
                // Ausnahmen f�r SYSTEM und BANKER
                if (userID == "SYSTEM" || userID == "BANKER")
                {
                    return 0; // Keine Begrenzung f�r diese Benutzer
                }

                // Abrufen des aktuellen Guthabens des Benutzers
                string sql = "SELECT balance FROM balances WHERE user = ?userID";
                int currentBalance = 0;

                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?userID", userID);

                    using (MySqlDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            currentBalance = reader.GetInt32("balance");
                        }
                    }
                }

                // �berpr�fen, ob das Guthaben �ber dem Maximum liegt und ggf. abziehen
                if (currentBalance > m_CurrencyMaximum)
                {
                    int excessAmount = currentBalance - m_CurrencyMaximum;

                    // Guthaben auf das Maximum reduzieren
                    sql = "UPDATE balances SET balance = ?newBalance WHERE user = ?userID";
                    using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                    {
                        cmd.Parameters.AddWithValue("?newBalance", m_CurrencyMaximum);
                        cmd.Parameters.AddWithValue("?userID", userID);
                        cmd.ExecuteNonQuery();
                    }

                    m_log.InfoFormat("[CheckMaximumMoney]: Reduced balance for user {0} by {1} to enforce maximum limit of {2}", userID, excessAmount, m_CurrencyMaximum);
                    return excessAmount; // R�ckgabe des abgezogenen Betrags
                }

                return 0; // Keine �nderung, falls das Guthaben innerhalb des Limits liegt
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[CheckMaximumMoney]: Error checking and updating user balance: {0}", ex.Message);
                throw;
            }
            finally
            {
                dbm.Release();
            }
        }




        /// <summary>Withdraws the money.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        /// <param name="senderID">The sender identifier.</param>
        /// <param name="amount">The amount.</param>
        public bool withdrawMoney(UUID transactionID, string senderID, int amount)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.withdrawMoney(transactionID, senderID, amount);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.withdrawMoney(transactionID, senderID, amount);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }
        // Beispiel für die Methode giveMoney:

        public bool giveMoney(UUID transactionID, string receiverID, int amount)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.giveMoney(transactionID, receiverID, amount);
            }
            catch (MySqlException e)
            {
                m_log.Error(e);
                dbm.Manager.Reconnect();
                return dbm.Manager.giveMoney(transactionID, receiverID, amount);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        public bool BuyMoney(UUID transactionID, string userID, int amount)
        {
            //m_log.DebugFormat("[BuyMoney]: Start - transactionID: {0}, userID: {1}, amount: {2}", transactionID, userID, amount);

            MySQLSuperManager dbm = GetLockedConnection();
            string sql = "UPDATE balances SET balance = balance + ?amount WHERE user = ?userID";

            try
            {
                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?amount", amount);
                    cmd.Parameters.AddWithValue("?userID", userID);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    bool result = (rowsAffected > 0);

                    if (result)
                    {
                        LogTransaction(transactionID, userID, amount);
                    }

                    //m_log.DebugFormat("[BuyMoney]: End - transactionID: {0}, userID: {1}, amount: {2}, result: {3}", transactionID, userID, amount, result);
                    return result;
                }
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                m_log.ErrorFormat("[BuyMoney]: SQL Exception - {0}", e.Message);
                dbm.Manager.Reconnect();

                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?amount", amount);
                    cmd.Parameters.AddWithValue("?userID", userID);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    bool result = (rowsAffected > 0);

                    if (result)
                    {
                        LogTransaction(transactionID, userID, amount);
                    }

                    //m_log.DebugFormat("[BuyMoney]: End after Reconnect - transactionID: {0}, userID: {1}, amount: {2}, result: {3}", transactionID, userID, amount, result);
                    return result;
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[BuyMoney]: General Exception - {0}", e.Message);
                return false;
            }
            finally
            {
                dbm.Release();
                //m_log.Debug("[BuyMoney]: Connection released");
            }
        }

        public void LogTransaction(UUID transactionID, string userID, int amount)
        {
            //string sql = "INSERT INTO transactions (transactionID, userID, amount, timestamp) VALUES (?transactionID, ?userID, ?amount, ?timestamp)";
            string sql = "INSERT INTO transactions (userID, amount, timestamp) VALUES (?userID, ?amount, ?timestamp)";

            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    //cmd.Parameters.AddWithValue("?transactionID", transactionID.ToString());
                    cmd.Parameters.AddWithValue("?userID", userID);
                    cmd.Parameters.AddWithValue("?amount", amount);
                    cmd.Parameters.AddWithValue("?timestamp", DateTime.UtcNow);

                    cmd.ExecuteNonQuery();
                }
            }
            finally
            {
                dbm.Release();
            }
        }


        /// <summary>
        /// Completes a viewer currency purchase as one database transaction.
        /// The avatar balance and the successful BuyMoney ledger row either both
        /// commit or both roll back. Calendar limits use UTC; weeks start Monday.
        /// </summary>
        public bool TryPurchaseCurrency(
            UUID transactionID,
            string receiverID,
            int amount,
            int totalDay,
            int totalWeek,
            int totalMonth,
            int currencyMaximum,
            out string message)
        {
            message = string.Empty;

            UUID receiverUUID;
            if (string.IsNullOrWhiteSpace(receiverID) ||
                !UUID.TryParse(receiverID, out receiverUUID) ||
                receiverUUID == UUID.Zero)
            {
                message = "A valid avatar ID is required.";
                return false;
            }

            if (amount <= 0)
            {
                message = "The currency purchase amount must be greater than zero.";
                return false;
            }

            if (totalDay < 0 || totalWeek < 0 || totalMonth < 0 || currencyMaximum < 0)
            {
                message = "Currency purchase limits cannot be negative.";
                return false;
            }

            if (transactionID == UUID.Zero)
                transactionID = UUID.Random();

            MySQLSuperManager dbm = GetLockedConnection();
            MySqlTransaction dbTransaction = null;

            try
            {
                MySqlConnection connection = dbm.Manager.dbcon;
                dbTransaction = connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);

                // Serialize all viewer purchases for this avatar. This prevents two
                // simultaneous requests from each passing the same period-limit check.
                int currentBalance;
                const string balanceSql =
                    "SELECT balance FROM balances WHERE user = ?receiverID FOR UPDATE";

                using (MySqlCommand balanceCommand = new MySqlCommand(balanceSql, connection, dbTransaction))
                {
                    balanceCommand.Parameters.AddWithValue("?receiverID", receiverID);
                    object balanceResult = balanceCommand.ExecuteScalar();
                    if (balanceResult == null || balanceResult == DBNull.Value)
                    {
                        dbTransaction.Rollback();
                        message = "The avatar does not have a currency account.";
                        return false;
                    }

                    currentBalance = Convert.ToInt32(balanceResult);
                }

                // The quote confirmation UUID is the idempotency key. A repeated
                // confirmation returns the original success without crediting twice.
                const string duplicateSql =
                    "SELECT receiver, amount, type, status FROM transactions " +
                    "WHERE UUID = ?transactionID LIMIT 1";

                bool duplicateFound = false;
                string recordedReceiver = string.Empty;
                int recordedAmount = 0;
                int recordedType = 0;
                int recordedStatus = 0;

                using (MySqlCommand duplicateCommand = new MySqlCommand(duplicateSql, connection, dbTransaction))
                {
                    duplicateCommand.Parameters.AddWithValue("?transactionID", transactionID.ToString());
                    using (MySqlDataReader reader = duplicateCommand.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            duplicateFound = true;
                            recordedReceiver = Convert.ToString(reader["receiver"]);
                            recordedAmount = Convert.ToInt32(reader["amount"]);
                            recordedType = Convert.ToInt32(reader["type"]);
                            recordedStatus = Convert.ToInt32(reader["status"]);
                        }
                    }
                }

                if (duplicateFound)
                {
                    if (string.Equals(recordedReceiver, receiverID, StringComparison.OrdinalIgnoreCase) &&
                        recordedAmount == amount &&
                        recordedType == (int)TransactionType.BuyMoney &&
                        recordedStatus == (int)Status.SUCCESS_STATUS)
                    {
                        dbTransaction.Commit();
                        message = "This currency purchase was already completed.";
                        return true;
                    }

                    dbTransaction.Rollback();
                    message = "The supplied purchase confirmation is already in use.";
                    return false;
                }

                long newBalanceLong = (long)currentBalance + amount;
                if (newBalanceLong > int.MaxValue)
                {
                    dbTransaction.Rollback();
                    message = "The purchase would exceed the supported balance range.";
                    return false;
                }

                if (currencyMaximum > 0 && newBalanceLong > currencyMaximum)
                {
                    dbTransaction.Rollback();
                    message = string.Format(
                        "This purchase would exceed the maximum balance of L${0}.",
                        currencyMaximum);
                    return false;
                }

                DateTime utcNow = DateTime.UtcNow;
                DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                DateTime dayStartUtc = utcNow.Date;
                int daysSinceMonday = ((int)utcNow.DayOfWeek + 6) % 7;
                DateTime weekStartUtc = utcNow.Date.AddDays(-daysSinceMonday);
                DateTime monthStartUtc = new DateTime(
                    utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

                int nowEpoch = checked((int)(utcNow - epoch).TotalSeconds);
                int dayStart = checked((int)(dayStartUtc - epoch).TotalSeconds);
                int weekStart = checked((int)(weekStartUtc - epoch).TotalSeconds);
                int monthStart = checked((int)(monthStartUtc - epoch).TotalSeconds);

                long dayTotal = 0;
                long weekTotal = 0;
                long monthTotal = 0;

                if (totalDay > 0 || totalWeek > 0 || totalMonth > 0)
                {
                    int earliestStart = nowEpoch;
                    if (totalDay > 0) earliestStart = Math.Min(earliestStart, dayStart);
                    if (totalWeek > 0) earliestStart = Math.Min(earliestStart, weekStart);
                    if (totalMonth > 0) earliestStart = Math.Min(earliestStart, monthStart);

                    const string totalsSql =
                        "SELECT " +
                        "COALESCE(SUM(CASE WHEN time >= ?dayStart THEN amount ELSE 0 END), 0) AS dayTotal, " +
                        "COALESCE(SUM(CASE WHEN time >= ?weekStart THEN amount ELSE 0 END), 0) AS weekTotal, " +
                        "COALESCE(SUM(CASE WHEN time >= ?monthStart THEN amount ELSE 0 END), 0) AS monthTotal " +
                        "FROM transactions " +
                        "WHERE receiver = ?receiverID AND type = ?transactionType " +
                        "AND status = ?successStatus AND time >= ?earliestStart AND time <= ?nowEpoch";

                    using (MySqlCommand totalsCommand = new MySqlCommand(totalsSql, connection, dbTransaction))
                    {
                        totalsCommand.Parameters.AddWithValue("?dayStart", dayStart);
                        totalsCommand.Parameters.AddWithValue("?weekStart", weekStart);
                        totalsCommand.Parameters.AddWithValue("?monthStart", monthStart);
                        totalsCommand.Parameters.AddWithValue("?receiverID", receiverID);
                        totalsCommand.Parameters.AddWithValue("?transactionType", (int)TransactionType.BuyMoney);
                        totalsCommand.Parameters.AddWithValue("?successStatus", (int)Status.SUCCESS_STATUS);
                        totalsCommand.Parameters.AddWithValue("?earliestStart", earliestStart);
                        totalsCommand.Parameters.AddWithValue("?nowEpoch", nowEpoch);

                        using (MySqlDataReader reader = totalsCommand.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                dayTotal = Convert.ToInt64(reader["dayTotal"]);
                                weekTotal = Convert.ToInt64(reader["weekTotal"]);
                                monthTotal = Convert.ToInt64(reader["monthTotal"]);
                            }
                        }
                    }
                }

                if (totalDay > 0 && dayTotal + amount > totalDay)
                {
                    dbTransaction.Rollback();
                    message = string.Format(
                        "The daily currency purchase limit is L${0}; L${1} remains available today.",
                        totalDay,
                        Math.Max(0L, (long)totalDay - dayTotal));
                    return false;
                }

                if (totalWeek > 0 && weekTotal + amount > totalWeek)
                {
                    dbTransaction.Rollback();
                    message = string.Format(
                        "The weekly currency purchase limit is L${0}; L${1} remains available this week.",
                        totalWeek,
                        Math.Max(0L, (long)totalWeek - weekTotal));
                    return false;
                }

                if (totalMonth > 0 && monthTotal + amount > totalMonth)
                {
                    dbTransaction.Rollback();
                    message = string.Format(
                        "The monthly currency purchase limit is L${0}; L${1} remains available this month.",
                        totalMonth,
                        Math.Max(0L, (long)totalMonth - monthTotal));
                    return false;
                }

                int newBalance = (int)newBalanceLong;
                const string updateBalanceSql =
                    "UPDATE balances SET balance = ?newBalance WHERE user = ?receiverID";

                using (MySqlCommand updateCommand = new MySqlCommand(updateBalanceSql, connection, dbTransaction))
                {
                    updateCommand.Parameters.AddWithValue("?newBalance", newBalance);
                    updateCommand.Parameters.AddWithValue("?receiverID", receiverID);
                    if (updateCommand.ExecuteNonQuery() != 1)
                    {
                        dbTransaction.Rollback();
                        message = "The avatar balance could not be updated.";
                        return false;
                    }
                }

                const string insertTransactionSql =
                    "INSERT INTO transactions " +
                    "(`UUID`,`sender`,`receiver`,`amount`,`senderBalance`,`receiverBalance`," +
                    "`objectUUID`,`objectName`,`regionHandle`,`regionUUID`,`type`,`time`," +
                    "`secure`,`status`,`commonName`,`description`) VALUES " +
                    "(?transactionID,?sender,?receiver,?amount,-1,?receiverBalance," +
                    "?objectUUID,?objectName,?regionHandle,?regionUUID,?type,?time," +
                    "?secure,?status,?commonName,?description)";

                using (MySqlCommand insertCommand = new MySqlCommand(insertTransactionSql, connection, dbTransaction))
                {
                    insertCommand.Parameters.AddWithValue("?transactionID", transactionID.ToString());
                    insertCommand.Parameters.AddWithValue("?sender", UUID.Zero.ToString());
                    insertCommand.Parameters.AddWithValue("?receiver", receiverID);
                    insertCommand.Parameters.AddWithValue("?amount", amount);
                    insertCommand.Parameters.AddWithValue("?receiverBalance", newBalance);
                    insertCommand.Parameters.AddWithValue("?objectUUID", UUID.Zero.ToString());
                    insertCommand.Parameters.AddWithValue("?objectName", string.Empty);
                    insertCommand.Parameters.AddWithValue("?regionHandle", string.Empty);
                    insertCommand.Parameters.AddWithValue("?regionUUID", UUID.Zero.ToString());
                    insertCommand.Parameters.AddWithValue("?type", (int)TransactionType.BuyMoney);
                    insertCommand.Parameters.AddWithValue("?time", nowEpoch);
                    insertCommand.Parameters.AddWithValue("?secure", UUID.Random().ToString());
                    insertCommand.Parameters.AddWithValue("?status", (int)Status.SUCCESS_STATUS);
                    insertCommand.Parameters.AddWithValue("?commonName", string.Empty);
                    insertCommand.Parameters.AddWithValue("?description", "Viewer currency purchase");

                    if (insertCommand.ExecuteNonQuery() != 1)
                    {
                        dbTransaction.Rollback();
                        message = "The purchase transaction could not be recorded.";
                        return false;
                    }
                }

                dbTransaction.Commit();
                message = string.Format("Successfully purchased L${0}.", amount);
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (dbTransaction != null)
                        dbTransaction.Rollback();
                }
                catch
                {
                    // Preserve the original database error for logging below.
                }

                m_log.ErrorFormat(
                    "[TRY PURCHASE CURRENCY]: Purchase failed for {0}, amount {1}: {2}",
                    receiverID,
                    amount,
                    ex);
                message = "The currency purchase could not be completed.";
                return false;
            }
            finally
            {
                if (dbTransaction != null)
                    dbTransaction.Dispose();
                dbm.Release();
            }
        }

        public bool BuyCurrency(string userID, int amount)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            TransactionData transaction = new TransactionData();
            transaction.TransUUID = UUID.Random();
            transaction.Sender = UUID.Zero.ToString();  // System sender
            transaction.Receiver = userID;
            transaction.Amount = amount;
            transaction.ObjectUUID = UUID.Zero.ToString();
            transaction.ObjectName = string.Empty;
            transaction.RegionHandle = string.Empty;
            transaction.Type = (int)TransactionType.BuyMoney; // Angenommen, BuyMoney ist ein g�ltiger Transaktionstyp ???
            transaction.Time = (int)((DateTime.UtcNow.Ticks - TicksToEpoch) / 10000000);
            transaction.Status = (int)Status.PENDING_STATUS;
            transaction.SecureCode = UUID.Random().ToString();
            transaction.CommonName = string.Empty;
            transaction.Description = "BuyCurrency " + DateTime.UtcNow.ToString();

            bool ret = addTransaction(transaction);
            if (!ret)
            {
                dbm.Release();
                return false;
            }

            try
            {
                // F�ge Geld dem Benutzerkonto hinzu
                ret = giveMoney(transaction.TransUUID, userID, amount);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                m_log.Error("[BuyCurrency]: SQL Exception - " + e.ToString());
                dbm.Manager.Reconnect();
                ret = giveMoney(transaction.TransUUID, userID, amount);
            }
            catch (Exception e)
            {
                m_log.Error("[BuyCurrency]: Exception - " + e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }

            if (ret)
            {
                m_log.InfoFormat("[BuyCurrency]: Successfully bought currency for user {0} in amount {1}", userID, amount);
            }
            return ret;
        }

        /// <summary>Sets the total sale.</summary>
        /// <param name="transaction">The transaction.</param>
        public bool setTotalSale(TransactionData transaction)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            if (transaction.Receiver == transaction.Sender) return false;
            if (transaction.Sender == UUID.Zero.ToString()) return false;

            int time = (int)((DateTime.UtcNow.Ticks - TicksToEpoch) / 10000000);
            try
            {
                return dbm.Manager.setTotalSale(transaction.Receiver, transaction.ObjectUUID, transaction.Type, 1, transaction.Amount, time);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.setTotalSale(transaction.Receiver, transaction.ObjectUUID, transaction.Type, 1, transaction.Amount, time);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Adds the transaction.</summary>
        /// <param name="transaction">The transaction.</param>
        public bool addTransaction(TransactionData transaction)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.addTransaction(transaction);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.addTransaction(transaction);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Adds the user.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="balance">The balance.</param>
        /// <param name="status">The status.</param>
        /// <param name="type">The type.</param>
        public bool addUser(string userID, int balance, int status, int type)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            TransactionData transaction = new TransactionData();
            transaction.TransUUID = UUID.Random();
            transaction.Sender = UUID.Zero.ToString();
            transaction.Receiver = userID;
            transaction.Amount = balance;
            transaction.ObjectUUID = UUID.Zero.ToString();
            transaction.ObjectName = string.Empty;
            transaction.RegionHandle = string.Empty;
            transaction.Type = (int)TransactionType.BirthGift;
            transaction.Time = (int)((DateTime.UtcNow.Ticks - TicksToEpoch) / 10000000); ;
            transaction.Status = (int)Status.PENDING_STATUS;
            transaction.SecureCode = UUID.Random().ToString();
            transaction.CommonName = string.Empty;
            transaction.Description = "addUser " + DateTime.UtcNow.ToString();

            bool ret = addTransaction(transaction);
            if (!ret)
            {
                dbm.Release();
                return false;
            }

            try
            {
                ret = dbm.Manager.addUser(userID, 0, status, type);		// make Balance Table
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                ret = dbm.Manager.addUser(userID, 0, status, type);     // make Balance Table
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }

            //
            if (ret) ret = giveMoney(transaction.TransUUID, userID, balance);
            return ret;
        }

        /// <summary>Updates the transaction status.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        /// <param name="status">The status.</param>
        /// <param name="description">The description.</param>
        public bool updateTransactionStatus(UUID transactionID, int status, string description)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.updateTransactionStatus(transactionID, status, description);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.updateTransactionStatus(transactionID, status, description);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Sets the trans expired.</summary>
        /// <param name="deadTime">The dead time.</param>
        public bool SetTransExpired(int deadTime)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.SetTransExpired(deadTime);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.SetTransExpired(deadTime);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Validates the transfer.</summary>
        /// <param name="secureCode">The secure code.</param>
        /// <param name="transactionID">The transaction identifier.</param>
        public bool ValidateTransfer(string secureCode, UUID transactionID)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.ValidateTransfer(secureCode, transactionID);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.ValidateTransfer(secureCode, transactionID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Fetches the transaction.</summary>
        /// <param name="transactionID">The transaction identifier.</param>
        public TransactionData FetchTransaction(UUID transactionID)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.FetchTransaction(transactionID);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.FetchTransaction(transactionID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return null;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Fetches the transaction.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        /// <param name="lastIndex">The last index.</param>
        public TransactionData FetchTransaction(string userID, int startTime, int endTime, int lastIndex)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            TransactionData[] arrTransaction;

            uint index = 0;
            if (lastIndex >= 0) index = Convert.ToUInt32(lastIndex) + 1;

            try
            {
                arrTransaction = dbm.Manager.FetchTransaction(userID, startTime, endTime, index, 1);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                arrTransaction = dbm.Manager.FetchTransaction(userID, startTime, endTime, index, 1);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return null;
            }
            finally
            {
                dbm.Release();
            }

            //
            if (arrTransaction.Length > 0)
            {
                return arrTransaction[0];
            }
            else
            {
                return null;
            }
        }

        // Pseudocode-Plan:
        // 1. Prüfe in DoTransfer, ob Sender und Empfänger identisch sind.
        // 2. Logge einen Fehler und breche die Transaktion ab, falls dies der Fall ist.
        // 3. Ergänze die Logik, damit der Fehler eindeutig im Log erscheint.

        public bool DoTransfer(UUID transactionUUID)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            bool do_trans = false;

            TransactionData transaction = new TransactionData();
            transaction = FetchTransaction(transactionUUID);

            if (transaction != null && transaction.Status == (int)Status.PENDING_STATUS)
            {
                // NEU: Prüfe auf Selbst-Transfer
                if (transaction.Sender == transaction.Receiver)
                {
                    m_log.ErrorFormat("[MONEY DB]: DoTransfer: Transfer von {0} zu sich selbst ist nicht erlaubt.", transaction.Sender);
                    dbm.Release();
                    return false;
                }

                int balance = getBalance(transaction.Sender);

                //check the amount
                if (transaction.Amount >= 0 && balance >= transaction.Amount)
                {
                    if (withdrawMoney(transactionUUID, transaction.Sender, transaction.Amount))
                    {
                        //If receiver not found, add it to DB.
                        if (getBalance(transaction.Receiver) == -1)
                        {
                            m_log.ErrorFormat("[MONEY DB]: DoTransfer: Receiver not found in balances table. {0}", transaction.Receiver);
                            dbm.Release();
                            return false;
                        }

                        if (giveMoney(transactionUUID, transaction.Receiver, transaction.Amount))
                        {
                            do_trans = true;
                        }
                        else
                        {	// give money to receiver failed. Refund Processing
                            m_log.ErrorFormat("[MONEY DB]: Give money to receiver {0} failed", transaction.Receiver);
                            //Return money to sender
                            if (giveMoney(transactionUUID, transaction.Sender, transaction.Amount))
                            {
                                m_log.ErrorFormat("[MONEY DB]: give money to receiver {0} failed but return it to sender {1} successfully", transaction.Receiver, transaction.Sender);
                                updateTransactionStatus(transactionUUID, (int)Status.FAILED_STATUS, "give money to receiver failed but return it to sender successfully");
                            }
                            else
                            {
                                m_log.ErrorFormat("[MONEY DB]: FATAL ERROR: Money withdrawn from sender: {0}, but failed to be given to receiver {1}",
                                                        transaction.Sender, transaction.Receiver);
                                updateTransactionStatus(transactionUUID, (int)Status.ERROR_STATUS, "give money to receiver failed, and return it to sender unsuccessfully!!!");
                            }
                        }
                    }
                    else
                    {	// withdraw money failed
                        m_log.ErrorFormat("[MONEY DB]: Withdraw money from sender {0} failed", transaction.Sender);
                    }
                }
                else
                {	// not enough balance to finish the transaction
                    m_log.ErrorFormat("[MONEY DB]: Not enough balance for user: {0} to apply the transaction.", transaction.Sender);
                }
            }
            else
            {	// Can not fetch the transaction or it has expired
                m_log.ErrorFormat("[MONEY DB]: The transaction:{0} has expired", transactionUUID.ToString());
            }

            //
            if (do_trans)
            {
                setTotalSale(transaction);
            }

            dbm.Release();

            return do_trans;
        }

        // by Fumi.Iseki
        /// <summary>Does the add money.</summary>
        /// <param name="transactionUUID">The transaction UUID.</param>
        public bool DoAddMoney(UUID transactionUUID)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            TransactionData transaction = new TransactionData();
            transaction = FetchTransaction(transactionUUID);

            if (transaction != null && transaction.Status == (int)Status.PENDING_STATUS)
            {
                //If receiver not found, add it to DB.
                if (getBalance(transaction.Receiver) == -1)
                {
                    m_log.ErrorFormat("[MONEY DB]: DoAddMoney: Receiver not found in balances table. {0}", transaction.Receiver);
                    dbm.Release();
                    return false;
                }
                //
                if (giveMoney(transactionUUID, transaction.Receiver, transaction.Amount))
                {
                    setTotalSale(transaction);
                    dbm.Release();
                    return true;
                }
                else
                {	// give money to receiver failed.
                    m_log.ErrorFormat("[MONEY DB]: Add money to receiver {0} failed", transaction.Receiver);
                    updateTransactionStatus(transactionUUID, (int)Status.FAILED_STATUS, "add money to receiver failed");
                }
            }
            else
            {	// Can not fetch the transaction or it has expired
                m_log.ErrorFormat("[MONEY DB]: The transaction:{0} has expired", transactionUUID.ToString());
            }

            dbm.Release();

            return false;
        }

        /// <summary>Tries the add user information.</summary>
        /// <param name="user">The user.</param>
        public bool TryAddUserInfo(UserInfo user)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            UserInfo userInfo = null;

            try
            {
                userInfo = dbm.Manager.fetchUserInfo(user.UserID);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                userInfo = dbm.Manager.fetchUserInfo(user.UserID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                dbm.Release();
                return false;
            }

            try
            {
                if (userInfo != null)
                {
                    //m_log.InfoFormat("[MONEY DB]: Found user \"{0}\", now update information", user.Avatar);
                    if (dbm.Manager.updateUserInfo(user)) return true;
                }
                else if (dbm.Manager.addUserInfo(user))
                {
                    //m_log.InfoFormat("[MONEY DB]: Unable to find user \"{0}\", add it to DB successfully", user.Avatar);
                    return true;
                }
                m_log.InfoFormat("[MONEY DB]: WARNNING: TryAddUserInfo: Unable to TryAddUserInfo.");
                return false;
            }
            catch (Exception e)
            {
                m_log.Error(e.ToString());
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Fetches the user information.</summary>
        /// <param name="userID">The user identifier.</param>
        public UserInfo FetchUserInfo(string userID)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            UserInfo userInfo = null;

            try
            {
                userInfo = dbm.Manager.fetchUserInfo(userID);
                return userInfo;
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                userInfo = dbm.Manager.fetchUserInfo(userID);
                return userInfo;
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return null;
            }
            finally
            {
                dbm.Release();
            }
        }

        /// <summary>Gets the transaction number.</summary>
        /// <param name="userID">The user identifier.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="endTime">The end time.</param>
        public int getTransactionNum(string userID, int startTime, int endTime)
        {
            MySQLSuperManager dbm = GetLockedConnection();

            try
            {
                return dbm.Manager.getTransactionNum(userID, startTime, endTime);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.getTransactionNum(userID, startTime, endTime);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return -1;
            }
            finally
            {
                dbm.Release();
            }
        }

        public bool UserExists(string userID)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.UserExists(userID);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.UserExists(userID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        public bool UpdateUserInfo(string userID, UserInfo updatedInfo)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.UpdateUserInfo(userID, updatedInfo);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.UpdateUserInfo(userID, updatedInfo);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        public bool DeleteUser(string userID)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.DeleteUser(userID);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.DeleteUser(userID);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return false;
            }
            finally
            {
                dbm.Release();
            }
        }

        public void LogTransactionError(UUID transactionID, string errorMessage)
        {
            m_log.ErrorFormat("[MONEY DB]: Transaction {0} failed with error: {1}", transactionID, errorMessage);
        }

        public IEnumerable<TransactionData> GetTransactionHistory(string userID, int startTime, int endTime)
        {
            MySQLSuperManager dbm = GetLockedConnection();
            try
            {
                return dbm.Manager.GetTransactionHistory(userID, startTime, endTime);
            }
            catch (MySql.Data.MySqlClient.MySqlException e)
            {
                e.ToString();
                dbm.Manager.Reconnect();
                return dbm.Manager.GetTransactionHistory(userID, startTime, endTime);
            }
            catch (Exception e)
            {
                m_log.Error(e);
                return new List<TransactionData>(); // Return an empty list instead of throwing an exception
            }
            finally
            {
                dbm.Release();
            }
        }


        public bool PerformMoneyTransfer(string senderID, string receiverID, int amount)
        {
            m_log.InfoFormat("[MONEY TRANSFER]: Transferring {0} from {1} to {2}.", amount, senderID, receiverID);

            // Beispielhafte Implementierung: F�hre den Geldtransfer durch
            try
            {
                MySQLSuperManager dbm = GetLockedConnection();
                string sql = "UPDATE balances SET balance = balance - ?amount WHERE user = ?senderID; " +
                             "UPDATE balances SET balance = balance + ?amount WHERE user = ?receiverID";

                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?amount", amount);
                    cmd.Parameters.AddWithValue("?senderID", senderID);
                    cmd.Parameters.AddWithValue("?receiverID", receiverID);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    bool result = (rowsAffected > 0);

                    if (result)
                    {
                        LogTransaction(UUID.Random(), senderID, -amount);
                        LogTransaction(UUID.Random(), receiverID, amount);
                    }

                    dbm.Release();

                    return result;
                }
            }
            catch (Exception)
            {
                //m_log.ErrorFormat("[MONEY TRANSFER]: Error transferring money: {0}", ex.Message);
                return false;
            }
        }

        public void InitializeUserCurrency(string agentId)
        {
            m_log.InfoFormat("[INITIALIZE USER CURRENCY]: Initializing currency for new user: {0}", agentId);
            int realMoney = 1000; // Beispielwert oder aus der MoneyServer.ini geladen
            int gameMoney = 10000; // Beispielwert oder aus der MoneyServer.ini geladen

            try
            {
                MySQLSuperManager dbm = GetLockedConnection();
                string sql = "INSERT INTO balances (user, balance) VALUES (?agentId, ?realMoney), (?agentId, ?gameMoney)";
                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?agentId", agentId);
                    cmd.Parameters.AddWithValue("?realMoney", realMoney);
                    cmd.Parameters.AddWithValue("?gameMoney", gameMoney);

                    cmd.ExecuteNonQuery();
                }
                m_log.InfoFormat("[INITIALIZE USER CURRENCY]: User {0} initialized with {1}� and {2}L$.", agentId, realMoney, gameMoney);
                dbm.Release();
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[INITIALIZE USER CURRENCY]: Error initializing user currency: {0}", ex.Message);
            }

        }

        public Hashtable ApplyFallbackCredit(string agentId)
        {
            m_log.WarnFormat("[FALLBACK CREDIT]: Applying fallback credit for user {0}", agentId);

            // Beispielhafte Implementierung: Fallback-Gutschrift in die Datenbank eintragen
            try
            {
                MySQLSuperManager dbm = GetLockedConnection();
                string sql = "UPDATE balances SET balance = balance + 100 WHERE user = ?agentId";
                using (MySqlCommand cmd = new MySqlCommand(sql, dbm.Manager.dbcon))
                {
                    cmd.Parameters.AddWithValue("?agentId", agentId);

                    cmd.ExecuteNonQuery();
                }
                dbm.Release();
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat("[FALLBACK CREDIT]: Error applying fallback credit: {0}", ex.Message);
            }

            return new Hashtable
            {
                { "success", true },
                { "creditedAmount", 100 },
                { "message", "Fallback credit applied due to transaction failure." }
            };

        }

    }
}
