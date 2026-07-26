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

using Nini.Config;

using NSL.Certificate.Tools;

using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Grid.MoneyServer;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Reflection;
using System.Threading;
using System.Timers;

using Timer = System.Timers.Timer;


/// <summary>
/// OpenSim Grid MoneyServer
/// </summary>
internal class MoneyServerBase : BaseOpenSimServer, IMoneyServiceCore
{
    private MoneyDBService dbService;

    private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

    private string connectionString = string.Empty;
    private uint m_moneyServerPort = 8008;         // 8008 is default server port
    private Timer checkTimer;

    // --- OpenSim Continuum addition: Stipends ---
    // Uses deterministic per-avatar/per-cycle transaction IDs and the existing
    // addTransaction/DoAddMoney/UpdateBalance path. The transaction ledger is
    // authoritative for retry safety; the local state file is only an optimization.
    private Timer m_stipendTimer;
    private bool m_stipendEnabled = false;
    private int m_stipendAmount = 0;
    private int m_stipendIntervalDays = 7;
    private DateTime m_stipendAnchorDateUtc = new DateTime(1970, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    private string m_stipendDescription = "Weekly stipend";
    private List<string> m_stipendEligibleAvatars = new List<string>();
    private readonly string m_stipendStateFile = Path.Combine(AppContext.BaseDirectory, "stipend_lastcycle.txt");
    private int m_stipendRunActive;

    private string m_certFilename = "";
    private string m_certPassword = "";
    private string m_cacertFilename = "";
    private string m_clcrlFilename = "";
    private bool m_checkClientCert = false;

    private int DEAD_TIME = 120;
    private int MAX_DB_CONNECTION = 10; // 10 is default

    // Testbereich
    // Maximum pro Tag:
    private int m_TotalDay = 100;
    // Maximum pro Woche:
    private int m_TotalWeek = 250;
    // Maximum pro Monat:
    private int m_TotalMonth = 500;
    // Maximum Besitz:
    private int m_CurrencyMaximum = 10000;
    // Geldkauf abschalten:
    private string m_CurrencyOnOff = "off";
    // Geldkauf nur f�r Gruppe:
    private bool m_CurrencyGroupOnly = false;
    private string m_CurrencyGroupName = "";


    private MoneyXmlRpcModule m_moneyXmlRpcModule;
    private MoneyDBService m_moneyDBService;

    private NSLCertificateVerify m_certVerify = new NSLCertificateVerify(); // Client Certificate

    private Dictionary<string, string> m_sessionDic = new Dictionary<string, string>();
    private Dictionary<string, string> m_secureSessionDic = new Dictionary<string, string>();
    private Dictionary<string, string> m_webSessionDic = new Dictionary<string, string>();

    IConfig m_server_config;
    IConfig m_cert_config;


    public MoneyServerBase()
    {
        try
        {
            // Initialize the console for the Money Server
            m_console = new LocalConsole("MoneyServer ");

            if (m_console != null)
            {
                // Set the main console instance to the Money Server console
                MainConsole.Instance = m_console;

                // Log a message to indicate that the Money Server is initializing
                m_log?.Info("[MONEY SERVER]: Initializing Money Server module and loading configurations...");
            }
            else
            {
                throw new InvalidOperationException("Failed to initialize LocalConsole instance.");
            }
        }
        catch (Exception ex)
        {
            // Log the exception
            m_log?.Error("An error occurred during MoneyServerBase initialization.", ex);
            throw;
        }
    }

    /// <summary>
    /// Work
    /// </summary>
    public void Work()
    {
        // Create a new timer to check transactions every 60 seconds
        checkTimer = new Timer
        {
            Interval = 60 * 1000,
            Enabled = true
        };

        // Add event handler to check transactions
        checkTimer.Elapsed += CheckTransaction;

        // Stipends are checked hourly. Each avatar/cycle uses a deterministic
        // transaction UUID, so retries and restarts cannot credit the same cycle twice.
        if (m_stipendEnabled)
        {
            m_stipendTimer = new Timer
            {
                Interval = 60 * 60 * 1000,
                AutoReset = true,
                Enabled = false
            };
            m_stipendTimer.Elapsed += CheckStipends;
        }

        try
        {
            // Start the timers.
            checkTimer.Start();
            if (m_stipendEnabled)
            {
                m_stipendTimer.Start();
                CheckStipends(null, null);
            }

            // Run the console prompt loop
            while (true)
            {
                m_console.Prompt();
            }
        }
        catch (Exception ex)
        {
            // Log any exceptions that occur
            m_log.ErrorFormat("Error in Work: {0}", ex.Message);
        }
        finally
        {
            // Stop the timer if it's still running
            if (checkTimer != null && checkTimer.Enabled)
            {
                checkTimer.Stop();
                checkTimer.Dispose();
            }
            if (m_stipendTimer != null && m_stipendTimer.Enabled)
            {
                m_stipendTimer.Stop();
                m_stipendTimer.Dispose();
            }
        }
    }

    /// <summary>
    /// Checks transactions.
    /// </summary>
    private void CheckTransaction(object sender, ElapsedEventArgs e)
    {
        if (m_moneyDBService == null)
        {
            m_log.Error("[CHECK TRANSACTION]: m_moneyDBService is null, cannot check transactions.");
            return;
        }

        try
        {
            long ticksToEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            int unixEpochTime = (int)((DateTime.UtcNow.Ticks - ticksToEpoch) / 10000000);
            int deadTime = unixEpochTime - DEAD_TIME;
            m_moneyDBService.SetTransExpired(deadTime);

            //m_log.Info("[CHECK TRANSACTION]: Transactions checked successfully.");
        }
        catch (Exception ex)
        {
            m_log.ErrorFormat("[CHECK TRANSACTION]: Error in CheckTransaction: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Checks the current deterministic stipend cycle hourly. The transaction
    /// ledger is the idempotency authority; the state file only suppresses work
    /// after an entire cycle has completed successfully.
    /// </summary>
    private void CheckStipends(object sender, ElapsedEventArgs e)
    {
        if (!m_stipendEnabled || m_moneyXmlRpcModule == null ||
            m_stipendAmount <= 0 || m_stipendEligibleAvatars.Count == 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref m_stipendRunActive, 1) != 0)
        {
            m_log.Warn("[STIPEND]: Previous stipend check is still running; this timer tick was skipped.");
            return;
        }

        try
        {
            string cycleKey = GetCurrentStipendCycleKey(DateTime.UtcNow);
            string completedCycle = ReadCompletedStipendCycle();
            if (string.Equals(completedCycle, cycleKey, StringComparison.Ordinal))
                return;

            m_log.InfoFormat(
                "[STIPEND]: Processing cycle {0}: {1} for {2} avatar(s).",
                cycleKey,
                m_stipendAmount,
                m_stipendEligibleAvatars.Count);

            bool completed = m_moneyXmlRpcModule.GrantStipends(
                m_stipendAmount,
                m_stipendDescription,
                m_stipendEligibleAvatars,
                cycleKey);

            if (completed)
            {
                WriteCompletedStipendCycle(cycleKey);
                m_log.InfoFormat("[STIPEND]: Cycle {0} completed.", cycleKey);
            }
            else
            {
                m_log.WarnFormat(
                    "[STIPEND]: Cycle {0} was not fully completed and will be retried. " +
                    "Already-successful avatar transactions will be skipped safely.",
                    cycleKey);
            }
        }
        catch (Exception ex)
        {
            m_log.ErrorFormat("[STIPEND]: Error in CheckStipends: {0}", ex);
        }
        finally
        {
            Interlocked.Exchange(ref m_stipendRunActive, 0);
        }
    }

    private string GetCurrentStipendCycleKey(DateTime utcNow)
    {
        DateTime anchor = m_stipendAnchorDateUtc.Date;
        int intervalDays = Math.Max(1, m_stipendIntervalDays);
        long elapsedDays = (long)Math.Floor((utcNow.Date - anchor).TotalDays);
        long cycleIndex;

        if (elapsedDays >= 0)
            cycleIndex = elapsedDays / intervalDays;
        else
            cycleIndex = -((-elapsedDays + intervalDays - 1) / intervalDays);

        DateTime cycleStart = anchor.AddDays(cycleIndex * intervalDays);
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0:yyyy-MM-dd}/{1}d",
            cycleStart,
            intervalDays);
    }

    private string ReadCompletedStipendCycle()
    {
        try
        {
            if (!File.Exists(m_stipendStateFile))
                return string.Empty;

            return File.ReadAllText(m_stipendStateFile).Trim();
        }
        catch (Exception ex)
        {
            // The transaction ledger remains the idempotency authority. A missing
            // or unreadable state file can cause a retry, but not a duplicate credit.
            m_log.WarnFormat(
                "[STIPEND]: Could not read state file {0}: {1}",
                m_stipendStateFile,
                ex.Message);
            return string.Empty;
        }
    }

    private void WriteCompletedStipendCycle(string cycleKey)
    {
        string temporaryFile = m_stipendStateFile + ".tmp";
        File.WriteAllText(temporaryFile, cycleKey + Environment.NewLine);
        File.Move(temporaryFile, m_stipendStateFile, true);
    }

    /// <summary>
    /// Startup Specific
    /// </summary>
    protected override void StartupSpecific()
    {
        m_log.Info("[MONEY SERVER]: Setup HTTP Server process");

        ReadIniConfig();

        // MoneyServer creates its own LocalConsole, so it must also connect the
        // standard OpenSim Console appender to that console.  LocalConsole.Output()
        // then clears the active prompt, writes asynchronous output, and redraws
        // "MoneyServer #" beneath it instead of printing through the prompt line.
        RegisterCommonAppenders(Config.Configs["Startup"]);

        try
        {
            if (m_certFilename != "")
            {
                m_httpServer = new BaseHttpServer(m_moneyServerPort, true, m_certFilename, m_certPassword);
                m_httpServer.CertificateValidationCallback = null;
                //
                if (m_checkClientCert)
                {
                    m_httpServer.CertificateValidationCallback = (RemoteCertificateValidationCallback)m_certVerify.ValidateClientCertificate;
                    m_log.Info("[MONEY SERVER]: Set RemoteCertificateValidationCallback");
                }
            }
            else
            {
                m_httpServer = new BaseHttpServer(m_moneyServerPort);
            }

            SetupMoneyServices();
            m_httpServer.Start();
            base.StartupSpecific();         // OpenSim/Framework/Servers/BaseOpenSimServer.cs
        }

        catch (Exception e)
        {
            m_log.ErrorFormat("[MONEY SERVER]: StartupSpecific: Fail to start HTTPS process");
            m_log.ErrorFormat("[MONEY SERVER]: StartupSpecific: Please Check Certificate File or Password. Exit");
            m_log.ErrorFormat("[MONEY SERVER]: StartupSpecific: {0}", e);
            Environment.Exit(1);
        }
    }

    public void ReadIniConfig()
    {
        MoneyServerConfigSource moneyConfig = new MoneyServerConfigSource();
        Config = moneyConfig.m_config;

        try
        {
            // [Startup]
            IConfig st_config = moneyConfig.m_config.Configs["Startup"];
            string PIDFile = st_config.GetString("PIDFile", "");
            if (PIDFile != "") Create_PIDFile(PIDFile);

            // [MySql]
            IConfig db_config = moneyConfig.m_config.Configs["MySql"];
            string sqlserver = db_config.GetString("hostname", "localhost");
            string database = db_config.GetString("database", "OpenSim");
            string username = db_config.GetString("username", "root");
            string password = db_config.GetString("password", "password");
            string pooling = db_config.GetString("pooling", "false");
            string port = db_config.GetString("port", "3306");
            MAX_DB_CONNECTION = db_config.GetInt("MaxConnection", MAX_DB_CONNECTION);

            connectionString = "Server=" + sqlserver + ";Port=" + port + ";Database=" + database + ";User ID=" +
                                        username + ";Password=" + password + ";Pooling=" + pooling + ";";

            // [MoneyServer]
            m_server_config = moneyConfig.m_config.Configs["MoneyServer"];
            DEAD_TIME = m_server_config.GetInt("ExpiredTime", DEAD_TIME);
            m_moneyServerPort = (uint)m_server_config.GetInt("ServerPort", (int)m_moneyServerPort);

            /*
            ; Testbereich
            ; Maximum pro Tag:
            TotalDay = 100;
            ; Maximum pro Woche:
            TotalWeek = 250;
            ; Maximum pro Monat:
            TotalMonth = 500;
            */

            m_TotalDay = m_server_config.GetInt("TotalDay", m_TotalDay);
            m_TotalWeek = m_server_config.GetInt("TotalWeek", m_TotalWeek);
            m_TotalMonth = m_server_config.GetInt("TotalMonth", m_TotalMonth);
            m_CurrencyMaximum = m_server_config.GetInt("CurrencyMaximum", m_CurrencyMaximum);

            m_CurrencyOnOff = m_server_config.GetString("CurrencyOnOff", m_CurrencyOnOff);
            m_CurrencyGroupOnly = m_server_config.GetBoolean("CurrencyGroupOnly", m_CurrencyGroupOnly);
            m_CurrencyGroupName = m_server_config.GetString("CurrencyGroupName", m_CurrencyGroupName);

            //
            // [Stipend] - off by default. Each configured cycle is recorded
            // with deterministic per-avatar transaction IDs.
            IConfig stipend_config = moneyConfig.m_config.Configs["Stipend"];
            if (stipend_config != null)
            {
                m_stipendEnabled = stipend_config.GetBoolean("Enabled", false);
                m_stipendAmount = stipend_config.GetInt("Amount", 0);
                m_stipendIntervalDays = stipend_config.GetInt("IntervalDays", 7);
                m_stipendDescription = stipend_config.GetString("Description", "Weekly stipend");

                string anchorValue = stipend_config.GetString("AnchorDateUtc", "1970-01-05").Trim();
                if (!DateTime.TryParseExact(
                    anchorValue,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                        System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out m_stipendAnchorDateUtc))
                {
                    m_log.WarnFormat(
                        "[MONEY SERVER]: Invalid [Stipend] AnchorDateUtc '{0}'; using 1970-01-05.",
                        anchorValue);
                    m_stipendAnchorDateUtc = new DateTime(1970, 1, 5, 0, 0, 0, DateTimeKind.Utc);
                }

                HashSet<string> uniqueAvatars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string avatarList = stipend_config.GetString("EligibleAvatars", "");
                foreach (string configuredUUID in avatarList.Split(','))
                {
                    string trimmed = configuredUUID.Trim();
                    if (trimmed.Length == 0)
                        continue;

                    if (!OpenMetaverse.UUID.TryParse(trimmed, out OpenMetaverse.UUID avatarID) ||
                        avatarID == OpenMetaverse.UUID.Zero)
                    {
                        m_log.WarnFormat(
                            "[MONEY SERVER]: Ignoring invalid [Stipend] EligibleAvatars UUID: {0}",
                            trimmed);
                        continue;
                    }

                    uniqueAvatars.Add(avatarID.ToString());
                }
                m_stipendEligibleAvatars = new List<string>(uniqueAvatars);

                if (m_stipendIntervalDays < 1)
                {
                    m_log.Error("[MONEY SERVER]: [Stipend] IntervalDays must be at least 1; stipends have been disabled.");
                    m_stipendEnabled = false;
                }

                if (m_stipendEnabled &&
                    (m_stipendAmount <= 0 || m_stipendEligibleAvatars.Count == 0))
                {
                    m_log.Error(
                        "[MONEY SERVER]: [Stipend] Enabled=true requires Amount > 0 and at least one valid EligibleAvatars UUID; stipends have been disabled.");
                    m_stipendEnabled = false;
                }
                else if (m_stipendEnabled)
                {
                    m_log.InfoFormat(
                        "[MONEY SERVER]: Stipends enabled: {0} every {1} day(s), anchored at {2:yyyy-MM-dd} UTC, for {3} avatar(s).",
                        m_stipendAmount,
                        m_stipendIntervalDays,
                        m_stipendAnchorDateUtc,
                        m_stipendEligibleAvatars.Count);
                }
            }



            //
            // [Certificate]
            m_cert_config = moneyConfig.m_config.Configs["Certificate"];
            if (m_cert_config == null)
            {
                m_log.Info("[MONEY SERVER]: [Certificate] section is not found. Using [MoneyServer] section instead");
                m_cert_config = m_server_config;
            }

            // HTTPS Server Cert (Server Mode)
            m_certFilename = m_cert_config.GetString("ServerCertFilename", m_certFilename);
            m_certPassword = m_cert_config.GetString("ServerCertPassword", m_certPassword);
            if (m_certFilename != "")
            {
                m_log.Info("[MONEY SERVER]: ReadIniConfig: Execute HTTPS comunication. Server Cert file is " + m_certFilename);
            }

            // Client Certificate
            m_checkClientCert = m_cert_config.GetBoolean("CheckClientCert", m_checkClientCert);
            m_cacertFilename = m_cert_config.GetString("CACertFilename", m_cacertFilename);
            m_clcrlFilename = m_cert_config.GetString("ClientCrlFilename", m_clcrlFilename);
            if (m_checkClientCert && (m_cacertFilename != ""))
            {
                m_certVerify.SetPrivateCA(m_cacertFilename);
                m_log.Info("[MONEY SERVER]: ReadIniConfig: Execute Authentication of Clients. CA file is " + m_cacertFilename);
            }
            else
            {
                m_checkClientCert = false;
            }
            if (m_checkClientCert)
            {
                if (m_clcrlFilename != "")
                {
                    m_certVerify.SetPrivateCRL(m_clcrlFilename);
                    m_log.Info("[MONEY SERVER]: ReadIniConfig: Execute Authentication of Clients. CRL file is " + m_clcrlFilename);
                }
            }

            // Initialisiere die MoneyDBService mit der Verbindungszeichenkette und der maxDBConnections
            dbService = new MoneyDBService();
            dbService.Initialise(connectionString, MAX_DB_CONNECTION);
        }
        catch (Exception ex)
        {
            m_log.Error("[MONEY SERVER]: ReadIniConfig: Fail to setup configure. Please check MoneyServer.ini. Exit", ex);
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Create PID File added by skidz
    /// </summary>
    protected void Create_PIDFile(string path)
        {
        try
        {
            string pidstring = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
            FileStream fs = File.Create(path);
            System.Text.ASCIIEncoding enc = new System.Text.ASCIIEncoding();
            Byte[] buf = enc.GetBytes(pidstring);
            fs.Write(buf, 0, buf.Length);
            fs.Close();
            m_pidFile = path;
        }

        catch (Exception) { }

    }

    protected virtual void SetupMoneyServices()
    {
        m_log.Info("[MONEY SERVER]: Connecting to Money Storage Server");

        m_moneyDBService = new MoneyDBService();
        m_moneyDBService.Initialise(connectionString, MAX_DB_CONNECTION);

        IConfigSource config = new IniConfigSource(); // Beispiel f�r das Erstellen einer IConfigSource
        m_moneyXmlRpcModule = new MoneyXmlRpcModule(connectionString, MAX_DB_CONNECTION);
        m_moneyXmlRpcModule.Initialise(m_version, m_moneyDBService, this, config);
        m_moneyXmlRpcModule.PostInitialise();
    }

    public bool IsCheckClientCert()
    {
        return m_checkClientCert;
    }

    public IConfig GetServerConfig()
    {
        return m_server_config;
    }

    public IConfig GetCertConfig()
    {
        return m_cert_config;
    }

    public BaseHttpServer GetHttpServer()
    {
        return m_httpServer;
    }

    public Dictionary<string, string> GetSessionDic()
    {
        return m_sessionDic;
    }

    public Dictionary<string, string> GetSecureSessionDic()
    {
        return m_secureSessionDic;
    }

    public Dictionary<string, string> GetWebSessionDic()
    {
        return m_webSessionDic;
    }

    class MoneyServerConfigSource
    {

        public IniConfigSource m_config;

        public MoneyServerConfigSource()
        {
            string configPath = Path.Combine(Directory.GetCurrentDirectory(), "MoneyServer.ini");
            if (File.Exists(configPath))
            {
                m_config = new IniConfigSource(configPath);
            }
        }

        public void Save(string path)
        {
            m_config.Save(path);
        }

    }
}
