// HoloPhysicsGuard.cs
//
// Copyright 2026 Fiona Sweet <fiona@pobox.holoneon.com>
//
// Region safety module for OpenSimulator.
// Sleeps selected physical root objects when a region is empty, records the
// reversible sleep state in a module-owned MySQL table, and wakes the objects
// when a root agent enters.
//
// Continuum reconciliation 0.5.0:
//   - preserves the upstream HoloPhysicsGuard design and attribution
//   - uses OpenSim's UpdatePrimFlags() path for live and persisted flag changes
//   - does not write directly to OpenSim's prims table
//   - validates Mode and applies conservative allow-list defaults
//   - prevents overlapping timer callbacks
//   - wakes only on an occupied transition instead of querying every timer tick
//   - supports inheriting ConnectionString from [DatabaseService]
//   - avoids deprecated VALUES(column) upsert syntax

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Timers;

using log4net;
using Mono.Addins;
using MySql.Data.MySqlClient;
using Nini.Config;
using OpenMetaverse;

using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

[assembly: Addin("HoloPhysicsGuard", "0.5.0")]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.VersionNumber)]

namespace HoloNeon.RegionModules
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "HoloPhysicsGuard")]
    public class HoloPhysicsGuard : INonSharedRegionModule
    {
        private const string ModuleVersion = "0.5.0";

        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly List<Scene> m_scenes = new List<Scene>();
        private readonly object m_operationLock = new object();

        private readonly Dictionary<UUID, DateTime> m_regionBecameEmptyAt =
            new Dictionary<UUID, DateTime>();

        private readonly Dictionary<UUID, bool> m_regionWasOccupied =
            new Dictionary<UUID, bool>();

        private bool m_enabled;
        private bool m_dryRun = true;
        private bool m_sleepWhenEmpty = true;
        private bool m_zeroVelocities = true;
        private bool m_verbose;
        private bool m_wakeOnStart;
        private bool m_wakeOnAvatarEnter = true;
        private bool m_autoCreateTable = true;
        private bool m_allowAllPhysicalObjects;

        private int m_checkIntervalSeconds = 30;
        private int m_emptyDelaySeconds = 60;
        private int m_timerRunning;

        private string m_mode = "ReportOnly";
        private string m_connectionString = String.Empty;
        private string m_connectionSource = "none";

        private string[] m_alwaysSleepNameContains = Array.Empty<string>();
        private string[] m_neverSleepNameContains = Array.Empty<string>();

        private System.Timers.Timer m_timer;

        public string Name
        {
            get { return "HoloPhysicsGuard"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["HoloPhysicsGuard"];
            if (config == null)
                return;

            m_enabled = config.GetBoolean("Enabled", false);
            if (!m_enabled)
            {
                m_log.Info("[HOLO PHYSICS GUARD]: Config found but Enabled=false; module disabled");
                return;
            }

            m_dryRun = config.GetBoolean("DryRun", true);
            m_sleepWhenEmpty = config.GetBoolean("SleepWhenEmpty", true);
            m_zeroVelocities = config.GetBoolean("ZeroVelocities", true);
            m_verbose = config.GetBoolean("Verbose", false);
            m_wakeOnStart = config.GetBoolean("WakeOnStart", false);
            m_wakeOnAvatarEnter = config.GetBoolean("WakeOnAvatarEnter", true);
            m_autoCreateTable = config.GetBoolean("AutoCreateTable", true);
            m_allowAllPhysicalObjects = config.GetBoolean("AllowAllPhysicalObjects", false);

            m_checkIntervalSeconds = Math.Max(
                5,
                config.GetInt("CheckIntervalSeconds", 30)
            );

            m_emptyDelaySeconds = Math.Max(
                0,
                config.GetInt("EmptyDelaySeconds", 60)
            );

            string configuredMode = config.GetString("Mode", "ReportOnly").Trim();
            if (m_dryRun)
            {
                m_mode = "ReportOnly";
            }
            else if (String.Equals(configuredMode, "ReportOnly", StringComparison.OrdinalIgnoreCase))
            {
                m_mode = "ReportOnly";
            }
            else if (String.Equals(configuredMode, "PersistSleep", StringComparison.OrdinalIgnoreCase))
            {
                m_mode = "PersistSleep";
            }
            else
            {
                m_log.ErrorFormat(
                    "[HOLO PHYSICS GUARD]: Invalid Mode '{0}'. Supported values are ReportOnly and PersistSleep; module disabled",
                    configuredMode
                );
                m_enabled = false;
                return;
            }

            m_alwaysSleepNameContains = SplitList(
                config.GetString("AlwaysSleepNameContains", String.Empty)
            );

            m_neverSleepNameContains = SplitList(
                config.GetString("NeverSleepNameContains", String.Empty)
            );

            if (m_alwaysSleepNameContains.Length == 0 && !m_allowAllPhysicalObjects)
            {
                m_log.Warn(
                    "[HOLO PHYSICS GUARD]: No AlwaysSleepNameContains entries and AllowAllPhysicalObjects=false; no objects are eligible for sleep"
                );
            }

            if (IsPersistMode())
            {
                ResolveConnectionString(source, config);

                if (String.IsNullOrWhiteSpace(m_connectionString))
                {
                    m_log.Error(
                        "[HOLO PHYSICS GUARD]: PersistSleep requires ConnectionString in [HoloPhysicsGuard] or [DatabaseService]; module disabled"
                    );
                    m_enabled = false;
                    return;
                }

                if (m_autoCreateTable)
                {
                    try
                    {
                        EnsureTable();
                    }
                    catch (Exception ex)
                    {
                        m_log.ErrorFormat(
                            "[HOLO PHYSICS GUARD]: Failed creating/checking module table: {0}",
                            ex
                        );
                        m_enabled = false;
                        return;
                    }
                }
            }

            m_timer = new System.Timers.Timer(m_checkIntervalSeconds * 1000);
            m_timer.Elapsed += OnTimer;
            m_timer.AutoReset = true;
            m_timer.Start();

            m_log.InfoFormat(
                "[HOLO PHYSICS GUARD]: Enabled v{0}. Mode={1}, DryRun={2}, WakeOnStart={3}, WakeOnAvatarEnter={4}, CheckInterval={5}s, EmptyDelay={6}s, AllowAll={7}, ConnectionSource={8}",
                ModuleVersion,
                m_mode,
                m_dryRun,
                m_wakeOnStart,
                m_wakeOnAvatarEnter,
                m_checkIntervalSeconds,
                m_emptyDelaySeconds,
                m_allowAllPhysicalObjects,
                m_connectionSource
            );
        }

        public void AddRegion(Scene scene)
        {
            if (!m_enabled)
                return;

            lock (m_scenes)
            {
                if (!m_scenes.Contains(scene))
                    m_scenes.Add(scene);
            }

            lock (m_regionWasOccupied)
                m_regionWasOccupied[scene.RegionInfo.RegionID] = false;

            m_log.InfoFormat(
                "[HOLO PHYSICS GUARD]: Added region {0} ({1})",
                scene.RegionInfo.RegionName,
                scene.RegionInfo.RegionID
            );
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_enabled || !m_wakeOnStart || !IsPersistMode())
                return;

            try
            {
                lock (m_operationLock)
                {
                    if (m_enabled)
                        WakeRegion(scene, "region_start");
                }
            }
            catch (Exception ex)
            {
                m_log.ErrorFormat(
                    "[HOLO PHYSICS GUARD]: WakeOnStart failed for region {0}: {1}",
                    scene.RegionInfo.RegionName,
                    ex
                );
            }
        }

        public void RemoveRegion(Scene scene)
        {
            lock (m_scenes)
                m_scenes.Remove(scene);

            lock (m_regionBecameEmptyAt)
                m_regionBecameEmptyAt.Remove(scene.RegionInfo.RegionID);

            lock (m_regionWasOccupied)
                m_regionWasOccupied.Remove(scene.RegionInfo.RegionID);
        }

        public void Close()
        {
            m_enabled = false;

            if (m_timer != null)
            {
                m_timer.Stop();
                m_timer.Elapsed -= OnTimer;
                m_timer.Dispose();
                m_timer = null;
            }

            lock (m_scenes)
                m_scenes.Clear();

            lock (m_regionBecameEmptyAt)
                m_regionBecameEmptyAt.Clear();

            lock (m_regionWasOccupied)
                m_regionWasOccupied.Clear();
        }

        private void ResolveConnectionString(IConfigSource source, IConfig config)
        {
            m_connectionString = config.GetString("ConnectionString", String.Empty).Trim();
            if (!String.IsNullOrWhiteSpace(m_connectionString))
            {
                m_connectionSource = "HoloPhysicsGuard";
                return;
            }

            IConfig databaseConfig = source.Configs["DatabaseService"];
            if (databaseConfig == null)
                return;

            m_connectionString = databaseConfig.GetString("ConnectionString", String.Empty).Trim();
            if (!String.IsNullOrWhiteSpace(m_connectionString))
                m_connectionSource = "DatabaseService";
        }

        private void OnTimer(object sender, ElapsedEventArgs e)
        {
            if (!m_enabled)
                return;

            if (Interlocked.Exchange(ref m_timerRunning, 1) != 0)
            {
                if (m_verbose)
                    m_log.Warn("[HOLO PHYSICS GUARD]: Skipping overlapping timer callback");

                return;
            }

            try
            {
                List<Scene> scenes;

                lock (m_scenes)
                    scenes = new List<Scene>(m_scenes);

                foreach (Scene scene in scenes)
                {
                    if (!m_enabled)
                        break;

                    try
                    {
                        CheckScene(scene);
                    }
                    catch (Exception ex)
                    {
                        m_log.ErrorFormat(
                            "[HOLO PHYSICS GUARD]: Error checking region {0}: {1}",
                            scene.RegionInfo.RegionName,
                            ex
                        );
                    }
                }
            }
            finally
            {
                Volatile.Write(ref m_timerRunning, 0);
            }
        }

        private void CheckScene(Scene scene)
        {
            UUID regionID = scene.RegionInfo.RegionID;
            bool occupied = scene.GetRootAgentCount() > 0;
            bool wasOccupied;

            lock (m_regionWasOccupied)
            {
                if (!m_regionWasOccupied.TryGetValue(regionID, out wasOccupied))
                    wasOccupied = false;

                m_regionWasOccupied[regionID] = occupied;
            }

            if (occupied)
            {
                lock (m_regionBecameEmptyAt)
                    m_regionBecameEmptyAt.Remove(regionID);

                if (!wasOccupied && m_wakeOnAvatarEnter && IsPersistMode())
                {
                    bool wakeComplete = false;

                    try
                    {
                        lock (m_operationLock)
                        {
                            if (m_enabled)
                                wakeComplete = WakeRegion(scene, "avatar_enter");
                        }
                    }
                    finally
                    {
                        if (!wakeComplete)
                        {
                            lock (m_regionWasOccupied)
                                m_regionWasOccupied[regionID] = false;
                        }
                    }
                }

                return;
            }

            if (!m_sleepWhenEmpty)
                return;

            DateTime emptySince;

            lock (m_regionBecameEmptyAt)
            {
                if (!m_regionBecameEmptyAt.TryGetValue(regionID, out emptySince))
                {
                    emptySince = DateTime.UtcNow;
                    m_regionBecameEmptyAt[regionID] = emptySince;

                    if (m_verbose)
                    {
                        m_log.InfoFormat(
                            "[HOLO PHYSICS GUARD]: Region {0} became empty",
                            scene.RegionInfo.RegionName
                        );
                    }

                    return;
                }
            }

            if ((DateTime.UtcNow - emptySince).TotalSeconds < m_emptyDelaySeconds)
                return;

            lock (m_operationLock)
            {
                if (m_enabled && scene.GetRootAgentCount() == 0)
                    SleepPhysicalObjects(scene);
            }

            // Avoid rescanning every timer tick. Empty regions are scanned again
            // after another EmptyDelaySeconds interval so newly rezzed physical
            // objects can still be caught.
            lock (m_regionBecameEmptyAt)
                m_regionBecameEmptyAt[regionID] = DateTime.UtcNow;
        }

        private void SleepPhysicalObjects(Scene scene)
        {
            int scanned = 0;
            int physical = 0;
            int slept = 0;
            int skipped = 0;
            int failed = 0;

            EntityBase[] entities = scene.GetEntities();

            foreach (EntityBase entity in entities)
            {
                if (!m_enabled || scene.GetRootAgentCount() > 0)
                    break;

                SceneObjectGroup sog = entity as SceneObjectGroup;
                if (sog == null || sog.RootPart == null)
                    continue;

                SceneObjectPart root = sog.RootPart;
                scanned++;

                if (!IsPhysical(root))
                    continue;

                physical++;

                string name = root.Name ?? String.Empty;
                if (!ShouldSleep(name))
                {
                    skipped++;

                    if (m_verbose)
                    {
                        m_log.InfoFormat(
                            "[HOLO PHYSICS GUARD]: Skip physical object region={0} name='{1}' uuid={2}",
                            scene.RegionInfo.RegionName,
                            name,
                            root.UUID
                        );
                    }

                    continue;
                }

                if (IsReportOnly())
                {
                    slept++;

                    m_log.WarnFormat(
                        "[HOLO PHYSICS GUARD]: REPORT would sleep physical object region={0} name='{1}' uuid={2} pos={3}",
                        scene.RegionInfo.RegionName,
                        name,
                        root.UUID,
                        root.GroupPosition
                    );

                    continue;
                }

                try
                {
                    RecordSleepInDb(scene, sog, root);
                    SetNonPhysicalLive(root);

                    if (m_zeroVelocities)
                        ZeroMotion(root);

                    sog.HasGroupChanged = true;
                    sog.ScheduleGroupForFullUpdate();

                    slept++;

                    m_log.InfoFormat(
                        "[HOLO PHYSICS GUARD]: Slept physical object region={0} name='{1}' uuid={2} pos={3}",
                        scene.RegionInfo.RegionName,
                        name,
                        root.UUID,
                        root.GroupPosition
                    );
                }
                catch (Exception ex)
                {
                    failed++;

                    m_log.ErrorFormat(
                        "[HOLO PHYSICS GUARD]: Failed sleeping object region={0} name='{1}' uuid={2}: {3}",
                        scene.RegionInfo.RegionName,
                        name,
                        root.UUID,
                        ex
                    );
                }
            }

            if (physical > 0 || failed > 0 || m_verbose)
            {
                m_log.InfoFormat(
                    "[HOLO PHYSICS GUARD]: Region {0}: scanned={1} physical={2} slept={3} skipped={4} failed={5} mode={6}",
                    scene.RegionInfo.RegionName,
                    scanned,
                    physical,
                    slept,
                    skipped,
                    failed,
                    m_mode
                );
            }
        }

        private bool WakeRegion(Scene scene, string reason)
        {
            List<SleepRow> rows = GetSleepRows(scene.RegionInfo.RegionID);
            if (rows.Count == 0)
                return true;

            int woke = 0;
            int stale = 0;
            int failed = 0;

            foreach (SleepRow row in rows)
            {
                SceneObjectGroup sog = FindSceneObjectGroup(scene, row.ObjectUUID);

                if (sog == null || sog.RootPart == null)
                {
                    try
                    {
                        DeleteSleepRow(row.RegionUUID, row.ObjectUUID);
                        stale++;

                        m_log.WarnFormat(
                            "[HOLO PHYSICS GUARD]: Removed stale sleep row region={0} object={1} name='{2}' reason={3}",
                            scene.RegionInfo.RegionName,
                            row.ObjectUUID,
                            row.ObjectName,
                            reason
                        );
                    }
                    catch (Exception ex)
                    {
                        failed++;

                        m_log.ErrorFormat(
                            "[HOLO PHYSICS GUARD]: Failed deleting stale sleep row region={0} object={1} reason={2}: {3}",
                            scene.RegionInfo.RegionName,
                            row.ObjectUUID,
                            reason,
                            ex
                        );
                    }

                    continue;
                }

                try
                {
                    SetPhysicalLive(sog.RootPart);
                    sog.HasGroupChanged = true;
                    sog.ScheduleGroupForFullUpdate();

                    // Delete only after the OpenSim object update has succeeded.
                    // If deletion fails, the row remains and the occupied-state
                    // transition is deliberately retried on the next timer pass.
                    DeleteSleepRow(row.RegionUUID, row.ObjectUUID);

                    woke++;

                    m_log.InfoFormat(
                        "[HOLO PHYSICS GUARD]: Woke object region={0} name='{1}' uuid={2} reason={3}",
                        scene.RegionInfo.RegionName,
                        sog.RootPart.Name,
                        sog.RootPart.UUID,
                        reason
                    );
                }
                catch (Exception ex)
                {
                    failed++;

                    m_log.ErrorFormat(
                        "[HOLO PHYSICS GUARD]: Failed waking object region={0} object={1} reason={2}: {3}",
                        scene.RegionInfo.RegionName,
                        row.ObjectUUID,
                        reason,
                        ex
                    );
                }
            }

            if (woke > 0 || stale > 0 || failed > 0 || m_verbose)
            {
                m_log.InfoFormat(
                    "[HOLO PHYSICS GUARD]: Wake region {0}: rows={1} woke={2} stale={3} failed={4} reason={5}",
                    scene.RegionInfo.RegionName,
                    rows.Count,
                    woke,
                    stale,
                    failed,
                    reason
                );
            }

            return failed == 0;
        }

        private SceneObjectGroup FindSceneObjectGroup(Scene scene, UUID rootPartUUID)
        {
            EntityBase[] entities = scene.GetEntities();

            foreach (EntityBase entity in entities)
            {
                SceneObjectGroup sog = entity as SceneObjectGroup;
                if (sog == null || sog.RootPart == null)
                    continue;

                if (sog.RootPart.UUID == rootPartUUID)
                    return sog;
            }

            return null;
        }

        private static bool IsPhysical(SceneObjectPart part)
        {
            return (part.Flags & PrimFlags.Physics) != 0;
        }

        private static void SetNonPhysicalLive(SceneObjectPart part)
        {
            bool setTemporary = (part.Flags & PrimFlags.TemporaryOnRez) != 0;
            bool setPhantom = (part.Flags & PrimFlags.Phantom) != 0;
            bool setVolumeDetect = part.VolumeDetectActive;

            part.UpdatePrimFlags(
                false,
                setTemporary,
                setPhantom,
                setVolumeDetect,
                false
            );
        }

        private static void SetPhysicalLive(SceneObjectPart part)
        {
            bool setTemporary = (part.Flags & PrimFlags.TemporaryOnRez) != 0;
            bool setPhantom = (part.Flags & PrimFlags.Phantom) != 0;
            bool setVolumeDetect = part.VolumeDetectActive;

            part.UpdatePrimFlags(
                true,
                setTemporary,
                setPhantom,
                setVolumeDetect,
                false
            );
        }

        private static void ZeroMotion(SceneObjectPart part)
        {
            part.Velocity = Vector3.Zero;
            part.AngularVelocity = Vector3.Zero;
        }

        private bool ShouldSleep(string name)
        {
            string normalizedName = name.ToLowerInvariant();

            foreach (string blocked in m_neverSleepNameContains)
            {
                if (blocked.Length > 0 && normalizedName.Contains(blocked))
                    return false;
            }

            if (m_alwaysSleepNameContains.Length == 0)
                return m_allowAllPhysicalObjects;

            foreach (string allowed in m_alwaysSleepNameContains)
            {
                if (allowed.Length > 0 && normalizedName.Contains(allowed))
                    return true;
            }

            return false;
        }

        private bool IsReportOnly()
        {
            return String.Equals(m_mode, "ReportOnly", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPersistMode()
        {
            return String.Equals(m_mode, "PersistSleep", StringComparison.OrdinalIgnoreCase);
        }

        private MySqlConnection OpenDb()
        {
            MySqlConnection connection = new MySqlConnection(m_connectionString);
            connection.Open();
            return connection;
        }

        private void EnsureTable()
        {
            const string sql = @"
CREATE TABLE IF NOT EXISTS holo_physics_guard_sleep (
    region_uuid CHAR(36) NOT NULL,
    object_uuid CHAR(36) NOT NULL,
    scene_group_id CHAR(36) NOT NULL,
    object_name VARCHAR(255) NOT NULL DEFAULT '',
    original_object_flags BIGINT UNSIGNED NOT NULL DEFAULT 0,
    slept_at INT UNSIGNED NOT NULL,
    slept_by VARCHAR(64) NOT NULL DEFAULT 'HoloPhysicsGuard',

    PRIMARY KEY (region_uuid, object_uuid),
    KEY idx_region_uuid (region_uuid),
    KEY idx_scene_group_id (scene_group_id),
    KEY idx_slept_at (slept_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            using (MySqlConnection connection = OpenDb())
            using (MySqlCommand command = new MySqlCommand(sql, connection))
                command.ExecuteNonQuery();

            m_log.Info("[HOLO PHYSICS GUARD]: Table holo_physics_guard_sleep ready");
        }

        private void RecordSleepInDb(Scene scene, SceneObjectGroup sog, SceneObjectPart root)
        {
            const string sql = @"
INSERT INTO holo_physics_guard_sleep
    (region_uuid, object_uuid, scene_group_id, object_name, original_object_flags, slept_at, slept_by)
VALUES
    (@region_uuid, @object_uuid, @scene_group_id, @object_name, @original_object_flags, @slept_at, 'HoloPhysicsGuard')
ON DUPLICATE KEY UPDATE
    scene_group_id = @scene_group_id,
    object_name = @object_name,
    slept_at = @slept_at,
    slept_by = 'HoloPhysicsGuard';";

            using (MySqlConnection connection = OpenDb())
            using (MySqlCommand command = new MySqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@region_uuid", scene.RegionInfo.RegionID.ToString());
                command.Parameters.AddWithValue("@object_uuid", root.UUID.ToString());
                command.Parameters.AddWithValue("@scene_group_id", sog.UUID.ToString());
                command.Parameters.AddWithValue("@object_name", Truncate(root.Name ?? String.Empty, 255));
                command.Parameters.AddWithValue("@original_object_flags", Convert.ToUInt64(root.Flags));
                command.Parameters.AddWithValue("@slept_at", UnixTimeNow());
                command.ExecuteNonQuery();
            }
        }

        private List<SleepRow> GetSleepRows(UUID regionID)
        {
            const string sql = @"
SELECT region_uuid, object_uuid, scene_group_id, object_name, original_object_flags
FROM holo_physics_guard_sleep
WHERE region_uuid = @region_uuid
ORDER BY slept_at ASC;";

            List<SleepRow> rows = new List<SleepRow>();

            using (MySqlConnection connection = OpenDb())
            using (MySqlCommand command = new MySqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@region_uuid", regionID.ToString());

                using (MySqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        SleepRow row = new SleepRow();
                        row.RegionUUID = UUID.Parse(reader.GetString("region_uuid"));
                        row.ObjectUUID = UUID.Parse(reader.GetString("object_uuid"));
                        row.SceneGroupID = UUID.Parse(reader.GetString("scene_group_id"));
                        row.ObjectName = reader.GetString("object_name");
                        row.OriginalObjectFlags = Convert.ToUInt64(reader["original_object_flags"]);
                        rows.Add(row);
                    }
                }
            }

            return rows;
        }

        private void DeleteSleepRow(UUID regionUUID, UUID objectUUID)
        {
            const string sql = @"
DELETE FROM holo_physics_guard_sleep
WHERE region_uuid = @region_uuid
  AND object_uuid = @object_uuid;";

            using (MySqlConnection connection = OpenDb())
            using (MySqlCommand command = new MySqlCommand(sql, connection))
            {
                command.Parameters.AddWithValue("@region_uuid", regionUUID.ToString());
                command.Parameters.AddWithValue("@object_uuid", objectUUID.ToString());
                command.ExecuteNonQuery();
            }
        }

        private static uint UnixTimeNow()
        {
            return (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        private static string Truncate(string value, int maxLength)
        {
            if (String.IsNullOrEmpty(value))
                return String.Empty;

            return value.Length <= maxLength
                ? value
                : value.Substring(0, maxLength);
        }

        private static string[] SplitList(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return Array.Empty<string>();

            string[] raw = value.Split(
                new char[] { ',' },
                StringSplitOptions.RemoveEmptyEntries
            );

            List<string> result = new List<string>();

            foreach (string item in raw)
            {
                string normalized = item.Trim().ToLowerInvariant();
                if (normalized.Length > 0)
                    result.Add(normalized);
            }

            return result.ToArray();
        }

        private class SleepRow
        {
            public UUID RegionUUID;
            public UUID ObjectUUID;
            public UUID SceneGroupID;
            public string ObjectName;
            public ulong OriginalObjectFlags;
        }
    }
}
