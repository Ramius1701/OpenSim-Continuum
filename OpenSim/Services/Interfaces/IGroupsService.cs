/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted under the OpenSimulator BSD license.
 */

using System.Collections.Generic;
using OpenSim.Framework;

namespace OpenSim.Services.Interfaces
{
    /// <summary>
    /// Read-only grid Groups operations required by trusted service consumers.
    /// Keeps portal code behind the configured provider instead of exposing
    /// Groups persistence tables.
    /// </summary>
    public interface IGroupsService
    {
        List<GroupMembershipData> GetAgentGroupMemberships(string requestingAgentID, string agentID);
    }
}
