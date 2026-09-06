using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace EmergencyExpanded
{
    public class Recipe_Debridement : Recipe_Surgery
    {
        public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
        {
            // 找出所有有污染度伤口、坏死或者局部活动性化脓感染的部位
            foreach (var part in pawn.RaceProps.body.AllParts)
            {
                if (pawn.health.hediffSet.HasHediff(EE_DefOf.EE_Necrosis, part) || 
                    pawn.health.hediffSet.HasHediff(HediffDefOf.WoundInfection, part) ||
                    pawn.health.hediffSet.hediffs.Any(h => h.Part == part && h is Hediff_Injury && h.TryGetComp<HediffComp_Contamination>()?.contamination > 0f))
                {
                    yield return part;
                }
            }
        }

        public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
        {
            if (billDoer != null)
            {
                if (CheckSurgeryFail(billDoer, pawn, ingredients, part, bill))
                {
                    return;
                }
                TaleRecorder.RecordTale(TaleDefOf.DidSurgery, billDoer, pawn);
            }

            bool didAnything = false;

            // 1. 移除坏死
            Hediff necrosis = pawn.health.hediffSet.hediffs.FirstOrDefault(h => h.def == EE_DefOf.EE_Necrosis && h.Part == part);
            if (necrosis != null)
            {
                pawn.health.RemoveHediff(necrosis);
                didAnything = true;
            }

            // 2. 治疗并削减局部化脓感染 (WoundInfection)
            Hediff woundInf = pawn.health.hediffSet.hediffs.FirstOrDefault(h => h.def == HediffDefOf.WoundInfection && h.Part == part);
            if (woundInf != null)
            {
                if (woundInf.Severity <= 0.35f)
                {
                    pawn.health.RemoveHediff(woundInf);
                }
                else
                {
                    woundInf.Severity = UnityEngine.Mathf.Max(0.1f, woundInf.Severity * (1f - EE_Constants.DebridementInfectionSeverityReduction));
                }
                didAnything = true;
            }

            // 3. 清除所有开放创面的污染度
            float medSkill = billDoer?.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 5f;
            float damageAmount = UnityEngine.Mathf.Max(EE_Constants.DebridementDamageMin, EE_Constants.DebridementDamageBase - (medSkill * EE_Constants.DebridementDamageSkillReduction));
            bool partDestroyed = false;

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs.Where(h => h.Part == part && h is Hediff_Injury).ToList();
            foreach (var h in hediffs)
            {
                var comp = h.TryGetComp<HediffComp_Contamination>();
                if (comp != null && comp.contamination > 0f)
                {
                    comp.contamination = 0f;
                    didAnything = true;
                }
            }

            // 4. 切除腐肉伤害 (进行安全边界约束，防止误切重要器官或当场致死)
            if (didAnything)
            {
                float partHealthBefore = pawn.health.hediffSet.GetPartHealth(part);
                if (partHealthBefore > 0f)
                {
                    bool isVital = Hediff_Necrosis.IsVitalCorePart(part);
                    float maxSafeFraction = isVital ? 0.25f : EE_Constants.DebridementMaxDamageFractionOfHealth;
                    float maxAllowedDamage = UnityEngine.Mathf.Max(1f, partHealthBefore * maxSafeFraction);
                    damageAmount = UnityEngine.Mathf.Min(damageAmount, maxAllowedDamage);

                    DamageInfo dinfo = new DamageInfo(DamageDefOf.Cut, damageAmount, 0f, -1f, billDoer, part, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
                    pawn.TakeDamage(dinfo);
                    
                    if (pawn.health.hediffSet.GetPartHealth(part) <= 0f && partHealthBefore > 0f)
                    {
                        partDestroyed = true;
                    }
                }
            }

            if (didAnything && pawn.Spawned)
            {
                if (partDestroyed)
                {
                    Messages.Message("EE_MessageDebridementAccidentalAmputation".Translate(billDoer?.LabelShort ?? "EE_Doctor".Translate(), pawn.LabelShort, part.Label), pawn, MessageTypeDefOf.NegativeHealthEvent);
                }
                else
                {
                    Messages.Message("EE_MessageDebridementSuccess".Translate(billDoer?.LabelShort ?? "EE_Doctor".Translate(), pawn.LabelShort, damageAmount.ToString("F1")), pawn, MessageTypeDefOf.PositiveEvent);
                }
            }
        }
    }
}
