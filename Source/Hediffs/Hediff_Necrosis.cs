using System.Collections.Generic;
using RimWorld;
using Verse;

namespace EmergencyExpanded
{
    public class Hediff_Necrosis : HediffWithComps
    {
        private bool hasDroppedOrHandled = false;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref hasDroppedOrHandled, "hasDroppedOrHandled", false);
        }

        public override void Tick()
        {
            base.Tick();

            if (pawn == null || pawn.Dead || hasDroppedOrHandled) return;

            // 每 600 tick 检查一次坏疽演进
            if (pawn.IsHashIntervalTick(600) && this.Severity >= 0.85f && this.Part != null)
            {
                // 判断是否是核心重要器官（躯干、头部、颈部、脑、脊椎、核心血泵）
                if (IsVitalCorePart(this.Part))
                {
                    // 核心部位严禁物理摧毁！转为诱发全身败血症和严重 SIRS 毒素释放
                    if (!pawn.health.hediffSet.HasHediff(EE_DefOf.EE_Sepsis))
                    {
                        pawn.health.AddHediff(EE_DefOf.EE_Sepsis);
                        if (pawn.Spawned)
                        {
                            Find.LetterStack.ReceiveLetter(
                                "EE_LetterSepsis_Label".Translate(),
                                "EE_LetterSepsis_Desc".Translate(pawn.LabelShort),
                                LetterDefOf.ThreatSmall,
                                pawn
                            );
                        }
                    }
                    hasDroppedOrHandled = true;
                }
                else if (EE_BodyPartCache.IsExtremityPart(this.Part) || (this.Part.def.tags != null && (this.Part.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbSegment) || this.Part.def.tags.Contains(BodyPartTagDefOf.MovingLimbSegment))))
                {
                    // 四肢与末梢：严重坏疽脱落（自然截肢）
                    BodyPartRecord deadPart = this.Part;
                    hasDroppedOrHandled = true;

                    if (pawn.Spawned)
                    {
                        Messages.Message("EE_MessageGangreneAmputation".Translate(pawn.LabelShort, deadPart.Label), pawn, MessageTypeDefOf.NegativeHealthEvent);
                    }

                    // 造成损毁切除
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.SurgicalCut, 99999f, 999f, -1f, null, deadPart, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true));
                }
            }
        }

        public static bool IsVitalCorePart(BodyPartRecord part)
        {
            if (part == null || part.def == null) return true;
            if (part.def == BodyPartDefOf.Torso || part.def == BodyPartDefOf.Head || part.def == BodyPartDefOf.Neck) return true;
            if (part.def.tags != null)
            {
                if (part.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource) ||
                    part.def.tags.Contains(BodyPartTagDefOf.BloodPumpingSource) ||
                    part.def.tags.Contains(BodyPartTagDefOf.BreathingPathway) ||
                    part.def.tags.Contains(BodyPartTagDefOf.Spine))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
