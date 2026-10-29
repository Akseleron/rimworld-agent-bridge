using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace AgentBridge
{
    /// <summary>Everything a human player needs to finish the game, driven through the same windows and rules
    /// the player uses (trade dialog, caravan dialog, new-game pages, world targeting). No cheats are involved,
    /// so these are Act commands and stay available in fair-play mode.</summary>
    internal static class Play
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // ---------------- trading (open with menu-choose "Trade with …" or a comms console option) ----------------
        private static Dialog_Trade TradeWindow => Find.WindowStack.Windows.OfType<Dialog_Trade>().FirstOrDefault() ?? throw new CommandError("no trade open: use menu-choose on a trader (or comms console) first");

        private static object TradeRow(Tradeable t, int i) => new
        {
            index = i, def = t.ThingDef?.defName, label = t.LabelCap, colony = t.CountHeldBy(Transactor.Colony), trader = t.CountHeldBy(Transactor.Trader),
            buy = t.ActionToDo == TradeAction.PlayerBuys ? Math.Abs(t.CountToTransfer) : (int?)null,
            sell = t.ActionToDo == TradeAction.PlayerSells ? Math.Abs(t.CountToTransfer) : (int?)null,
            buyPrice = t.TraderWillTrade && t.CountHeldBy(Transactor.Trader) > 0 ? t.GetPriceFor(TradeAction.PlayerBuys) : (float?)null,
            sellPrice = t.TraderWillTrade && t.CountHeldBy(Transactor.Colony) > 0 ? t.GetPriceFor(TradeAction.PlayerSells) : (float?)null,
            currency = t.IsCurrency ? true : (bool?)null,
        };

        [Command("trade", Access.Read, "open trade: goods with counts and prices {filter?, all=false}; silver balance after the current offer")]
        private static object Trade(Args a)
        {
            var _ = TradeWindow;
            var rows = TradeSession.deal.AllTradeables.Select((t, i) => (t, i))
                .Where(x => x.t.Interactive || x.t.IsCurrency)
                .Where(x => a.Bool("all", false) || x.t.CountToTransfer != 0 || x.t.IsCurrency || x.t.TraderWillTrade)
                .Where(x => a.Str("filter") is not string f || (x.t.ThingDef?.defName.Has(f) ?? false) || x.t.Label.Has(f));
            var cur = TradeSession.deal.CurrencyTradeable;
            return new
            {
                trader = TradeSession.trader.TraderName, negotiator = TradeSession.playerNegotiator?.ThingID, gift = TradeSession.giftMode ? true : (bool?)null,
                silverColony = cur?.CountPostDealFor(Transactor.Colony), silverTrader = cur?.CountPostDealFor(Transactor.Trader),
                traderCanPay = TradeSession.deal.DoesTraderHaveEnoughSilver(),
                goods = rows.Take(a.Int("limit", 150)).Select(x => TradeRow(x.t, x.i)).ToList(),
            };
        }

        [Command("trade-set", Access.Act, "set an amount in the open trade {index|def, buy?|sell? (0 resets)}")]
        private static object TradeSet(Args a)
        {
            var _ = TradeWindow;
            var list = TradeSession.deal.AllTradeables;
            Tradeable t = a.Has("index") ? list.ElementAtOrDefault(a.Int("index", -1)) ?? throw new CommandError("bad index")
                : list.FirstOrDefault(x => x.ThingDef?.defName.Equals(a.Need("def"), StringComparison.OrdinalIgnoreCase) == true && x.Interactive) ?? throw new CommandError("not tradeable here: " + a.Str("def"));
            if (!t.Interactive) throw new CommandError("not adjustable");
            int n = a.Has("buy") ? a.Int("buy", 0) : -a.Int("sell", 0);
            // Positive CountToTransfer moves goods toward the colony for tradeables; verify by the resulting action.
            int target = n;
            if (n != 0 && !t.CanAdjustTo(target).Accepted) target = -n;
            var report = t.CanAdjustTo(target);
            if (!report.Accepted) throw new CommandError(report.Reason.NullOrEmpty() ? "cannot set that amount" : report.Reason);
            t.AdjustTo(target);
            if (n > 0 && t.ActionToDo != TradeAction.PlayerBuys || n < 0 && t.ActionToDo != TradeAction.PlayerSells) { t.AdjustTo(-target); }
            TradeSession.deal.UpdateCurrencyCount();
            return TradeRow(t, list.IndexOf(t));
        }

        [Command("trade-accept", Access.Act, "execute the open trade (fails like the Accept button if silver is short unless force=true)")]
        private static object TradeAccept(Args a)
        {
            var window = TradeWindow;
            if (!TradeSession.deal.DoesTraderHaveEnoughSilver() && !a.Bool("force", false)) throw new CommandError("trader has too little silver for this offer; reduce it or pass force=true (the game's confirm dialog)");
            if (!TradeSession.deal.TryExecute(out bool traded)) throw new CommandError("trade rejected: " + string.Join("; ", TradeSession.deal.cannotSellReasons.ToArray()));
            if (traded) TradeSession.playerNegotiator?.GetCaravan()?.RecacheInventory();
            window.Close(false);
            return new { traded };
        }

        [Command("trade-cancel", Access.Act, "close the trade without trading")]
        private static object TradeCancel(Args a) { TradeWindow.Close(false); return "closed"; }

        // ---------------- caravans and the world map ----------------
        internal static Caravan CaravanArg(Args a, string key = "caravan")
        {
            string id = a.Need(key).Replace("caravan:", "");
            // Fair play: only your own caravans take orders (NPC traders are world objects too).
            return Find.WorldObjects.Caravans.FirstOrDefault(c => (c.ID.ToString() == id || c.Name == id) && (!Bridge.Settings.fairPlay || c.IsPlayerControlled)) ?? throw new CommandError("no caravan " + id + "; see caravans");
        }

        [Command("caravans", Access.Read, "player caravans: tile, destination, pawns, mass, food days")]
        private static object Caravans(Args a) => Find.WorldObjects.Caravans.Where(c => c.IsPlayerControlled).Select(c => new
        {
            id = c.ID, name = c.Name, tile = (int)c.Tile, moving = c.pather.Moving ? true : (bool?)null, destination = c.pather.Moving ? (int)c.pather.Destination : (int?)null,
            pawns = c.PawnsListForReading.Select(p => p.ThingID).ToList(), mass = c.MassUsage, capacity = c.MassCapacity,
            foodDays = c.DaysWorthOfFood.days, resting = c.NightResting ? true : (bool?)null,
        }).ToList();

        [Command("world-objects", Access.Read, "settlements and sites {near: tile (default home/caravan), radius=30, kind?, faction?}")]
        private static object WorldObjects(Args a)
        {
            int origin = a.Int("near", Find.CurrentMap?.Tile ?? Find.WorldObjects.Caravans.FirstOrDefault(c => c.IsPlayerControlled)?.Tile ?? 0);
            float radius = a.Float("radius", 30); string? kind = a.Str("kind"), fac = a.Str("faction");
            return Find.WorldObjects.AllWorldObjects
                .Select(o => new { o, dist = Find.WorldGrid.ApproxDistanceInTiles(origin, o.Tile) })
                .Where(x => x.dist <= radius && (kind == null || x.o.def.defName.Has(kind)) && (fac == null || (x.o.Faction?.Name ?? "").Has(fac)))
                .OrderBy(x => x.dist).Take(a.Int("limit", 60))
                .Select(x => new { id = x.o.ID, kind = x.o.def.defName, name = x.o.LabelCap.ToString(), tile = (int)x.o.Tile, dist = Math.Round(x.dist, 1), faction = x.o.Faction?.Name, hostile = x.o.Faction != null && x.o.Faction.HostileTo(Faction.OfPlayer) ? true : (bool?)null })
                .ToList();
        }

        [Command("world-tile", Access.Read, "world tile details {tile}: biome, terrain, temperature, travel speed, objects")]
        private static object WorldTile(Args a)
        {
            int tile = a.Int("tile", Find.CurrentMap?.Tile ?? 0);
            var t = Find.WorldGrid[tile];
            return new
            {
                tile, biome = t.PrimaryBiome?.defName, hilliness = t.hilliness.ToString(), temperature = Find.World.tileTemperatures.GetOutdoorTemp(tile),
                growingDays = GenTemperature.TwelfthsInAverageTemperatureRange(tile, 6f, 42f)?.Count * 5,
                movementDifficulty = WorldPathGrid.CalculatedMovementDifficultyAt(tile, false), objects = Find.WorldObjects.ObjectsAt(tile).Select(o => o.LabelCap.ToString()).ToList(),
            };
        }

        [Command("caravan-form", Access.Act, "form a caravan through the game's dialog {pawns:[ids], items:[{def,count}], tile?: destination}; colonists pack and leave the map")]
        private static object CaravanForm(Args a)
        {
            var map = G.Map;
            if (!a.Has("tile")) throw new CommandError("need tile: the destination (see world-objects / world-tile); the game requires a route");
            long feedBefore = Feed.Last;
            var dialog = new Dialog_FormCaravan(map);
            Find.WindowStack.Add(dialog);  // PostOpen builds transferables and starts the route planner, as for the player
            try
            {
                if (a.Has("tile")) dialog.Notify_ChoseRoute(a.Int("tile", -1));
                if (Find.WorldRoutePlanner.Active) Find.WorldRoutePlanner.Stop();
                var trans = dialog.transferables;
                foreach (var t in trans) t.AdjustTo(0);
                var ids = new HashSet<string>(a.Strings("pawns"));
                if (ids.Count == 0) throw new CommandError("need pawns");
                foreach (var t in trans.Where(t => t.AnyThing is Pawn p && ids.Contains(p.ThingID))) { t.AdjustTo(1); ids.Remove(t.AnyThing.ThingID); }
                if (ids.Count > 0) throw new CommandError("cannot send: " + string.Join(", ", ids.ToArray()));
                foreach (var item in a.List("items").OfType<Dictionary<string, object?>>())
                {
                    var ia = new Args(item); string def = ia.Need("def"); int want = ia.Int("count", 1);
                    foreach (var t in trans.Where(t => !(t.AnyThing is Pawn) && t.ThingDef.defName.Equals(def, StringComparison.OrdinalIgnoreCase)))
                    {
                        int take = Math.Min(want, t.GetMaximumToTransfer()); t.AdjustTo(take); want -= take;
                        if (want <= 0) break;
                    }
                    if (want > 0) throw new CommandError("not enough " + def + " reachable (" + want + " missing)");
                }
                typeof(Dialog_FormCaravan).GetMethod("Notify_TransferablesChanged", Inst)?.Invoke(dialog, null);
                bool ok = (bool)typeof(Dialog_FormCaravan).GetMethod("TryFormAndSendCaravan", Inst)!.Invoke(dialog, null);
                if (!ok)
                {
                    var why = Feed.Find(feedBefore, "message", new System.Text.RegularExpressions.Regex("RejectInput|Caution"));
                    throw new CommandError("the game refused: " + (why?.text ?? "see windows/feed (overweight, no exit, no food…)"));
                }
                dialog.Close(false);
                CameraJumper.TryHideWorld();
                return new { forming = true, pawns = a.Strings("pawns") };
            }
            catch { if (Find.WindowStack.IsOpen(dialog)) dialog.Close(false); if (Find.WorldRoutePlanner.Active) Find.WorldRoutePlanner.Stop(); CameraJumper.TryHideWorld(); throw; }
        }

        [Command("caravan-move", Access.Act, "send a caravan to a tile {caravan, tile}")]
        private static object CaravanMove(Args a)
        {
            var c = CaravanArg(a); int tile = a.Int("tile", -1);
            if (!c.pather.StartPath(tile, null, true)) throw new CommandError("no path to tile " + tile);
            return new { caravan = c.ID, destination = tile, eta = CaravanArrivalTimeEstimator.EstimatedTicksToArrive(c, true) / 2500f + "h" };
        }

        [Command("world-menu", Access.Read, "right-click options for a caravan at a world tile {caravan, tile} (enter, attack, trade, settle…)")]
        private static List<FloatMenuOption> WorldOptions(Args a) => FloatMenuMakerWorld.ChoicesAtFor(a.Int("tile", -1), CaravanArg(a)).Where(o => !G.DevOnly(o.Label)).ToList();

        private static object WorldMenu(Args a) => WorldOptions(a).Select((o, i) => new { index = i, label = o.Label, disabled = o.Disabled ? true : (bool?)null }).ToList();

        [Command("world-menu-choose", Access.Act, "choose a world right-click option {caravan, tile, label|index}")]
        private static object WorldMenuChoose(Args a)
        {
            var opts = WorldOptions(a);
            var o = a.Has("index") ? opts.ElementAtOrDefault(a.Int("index", -1)) : opts.FirstOrDefault(x => x.Label.Has(a.Need("label")));
            if (o == null) throw new CommandError("no option; have: " + string.Join(" | ", opts.Select(x => x.Label).ToArray()));
            if (o.Disabled) throw new CommandError("disabled: " + o.Label);
            o.Chosen(true, null);
            return new { chosen = o.Label };
        }

        [Command("world-target", Access.Act, "complete active world targeting (transport pods, rituals, quests) {tile | object}")]
        private static object WorldTarget(Args a)
        {
            var wt = Find.WorldTargeter;
            if (!wt.IsTargeting) throw new CommandError("not targeting on the world map; press the gizmo first");
            GlobalTargetInfo target = a.Has("object") ? new GlobalTargetInfo(Find.WorldObjects.AllWorldObjects.FirstOrDefault(o => o.ID == a.Int("object", -1)) ?? throw new CommandError("no world object"))
                : new GlobalTargetInfo(a.Int("tile", -1));
            var action = (Func<GlobalTargetInfo, bool>)typeof(WorldTargeter).GetField("action", Inst)!.GetValue(wt);
            // What WorldTargeter checks on a click: tiles only when allowed, and its target filter.
            if (Bridge.Settings.fairPlay)
            {
                if (!target.HasWorldObject && typeof(WorldTargeter).GetField("canTargetTiles", Inst)?.GetValue(wt) is false) throw new CommandError("this targeting needs a world object, not a tile");
                if (typeof(WorldTargeter).GetField("canSelectTarget", Inst)?.GetValue(wt) is Func<GlobalTargetInfo, bool> can && !can(target)) throw new CommandError("invalid target");
            }
            bool done = action(target);
            if (done) wt.StopTargeting();
            return new { accepted = done };
        }

        // ---------------- colony policies ----------------
        [Command("policies", Access.Read, "apparel, food and drug policies, medical care levels, allowed areas")]
        private static object Policies(Args a) => new
        {
            apparel = Current.Game.outfitDatabase.AllOutfits.Select(p => p.label).ToList(),
            food = Current.Game.foodRestrictionDatabase.AllFoodRestrictions.Select(p => p.label).ToList(),
            drugs = Current.Game.drugPolicyDatabase.AllPolicies.Select(p => p.label).ToList(),
            medical = Enum.GetNames(typeof(MedicalCareCategory)),
            areas = G.Map.areaManager.AllAreas.Where(x => x.AssignableAsAllowed()).Select(x => x.Label).ToList(),
        };

        [Command("assign", Access.Act, "assign policies to pawns {ids, apparel?, food?, drugs?, medical?, area? ('' = unrestricted)}")]
        private static object Assign(Args a)
        {
            T Find<T>(IEnumerable<T> all, string name, Func<T, string> label) => all.FirstOrDefault(x => label(x).Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new CommandError("no policy '" + name + "'; see policies");
            foreach (var p in G.OwnPawns(a))
            {
                if (a.Str("apparel") is string ap) p.outfits.CurrentApparelPolicy = Find(Current.Game.outfitDatabase.AllOutfits, ap, x => x.label);
                if (a.Str("food") is string fo) p.foodRestriction.CurrentFoodPolicy = Find(Current.Game.foodRestrictionDatabase.AllFoodRestrictions, fo, x => x.label);
                if (a.Str("drugs") is string dr) p.drugs.CurrentPolicy = Find(Current.Game.drugPolicyDatabase.AllPolicies, dr, x => x.label);
                if (a.Str("medical") is string me) p.playerSettings.medCare = (MedicalCareCategory)Enum.Parse(typeof(MedicalCareCategory), me, true);
                if (a.Has("area")) p.playerSettings.AreaRestrictionInPawnCurrentMap = a.Str("area") is string ar && ar.Length > 0 ? Find(p.Map.areaManager.AllAreas.Where(x => x.AssignableAsAllowed()), ar, x => x.Label) : null;
            }
            return G.Pawns(a).Select(p => new { id = p.ThingID, apparel = p.outfits?.CurrentApparelPolicy?.label, food = p.foodRestriction?.CurrentFoodPolicy?.label, drugs = p.drugs?.CurrentPolicy?.label, medical = p.playerSettings?.medCare.ToString(), area = p.playerSettings?.AreaRestrictionInPawnCurrentMap?.Label }).ToList();
        }

        [Command("area-edit", Access.Act, "paint an area {name: Home|<allowed area>, x,z,x2?,z2?, remove=false, create=false}")]
        private static object AreaEdit(Args a)
        {
            var map = G.Map; string name = a.Need("name");
            Area? area = name.Equals("home", StringComparison.OrdinalIgnoreCase) ? map.areaManager.Home : map.areaManager.AllAreas.FirstOrDefault(x => x.Label.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (area == null)
            {
                if (!a.Bool("create", false)) throw new CommandError("no area '" + name + "' (create=true makes an allowed area)");
                if (!map.areaManager.TryMakeNewAllowed(out Area_Allowed made)) throw new CommandError("area limit reached");
                made.SetLabel(name); area = made;
            }
            var c1 = G.Cell(a); var c2 = a.Has("x2") ? G.Cell(a, "x2", "z2") : c1;
            bool on = !a.Bool("remove", false);
            foreach (var c in CellRect.FromLimits(c1, c2).Cells) area[c] = on;
            return new { area = area.Label, cells = area.TrueCount };
        }

        // ---------------- storage ----------------
        private static IStoreSettingsParent StorageOf(string id)
        {
            if (id.StartsWith("zone@"))
            {
                var xz = id.Substring(5).Split(',');
                return G.Map.zoneManager.ZoneAt(new IntVec3(int.Parse(xz[0]), 0, int.Parse(xz[1]))) as Zone_Stockpile ?? throw new CommandError("no stockpile at " + id);
            }
            if (id.StartsWith("zone:")) return G.Map.zoneManager.AllZones.OfType<Zone_Stockpile>().FirstOrDefault(z => z.label.Equals(id.Substring(5), StringComparison.OrdinalIgnoreCase)) ?? throw new CommandError("no stockpile " + id);
            return G.Thing(id) as IStoreSettingsParent ?? throw new CommandError(id + " has no storage settings");
        }

        [Command("storage", Access.Act, "stockpile/shelf filter {id: ThingID | zone:<label> | zone@x,z, allow?:[defs or categories], disallow?:[...], all?: true|false, priority?: Low..Critical}; no changes = show")]
        private static object Storage(Args a)
        {
            var parent = StorageOf(a.Need("id"));
            var s = parent.GetStoreSettings();
            if (a.Has("all")) { if (a.Bool("all", true)) s.filter.SetAllowAll(parent.GetParentStoreSettings()?.filter); else s.filter.SetDisallowAll(); }
            void Apply(string name, bool allow)
            {
                if (DefDatabase<ThingDef>.GetNamedSilentFail(name) is ThingDef td) s.filter.SetAllow(td, allow);
                else if (DefDatabase<ThingCategoryDef>.GetNamedSilentFail(name) is ThingCategoryDef cat) s.filter.SetAllow(cat, allow);
                else throw new CommandError("no ThingDef or ThingCategoryDef '" + name + "'");
            }
            foreach (var n in a.Strings("allow")) Apply(n, true);
            foreach (var n in a.Strings("disallow")) Apply(n, false);
            if (a.Str("priority") is string pr) s.Priority = (StoragePriority)Enum.Parse(typeof(StoragePriority), pr, true);
            return new { priority = s.Priority.ToString(), allowedDefs = s.filter.AllowedDefCount, summary = s.filter.Summary };
        }

        // ---------------- prisoners ----------------
        /// <summary>The prisoner tab's filter (ITab_Pawn_Visitor.CanUsePrisonerInteractionMode).</summary>
        private static bool PrisonerModeShown(Pawn p, PrisonerInteractionModeDef mode)
        {
            if (!p.guest.Recruitable && mode.hideIfNotRecruitable) return false;
            if (p.IsWildMan() && !mode.allowOnWildMan) return false;
            if (mode.hideIfNoBloodfeeders && p.MapHeld != null && !(ModsConfig.BiotechActive && p.MapHeld.mapPawns.FreeColonistsAndPrisonersSpawned.Any(c => c.IsBloodfeeder()))) return false;
            if (mode.hideOnHemogenicPawns && ModsConfig.BiotechActive && p.genes != null && p.genes.HasActiveGene(GeneDefOf.Hemogenic)) return false;
            if (!mode.allowInClassicIdeoMode && Find.IdeoManager.classicMode) return false;
            if (ModsConfig.AnomalyActive)
            {
                if (mode.hideIfNotStudiableAsPrisoner && !(p.TryGetComp<CompStudiable>(out var comp) && comp.EverStudiable() && p.kindDef.studiableAsPrisoner && !p.everLostEgo)) return false;
                if (mode.hideIfGrayFleshNotAppeared && !Find.Anomaly.hasSeenGrayFlesh) return false;
            }
            return true;
        }

        [Command("prisoner", Access.Act, "set a prisoner's interaction {id, mode: e.g. MaintainOnly|AttemptRecruit|ReduceResistance|Release|Execution|Enslave}; no mode lists modes")]
        private static object Prisoner(Args a)
        {
            var p = G.Pawn(a);
            if (!p.IsPrisonerOfColony) throw new CommandError(p.ThingID + " is not a prisoner of the colony");
            if (a.Str("mode") is string m)
            {
                var def = DefDatabase<PrisonerInteractionModeDef>.AllDefs.FirstOrDefault(d => d.defName.Equals(m, StringComparison.OrdinalIgnoreCase)) ?? throw new CommandError("no mode " + m);
                if (Bridge.Settings.fairPlay && !PrisonerModeShown(p, def)) throw new CommandError("fair-play mode: " + def.defName + " is not offered for this prisoner");
                p.guest.SetExclusiveInteraction(def);
            }
            return new { mode = p.guest.ExclusiveInteractionMode?.defName, resistance = p.guest.resistance, will = p.guest.will, modes = a.Has("mode") ? null : DefDatabase<PrisonerInteractionModeDef>.AllDefs.Where(d => !Bridge.Settings.fairPlay || PrisonerModeShown(p, d)).Select(d => d.defName).ToList() };
        }

        [Command("fair-play", Access.Act, "switch fair-play mode ON for this player (only the player can switch it off, in the mod settings)")]
        private static object FairPlay(Args a)
        {
            if (!a.Bool("on", true)) throw new CommandError("fair play can only be switched off by the player in Options → Mod settings → Agent Bridge");
            Bridge.Settings.fairPlay = true; Bridge.Settings.Write();
            return new { fairPlay = true };
        }

        // ---------------- colony status ----------------
        [Command("colony", Access.Read, "colony progress: days, colonists, wealth, research, threats, game over")]
        private static object Colony(Args a)
        {
            var maps = Find.Maps.Where(m => m.IsPlayerHome).ToList();
            return new
            {
                days = GenDate.DaysPassed, colonists = maps.Sum(m => m.mapPawns.FreeColonistsSpawnedCount), downed = maps.Sum(m => m.mapPawns.FreeColonistsSpawned.Count(p => p.Downed)),
                prisoners = maps.Sum(m => m.mapPawns.PrisonersOfColonySpawnedCount), wealth = maps.Sum(m => m.wealthWatcher.WealthTotal),
                researched = DefDatabase<ResearchProjectDef>.AllDefs.Count(d => d.IsFinished), research = Find.ResearchManager.GetProject()?.defName,
                caravans = Find.WorldObjects.Caravans.Count(c => c.IsPlayerControlled), storyteller = Find.Storyteller.def.defName, difficulty = Find.Storyteller.difficultyDef?.defName,
                fairPlay = Bridge.Settings.fairPlay ? true : (bool?)null, colonyLost = maps.Count == 0 && Find.WorldObjects.Caravans.All(c => !c.IsPlayerControlled) ? true : (bool?)null,
            };
        }
    }
}
