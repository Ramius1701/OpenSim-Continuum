// Regression for Tranquillity 3bba0e510a: use real CoreModules completions
// loaded into a second context, then deliver through the production plugins.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared.Api.Plugins;

var context = new CompletionContext();
var core = context.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "OpenSim.Region.CoreModules.dll"));
var scene = new Scene(new RegionInfo { RegionID = UUID.Random(), RegionName = "Completion regression" });
var events = new List<(object Target, EventParams Event)>();
var engine = Proxy.Create<IScriptEngine>((method, args) => method.Name switch
{
    "get_World" => scene,
    "PostObjectEvent" or "PostScriptEvent" => Capture(args),
    _ => throw new NotSupportedException(method.Name)
});
bool Capture(object[] args)
{
    events.Add((args[0], (EventParams)args[1]));
    return true;
}
var manager = (AsyncCommandManager)RuntimeHelpers.GetUninitializedObject(typeof(AsyncCommandManager));
manager.m_ScriptEngine = engine;
var engines = (List<IScriptEngine>)typeof(AsyncCommandManager)
    .GetField("m_ScriptEngines", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
engines.Add(engine);
try
{
    var httpType = core.GetType("OpenSim.Region.CoreModules.Scripting.HttpRequest.HttpRequestClass", true);
    Check(httpType.Assembly != typeof(OpenSim.Region.CoreModules.Scripting.HttpRequest.HttpRequestClass).Assembly,
        "HTTP completion must come from a different assembly context");
    var requests = new Queue<IHttpServiceRequest>();
    var ids = new[] { UUID.Random(), UUID.Random() };
    for (int i = 0; i < ids.Length; i++)
    {
        var completion = (IHttpServiceRequest)Activator.CreateInstance(httpType);
        completion.ReqID = ids[i];
        completion.LocalID = 42;
        httpType.GetProperty("Status").SetValue(completion, i == 0 ? 200 : 499);
        httpType.GetProperty("ResponseBody").SetValue(completion, i == 0 ? "response Ω" : "timeout");
        requests.Enqueue(completion);
    }
    scene.RegisterModuleInterface(Proxy.Create<IHttpRequestModule>((method, args) =>
        method.Name == "GetNextCompletedRequest" ? requests.Count > 0 ? requests.Dequeue() : null
        : throw new NotSupportedException(method.Name)));
    var http = new HttpRequest(manager);
    http.CheckHttpRequests();
    http.CheckHttpRequests();
    Check(events.Count == 2, "HTTP queue must drain exactly once");
    for (int i = 0; i < 2; i++)
    {
        var result = events[i];
        Check((uint)result.Target == 42 && result.Event.EventName == "http_response", "HTTP routing");
        Check(result.Event.Params[0].ToString() == ids[i].ToString(), "HTTP request identity");
        Check(result.Event.Params[1].ToString() == (i == 0 ? "200" : "499"), "HTTP status");
        Check(result.Event.Params[3].ToString() == (i == 0 ? "response Ω" : "timeout"), "HTTP body");
    }

    events.Clear();
    var item = UUID.Random();
    var channel = UUID.Random();
    var rpcType = core.GetType("OpenSim.Region.CoreModules.Scripting.XMLRPC.RPCRequestInfo", true);
    var rpc = (IXmlRpcRequestInfo)Activator.CreateInstance(rpcType, 43u, item, channel, "request Ω", "7");
    var pending = new Queue<IXmlRpcRequestInfo>();
    pending.Enqueue(rpc);
    int removals = 0;
    scene.RegisterModuleInterface(Proxy.Create<IXMLRPC>((method, args) =>
    {
        switch (method.Name)
        {
            case "GetNextCompletedRequest": return pending.Count > 0 ? pending.Dequeue() : null;
            case "GetNextCompletedSRDRequest": return null;
            case "RemoveCompletedRequest":
                Check((UUID)args[0] == rpc.GetMessageID(), "XML-RPC removal identity");
                removals++;
                return null;
            default: throw new NotSupportedException(method.Name);
        }
    }));
    var xml = new XmlRequest(manager);
    xml.CheckXMLRPCRequests();
    xml.CheckXMLRPCRequests();
    Check(events.Count == 1 && removals == 1, "XML-RPC must deliver and remove exactly once");
    Check((UUID)events[0].Target == item && events[0].Event.EventName == "remote_data", "XML-RPC routing");
    var values = events[0].Event.Params;
    Check(values[0].ToString() == "2" && values[1].ToString() == channel.ToString()
        && values[2].ToString() == rpc.GetMessageID().ToString()
        && values[4].ToString() == "7" && values[5].ToString() == "request Ω", "XML-RPC payload");
    var secondScene = new Scene(new RegionInfo { RegionID = UUID.Random(), RegionName = "Second engine" });
    int secondPolls = 0;
    secondScene.RegisterModuleInterface(Proxy.Create<IXMLRPC>((method, args) =>
    {
        if (method.Name == "GetNextCompletedRequest") secondPolls++;
        return null;
    }));
    var secondEngine = Proxy.Create<IScriptEngine>((method, args) =>
        method.Name == "get_World" ? secondScene : throw new NotSupportedException(method.Name));
    var secondManager = (AsyncCommandManager)RuntimeHelpers.GetUninitializedObject(typeof(AsyncCommandManager));
    secondManager.m_ScriptEngine = secondEngine;
    var xmlPlugins = (Dictionary<IScriptEngine, XmlRequest>)typeof(AsyncCommandManager)
        .GetField("m_XmlRequest", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    engines.Add(secondEngine);
    xmlPlugins.Add(engine, xml);
    xmlPlugins.Add(secondEngine, new XmlRequest(secondManager));
    try
    {
        typeof(AsyncCommandManager).GetMethod("DoOneCmdHandlerPass", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, null);
        Check(secondPolls == 1, "Async command pass must poll the second region's XML-RPC module");
    }
    finally
    {
        engines.Remove(secondEngine);
        xmlPlugins.Remove(engine);
        xmlPlugins.Remove(secondEngine);
    }
    Console.WriteLine("PASS: cross-context HTTP success/error and XML-RPC delivery, exactly-once queue draining, and second-region XML-RPC polling.");
}
finally
{
    engines.Remove(engine);
    context.Unload();
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

public class Proxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Handler;
    protected override object Invoke(MethodInfo method, object[] args) => Handler(method, args);
    public static T Create<T>(Func<MethodInfo, object[], object> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, Proxy>();
        ((Proxy)(object)proxy).Handler = handler;
        return proxy;
    }
}

sealed class CompletionContext : AssemblyLoadContext
{
    public CompletionContext() : base(isCollectible: true) { }
    protected override Assembly Load(AssemblyName name) => Default.LoadFromAssemblyName(name);
}
