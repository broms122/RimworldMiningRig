﻿using PipeSystem;
using RimWorld;
using UnityEngine;
using Verse;
using ItemProcessor;

namespace VCHEBroms
{
    public class CompProperties_Refinery : CompProperties
    {
        public CompProperties_Refinery()
        {
            compClass = typeof(CompRefinery);
        }
    }

    public class CompRefinery : ThingComp
    {
        private CompResourceProcessor resource;
        private Vector3 fireDrawPos;
        public Building_ItemProcessor itemProcessor;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (parent is Building_ItemProcessor processor)
            {
                itemProcessor = processor;
            }


            resource = parent.GetComp<CompResourceProcessor>();

            fireDrawPos = parent.DrawPos;
            fireDrawPos.y += 3f / 74f;
            fireDrawPos.z += 1.25f;
            
        }

        public override void PostDraw()
        {
            if (itemProcessor.processorStage == ProcessorStage.Working)
            {
                CompFireOverlay.FireGraphic.Draw(fireDrawPos, Rot4.North, parent);
            }
        }
    }
}