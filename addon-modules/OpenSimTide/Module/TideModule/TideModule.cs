using System;
using System.Net;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using CultureInfo = System.Globalization.CultureInfo;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenMetaverse;
using Mono.Addins;



namespace TideModule
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "OpenSimTide")]
    public class OpenSimTide : INonSharedRegionModule
    {
        #region Fields
        private static readonly ILog m_log = LogManager.GetLogger (MethodBase.GetCurrentMethod ().DeclaringType);

        public string Name { get { return m_name; } }
        public Type ReplaceableInterface { get { return null; } }

        private const int TICKS_PER_SECOND = 10000000;

        public string m_name = "OpenSimTide";
        private string m_regionConfigDir = "";
        private uint m_frame = 0;
        private int m_frameUpdateRate = 100;
        private bool m_enabled = false;
        private bool m_ready = false;
         
        private float m_tideLevel = 20.0f;  //current water level in m
        private float m_lowTide = 18.0f;    //low water level in m
        private float m_highTide = 22.0f;   //high water level in m
        private ulong m_cycleTime = 3600;      //low->high->low time in seconds
        private DateTime m_lowTideTime = new DateTime(); // datetime indicating when next low tide will be
        private DateTime m_highTideTime = new DateTime();  // datetime indicating when next hightide will be
        private bool m_hasTideTimes;
        private bool m_tideDirection = true;    // which direction is the tide travelling, 'Coming In'(true) or 'Going Out'(false)
        private bool m_lastTideDirection = true;
        private bool m_tideInfoDebug = false;  //do we chat the tide to the OpenSim console?
        private bool m_tideInfoBroadcast = true; //do we chat the tide to the region?
        private int m_tideInfoChannel = 5555; //chat channel for all tide info
        private int m_tideLevelChannel = 5556; //chat channel for just the tide level in m
        private int m_tideAnnounceCount = 5; //how many times do we announce the turning tide
        private int m_tideAnnounceCounter = 0; //counter we use to count announcements of low or high tide
        private string m_tideAnnounceMsg = "";
        private bool m_restoreWaterOnStop = true;
        private double m_originalWaterHeight;
        private bool m_hasOriginalWaterHeight;
        private readonly object m_lifecycleSync = new object();
        private readonly object m_operationSync = new object();
        private bool m_closed;

        public Scene m_scene;
        public IConfigSource m_config;
        public RegionInfo m_regionInfo;
        public Dictionary<string, Scene> mScene = new Dictionary<string, Scene> ();
        public Vector3 m_shoutPos = new Vector3(128f, 128f, 30f);

        #endregion


        #region IRegionModuleBase implementation
        public void Initialise (IConfigSource source)
        {
            m_config = source;
        }


        public void Close ()
        {
            Scene scene;
            lock (m_lifecycleSync)
            {
                m_closed = true;
                scene = m_scene;
            }
            if (scene != null)
                StopTide(scene);
        }


        public void AddRegion (Scene scene)
        {
            if (scene == null)
                return;

            lock (m_lifecycleSync)
            {
                if (m_closed)
                    return;

                if (ReferenceEquals(m_scene, scene))
                    return;
                if (m_scene != null)
                {
                    m_log.ErrorFormat(
                        "[{0}]: AddRegion called for a second region on a non-shared module instance; request ignored",
                        m_name);
                    return;
                }
            }

            IConfig cnf;
            m_log.InfoFormat("[{0}]: Adding region '{1}' to this module", m_name, scene.RegionInfo.RegionName);

            cnf = m_config.Configs["Startup"];
            m_regionConfigDir = cnf == null
                ? Path.Combine(Util.configDir(), "bin/Regions/")
                : cnf.GetString("regionload_regionsdir", Path.Combine(Util.configDir(), "bin/Regions/"));

            cnf = m_config.Configs[scene.RegionInfo.RegionName];
            
            if(cnf == null)
            {
                m_log.InfoFormat("[{0}]: No region section [{1}] found in addon-modules/{2}/config/*.ini configuration files.", m_name, scene.RegionInfo.RegionName, m_name);
                //string moduleConfigFile = Path.Combine(Util.configDir(),m_name + ".ini");
                string moduleConfigFile = Path.Combine(m_regionConfigDir, "Regions.ini");
                try
                {
                    m_log.InfoFormat("[{0}]: Checking {1} for [{2}] section containing valid config keys", m_name, moduleConfigFile, scene.RegionInfo.RegionName);
                    m_config = new IniConfigSource(moduleConfigFile);
                    cnf = m_config.Configs[scene.RegionInfo.RegionName];
                }
                catch (Exception)
                {
                    cnf = null;
                }
                    
                if (cnf == null)
                {
                    m_log.InfoFormat("[{0}]: No region section [{1}] found in configuration {2}. Tide in this region is set to Disabled", m_name, scene.RegionInfo.RegionName, moduleConfigFile);
                    m_enabled = false;
                    return;
                }
            }
              
            m_enabled = cnf.GetBoolean("TideEnabled", false);
            
            if (m_enabled)
            {
                m_frameUpdateRate = Math.Max(1, cnf.GetInt("TideUpdateRate", 150));
                m_lowTide = cnf.GetFloat("TideLowWater", 18.0f);
                m_highTide = cnf.GetFloat("TideHighWater", 22.0f);
                int configuredCycleTime = cnf.GetInt("TideCycleTime", 3600);
                m_cycleTime = (ulong)Math.Max(10, configuredCycleTime);
                m_tideInfoDebug = cnf.GetBoolean("TideInfoDebug", false);
                m_tideInfoBroadcast = cnf.GetBoolean("TideInfoBroadcast", true);
                m_tideInfoChannel = cnf.GetInt("TideInfoChannel", 5555);
                m_tideLevelChannel = cnf.GetInt("TideLevelChannel", 5556);
                m_tideAnnounceCount = Math.Max(0, cnf.GetInt("TideAnnounceCount", 5));
                m_restoreWaterOnStop = cnf.GetBoolean("TideRestoreWaterOnStop", true);

                if (m_highTide < m_lowTide)
                {
                    float swap = m_lowTide;
                    m_lowTide = m_highTide;
                    m_highTide = swap;
                    m_log.WarnFormat(
                        "[{0}]: TideHighWater was below TideLowWater in {1}; values were swapped",
                        m_name,
                        scene.RegionInfo.RegionName);
                }

                m_log.InfoFormat("[{0}]: Enabled with an update rate every {1} frames, Low Water={2}m, High Water={3}m, Cycle Time={4} secs", m_name, m_frameUpdateRate, m_lowTide, m_highTide, m_cycleTime);
                m_log.InfoFormat("[{0}]: Info Channel={1}, Water Level Channel={2}, Info Broadcast is {3}, Announce Count={4}", m_name, m_tideInfoChannel, m_tideLevelChannel, m_tideInfoBroadcast, m_tideAnnounceCounter);

                lock (m_lifecycleSync)
                {
                    if (m_scene != null)
                    {
                        m_log.ErrorFormat(
                            "[{0}]: Region ownership changed while loading {1}; tide was not attached",
                            m_name,
                            scene.RegionInfo.RegionName);
                        return;
                    }

                    m_frame = 0;
                    m_tideAnnounceCounter = 0;
                    m_tideAnnounceMsg = string.Empty;
                    m_hasTideTimes = false;
                    m_shoutPos = new Vector3(scene.RegionInfo.RegionSizeX / 2f, scene.RegionInfo.RegionSizeY / 2f, 30f);
                    m_scene = scene;
                    m_originalWaterHeight = scene.RegionInfo.RegionSettings.WaterHeight;
                    m_hasOriginalWaterHeight = true;
                    scene.EventManager.OnFrame += TideUpdate;
                    m_ready = true; // Mark Module Ready only after ownership and callback registration are complete.
                }
            }
            else
            {
                m_log.InfoFormat("[{0}]: Tide in this region is set to Disabled", m_name);
            }
        }

        public void RemoveRegion (Scene scene)
        {
            if (scene == null)
                return;

            m_log.InfoFormat("[{0}]: Removing region '{1}' from this module", m_name, scene.RegionInfo.RegionName);
            StopTide(scene);
        }


        public void RegionLoaded (Scene scene)
        {

        }

        #endregion

        #region TideModule
        // Place your methods here
        public void TideUpdate ()
        {
            lock (m_operationSync)
            {
                try
                {
                    UpdateTideState();
                }
                catch (Exception e)
                {
                    Scene scene = m_scene;
                    m_log.WarnFormat(
                        "[{0}]: Tide update failed in {1}: {2}",
                        m_name,
                        scene == null ? "unknown region" : scene.RegionInfo.RegionName,
                        e.Message);
                }
            }
        }

        private void UpdateTideState()
        {
            ulong timeStamp;
            double cyclePos; //cycles from 0.0000000001 to 0.999999999999
            double cycleRadians;
            double tideRange;
            double tideMiddle;
            string tideLevelMsg;
            DateTime now;
           
         if (((m_frame++ % m_frameUpdateRate) != 0) || !m_ready || m_scene == null) {
                return;
            }
            now = DateTime.UtcNow;
            timeStamp = (ulong)now.Ticks;

            cyclePos = (double)(timeStamp % (m_cycleTime * TICKS_PER_SECOND)) / (m_cycleTime * TICKS_PER_SECOND);
            cycleRadians = cyclePos * Math.PI * 2;

            if (cyclePos < 0.5) m_tideDirection = false; else m_tideDirection = true;

            bool directionChanged = m_hasTideTimes && m_tideDirection != m_lastTideDirection;
            if (!m_hasTideTimes || directionChanged)
            { // initialize or re-calculate the next tide times
                if (cyclePos < 0.5)
                { // tide just changed to be high->low
                    m_lowTideTime = now.AddSeconds((double)(m_cycleTime * (0.5 - cyclePos)));
                    m_highTideTime = m_lowTideTime.AddSeconds((double)(m_cycleTime / 2));
                    if (directionChanged)
                        m_tideAnnounceMsg = "High Tide";
                }
                else
                {   //tide just changed to be low->high
                    m_highTideTime = now.AddSeconds((double)(m_cycleTime * (1.0 - cyclePos)));
                    m_lowTideTime = m_highTideTime.AddSeconds((double)(m_cycleTime / 2));
                    if (directionChanged)
                        m_tideAnnounceMsg = "Low Tide";
                }
                m_lastTideDirection = m_tideDirection;
                m_hasTideTimes = true;
            }
            tideRange = (double) (m_highTide - m_lowTide) / 2;
            tideMiddle = (double) m_lowTide + tideRange;
            m_tideLevel = (float) (Math.Cos(cycleRadians) * tideRange + tideMiddle);

            tideLevelMsg = "Current Server Time (UTC): " + now.ToString("T", CultureInfo.InvariantCulture) + "\n";
            tideLevelMsg += "Current Tide Level: " + m_tideLevel.ToString(CultureInfo.InvariantCulture) + "\n";
            tideLevelMsg += "Low Tide Time: " + m_lowTideTime.ToString("T", CultureInfo.InvariantCulture) + "\n";
            tideLevelMsg += "Low Tide Level: " + m_lowTide.ToString(CultureInfo.InvariantCulture) + "\n";
            tideLevelMsg += "High Tide Time: " + m_highTideTime.ToString("T", CultureInfo.InvariantCulture) + "\n";
            tideLevelMsg += "High Tide Level: " + m_highTide.ToString(CultureInfo.InvariantCulture) + "\n";
            tideLevelMsg += "Tide Direction: " + ((m_tideDirection) ? "Coming In" : "Going Out") + "\n";
            tideLevelMsg += "Cycle Position: " + cyclePos.ToString(CultureInfo.InvariantCulture) + "\n";
            if (m_tideAnnounceMsg != "")
            {
                if (m_tideAnnounceCounter >= m_tideAnnounceCount)
                {
                    m_tideAnnounceCounter = 0;
                    m_tideAnnounceMsg = "";
                }
                else
                {
                    tideLevelMsg += "Tide Warning: " + m_tideAnnounceMsg + "\n";
                    m_tideAnnounceCounter++;
                }
            }

            if (m_tideInfoDebug) m_log.InfoFormat("[{0}]: Sea Level currently at {1}m in Region: {2}", m_name, m_tideLevel, m_scene.RegionInfo.RegionName);

            if (m_tideInfoDebug) m_log.InfoFormat("[{0}]: Updating Region: {1}", m_name, m_scene.RegionInfo.RegionName);

            m_scene.RegionInfo.RegionSettings.WaterHeight = m_tideLevel;
            m_scene.EventManager.TriggerRequestChangeWaterHeight(m_tideLevel);
            m_scene.EventManager.TriggerTerrainTick();
            
            if (m_tideInfoBroadcast)
            {
                m_scene.SimChatBroadcast(tideLevelMsg, ChatTypeEnum.Region, m_tideInfoChannel, m_shoutPos, "TIDE", UUID.Zero, false);
                m_scene.SimChatBroadcast(m_tideLevel.ToString(CultureInfo.InvariantCulture), ChatTypeEnum.Region, m_tideLevelChannel, m_shoutPos, "TIDE", UUID.Zero, false);
            }
        }

        private void StopTide(Scene scene)
        {
            if (scene == null)
                return;

            lock (m_lifecycleSync)
            {
                if (!ReferenceEquals(m_scene, scene))
                    return;

                m_ready = false;
                scene.EventManager.OnFrame -= TideUpdate;

                // Wait for an OnFrame callback that already entered the update
                // path before restoring the original height. This makes region
                // removal a real mutation barrier instead of allowing a late
                // callback to overwrite the restored water level.
                lock (m_operationSync)
                {
                    if (m_restoreWaterOnStop && m_hasOriginalWaterHeight)
                    {
                        float original = (float)m_originalWaterHeight;
                        scene.RegionInfo.RegionSettings.WaterHeight = original;
                        scene.EventManager.TriggerRequestChangeWaterHeight(original);
                        scene.EventManager.TriggerTerrainTick();
                        m_log.InfoFormat(
                            "[{0}]: Restored water height to {1:0.###}m in Region: {2}",
                            m_name,
                            original,
                            scene.RegionInfo.RegionName);
                    }
                }

                m_hasOriginalWaterHeight = false;
                m_hasTideTimes = false;
                m_tideAnnounceCounter = 0;
                m_tideAnnounceMsg = string.Empty;
                m_scene = null;
            }
        }
        #endregion
    }
}
