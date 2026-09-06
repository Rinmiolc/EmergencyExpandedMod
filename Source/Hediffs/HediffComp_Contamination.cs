using RimWorld;
using Verse;
using UnityEngine;
using System.Text;

namespace EmergencyExpanded
{
    public class HediffCompProperties_Contamination : HediffCompProperties
    {
        public HediffCompProperties_Contamination()
        {
            this.compClass = typeof(HediffComp_Contamination);
        }
    }

    public class HediffComp_Contamination : HediffComp
    {
        public HediffCompProperties_Contamination Props => (HediffCompProperties_Contamination)this.props;

        // 污染度 0.0 ~ 1.0+
        public float contamination = 0f;
        
        // 初始评估标记，防止重复赋予初始污染
        private bool initialized = false;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref contamination, "contamination", 0f);
            Scribe_Values.Look(ref initialized, "initialized", false);
        }

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);
            if (!initialized && dinfo.HasValue)
            {
                InitializeContamination(dinfo.Value);
                initialized = true;
            }
        }

        private void InitializeContamination(DamageInfo dinfo)
        {
            float baseContamination = EE_Constants.ContaminationBase; // 默认基础污染

            if (dinfo.Def != null)
            {
                // 烧伤：高温灭菌，初始几乎无污染
                if (dinfo.Def == DamageDefOf.Burn || dinfo.Def == DamageDefOf.Flame || (dinfo.Def.armorCategory != null && dinfo.Def.armorCategory.defName == "Heat"))
                {
                    baseContamination = 0.0f; 
                }
                // 枪伤/破片伤：初始污染高 (兼容 CE 远程弹药与破片)
                else if (dinfo.Def.isRanged || dinfo.Def.defName.Contains("Fragment"))
                {
                    baseContamination += EE_Constants.ContaminationRangedAdded;
                }
                // 动物撕咬：污染极高
                else if (dinfo.Def == DamageDefOf.Bite || dinfo.Def.defName.IndexOf("bite", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    baseContamination += EE_Constants.ContaminationBiteAdded;
                }
                // 利器砍伤 (兼容各类锋利近战武器)
                else if (dinfo.Def.armorCategory == DamageArmorCategoryDefOf.Sharp)
                {
                    baseContamination += EE_Constants.ContaminationSharpAdded;
                }
                // 钝器伤/开放性骨折
                else if (dinfo.Def == DamageDefOf.Blunt || dinfo.Def == DamageDefOf.Crush || (dinfo.Def.armorCategory != null && dinfo.Def.armorCategory.defName == "Blunt"))
                {
                    baseContamination += EE_Constants.ContaminationBluntAdded;
                }
            }

            this.contamination = Mathf.Clamp01(baseContamination);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            if (Pawn == null || Pawn.Dead) return;

            // 每 EE_Constants.ContaminationCheckInterval tick 检查一次环境，大幅降低性能消耗
            if (Pawn.IsHashIntervalTick(EE_Constants.ContaminationCheckInterval))
            {
                UpdateContaminationFromEnvironment();
                CheckInfectionProgression();
            }
        }

        private void UpdateContaminationFromEnvironment()
        {
            // 如果伤口是永久性陈旧伤疤，不再受环境污染
            if (this.parent.IsPermanent()) return;

            float environmentFactor = 0f;

            if (Pawn.Spawned && Pawn.Map != null)
            {
                // 1. 室内综合清洁度计算 (优先使用房间环境，比单纯看地板贴图更符合 RimWorld 机制)
                Room room = Pawn.GetRoom();
                if (room != null && !room.PsychologicallyOutdoors)
                {
                    float roomCleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
                    if (roomCleanliness < 0)
                    {
                        environmentFactor += EE_Constants.ContaminationCleanlinessFactor * Mathf.Abs(roomCleanliness);
                    }
                }
                else
                {
                    // 室外：计算地表天然清洁度
                    TerrainDef terrain = Pawn.Position.GetTerrain(Pawn.Map);
                    if (terrain != null)
                    {
                        float terrainCleanliness = terrain.GetStatValueAbstract(StatDefOf.Cleanliness);
                        if (terrainCleanliness < 0)
                        {
                            environmentFactor += EE_Constants.ContaminationCleanlinessFactor * Mathf.Abs(terrainCleanliness);
                        }
                    }
                }

                // 2. 地面直接接触判定 (倒地小人全身接触；站立小人的下肢伤口踩踏接触)
                bool isLowerExtremity = this.parent.Part != null && (
                    this.parent.Part.def.defName.IndexOf("leg", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    this.parent.Part.def.defName.IndexOf("foot", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    this.parent.Part.def.defName.IndexOf("toe", System.StringComparison.OrdinalIgnoreCase) >= 0);

                if (Pawn.Downed || isLowerExtremity)
                {
                    TerrainDef terrain = Pawn.Position.GetTerrain(Pawn.Map);
                    float directContactMultiplier = Pawn.Downed ? 1.0f : EE_Constants.ContaminationWalkingExtremityFactor;

                    if (terrain != null && (terrain.defName.Contains("Mud") || terrain.defName.Contains("Water") || terrain.defName.Contains("Swamp")))
                    {
                        environmentFactor += EE_Constants.ContaminationMudFactor * directContactMultiplier;
                    }

                    // 地面上是否有血迹、呕吐物等污垢
                    var thingList = Pawn.Position.GetThingList(Pawn.Map);
                    bool hasFilth = false;
                    for (int i = 0; i < thingList.Count; i++)
                    {
                        if (thingList[i].def != null && thingList[i].def.category == ThingCategory.Filth)
                        {
                            hasFilth = true;
                            break;
                        }
                    }

                    if (hasFilth)
                    {
                        environmentFactor += EE_Constants.ContaminationFilthFactor * directContactMultiplier;
                    }
                }
            }

            // 3. 伤口未包扎时，自然污染微量上升 (细菌繁殖)
            if (!this.parent.IsTended())
            {
                environmentFactor += EE_Constants.ContaminationUntendedFactor;
            }

            // 4. 针对烧伤的特殊环境污染乘数（皮肤屏障丧失，极易受环境细菌侵蚀）
            HediffComp_Burn burnComp = this.parent.TryGetComp<HediffComp_Burn>();
            if (burnComp != null)
            {
                int degree = burnComp.BurnDegree;
                if (degree == 3) environmentFactor *= EE_Constants.BurnEnvMultiplierDegree3;
                else if (degree == 2) environmentFactor *= EE_Constants.BurnEnvMultiplierDegree2;
                else if (degree == 1) environmentFactor *= EE_Constants.BurnEnvMultiplierDegree1;
            }

            if (environmentFactor > 0f)
            {
                this.contamination = Mathf.Clamp01(this.contamination + environmentFactor);
            }
            else if (this.parent.IsTended() && this.contamination > 0f)
            {
                // 包扎良好且环境洁净时，体内白细胞 (血液过滤) 缓慢清除微量杂菌
                float filtration = Pawn.health.capacities.GetLevel(PawnCapacityDefOf.BloodFiltration);
                if (filtration > 0.6f)
                {
                    this.contamination = Mathf.Clamp01(this.contamination - (0.001f * filtration));
                }
            }
        }

        private void CheckInfectionProgression()
        {
            Hediff localInf = Pawn.health.hediffSet.hediffs.Find(h => (h.def == HediffDefOf.WoundInfection || h.def == EE_DefOf.EE_Necrosis) && h.Part == this.parent.Part);

            if (this.contamination >= EE_Constants.ContaminationLocalInfectionThreshold)
            {
                if (localInf == null)
                {
                    // 判断应该引发哪种局部感染：
                    // 仅重度压砸伤 (Crush 伤害>=10) 或开放性骨折或超极度污染 (>=0.8) 引发深部坏死，其余开放创伤引发蜂窝织炎/原版感染
                    bool isSevereNecrotic = (this.parent.def.defName.Contains("Crush") && this.parent.Severity >= 10f) ||
                                            (EE_DefOf.EE_OpenFracture != null && this.parent.def == EE_DefOf.EE_OpenFracture) ||
                                            this.contamination >= 0.80f;
                    HediffDef targetLocalInfection = isSevereNecrotic ? EE_DefOf.EE_Necrosis : HediffDefOf.WoundInfection;

                    Pawn.health.AddHediff(targetLocalInfection, this.parent.Part);
                    localInf = Pawn.health.hediffSet.hediffs.Find(h => h.def == targetLocalInfection && h.Part == this.parent.Part);
                    if (Pawn.Spawned)
                    {
                        Messages.Message("EE_MessageWoundContaminatedInfection".Translate(Pawn.LabelShort, targetLocalInfection.label), Pawn, MessageTypeDefOf.NegativeHealthEvent);
                    }
                }
            }

            // 多伤口聚合保护：如果同一部位存在多个伤口，只由该部位污染度最高的主伤口驱动感染严重度恶化，防止数值指数级叠加爆炸
            bool isPrimaryWoundOnPart = true;
            if (this.parent.Part != null)
            {
                var hediffs = Pawn.health.hediffSet.hediffs;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    if (hediffs[i] != this.parent && hediffs[i].Part == this.parent.Part && hediffs[i] is Hediff_Injury otherInjury)
                    {
                        var otherComp = otherInjury.TryGetComp<HediffComp_Contamination>();
                        if (otherComp != null && otherComp.contamination > this.contamination)
                        {
                            isPrimaryWoundOnPart = false;
                            break;
                        }
                    }
                }
            }

            // 如果已经有局部感染，基于最高污染创面动态增加严重度
            if (isPrimaryWoundOnPart && localInf != null && this.contamination > 0f)
            {
                float extraSeverity = this.contamination * EE_Constants.InfectionDynamicSeverityBase;
                
                // 烧伤的坏死组织加速感染
                HediffComp_Burn localBurnComp = this.parent.TryGetComp<HediffComp_Burn>();
                if (localBurnComp != null)
                {
                    if (localBurnComp.BurnDegree == 3) extraSeverity *= EE_Constants.BurnInfectionFactorDegree3;
                    else if (localBurnComp.BurnDegree == 2) extraSeverity *= EE_Constants.BurnInfectionFactorDegree2;
                }

                localInf.Severity += extraSeverity;
                
                // 动态渗出：如果局部感染达到化脓期(>=0.66) 或 坏死期，概率性在地面生成污垢
                if (localInf.Severity >= 0.66f && Pawn.Spawned && Pawn.Map != null)
                {
                    if (Rand.Chance(0.2f)) // 每次检查有 20% 概率生成化脓污垢
                    {
                        FilthMaker.TryMakeFilth(Pawn.Position, Pawn.Map, ThingDefOf.Filth_Blood, 1, FilthSourceFlags.None);
                    }
                }
            }

            // 触发全身败血症：极度污染 (>= 0.95) 或 局部感染严重且高污染
            bool triggerSepsis = false;
            float sepsisThreshold = EE_Constants.ContaminationSepsisThreshold;
            
            // III度烧伤降低败血症门槛
            HediffComp_Burn sepsisBurnComp = this.parent.TryGetComp<HediffComp_Burn>();
            if (sepsisBurnComp != null && sepsisBurnComp.BurnDegree == 3)
            {
                sepsisThreshold = EE_Constants.BurnSepsisThresholdDegree3;
            }

            if (this.contamination >= 0.95f) 
            {
                triggerSepsis = true;
            }
            else if (this.contamination >= sepsisThreshold && localInf != null && localInf.Severity >= 0.66f)
            {
                triggerSepsis = true;
            }

            if (triggerSepsis)
            {
                // 触发全身败血症
                if (EE_DefOf.EE_Sepsis != null && !Pawn.health.hediffSet.HasHediff(EE_DefOf.EE_Sepsis))
                {
                    Pawn.health.AddHediff(EE_DefOf.EE_Sepsis);
                    if (Pawn.Spawned)
                    {
                        Find.LetterStack.ReceiveLetter("EE_LetterSepsis_Label".Translate(), "EE_LetterSepsis_Desc".Translate(Pawn.LabelShort), LetterDefOf.ThreatSmall, Pawn);
                    }
                    
                    // 联动触发 SIRS
                    if (EE_DefOf.SIRS != null && !Pawn.health.hediffSet.HasHediff(EE_DefOf.SIRS))
                    {
                        Pawn.health.AddHediff(EE_DefOf.SIRS);
                    }
                }
            }
        }

        public override void CompTended(float quality, float maxQuality, int batchPosition = 0)
        {
            base.CompTended(quality, maxQuality, batchPosition);

            // 包扎可以降低污染度，品质越高降低越多
            float reduction = EE_Constants.ContaminationTendReductionBase + (quality * EE_Constants.ContaminationTendReductionFactor); 
            this.contamination = Mathf.Clamp01(this.contamination - reduction);
        }

        public override string CompLabelInBracketsExtra
        {
            get
            {
                if (contamination > 0.10f)
                {
                    return "EE_ContaminationDegree".Translate((contamination * 100f).ToString("F0"));
                }
                return null;
            }
        }

        public override string CompTipStringExtra
        {
            get
            {
                if (contamination > 0.0f)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("EE_WoundContaminationDegree".Translate((contamination * 100f).ToString("F1")));
                    if (contamination > 0.5f)
                    {
                        sb.AppendLine("EE_WoundContaminationWarningHigh".Translate());
                    }
                    else if (contamination > 0.2f)
                    {
                        sb.AppendLine("EE_WoundContaminationRisk".Translate());
                    }
                    return sb.ToString().TrimEndNewlines();
                }
                return null;
            }
        }
    }
}
