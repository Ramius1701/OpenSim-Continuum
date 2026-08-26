/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the conditions in LICENSE.txt
 * are met.
 */

using System;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared.Api.Interfaces;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;

#pragma warning disable IDE1006

namespace OpenSim.Region.ScriptEngine.Shared.Api
{
    /// <summary>
    /// Narrow script compatibility API from the archived opensim-lickx donor.
    /// </summary>
    [Serializable]
    public class Lickx_Api : ILickx_Api, IScriptApi
    {
        private IScriptEngine m_ScriptEngine;

        public void Initialize(IScriptEngine scriptEngine, SceneObjectPart host,
            TaskInventoryItem item)
        {
            m_ScriptEngine = scriptEngine;
        }

        /// <summary>
        /// Returns the viewer identification reported by a currently connected
        /// agent, or an empty string for an invalid or offline agent.
        /// </summary>
        public LSL_String lxGetAgentViewer(LSL_Key avkey)
        {
            if (m_ScriptEngine == null ||
                !UUID.TryParse(avkey.m_string, out UUID agentID))
            {
                return string.Empty;
            }

            AgentCircuitData circuit =
                m_ScriptEngine.World.AuthenticateHandler.GetAgentCircuitData(agentID);
            return circuit == null ? string.Empty : Util.GetViewerName(circuit);
        }
    }
}
