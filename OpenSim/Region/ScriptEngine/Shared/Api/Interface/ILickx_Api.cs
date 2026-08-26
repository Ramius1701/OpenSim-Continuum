/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the conditions in LICENSE.txt
 * are met.
 */

#pragma warning disable IDE1006

using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;

namespace OpenSim.Region.ScriptEngine.Shared.Api.Interfaces
{
    /// <summary>
    /// Script extensions preserved from the archived opensim-lickx donor.
    /// </summary>
    public interface ILickx_Api
    {
        LSL_String lxGetAgentViewer(LSL_Key avkey);
    }
}
