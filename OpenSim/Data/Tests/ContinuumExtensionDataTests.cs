using System;
using System.IO;
using NUnit.Framework;
using OpenMetaverse;
using OpenSim.Data.SQLite;
using OpenSim.Framework;

namespace OpenSim.Data.Tests
{
    [TestFixture]
    public class ContinuumExtensionDataTests
    {
        private string m_databasePath;
        private string m_connectionString;

        [SetUp]
        public void SetUp()
        {
            m_databasePath = Path.Combine(Path.GetTempPath(),
                "continuum-data-" + Guid.NewGuid().ToString("N") + ".db");
            m_connectionString = "URI=file:" + m_databasePath + ",version=3";
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(m_databasePath))
                File.Delete(m_databasePath);
        }

        [Test]
        public void SQLiteUserAliasesRoundTrip()
        {
            SQLiteUserAliasData store = new SQLiteUserAliasData(
                m_connectionString, "UserAlias");
            UUID aliasID = UUID.Random();
            UUID userID = UUID.Random();
            UserAliasData data = new UserAliasData
            {
                AliasID = aliasID,
                UserID = userID,
                Description = "OAR provenance alias"
            };

            Assert.That(store.Store(data), Is.True);
            Assert.That(data.Id, Is.GreaterThan(0));
            UserAliasData loaded = store.GetUserForAlias(aliasID);
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.UserID, Is.EqualTo(userID));
            Assert.That(store.GetUserAliases(userID), Has.Count.EqualTo(1));
            Assert.That(store.Delete("AliasID", aliasID.ToString()), Is.True);
            Assert.That(store.GetUserForAlias(aliasID), Is.Null);
        }

        [Test]
        public void SQLiteAbuseReportsRoundTripWithoutLeakingImageInLists()
        {
            SQLiteAbuseReportsData store = new SQLiteAbuseReportsData(m_connectionString);
            AbuseReportData report = new AbuseReportData
            {
                SenderID = UUID.Random(),
                SenderName = "Reporter Resident",
                Time = 12345,
                AbuseRegionID = UUID.Random(),
                AbuseRegionName = "Test Region",
                AbuserID = UUID.Random(),
                AbuserName = "Reported Resident",
                Category = "Harassment",
                Details = "Contract test",
                Summary = "Abuse report summary",
                ImageData = new byte[] { 1, 2, 3, 4 }
            };

            Assert.That(store.Store(report), Is.True);
            Assert.That(report.ReportID, Is.GreaterThan(0));
            Assert.That(store.Get(report.ReportID, true).ImageData,
                Is.EqualTo(report.ImageData));
            Assert.That(store.Get(0, 10, "Open")[0].ImageData, Is.Empty);

            UUID moderatorID = UUID.Random();
            Assert.That(store.UpdateModeration(report.ReportID, "Closed", "Reviewed",
                moderatorID, "Moderator Resident", 23456), Is.True);
            AbuseReportData updated = store.Get(report.ReportID, false);
            Assert.That(updated.Status, Is.EqualTo("Closed"));
            Assert.That(updated.ModeratorID, Is.EqualTo(moderatorID));
            Assert.That(updated.ImageData, Is.Empty);
        }

        [Test]
        public void SQLiteExperiencesPersistPermissionsMetadataAndKeyValues()
        {
            SQLiteExperienceData store = new SQLiteExperienceData(m_connectionString);
            UUID experienceID = UUID.Random();
            UUID ownerID = UUID.Random();
            UUID groupID = UUID.Random();
            ExperienceInfoData info = new ExperienceInfoData
            {
                public_id = experienceID,
                owner_id = ownerID,
                group_id = groupID,
                logo = UUID.Random(),
                name = "Continuum Test Experience",
                description = "Datastore contract test",
                marketplace = string.Empty,
                slurl = "hop://example.invalid/Test/128/128/25",
                maturity = 0,
                properties = 1
            };

            Assert.That(store.UpdateExperienceInfo(info), Is.True);
            Assert.That(store.GetExperienceInfos(new[] { experienceID }), Has.Length.EqualTo(1));
            Assert.That(store.GetAgentExperiences(ownerID), Contains.Item(experienceID));
            Assert.That(store.GetGroupExperiences(groupID), Contains.Item(experienceID));
            Assert.That(store.FindExperiences("Continuum Test"), Has.Length.EqualTo(1));

            UUID agentID = UUID.Random();
            Assert.That(store.SetExperiencePermissions(agentID, experienceID, true), Is.True);
            Assert.That(store.GetExperiencePermissions(agentID)[experienceID], Is.True);
            Assert.That(store.SetExperiencePermissions(agentID, experienceID, false), Is.True);
            Assert.That(store.GetExperiencePermissions(agentID)[experienceID], Is.False);
            Assert.That(store.ForgetExperiencePermissions(agentID, experienceID), Is.True);

            Assert.That(store.SetKeyValue(experienceID, "key", "value"), Is.True);
            Assert.That(store.GetKeyValue(experienceID, "key"), Is.EqualTo("value"));
            Assert.That(store.GetKeys(experienceID, 0, 10), Is.EqualTo(new[] { "key" }));
            Assert.That(store.GetKeyCount(experienceID), Is.EqualTo(1));
            Assert.That(store.GetKeyValueSize(experienceID), Is.EqualTo(8));
            Assert.That(store.DeleteKey(experienceID, "key"), Is.True);
        }
    }
}
