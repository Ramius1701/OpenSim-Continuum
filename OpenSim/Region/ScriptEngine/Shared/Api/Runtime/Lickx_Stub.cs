/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the conditions in LICENSE.txt
 * are met.
 */

using System.Runtime.CompilerServices;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared.Api.Interfaces;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;

#pragma warning disable IDE1006

namespace OpenSim.Region.ScriptEngine.Shared.ScriptBase
{
    public partial class ScriptBaseClass
    {
        public ILickx_Api m_Lickx_Functions;

        public void ApiTypeLickx(IScriptApi api)
        {
            if (api is ILickx_Api lickxApi)
                m_Lickx_Functions = lickxApi;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LSL_String lxGetAgentViewer(LSL_Key avkey)
        {
            return m_Lickx_Functions.lxGetAgentViewer(avkey);
        }
    }
}
