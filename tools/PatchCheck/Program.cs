using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

// Resolves every Harmony patch in SubnautiCraft.dll against the game's own assemblies, the way Harmony would at
// startup: the target method must exist and be unambiguous, and every injected parameter must exist on it.
internal static class Program
{
    private static string[] Dirs;

    /// <summary>Arguments: Subnautica's folder (Steam's by default), the folder with SubnautiCraft.dll (subnautica/bin/Release).</summary>
    private static int Main(string[] args)
    {
        string game = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\Steam\steamapps\common\Subnautica";
        string plugin = args.Length > 1 ? args[1] : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), @"..\..\subnautica\bin\Release"));
        Dirs = new[] { Path.Combine(game, @"Subnautica_Data\Managed"), Path.Combine(game, @"BepInEx\core"), plugin };
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string d in Dirs)
            {
                string p = Path.Combine(d, name);
                if (File.Exists(p))
                {
                    return Assembly.LoadFrom(p);
                }
            }

            return null;
        };
        return Run();
    }

    private static int Run()
    {
        Assembly plugin = Assembly.LoadFrom(Path.Combine(Dirs[2], "SubnautiCraft.dll"));
        int ok = 0, bad = 0;
        foreach (Type type in AccessTools.GetTypesFromAssembly(plugin))
        {
            List<HarmonyMethod> attrs = HarmonyMethodExtensions.GetFromType(type);
            if (attrs == null || attrs.Count == 0)
            {
                continue;
            }

            var targets = new List<MethodBase>();
            string error = null;
            try
            {
                MethodInfo many = AccessTools.Method(type, "TargetMethods");
                MethodInfo one = AccessTools.Method(type, "TargetMethod");
                if (many != null)
                {
                    targets.AddRange(((IEnumerable<MethodBase>)many.Invoke(null, null)).Where(m => m != null));
                    if (targets.Count == 0)
                    {
                        error = "TargetMethods found nothing";
                    }
                }
                else if (one != null)
                {
                    var m = (MethodBase)one.Invoke(null, null);
                    if (m == null)
                    {
                        error = "TargetMethod found nothing";
                    }
                    else
                    {
                        targets.Add(m);
                    }
                }
                else
                {
                    HarmonyMethod info = HarmonyMethod.Merge(attrs);
                    MethodBase m;
                    switch (info.methodType)
                    {
                        case MethodType.Getter:
                            m = AccessTools.PropertyGetter(info.declaringType, info.methodName);
                            break;
                        case MethodType.Setter:
                            m = AccessTools.PropertySetter(info.declaringType, info.methodName);
                            break;
                        case MethodType.Constructor:
                            m = AccessTools.Constructor(info.declaringType, info.argumentTypes);
                            break;
                        default:
                            m = AccessTools.Method(info.declaringType, info.methodName, info.argumentTypes);
                            break;
                    }

                    if (m == null)
                    {
                        error = $"no method {info.declaringType?.Name}.{info.methodName}";
                    }
                    else
                    {
                        targets.Add(m);
                    }
                }
            }
            catch (Exception e)
            {
                error = e.GetBaseException().GetType().Name + ": " + e.GetBaseException().Message;
            }

            if (error == null)
            {
                foreach (MethodBase target in targets)
                {
                    var names = new HashSet<string>(target.GetParameters().Select(p => p.Name));
                    foreach (MethodInfo patch in type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                        .Where(p => p.Name == "Prefix" || p.Name == "Postfix" || p.Name == "Finalizer"))
                    {
                        foreach (ParameterInfo p in patch.GetParameters())
                        {
                            if (!p.Name.StartsWith("__") && !names.Contains(p.Name))
                            {
                                error = $"{patch.Name} asks for '{p.Name}', which {target.DeclaringType?.Name}.{target.Name} doesn't have";
                            }
                        }
                    }
                }
            }

            if (error == null)
            {
                ok++;
                Console.WriteLine($"ok   {type.Name} -> {string.Join(", ", targets.Select(t => t.DeclaringType?.Name + "." + t.Name))}");
            }
            else
            {
                bad++;
                Console.WriteLine($"FAIL {type.Name}: {error}");
            }
        }

        // the game's private parts the plugin reaches by reflection (static FieldInfo/MethodInfo fields): all there?
        int members = 0, missing = 0;
        foreach (Type type in AccessTools.GetTypesFromAssembly(plugin))
        {
            foreach (FieldInfo f in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(f => typeof(MemberInfo).IsAssignableFrom(f.FieldType)))
            {
                object value;
                try
                {
                    value = f.GetValue(null);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"?    {type.Name}.{f.Name}: couldn't check ({e.GetBaseException().GetType().Name})");
                    continue;
                }

                members++;
                if (value == null)
                {
                    missing++;
                    Console.WriteLine($"FAIL {type.Name}.{f.Name}: the game has no such member");
                }
            }
        }

        Console.WriteLine($"{members - missing} reflected members found, {missing} missing");
        Console.WriteLine($"{ok} patches resolve, {bad} don't");
        return bad == 0 && missing == 0 ? 0 : 1;
    }
}
