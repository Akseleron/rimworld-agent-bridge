# Adding commands from your mod

**English** · [Русский](EXTENDING.ru.md)

Your mod does **not** need to reference Agent Bridge. Declare an attribute with this exact name anywhere in your
assembly and mark public static methods with it:

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class AgentBridgeCommandAttribute : Attribute
{
    public AgentBridgeCommandAttribute(string name, string help, string access = "Act") { }
}

public static class MyModAgentCommands
{
    // args: the JSON request object (string, long, double, bool, null, List<object>, Dictionary<string, object>)
    [AgentBridgeCommand("mymod-status", "status of MyMod {verbose?}", "Read")]
    public static object Status(IDictionary<string, object> args)
        => new { enabled = MyMod.Enabled, count = MyMod.Things.Count };
}
```

Agent Bridge finds these at startup (after all mods load). Access is one of `Read`, `Act`, `Dev`, `Power` and is
enforced by the player's settings. Return anonymous objects, dictionaries, lists or primitives; game objects are
converted to ids (`Thing` → ThingID, `Def` → defName, `IntVec3` → `[x,z]`). Throw any exception to return an error.

Mods that reference Agent Bridge directly may instead call
`AgentBridge.Api.Register(name, help, Func<IDictionary<string, object>, object> run, access)`.

Commands appear in `help` and `capabilities.extensions` as `<packageId>:<name>`.
