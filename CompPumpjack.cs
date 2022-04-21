using PipeSystem;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace VCHEBroms
{
    public class CompProperties_Pumpjack : CompProperties
    {
        public CompProperties_Pumpjack()
        {
            compClass = typeof(CompPumpjack);
        }

        public int ticksPerPortion = 60;
        public SoundDef ambientSound;
        public int resourcePerPortion = 10;
    }

    [StaticConstructorOnStartup]
    public class CompPumpjack : ThingComp
    {
        private static readonly Material PumpjackBottom = MaterialPool.MatFrom("Things/Building/Production/DeepchemPumpjack_Bottom");
        private static readonly Material PumpjackPump = MaterialPool.MatFrom("Things/Building/Production/DeepchemPumpjack_Pump");

        private Vector3 pumpPos = Vector3.zero;
        private Vector3 pumpPosMax;
        private Vector3 bottomPos;
        private Vector3 trueCenter;


        private List<CompResourceStorage> resourceList;

        private CompResourceStorage resource;
        private CompPowerTrader powerComp;

        private Sustainer sustainer;
        private int nextProduceTick = -1;
        private bool noCapacity = false;
        private bool goingUp = true;
        private bool cycleOver = true;
        private List<IntVec3> adjCells;

        internal List<IntVec3> lumpCells;

        Dictionary<ThingWithComps, CompResourceStorage> resourceStorageComps = new Dictionary<ThingWithComps, CompResourceStorage>();

        public CompProperties_Pumpjack Props => (CompProperties_Pumpjack)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {

            // instead of private CompResourceStorage resource;  you need a list of CompResourceStorage
            // and then in TryProducePortion, you need to search the right net
            // easiest way I see would be to check there net resource tag
            // and set the nets tags to be the resource label


            base.PostSpawnSetup(respawningAfterLoad);

            resource = parent.GetComp<CompResourceStorage>();
            powerComp = parent.GetComp<CompPowerTrader>();
            adjCells = GenAdj.CellsAdjacent8Way(parent).ToList();

            trueCenter = parent.TrueCenter();
            if (pumpPos.x != trueCenter.x)
                pumpPos = trueCenter + new Vector3(0f, 0.75f, 0f);

            bottomPos = trueCenter + new Vector3(0f, 1f, 0f);
            pumpPosMax = trueCenter + new Vector3(0f, 0f, 1.1f);




            var thingDefList1 = DefDatabase<ThingDef>.AllDefsListForReading;                      // test loop of dictionary
            for (int i = 0; i < thingDefList1.Count; i++)
            {
                var thingDefListvar = thingDefList1[i];

                if (thingDefListVar.comps.compClass == typeof(CompResourceStorage))
                {
                    resourceStorageComps.Add(thingDefList1[i], thingDefListvar.comps.compClass);
                };
            }





            if (lumpCells.NullOrEmpty())
            {
                lumpCells = new List<IntVec3>();
                var treated = new HashSet<IntVec3>();
                var toCheck = new Queue<IntVec3>();

                var cell = parent.Position;
                var map = parent.Map;

                toCheck.Enqueue(cell);
                treated.Add(cell);

                while (toCheck.Count > 0)
                {
                    var temp = toCheck.Dequeue();
                    lumpCells.Add(temp);

                    var neighbours = GenAdjFast.AdjacentCellsCardinal(temp);
                    for (int i = 0; i < neighbours.Count; i++)
                    {
                        var n = neighbours[i];
                        if (!treated.Contains(n) && map.deepResourceGrid.ThingDefAt(n) is ThingDef r)
                        {
                            treated.Add(n);
                            toCheck.Enqueue(n);
                        }
                    }
                }

                lumpCells.SortByDescending(c => c.DistanceTo(cell));
            }

            LongEventHandler.ExecuteWhenFinished(delegate
            {
                StartSustainer();
            });
        }

        public override void PostDeSpawn(Map map)
        {
            nextProduceTick = -1;
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref nextProduceTick, "nextProduceTick", 0);
            Scribe_Values.Look(ref noCapacity, "noCapacity", false);
            Scribe_Values.Look(ref cycleOver, "cycleOver");
            Scribe_Values.Look(ref goingUp, "goingUp");
            Scribe_Values.Look(ref pumpPos, "pumpPos");

            Scribe_Collections.Look(ref lumpCells, "lumpCells", LookMode.Value);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (parent.Spawned && lumpCells.Count > 0)
            {
                var ticksGame = Find.TickManager.TicksGame;
                if (nextProduceTick == -1)
                {
                    nextProduceTick = ticksGame + Props.ticksPerPortion; //changed 1 to Props.resourcePerPortion
                }
                else if (ticksGame >= nextProduceTick)
                {
                    TryProducePortion();
                    nextProduceTick = ticksGame + Props.ticksPerPortion; //changed 1 to Props.resourcePerPortion
                }

                if (!cycleOver)
                {
                    if (goingUp)
                    {
                        pumpPos.z += 0.01f;
                        if (pumpPos.z > pumpPosMax.z)
                            goingUp = false;
                    }
                    else
                    {
                        pumpPos.z -= 0.03f;
                        if (pumpPos.z < trueCenter.z)
                        {
                            cycleOver = true;
                            goingUp = true;
                        }
                    }
                }

                sustainer?.Maintain();
            }
            else
            {
                EndSustainer();
            }
        }

        public override string CompInspectStringExtra()
        {
            if (parent.Spawned)
            {
                if (noCapacity)
                {
                    return "VCHE_CantPump".Translate();
                }
                if (lumpCells.Count == 0)
                {
                    return "DeepDrillNoResources".Translate();
                }
            }
            return null;
        }

        public override void PostDraw()
        {
            base.PostDraw();
            DrawMat(PumpjackPump, pumpPos);
            DrawMat(PumpjackBottom, bottomPos);
        }

        private void TryProducePortion()
        {
            // No power -> Return
            if (powerComp != null && !powerComp.PowerOn)
            {
                EndSustainer();
                return;
            }
            // Get resource
            bool nextResource = GetNextResource(out ThingDef resDef, out int count, out IntVec3 cell);
            // Resource is null -> Return
            if (resDef == null)
                return;

            var map = parent.Map;
            // Resource comp is here
            
            
            foreach(KeyValuePair<ThingDef, CompResourceStorage> keyValue in resourceStorageComps) // test tbd
            { 
                if (resource != null && resDef == keyValue.Key.resource.name) // test tbd
                {
                    var net = resource.PipeNet;    // previously var net = resource.PipeNet;
                    if (net.connectors.Count > 1)
                    {
                        noCapacity = net.AvailableCapacity <= 0;

                        if (!noCapacity)
                        {
                            parent.Map.deepResourceGrid.SetAt(cell, resDef, count - Props.resourcePerPortion); //changed 1 to Props.resourcePerPortion
                            net.DistributeAmongStorage(Props.resourcePerPortion);

                            if (cycleOver) cycleOver = false;
                            StartSustainer();
                        }
                        else
                        {
                            EndSustainer();
                        }
                        return;
                    }
                }
            }

            // If there is no resource comp
            // Deplete resource by one
            if (nextResource)
            {
                StartSustainer();
                parent.Map.deepResourceGrid.SetAt(cell, resDef, count - Props.resourcePerPortion); //changed 1 to Props.resourcePerPortion
                // Spawn items
                for (int i = 0; i < adjCells.Count; i++)
                {
                    // Find an output cell
                    var c = adjCells[i];
                    if (c.Walkable(map))
                    {
                        // Try find thing of the same def
                        var t = c.GetFirstThing(map, resDef);
                        if (t != null)
                        {
                            if ((t.stackCount + Props.resourcePerPortion) > t.def.stackLimit) //changed 1 to Props.resourcePerPortion
                                continue;
                            // We found some, modifying stack size
                            t.stackCount += Props.resourcePerPortion;
                        }
                        else
                        {
                            // We didn't find any, creating thing
                            t = ThingMaker.MakeThing(resDef);
                            t.stackCount = Props.resourcePerPortion;
                            GenPlace.TryPlaceThing(t, c, map, ThingPlaceMode.Near);
                        }
                        break;
                    }
                }
                if (cycleOver) cycleOver = false;
            }
        }

        private bool GetNextResource(out ThingDef resDef, out int countPresent, out IntVec3 cell)
        {
            var map = parent.Map;
            lumpCells.RemoveAll(c => map.deepResourceGrid.ThingDefAt(c) == null);

            if (lumpCells.Count > 0)
            {
                var c = lumpCells[0];

                if (map.deepResourceGrid.ThingDefAt(c) is ThingDef r)
                {
                    resDef = r;
                    countPresent = map.deepResourceGrid.CountAt(c);
                    cell = c;
                    return true;
                }
                else
                {
                    resDef = null;
                    countPresent = 0;
                    cell = c;
                    lumpCells.RemoveAt(0);
                    return false;
                }
            }
            resDef = null;
            countPresent = 0;
            cell = IntVec3.Invalid;
            return false;
        }

        private void DrawMat(Material mat, Vector3 drawPos)
        {
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(drawPos, parent.Rotation.AsQuat, new Vector3(3, 1, 3)), mat, 0);
        }

        public void StartSustainer()
        {
            if (!Props.ambientSound.NullOrUndefined() && sustainer == null)
            {
                SoundInfo info = SoundInfo.InMap(parent);
                sustainer = Props.ambientSound.TrySpawnSustainer(info);
            }
        }

        public void EndSustainer()
        {
            if (sustainer != null)
            {
                sustainer.End();
                sustainer = null;
            }
        }
    }
}