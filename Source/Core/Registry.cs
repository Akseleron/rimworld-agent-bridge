using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace AgentBridge
{
    /// <summary>What a command may do; settings gate each level.</summary>
    public enum Access
    {
        Read,   // observation only
        Act,    // ordinary player actions (orders, designations, save/load)
        Dev,    // world editing the player could only do with Dev mode
        Power,  // arbitrary code/reflection (eval, exec, obj set/call, reflect)
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CommandAttribute : Attribute
    {
        public readonly string Name, Help;
        public readonly Access Access;
        public CommandAttribute(string name, Access access, string help) { Name = name; Access = access; Help = help; }
    }

    public sealed class CommandInfo
    {
        public string Name = "", Help = "", Source = "";
        public Access Access;
        public Func<Args, object?> Run = null!;
    }

    /// <summary>Public extension API. Other mods can add commands without referencing this assembly by declaring
    /// their own attribute class named AgentBridgeCommandAttribute(string name, string help[, string access]) on a
    /// public static method taking IDictionary&lt;string, object&gt; (JSON args) and returning any JSON-friendly object.
    /// Mods that do reference us can call Api.Register directly.</summary>
    public static class Api
    {
        public static void Register(string name, string help, Func<IDictionary<string, object?>, object?> run, string access = "Act", string? source = null)
            => Registry.Add(new CommandInfo { Name = name, Help = help, Access = ParseAccess(access), Source = source ?? Assembly.GetCallingAssembly().GetName().Name, Run = a => run(a.Raw) });

        internal static Access ParseAccess(string? s) => s != null && Enum.IsDefined(typeof(Access), s) ? (Access)Enum.Parse(typeof(Access), s) : Access.Act;
    }

    internal static class Registry
    {
        public static readonly Dictionary<string, CommandInfo> Commands = new Dictionary<string, CommandInfo>(StringComparer.OrdinalIgnoreCase);

        public static void Add(CommandInfo c) { lock (Commands) Commands[c.Name] = c; }

        public static void ScanOwn()
        {
            foreach (var type in typeof(Registry).Assembly.GetTypes())
                foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = (CommandAttribute?)Attribute.GetCustomAttribute(m, typeof(CommandAttribute));
                    if (attr == null) continue;
                    var run = (Func<Args, object?>)Delegate.CreateDelegate(typeof(Func<Args, object?>), m);
                    Add(new CommandInfo { Name = attr.Name, Help = attr.Help, Access = attr.Access, Source = "core", Run = run });
                }
        }

        /// <summary>Finds AgentBridgeCommandAttribute-marked methods in other loaded mods (matched by name, so
        /// those mods need no hard dependency on Agent Bridge). Returns the number of commands found.</summary>
        public static int ScanMods()
        {
            int found = 0;
            foreach (var mod in LoadedModManager.RunningModsListForReading)
                foreach (var asm in mod.assemblies.loadedAssemblies)
                {
                    if (asm == typeof(Registry).Assembly) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
                    foreach (var type in types)
                        foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        {
                            CustomAttributeData? data;
                            try { data = m.GetCustomAttributesData().FirstOrDefault(d => d.AttributeType.Name == "AgentBridgeCommandAttribute"); }
                            catch { continue; }
                            if (data == null || data.ConstructorArguments.Count < 2) continue;
                            var ps = m.GetParameters();
                            if (ps.Length != 1 || !ps[0].ParameterType.IsAssignableFrom(typeof(Dictionary<string, object?>))) continue;
                            var method = m;
                            Add(new CommandInfo
                            {
                                Name = (string)data.ConstructorArguments[0].Value,
                                Help = (string)data.ConstructorArguments[1].Value,
                                Access = Api.ParseAccess(data.ConstructorArguments.Count > 2 ? data.ConstructorArguments[2].Value as string : null),
                                Source = mod.PackageId,
                                Run = a =>
                                {
                                    try { return method.Invoke(null, new object[] { a.Raw }); }
                                    catch (TargetInvocationException e) { throw new CommandError((e.InnerException ?? e).Message); }
                                },
                            });
                            found++;
                        }
                }
            return found;
        }
    }
}
