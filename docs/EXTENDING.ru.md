# Добавление команд из своего мода

[English](EXTENDING.md) · **Русский**

Вашему моду **не нужно** ссылаться на Agent Bridge. Объявите в своей сборке атрибут с точно таким именем и пометьте им
публичные статические методы:

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class AgentBridgeCommandAttribute : Attribute
{
    public AgentBridgeCommandAttribute(string name, string help, string access = "Act") { }
}

public static class MyModAgentCommands
{
    // args: JSON-объект запроса (string, long, double, bool, null, List<object>, Dictionary<string, object>)
    [AgentBridgeCommand("mymod-status", "status of MyMod {verbose?}", "Read")]
    public static object Status(IDictionary<string, object> args)
        => new { enabled = MyMod.Enabled, count = MyMod.Things.Count };
}
```

Agent Bridge находит такие методы при запуске, после загрузки всех модов. Уровень доступа — `Read`, `Act`, `Dev` или
`Power`; его ограничивают настройки игрока.

Возвращайте анонимные объекты, словари, списки или примитивы. Игровые объекты превращаются в идентификаторы:
`Thing` → ThingID, `Def` → defName, `IntVec3` → `[x,z]`. Чтобы вернуть ошибку, бросьте любое исключение.

Моды, которые ссылаются на Agent Bridge напрямую, могут вместо этого вызвать
`AgentBridge.Api.Register(name, help, Func<IDictionary<string, object>, object> run, access)`.

Такие команды видны в `help` и в `capabilities.extensions` как `<packageId>:<имя>`.
