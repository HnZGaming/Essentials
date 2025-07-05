using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Essentials.Conditions;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Torch.Commands;

namespace Essentials.Commands
{
    public static class ConditionsChecker
    {
        private static List<Condition> _conditionLookup;
        public static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public static void Init()
        {
            _conditionLookup = new List<Condition>();

            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany((x) =>
                {
                    try
                    {
                        return x.GetTypes();
                    }
                    catch (Exception e) // ignored 
                    {
                        return new Type[0];
                    }
                }).Where(t => t.IsDefined(typeof(ConditionModule)));

            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                foreach (var m in methods)
                {
                    var a = m.GetCustomAttribute<ConditionAttribute>();
                    if (a == null)
                        continue;

                    var c = new Condition(m, a);

                    _conditionLookup.Add(c);
                }
            }
        }

        public static List<Condition> GetAllConditions()
        {
            return _conditionLookup;
        }

        public static IEnumerable<MyCubeGrid> ScanConditions(CommandContext context, IReadOnlyList<string> args)
        {
            var conditions = new List<Func<MyCubeGrid, bool?>>();

            for (var i = 0; i < args.Count; i++)
            {
                string parameter;
                if (i + 1 >= args.Count)
                {
                    parameter = null;
                }
                else
                {
                    parameter = args[i + 1];
                }

                var arg = args[i];

                if (parameter != null)
                {
                    //parameter is the name of a command. Assume this command requires no parameters
                    if (_conditionLookup.Any(c => parameter.Equals(c.Command, StringComparison.CurrentCultureIgnoreCase) || parameter.Equals(c.InvertCommand, StringComparison.CurrentCultureIgnoreCase)))
                    {
                        parameter = null;
                    }
                    //next string is a parameter, so pass it to the condition and skip it next loop
                    else
                        i++;
                }

                bool found = false;

                foreach (var condition in _conditionLookup)
                {
                    if (arg.Equals(condition.Command, StringComparison.CurrentCultureIgnoreCase))
                    {
                        conditions.Add(g => condition.Evaluate(g, parameter, false, context));
                        found = true;
                        break;
                    }
                    else if (arg.Equals(condition.InvertCommand, StringComparison.CurrentCultureIgnoreCase))
                    {
                        conditions.Add(g => condition.Evaluate(g, parameter, true, context));
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    context.Respond($"Unknown argument '{arg}'");
                    return new List<MyCubeGrid>();
                }
            }

            //default scan to find grids without pilots
            if (!args.Contains("haspilot", StringComparer.CurrentCultureIgnoreCase))
                conditions.Add(g => !ConditionsImplementations.Piloted(g));

            var economyGrids = GetEconomyStationGridIdSet();
            Log.Info($"economy grids: {string.Join(", ", economyGrids)}");
            var resultList = new List<MyCubeGrid>();
            foreach (var group in MyCubeGridGroups.Static.Logical.Groups)
            {
                //if (group.Nodes.All(grid => conditions.TrueForAll(func => func(grid.NodeData))))
                bool res = true;
                foreach (var node in group.Nodes)
                {
                    if (node.NodeData.Projector != null)
                        continue;

                    foreach (var c in conditions)
                    {
                        bool? r = c.Invoke(node.NodeData);
                        if (r == null)
                        {
                            res = false;
                            break;
                        }

                        if (r == true)
                        {
                            continue;
                        }

                        res = false;
                        break;
                    }

                    if (!res)
                    {
                        break;
                    }
                }

                if (res)
                {
                    foreach (var grid in group.Nodes.Select(n => n.NodeData))
                    {
                        if (grid.Projector != null) continue;
                        if (economyGrids.Contains(grid.EntityId))
                        {
                            Log.Info($"skipped economy grid: {grid.DisplayName}");
                            continue;
                        }

                        resultList.Add(grid);
                    }
                }
            }

            return resultList;
        }

        static HashSet<long> GetEconomyStationGridIdSet()
        {
            var set = new HashSet<long>();
            foreach (var (_, faction) in MySession.Static.Factions)
            foreach (var station in faction.Stations)
            {
                var gridId = station.StationEntityId;
                if (gridId == 0) continue;

                set.Add(gridId);
            }

            return set;
        }
    }
}