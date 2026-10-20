# Flash 测试记录

[返回 Flash 入口](README.md)

本文记录 Flash 的可复跑验证入口与当前证据。方法定义见 [准入与校验](admission-and-verification.md)。

## 1. 复跑入口

纯内核合同（K0），不依赖游戏：

```bash
DOTNET_TieredCompilation=0 dotnet run --project tools/testing/checks/FlashKernelChecks -c Release
```

差分探针（K1 单步穷举、K2 随机游走），需要游戏程序集与已构建的模组：

```bash
dotnet build CombatSolver.csproj -c Release -p:CopyModOnBuild=false
dotnet build tools/search/OfflineSearchHarness/OfflineSearchHarness.csproj -c Release
OFFLINE_HARNESS_FLASH_DIFF=1 OFFLINE_HARNESS_FLASH_DIFF_WALKS=32 \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M1 --out <输出目录>
```

PowerShell 用 `$env:OFFLINE_HARNESS_FLASH_DIFF = '1'` 设置同名变量后运行相同 `dotnet` 命令。探针把结果写入输出目录的 `flash-diff.txt`（首行为汇总），并在 `harness-result.json` 的 `flashDiff` 字段记录同样的计数。`OFFLINE_HARNESS_FLASH_DIFF_STEPS` 控制每条随机游走的最大步数（默认 60）。

主搜索分发（`BeamSolveDispatch`）在协调器模式下复跑，日志中的 `[CombatSolver/Flash]` 行记录对照或采用结果：

```bash
OFFLINE_HARNESS_FLASH_MODE=Shadow \
  dotnet .local/tool-build/OfflineSearchHarness/bin/Release/net9.0/OfflineSearchHarness.dll \
  --character IRONCLAD --encounter NIBBITS_NORMAL --seed FLASH1 --milestone M2 \
  --search-mode Coordinator --budget-ms 10000 --out <输出目录>
```

无人测试中的实机部署用 `--flash-search-mode-for-test Off|Shadow|On`（PowerShell `-FlashSearchModeForTest`）临时覆盖设置；`--export-bug-report-after-combat` 导出的问题包在 `diagnostics/logs/combat/*.jsonl` 中保留 `[CombatSolver/Flash]` 行：

```bash
./tools/testing/run-unattended-test.sh --scenario-id FLASH-ON-NIBBITS --character-id IRONCLAD --encounter-id NIBBITS_NORMAL \
  --enemy-current-hp 1000 --headless-fast-mode-for-test Instant --deployment-fast-mode-for-test Instant \
  --deployment-inter-action-delay-seconds-for-test 0 --expected-unexpected-replans-at-most 0 \
  --flash-search-mode-for-test On --timeout-seconds 120 --cleanup-instance-on-exit
```

## 2. 当前证据（2026-10-19，Linux）

F1 纵切与内容批次 P1–P12 的逐批证据已归档到 [内容批次测试记录](../archive/flash-search/testing-content-batches-2026-10-18.md)，强制行动/眩晕（KS）、敌人生成（KM）与早期端到端测试归档到 [KS、KM 与早期端到端测试记录](../archive/flash-search/testing-batches-2026-10-19.md)，第二幕 P13 的逐根证据归档到 [第二幕 P13 测试记录](../archive/flash-search/testing-act2-p13-2026-10-19.md)；下面的全量回归覆盖其中全部根。

全量回归（`.local/tool-tasks/flash-search/regress.sh`，或按根分片并行的 `regress-par.sh`；SK、PT、DF、RG、NB、LR、MB、LS、ID、RQ、CX、TW、PF、PM、KB、SP、DX3、A3B、K4C、ST、PA、PC、DX4、PR、CP 合入后）：FlashKernelChecks 通过；150 个自然遭遇根（铁甲战士初始牌组覆盖三幕已实现遭遇的 FLASHM1/FLASHM2，另有其他四个角色的初始牌组各 6 条，以及三个第三幕首领在 `--act-index 2` 下开启首领策略的 6 条）、238 个固定根（`coverage/fixtures/cards/flash-*.json`）与 64 个生成牌组（含 29 个非铁甲战士牌组、11 个第三幕牌组）（`coverage/corpora/flash-decks/`）每根 32 条随机游走，K1 单步、K2 游走、K4 评估与 K5 下界输入全部零差异，没有准入拒绝；`unsupported` 全部为 0。

### 2.1 覆盖缺口

怪物（KM2、KA，2026-10-19）：KM2 补上空 `EncounterModel.Slots` 的槽位回退（未知槽名与现有引擎同为 -1）、失衡眩晕、沉睡、千足虫重接、寄生死亡未结算时阻止战斗结束、Surrounded 与 CurlUp 退役状态的惰性规则、猎杀者的 Tender 计数投影，实现碗虫（普通/弱）、沉睡甲虫、千足虫、异蛙寄生虫、猎杀者、Kaiser Crab、Louse Progenitor；KA 补上卡牌苦难与附魔（导入、施加/清除、归一化、附魔伤害/格挡/重放、尾部 OnPlay，18 个附魔），实现仪式兽、藤蔓蹒跚者、邪恶气体（LIVING_FOG）、注入棱晶与 MysticLighter。`coverage/fixtures/cards/flash-km2-*.json` 8 个、`flash-ka-*.json` 5 个及 12 个新遭遇的 FLASHM1/FLASHM2 自然根，每根 48 条游走零差异、`unsupported=0`。邪恶气体会反复生成毒气弹，极长的战斗可能超出 8 个已知敌人的容量，此时按容量边界回落。

第三幕（A3，2026-10-19）：实现荣耀的全部 18 个遭遇（25 种新怪物，含 Queen 的强制行动、Aeonglass 的力量与凋零计数、MagiKnight 的 Dampen 降级与恢复、Turret Operator、Soul Nexus、Living Shield 等），19 个 `coverage/fixtures/cards/flash-a3-*.json` 与 18 个遭遇的 FLASHM1/FLASHM2 自然根每根 48 条游走零差异；`FlashCard` 增加 `DampenOriginalUpgrade`，`FlashBattle` 12000 字节。第一、二幕剩余（P16）：剩余 16 张无色卡全部实现，AttackPotion、SkillPotion、PowerPotion、ColorlessPotion 补齐到 `PotionChoiceSupport` 的选择回放翻译后实现，另实现 GigantificationPotion；7 个 `flash-p16-*.json` 零差异。

剩余怪物（MB，2026-10-19）：知识恶魔的怪物行动现在产生待处理的固定选项选择（`FlashChoiceKind.SelectOption`，接入选择翻译、回放与差分比较），实现四个诅咒 Power 与诅咒卡（只作为选项）并投影 `_knowledgeDemonCurseCounters`；无餍者的目标化 SandpitPower（敌方回合开始按行减层、归零击杀目标）、FranticEscape、评估中的 `SandpitRemaining` 与“1 层流沙必死”威胁特例；新 Hook `AfterSideTurnEndLate`（`FlashHookMaskExt`）。EntropicBrew 按现有引擎冻结战斗外随机药水池，免费药水与生成药水的用量和战略成本进入评估（K4 覆盖）。两个首领的 FLASHM1/FLASHM2 自然根与 `flash-mb-*` 固定根零差异。

偷窃与瀑布巨兽（TW，2026-10-19）：实现 THIEVING_HOPPER_WEAK、GREMLIN_MERC_NORMAL 与 WATERFALL_GIANT_BOSS，三幕遭遇全部覆盖。偷窃：THIEVERY 按 `ThievingHopper._stealPriorities` 的稀有度优先从抽牌堆与弃牌堆取牌组卡并移出战斗，被偷卡的完整值存进 `FlashBattle.DetachedCards`（容量 2 → 3，与 Nightmare 共用），击杀回收、逃跑不回收；ThieveryPower 的金币、HeistPower、SurprisePower 的死亡生成与 Heist 转移、逃跑时已知敌人的压实与下标重映射；`OutstandingStolenResource` 进入评分与路线键，`MinimumOutstandingStolenResource` 纳入 K4，偷窃根照现有引擎以 `theft_policy` 禁用 incumbent 下界，`PreserveResources` 策略下的资源保全代表只在偷窃根比较。瀑布巨兽：SteamEruption 的死亡改写（HP 钳制、强制 ABOUT_TO_BLOW、保留本 Power 并清其余 Power）、ABOUT_TO_BLOW/EXPLODE 强制行动、压力枪的动态伤害、爆发阶段 `EffectiveEnemyHp` 为 0 与阻止战斗结束；`FlashEnemy.SteamPhase` 使用原填充字节，`FlashBattle` 12320 → 12408 字节。瀑布巨兽的死亡改写暴露 11 个卡牌定义漏掉的时机：照 `CorePowerSupport.ApplyCardPowers` 在镜像攻击之后、专属补偿施加敌方 Power 之前结算敌人死亡 Power（Bash、Uppercut、Thunderclap、SuckerPunch、PoisonedStab、Comet 等）。4 个 `coverage/fixtures/cards/flash-tw-*.json` 与 6 个自然根 48 条游走零差异，临时计数确认偷窃、回收、逃跑、Surprise 生成与蒸汽三阶段在游走中触发；强制 THIEVING_HOPPER_WEAK 的 180 个生成牌组中 165 个零差异，8 个在 Hopper 逃跑后现有引擎仍保留 FlutterPower（持有者已离开已知敌人）而无法投影，按缺失能力记录。48 根基准路线与每根转移数逐根不变。

设置移植（ST，2026-10-20）：`FlashScoreBridge.RejectUnsupportedSettings` 不再拒绝原版成长额度、遗物计数目标、连续致命定价与上下文排序。成长额度的十个原版来源（HandOfGreed、TheHunt、Feed、Royalties、Alchemize、GeneticAlgorithm、TheScythe、Goopy、ForbiddenGrimoire、MadScience）用五个打包计数器记录，`GrowthHpCredit` 等进入评分、终局键与早停门控；遗物计数目标照 `RelicCounterPolicy` 复刻满足掩码、距离、优先级与 Meat on the Bone 的回复信用；连续致命定价与上下文排序进入 `BeamRankScore`，上下文排序模型的 28 个特征公式抽成原搜索与 Flash 共用的一份。K4 新增成长与遗物目标的逐字段特征，差分探针在这两个排序开关下逐状态比较 `BeamRankScore`。成长 8 根、遗物 6 根、两个排序开关各 2 根的 48 条游走零差异；四项设置各 4 根的协调器开启模式全部 `FLASH_ADOPTED:verified`；默认设置下 48 根基准逐根路线、转移数与展开数相同。开发策略（只供开发者调原搜索排序，不是玩家设置）的 `Prioritize`/`Retain` 钩子直接作用于原搜索的候选截断与必保通道，Flash 没有等价结构，本 PR 中只在关闭模式下支持；第三方登记的成长来源与战略效果仍不在范围内。

评估中的动态关键字（K4C，2026-10-19）：第三幕 REGENT-04（KNIGHTS_ELITE）有 33 个状态的 K4 `LiveDeckClutter` 比现有评估多 1–2。HexPower 在战斗中给被诅咒（Hexed）的牌动态追加“虚无”关键字，不写进卡牌字段；现有引擎的牌库杂质判定经 `Hook.ModifyKeywordsInCombat` 读到它，Flash 评分只读了卡牌自身关键字。`FlashScorer` 的杂质判定与 Pagestorm 的未来虚无牌统计改用内核已有的 `FlashKernel.HasKeyword`。修正后第一、二幕五个调查各 30 个根与第三幕 150 个根的 48 条游走全部 `scoreFailed=0`、`boundFailed=0`，REGENT-04 收进 `coverage/corpora/flash-decks/`；48 根基准逐根不变。

第三幕首领策略（A3B，2026-10-19）：`SearchPolicySnapshot.Act3BossStrategy` 在 TEST_SUBJECT_BOSS、AEONGLASS_BOSS、QUEEN_BOSS 默认开启，此前 Flash 遇到它整根拒绝，每局的最终首领战都回落现有搜索；生产默认设置下这是唯一实际生效的整根策略拒绝（成长额度与遗物计数目标只在玩家配置后出现，连续致命定价与上下文排序只在测试合同中使用）。移植全部读取点：`StrategicEffectModel.Requirements` 的首领开关，Lethality 首击、Pagestorm 虚无抽牌、DanseMacabre 高费出牌、Demesne 未来能量与抽牌、PrepTime 的首领估值分支，`CaptureFirstAttackDamage` 与 `CaptureAct3BossInteractionPotential`；K4 新增 7 个逐字段特征（现有引擎侧在 `SimulationSnapshot` 上记录同名只读值，不进入状态键）。搜索阶段移植首个回合层的承诺展开：取回了付得起的能力牌（MoveToHand 主选择或预知之滴）的中间状态不参与排序，直接展开其全部合法后继，计数与去重照常；现有引擎的回合层局部墙钟切片在 Flash 中本来不存在。50 个第三幕首领牌组从 `score=rejected` 变为逐字段一致；新增 6 个首领自然根零差异。20 个首领牌组的协调器对照 19 同 1 好、0 差（IRONCLAD-11 由未完成变为第 13 回合胜利），总耗时 357.1 s → 182.5 s（−49%），20/20 主搜索 `FLASH_ADOPTED:verified`；48 根基准路线逐根相同。

缺失的药水与遗物（PR，2026-10-20）：按 FI 盘点补齐 9 瓶药水（AMBERGRIS、BONE_BREW、COSMIC_CONCOCTION、CUNNING_POTION、FOCUS_POTION、POISON_POTION、POTION_OF_CAPACITY、POTION_OF_DOOM、POT_OF_GHOULS）与 AMBERGRIS_POWER、CONFUSED_POWER，以及 34 个遗物：17 个现有引擎有镜像的按机制实现（含 PaelsEye 的额外回合——敌方回合跳过、轮次不加一、Rampart 门控；FurCoat 新生成敌人当前生命为 1；MusicBox、PaelsLegion、Metronome 等按单次出牌身份记账），7 个战斗内有行为且现有引擎经通用路径支持的实现，10 个只在战斗外或根之前生效的写无 Hook 定义并注明依据。新 Hook `AfterFlush`、`ShouldTakeExtraTurn`、`AfterTakingExtraTurn`、`AfterRelicRound`（`FlashHookMaskExt` 3–6 位），根级布尔 `HasPaelsLegion`、`FurCoatSpawnHpOne` 跳过无关根的派发；布局不变。保持拒绝的 3 个：BYRDPIP（原版宠物，现有引擎只模拟 Osty）、HISTORY_COURSE（需要跨回合保存上一张攻击牌的完整卡值，现有表示没有空槽）、DEPRECATED_RELIC（不可获得）。8 个 `coverage/fixtures/cards/flash-pr-*.json` 零差异。

协调器路线核对（DX4，2026-10-20）：随机游走走不到、协调器选中的路线才触发的分歧，用协调器开启模式的逐步核对来找：在第三幕调查 150 个、`rq-set` 46 个与 `coverage/corpora/flash-decks` 64 个牌组上跑满整场。修正 3 类根因：固定前缀之后怪物攻击的力量/敏捷基线取请求根而不是前缀末尾（现有引擎的 `_rootPowerAmounts` 在请求根捕获，前缀里 MIASMA 偷到的敏捷不进基线）；字节相同的 Sly 牌被弃置后，自动打出按一次动作内的瞬态身份标记定位被弃的那一张，而不是全局同值序号（标记区间与 BeatDown 分开，耗尽抛容量异常）；DrainPower 在战斗正在结束的那一步不升级弃牌（现有引擎的升级在战斗结束时不生效）。修正后 260 个根的协调器开启模式 5,235 次核对通过、0 次不一致；3 个 `coverage/fixtures/cards/flash-dx4-*.json` 都做了撤掉修正必失败、恢复后零差异的双向验证，其中一个用固定前缀差分（离线宿主新增 `FlashPrefixActions`）。同一检测面上剩余的回落只有容量边界（REGENT-14、REGENT-V-06）与 7 个根的 `flash-potion-policy-no-route`，后者是否现有求解器同样无路线待逐根核对。

第三幕随机牌组（DX3，2026-10-19）：此前的随机牌组调查只生成第一、二幕。以 `ACT_OVERRIDE=2 deck-survey.sh` 为五个角色各生成 30 个第三幕牌组（全部准入），48 条游走差分中 33 个根不干净：16 个在 Dampen 降级后缺少 0 级规则而记 `unsupported`，17 个有 K1/K2 分歧。按现有引擎修正 15 类：Dampen 降级补齐 0 级规则、退役记录惰性处理、克隆/变化/替换清除记录，Rampage 与 TheScythe 降级后恢复累计伤害；Galvanic 反伤落在卡牌所有者；ChainsOfBinding 只跳复制品（`IsDupe`）而非克隆；不宁提灯不翻倍非卡牌来源的减益；Vambrace 在嵌套 Sly 自动打出后恢复出牌作用域；Rampart 按 Power 表行遍历（含离场持有者）；Axebot 补货平移生命区间；敌方回合召出的怪物立即记录初始行动；Hellraiser 的无限生命与空集合计数；Gigantification 死亡后的层数回写；9 张攻击加抽牌的卡在镜像攻击后、补偿前结算敌人死亡；出牌中写当前卡变量按出牌位置而非打出堆顶。修正后 150/150 根零差异；13 个 `coverage/fixtures/cards/flash-dx3-*.json` 零差异，每个角色 2 个第三幕牌组收进 `coverage/corpora/flash-decks/`；48 根基准路线与分配不变。第三幕首领根的评估由 A3B 移植。仍记录的边界：字节相同的 Sly 牌与弃牌堆中的同值副本可能被选中另一份（牌堆内容相同、顺序不同），本批根与固定根未触发。

容量边界（CP，2026-10-20）：Flash 遇到容量上限不再整次放弃。展开中某个子状态装不下牌堆或敌人槽时只丢弃该分支并计数（`flash_capacity_dropped`），敌人槽 10 → 12，`FlashBattle` 12,472 → 12,600 字节；`next`/`ended` 两个可增长池各按 200 MiB 封顶（16,640 个状态），`ended` 达上限按回合边界同一规则压缩到 `width × 8`，`next` 达上限逐个丢弃，压缩与丢弃保留计划成员的收益计数与必保席位。REGENT-V-06/REGENT-14 开启模式 0 次容量回落，峰值托管堆 2,184 → 472 MB、1,888 → 932 MB；48 根基准逐根路线、转移、展开与去重和合入前相同；默认 46/33 根协调器对照与主线逐项相同（11/30/5、−65；9/20/4、−58），`flash-boundary` 回落为 0。根状态本身超过容量仍在导入时拒绝。

内核边界（KB，2026-10-19）：79 个牌组的协调器开启模式日志中，内核边界回落只有两类，本批全部消除。Thrash 的小数卡牌变量：现有引擎把 `ModifyDamage` 的 decimal 结果同时加进 `DynamicVars.Damage.BaseValue` 与私有 `ExtraDamage`，伤害管线全程 decimal、只在 HP 损失处截断；Flash 在 `FlashBattle` 头部末尾追加 4 槽的精确 decimal 值表（卡上 `Private` 记槽位，稳定边界按分离卡牌与牌堆顺序规范化，克隆写时复制，降级保留偏移，对应 `AfterDowngraded` 的回加），伤害与 `FlashScorer` 在现有引擎相同的位置读取并转 double；`FlashBattle` 12408 → 12472 字节。BeatDown：现有引擎持有选中卡的对象引用，前一张自动打出把后续选中卡移进手牌、抽牌堆或消耗堆后从新位置打出；Flash 用一次动作内的瞬态标记 `FlashCard.TransientMark` 定位，动作结束前清零。残余显式边界：4 个以上不同小数值并存、未声明小数变量的卡收到小数偏移、累加达到 `BaseValue` 的 999,999,999 钳制、BeatDown 选中卡被移出战斗（现有原版内容不可达）。4 个 `coverage/fixtures/cards/flash-kb-*.json` 零差异，临时计数确认小数更新与 88 次非弃牌堆位置的自动打出；IRONCLAD-02/18/50/53 与 SILENT-C-20 的开启模式内核边界回落 42 → 0，On 合计耗时 76.2 s → 49.4 s，质量 1 好 4 同；48 根基准耗时与分配不变。

卡牌（铁甲战士剩余 24 张，P14）：实现 Aggression、StoneArmor、Unmovable、Rupture 与 AggressionPower、UnmovablePower、RupturePower（StoneArmor 复用 PlatingPower）；4 个 `coverage/fixtures/cards/flash-p14-*.json` 每根 48 条游走零差异。RupturePower 的 Aux0..2 只在出牌期间存放待结算力量与伤害来源标记，稳定边界为 0，投影只认领空的 `RupturePredictionState`。跳过 20 张：自动打出（Cascade、Havoc、Hellraiser、HowlFromBeyond、Stampede）、卡池随机生成（InfernalBlade、Stoke）、变化（PrimalForce）、改费用（Corruption、Unrelenting、Stomp，后者另需扩充 `CardEnergyCostModifiers` 容量）、出牌次数（OneTwoPunch）、AfterPowerAmountChanged（Vicious）、战斗全程未格挡受伤计数（TearAsunder）、跨卡计算伤害（Thrash）、多人专用（Blaze、DemonicShield、Midnight、Outrage、Tank）。

卡牌（无色卡池，P15）：新增 28 张无色卡（含 Finesse、Impatience、Production、Salvo、SecretTechnique、DarkShackles 等）与 5 个 Power（DarkShackles、Fasten、NoBlock、PrepTime、TheGambit），8 个 `coverage/fixtures/cards/flash-p15-*.json` 每根 48 条游走 K1/K2/K4/K5 零差异；MindBlast 与 Rend 的计算伤害乘数、TheGambit 的死亡预测按现有评估补进 FlashScorer/FlashScoreBridge。跳过 37 张：多人专用 12、自动打出 3（BeatDown、Catastrophe、Mayhem）、卡池随机生成/变化 7（Alchemize、Calamity、Discovery、Entropy、JackOfAllTrades、Jackpot、Splash）、随机抽样与回手历史 5、伤害/击杀历史 4、按实例区分的 Power 4（Automation、Panache、RollingBoulder、TheBomb）、尚未派发的 Hook 2（Nostalgia、Stratagem）。

角色（静默猎手，SK，2026-10-19）：内核补上弃牌管线与 `AfterCardDiscarded`（Sly 按弃置顺序自动打出）、中毒（`TriggerPoison` 与 Accelerant）、Shiv 生成与动态全体目标、下回合抽牌/能量、Blur、DoubleDamage、出牌期金额快照（Afterimage、Strangle）与多选卡牌选择的接线；实现初始牌组、RingOfTheSnake、73 张静默猎手卡牌（含 Shiv）与 21 个 Power，MementoMori、PreciseCut、Murder 的计算伤害照现有评估移植。7 个 `coverage/fixtures/cards/flash-sk-*.json` 与 NIBBITS/SLIMES/CULTISTS 的静默猎手初始牌组 FLASHM1/FLASHM2 自然根每根 48 条游走 K1/K2/K4/K5 零差异、`unsupported=0`；K0 增加弃牌/Sly/中毒/动态目标/快照/下回合资源合同，`FlashBattle` 仍为 12000 字节。跳过 15 张卡（TheHunt、KnifeTrap、EchoingSlash、Anointed、Burst、Mirage、ToolsOfTheTrade、Entropy、Nightmare、Shadowmeld、Nostalgia、Stratagem、Omnislice、PhantomBlades、RollingBoulder）与 9 个遗物（UnsettlingLamp、SneckoSkull、PaperKrane、UnceasingTop、ReptileTrinket、Vambrace、Tingsha、HelicalDart、BeltBuckle），缺失能力按项记录在 `.local` 任务报告中，主要是战斗内奖励、牌堆筛选后取样/自动打出、单次攻击多轮伤害、计算格挡、回合开始选择、实例型 Power 与 Power 给予数量修正 Hook。
角色（故障机器人，DF，2026-10-19）：充能球内核照现有引擎实现导入与投影（`FlashOrb.Aux` 承载 Dark 的累积激发值与 Glass 的递减被动值）、生成（满槽先激发最左侧）、激发（多次激发只出队一次）、回合末与回合开始被动及触发次数 Hook、Focus 修正（Dark 激发不受 Focus 影响）、球槽增减、Lightning 随机目标与 Chaos 等的随机生成，并补上 `AfterOrbChanneled`、`AfterOrbEvoked`、`AfterPotionUsed`；实现初始牌组、CrackedCore、81 张故障机器人卡牌、25 个 Power、5 种球与 6 个遗物（CrackedCore、RunicCapacitor、SymbioticVirus、GoldPlatedCables、PowerCell、ReptileTrinket），评估补上充能球保留价值并纳入 K4。8 个 `coverage/fixtures/cards/flash-df-*.json` 与 NIBBITS/SLIMES/CULTISTS 的故障机器人初始牌组 FLASHM1/FLASHM2 自然根每根 48 条游走零差异、`unsupported=0`。跳过 UnsettlingLamp（卡牌实例身份与逐卡翻倍账本）；10 张不在调查清单的故障机器人池卡未实现，含随机角色卡生成效果的根因此按 `UnsupportedClosure` 拒绝。

角色（储君，RG，2026-10-19）：内核补上星能的获得、消耗与费用修正 Hook、X 星能、`LastStarsSpent`、`AfterStarsGained`、锻造与君王之刃、AutoPrePlay（Bombardment、Mayhem 的计划自动打出）与卡牌结果去向覆盖；实现初始牌组、DivineRight、59 张储君卡牌（含君王之刃）、16 个 Power 与 8 个遗物，K4 补上下回合星能资源项、CrescentSpear 乘数与君王之刃的重放变量。`_swordSageReplayBonuses` 在没有 SwordSagePower 且全部已应用加成为 0 时按惰性处理（依据见准入文档）。4 个 `coverage/fixtures/cards/flash-rg-*.json` 与 NIBBITS/SLIMES/CULTISTS 的储君初始牌组 FLASHM1/FLASHM2 自然根每根 48 条游走零差异（`flash-rg-c-powers` 因成长目标记 `bound=disabled(growth_targets)`）。跳过 24 张卡（手牌/牌堆选择协议、战斗历史计数、卡牌实例账本与实例型 Power、生成闭包含未实现卡、未派发的卡牌 Hook、VoidForm 的费用 Hook 组合）与 3 个遗物（UnceasingTop、Vambrace、UnsettlingLamp）；30 个随机储君牌组中 2 个已能准入。

角色（亡灵契约师，NB，2026-10-19）：内核补上 Osty 的导入与投影、召唤/复活/增益/治疗、`DieForYouPower` 经 `ModifyUnblockedDamageTarget` 的代受伤害与溢出、Osty 作为攻击者与 Osty/玩家死亡、两侧回合末的批量 Doom 与 `DoomKill`、`AfterHandEmptied`（UnceasingTop）以及 Osty 本回合攻击数、本回合施加 Doom、战斗内虚无出牌数三个计数；实现初始牌组、BoundPhylactery、75 张亡灵契约师卡牌、Soul 与 22 个 Power，K4 新增 Osty 生命字段并移植 Osty 代受伤害的威胁投影、Soul 与下回合召唤的评估项。`FlashHookMask` 的 64 位在 RG 后已用满，`ModifyUnblockedDamageTarget` 放进新的 `FlashHookMaskExt`（同一反射计算，`CollectListeners` 两个重载共用一份收集顺序）。虚无出牌计数照现有引擎按 `HasKeyword` 判定，Hexed 牌也计入（第三幕骑士精英根的回归由此发现）。10 个 `coverage/fixtures/cards/flash-nb-*.json` 与 NIBBITS/SLIMES/CULTISTS 的亡灵契约师初始牌组 FLASHM1/FLASHM2 自然根零差异、`unsupported=0`。跳过 6 张卡（Fetch、Delay 现有引擎本身缺补偿；SentryMode、CallOfTheVoid 需要新的生成路径；Misery 需要克隆任意 Power 实例；Pagestorm 未在本批完成）与 2 个遗物（Vambrace 需要卡牌实例身份，BigHat 需要角色池加关键字过滤的生成）。

储君剩余内容（LR，2026-10-19）：补上手牌/抽牌堆/弃牌堆选择与回合开始选择（Tyranny、ForegoneConclusion 由结束回合动作携带）、`Duplicate` 与 `AutoPlayRepeated` 选择效果、Charge/Guards/Begone 的原位变化、本场生成卡计数（Supermassive）、按目标的本回合强化攻击命中数（BeatIntoShape，复用 `FlashEnemy` 填充字节）、VoidForm 的 Late 能量费用与星能费用及评估项；实现 19 张储君卡牌、3 张 Minion 令牌、4 个 Power 与 KingsCourage、StarPotion。一个动作可携带的选择数从 4 提到 8（DecisionsDecisions 的嵌套自动打出）；更深的链是容量边界而不是加大内联数组，因为每个 Beam 节点都存完整动作，32 个槽位在 48 根基准上多约 10% 托管分配。4 个 `coverage/fixtures/cards/flash-lr-*.json` 零差异；30 个随机储君牌组准入 2 → 9，9 个准入牌组 48 条游走零差异（REGENT-15 有游走触及 8 个已知敌人的容量边界）。BundleOfJoy、Quasar、ManifestAuthority、SpectrumShift 的候选闭包经过需要卡牌实例身份的卡，仍拒绝。

其他角色剩余内容（LS，2026-10-19）：先修正非铁甲战士随机牌组差分暴露的 6 处分歧（DEFECT-05/08/26/27/28、NECROBINDER-16），再实现静默猎手的 KnifeTrap、TheHunt、EchoingSlash、ToolsOfTheTrade、Burst、Shadowmeld、Mirage（计算格挡）与闭包所需的 Anticipate、SerpentForm、Untouchable，遗物 Tingsha、HelicalDart、BeltBuckle、PaperKrane（虚弱/易伤乘数修正）、SneckoSkull（新 Hook `ModifyPowerAmountGiven`）；亡灵契约师 SentryMode（SweepingGaze 生成）、Misery、Pagestorm、DanseMacabre、ReaperForm、Seance 与 BigHat；故障机器人 Leap、BoostAway、MeteorStrike、MomentumStrike、Quadcast 与 EssenceOfDarkness。随机牌组准入：静默猎手 9 → 20/30、亡灵契约师 12 → 13/30、故障机器人 22 → 27/30，新准入的 60 个牌组 48 条游走零差异；17 个代表牌组收进 `coverage/corpora/flash-decks/`。合并时补上一处嵌套出牌的上下文恢复：外层出牌在嵌套自动打出返回后恢复自己的出牌堆槽位，使 `CurrentPlayCard`（MomentumStrike 改自身费用、附魔尾部）仍指向本张牌。跳过 CallOfTheVoid（闭包含已跳过的 Delay/Fetch）、Delay（现有引擎缺格挡补偿）与需要卡牌实例身份的 Fetch、Nightmare、PhantomBlades、RollingBoulder、UnsettlingLamp、Vambrace。

卡牌实例身份（ID，2026-10-19）：现有引擎按卡牌对象或 Power 实例记账的状态改为随卡值移动的表示：SwordSage 的已应用重放加成与 Fetch 的“本回合打出过”写进已有的 `FlashCard.Private`，Nightmare 的选定卡完整值存进 `FlashBattle.DetachedCards`（容量 2），Monologue、Orbit、PaleBlueDot、Nightmare 每个实例一行、用 `Aux` 记各自的计数；Monologue、Vambrace 与 UnsettlingLamp 的触发卡只存在一次出牌的 `FlashExec`，稳定边界为空（布局见 [状态布局](state-layout.md) 2.5）。实现储君 Monologue、SwordSage、SummonForth、Orbit、PaleBlueDot，静默猎手 Nightmare、PhantomBlades，亡灵契约师 Fetch、Delay（CallOfTheVoid 的闭包随之可用），无色 BundleOfJoy、Quasar、ManifestAuthority、SpectrumShift，遗物 Vambrace、UnsettlingLamp；Orbit 的战略评估项按现有引擎的退款窗口单独计算。已知敌人容量 8 → 10（REGENT-15 的长游走召出第 9 个敌人），`FlashBattle` 12000 → 12320 字节，`FlashCard` 仍为 92 字节；没有身份内容的根按根级 `HasIdentityContent` 跳过身份扫描，48 根基准路线逐根不变，FlashBeam 热耗时中位数约 +0.9%（单次波动大于此值），托管分配均值 32.1 → 32.8 MB。新准入的根顺带暴露并修正 5 处既有差异：两张 THRUMMING_HATCHET 同时回手时第二张被漏掉、药水目标的 Surrounded 朝向（`BeforePotionUsed`）、SandpitPower 的类型（原生为 Buff，影响 Rend 的评估）、Monologue 撤回临时力量压缩 Power 表后删错行、监听过程中 Power 自移除造成的 occurrence 漂移。7 个 `coverage/fixtures/cards/flash-id-*.json` 每根 48 条游走零差异，临时计数确认各身份路径在游走中触发；随机牌组准入：储君 9 → 30/30、亡灵契约师 13 → 24/30（剩余 6 个因 MELANCHOLY 的局外 run-listener Hook 拒绝），新准入牌组 48 条游走零差异。

附魔与 Melancholy（CX，2026-10-19）：在 ID/RQ 之后的代码上重新调查第一、二幕随机牌组（铁甲战士 60 个、其余四个角色各 30 个），只剩亡灵契约师 6 个牌组因 MELANCHOLY 的局外监听 Hook 被拒，其余全部准入。现有引擎经局外监听派发 `AfterDeath` 时用 `State.FindCard` 查找该牌，牌组副本不在战斗牌堆中、没有效果，因此定义可声明牌组副本惰性（`DeckCopyOutsideCombatIsInert`），战斗副本的降费照旧执行。实现附魔 Imbued（第 1 回合在 Mayhem 计划自动打出之后自动打出）、Slither、SlumberingEssence（新 Hook `BeforeFlush`）与 PerfectFit（新 Hook `ModifyShuffleOrder`，洗牌之后、写回抽牌堆之前），两个 Hook 位于 `FlashHookMaskExt` 1–2 位，没有对应附魔的根按根级布尔跳过派发。新准入的根暴露一处既有差异：BoneShards 在 Osty 缺失或死亡时照 `BoneShardsOnPlay` 跳过攻击与格挡。随机牌组准入：亡灵契约师 24 → 30/30，五个角色全部准入；新准入的 6 个牌组 48 条游走零差异，NECROBINDER-06/24/26 收进 `coverage/corpora/flash-decks/`。`flash-cx-a-enchantments`、`flash-cx-b-melancholy` 零差异，临时计数确认 Slither、SlumberingEssence、PerfectFit 与降费在游走中触发；搜索根固定在出牌阶段，第 1 回合的 Imbued 自动打出在根之前已发生，只由 K0 合同覆盖。48 根基准路线逐根不变，托管分配不变。

遗物（R2）：新增 122 个遗物定义（34 个有战斗行为，88 个只在战斗外或根之前生效、逐个注明原版 Hook）与 SelfFormingClayPower；PaperPhrog 的效果由易伤倍率承载。7 个 `coverage/fixtures/cards/flash-r2-*.json` 每根 48 条游走零差异。跳过的遗物：TheAbacus（AfterShuffle 未派发）、MysticLighter（附魔伤害修正）、IceCream（能量重置）、ChemicalX（ModifyXValue）、RuinedHelmet（Power 数量修正 Hook）、GhostSeed（遗物的 AfterCardEnteredCombat）。目标 8 个药水全部跳过（随机生成与自动打出待 KG1，Duplicator 需出牌次数修改，Gigantification 需对象身份）。

内核能力（KG1，自动打出、卡池随机生成、变化）：`FlashKernel.AutoPlay`/`AutoPlayFromDrawPile` 复用打出管线，不可打出、无目标或 `ShouldPlay` 拒绝时移入结果牌堆，随机目标走 CombatTargets 流；新增 `AfterAutoPostPlayPhaseEntered` 派发点。生成池由 Bridge 按现有枚举冻结，内核按现有取样（去重或有放回）抽取，候选缺定义时按 `UnsupportedClosure` 拒绝；PrimalForce 用 `ReplaceCard` 原位替换为 GiantRock。实现 Havoc、Cascade、Hellraiser、HowlFromBeyond、Stampede、BeatDown、Catastrophe、PrimalForce、Jackpot；InfernalBlade、Stoke、Discovery、JackOfAllTrades、Splash、Calamity、Alchemize 已有定义，但生成闭包仍含未定义的卡或药水，根被拒绝；Entropy 跳过。3 个 `coverage/fixtures/cards/flash-kg1-*.json` 每根 48 条游走零差异，临时计数确认自动打出、生成与变化在游走中发生。

药水（R3）：DistilledChaos 用 `AutoPlayFromDrawPile` 实现，3 个 `coverage/fixtures/cards/flash-r3-*.json` 每根 48 条游走零差异；OrobicAcid 按现有卡池生成语义实现，但生成闭包仍缺 OneTwoPunch、Stomp、Thrash、Unrelenting、Vicious 等卡，根被拒绝。AttackPotion、SkillPotion、PowerPotion、ColorlessPotion 暂不定义：它们的 0..1 生成选择由 `PotionChoiceSupport` 结算，Flash 的回放翻译尚未覆盖。EntropicBrew（战斗外药水池与免费状态）、Duplicator、GigantificationPotion、Ambergris 与角色专属药水跳过。

内核 Hook（KG2）：新增改费用（普通与 Late 两遍）、出牌去向、出牌次数、`AfterPowerAmountChanged`（按现有引擎的延迟队列在同样的位置排空）、`AfterShuffle`、`ModifyXValue`、玩家能量重置判定、接收侧 Power 数量修正、遗物 `AfterCardEnteredCombat` 的派发，`AfterDamageReceived` 带卡牌来源。实现 Corruption、Unrelenting、Stomp、OneTwoPunch、Vicious、TearAsunder、Thrash、Duplicator、TheAbacus、ChemicalX、IceCream、RuinedHelmet、GhostSeed；RupturePower 改为直接读取伤害来源。为容纳 Stomp 逐次追加的费用修饰，单卡修饰容量 2 → 16，`FlashCard` 90 字节、`FlashBattle` 11808 字节：48 根基准路线不变，FlashBeam 搜索耗时 +3.5%，每次求解托管分配平均 16 → 27 MB，留作之后压缩卡牌布局的候选。4 个 `coverage/fixtures/cards/flash-kg2-*.json` 每根 48 条游走零差异，临时计数确认每个新派发点在游走中触发。UnsettlingLamp（卡牌实例身份）与 SneckoSkull（无 Poison 定义）跳过。

### 2.2 搜索演进摘要

完整表格与分析见 [搜索演进测试记录](../archive/flash-search/testing-search-2026-10-19.md)。

- 评估移植（S1）：`FlashScorer` 逐项移植 `CombatBeamSolver.Snapshot` 与 `BeamRankScore`，K4 对投影一致的全部状态逐字段比较，`Score` 逐位相等。
- 宽度：48 根基准（宿主默认档位）中宽度与节点 ×4 是第一个总失血优于现有搜索的档位（13 好 / 28 同 / 7 差，失血 −9），设为 `ProfileWidthScale` 默认值；状态池按需增长、子状态在池槽内原地执行后路线不变、耗时下降。
- incumbent 下界剪枝（SB）：下界输入由 K5 逐状态核对；FlashBeam 转移数 −73%，相对现有搜索 14 好 / 24 同 / 10 差、失血 −16。变差根来自 incumbent 发现顺序与更简单的保留结构，不是剪枝语义。
- 热路径与分配（PF，2026-10-19）：EventPipe 采样把多数样本归到整块复制，但逐段计时显示复制只占约 2%，真正的热点是卡区 Hook 扫描、逐状态评估与状态池分配。保留的优化：`FlashHooks.CollectListeners` 先用卡牌/苦难/附魔定义 Hook 掩码的并集判断，无交集时跳过卡区扫描；只供保留结构读取的可达手牌估值推迟到状态入池时计算（去重与终局子状态不算）；可达估值复用 `CanPlayCard` 已算出的费用；`MeasureDefensiveBlockReserve` 只读原状态；排序改用 struct 比较器、去掉每次剪枝的委托与小数组分配。8 个自然根（10 秒预算，绑定同一 CPU，A B / B A 交替 3 组）`FlashBeamSolver.Solve` 中位 348.3 → 258.6 ms（−25.8%）；48 根基准与 4 个协调器代表根逐根路线、转移数、展开数与去重数相同。试过跨求解的状态池缓存：Medium 档位单次求解的四个池峰值 116–223 MB（`ended` 层按倍增扩到 11,520 槽而峰值子状态约 7k–10k，`CopyTopTo` 入选率 0.15–0.41），64 MiB 上限下从不命中，反而使托管堆碎片从约 5 MB 增到约 82 MB，故未保留；单次求解的池内存是下一步的压缩对象。
- 状态池分块（PM，2026-10-19）：`FlashBeamSolver.Pool` 的状态数组与平行数组改为固定大小分块（状态块 64 槽约 0.79 MB，其余数组每块不小于 85 KB），块按需追加、从不复制，重复与终局子状态不占新槽，消除了倍增扩容的整块复制（OA 测得这部分占 8 个代表根整场托管分配的 24%）。9 个代表根（Medium、宽度组合、开启模式）峰值托管堆合计 4,626 → 2,999 MB（−35%），总分配 117.7 → 78.7 GB（−33%），战略结果逐根相同；机器空闲时的成对墙钟 +0.4% / +1.1%（SILENT-C-20 / C-13）；48 根基准逐根路线、展开、转移与去重数相同。跨求解的块池能在 Medium 命中但使碎片升到约 180 MB，未保留。REGENT-V-06 的 14 次容量回落来自 `ended` 层：`width × (MaxLegalActions + 1)` 是每个动作层的上界，而 `ended` 在一个回合内按动作深度累积，该根一回合 434 层、真实需求约 10.4 万个状态（峰值约 3.4 GB），因此保持显式容量边界并整次回落。直接计时（IRONCLAD-30）显示整状态复制合计约占 Flash 墙钟 9–11%，`ProjectHpAfterThreat` 的复制约 1.1%，EventPipe 采样对复制的归因偏高。

### 2.3 协调器宽度组合（2026-10-19）

分发点扩展到精炼成员，并新增内核 `BeforeAttack` Hook（活力的捕获从修正 Hook 移入，修正 Hook 不再改写状态）。全量回归 101 根 K1/K2/K4/K5 零差异。

`WARMUPS=2 COORD_EXTRA_ARGS=--use-portfolio coord.sh`（48 根，宿主默认档位，开启生产默认的宽度组合）：On 相对 Off 14 好 / 30 同 / 4 差、失血合计 −31，总耗时 27.0 s → 23.9 s。Flash 采用主搜索 144 次、精炼成员 288 次；精炼成员回落 249 次（仅基础分与带权重扰动的有界进攻成员在准入时拒绝）。

随后移植仅基础分与权重扰动两种中途排序变体（公式照搬 `CombatBeamSolver.BeamRankScore`，输入项均已由 K4 逐字段核对）。实机默认的 Medium 档位（宽度 60、120,000 节点、120 秒，开启宽度组合，24 个自然根 FLASHM1，每进程预热 1 次）：

| 配置 | On 更好 / 相同 / 更差 | 失血合计差 | Off → On 总耗时 | 精炼成员 |
| --- | --- | --- | --- | --- |
| 变体未移植 | 2 / 19 / 3 | +1 | 21.9 s → 17.6 s | 采用 96、回落 82 |
| 变体已移植 | 2 / 18 / 4 | +3 | 21.2 s → 15.0 s | 采用 144、回落 0 |

另试过把评估的威胁投影改为直接读取被评估状态、首次写入前才复制：路线与转移数不变，搜索耗时 4.32 s → 4.47 s，没有可测收益，未保留。

### 2.4 固定前缀续接分发（FC，2026-10-19）

`FrontierContinuationScheduler.Dispatch`/`DispatchOptional` 改走 `BeamSolveDispatch.SolveContinuation`，只把无药、无承诺、无侦察、无最早药水回合的干净请求交给 Flash；前缀由旧引擎按 `ApplyFixedPrefix` 同一规则回放，Flash 从前缀末尾搜索，动作计数从 `Prefix.Length` 起算，物化仍把前缀 + Flash 路线整条交给现有求解器。全量回归 `regress.sh fc-regression 32` 101 根 K1/K2/K4/K5 零差异（K0 通过），SUMMARY 见任务记录。

协调器对照（同一台机器，`WARMUPS=1 COORD_EXTRA_ARGS="--use-portfolio --profile Medium" coord.sh ... 120000 M2 FLASHM1`，24 个自然根）：

| 配置 | On 更好 / 相同 / 更差 | 失血合计差 | Off 总耗时 | On 总耗时 |
| --- | --- | --- | --- | --- |
| 改动前（fc-coord-baseline） | 2 / 18 / 4 | +3 | 22.1 s | 15.3 s |
| 固定前缀续接（fc-coord） | 3 / 17 / 4 | −1 | 23.0 s | 8.4 s |

Off 侧 24 根的 `search.solverMetrics`、`search.planActions`、`search.cachedContinuations` 与 `quality.json` 与改动前逐字段相同。相对改动前只有 `NIBBITS_NORMAL-FLASHM1` 变化：28 失血 / 9 回合 → 24 / 8。4 个变差根（`INKLETS_NORMAL`、`OVERGROWTH_CRAWLERS`、`RUBY_RAIDERS_NORMAL`、`SEAPUNK_NORMAL`）与改动前的 On 结果完全相同，属于既有主搜索/精炼成员的 Flash 保留结构差异；其中 `OVERGROWTH_CRAWLERS` 与 `SEAPUNK_NORMAL` 的续接请求被 Flash 采用，但最终选择仍与改动前 On 相同。

续接 Flash 行按用途统计（`fc-coord` 的 On 日志）：`OpeningTargetVariant` 采用 50 次（前缀 3 动作 ×10、4 动作 ×40）、`TurnBoundaryRescue` 采用 22 次（前缀 3×4、4×18），回落 0 次；`PrimaryBeam` 采用 72、`RefinementBeam` 采用 216。

Shadow 隔离（`NIBBITS_NORMAL`、`SLIMES_NORMAL`、`RUBY_RAIDERS_NORMAL`，FLASHM1，Medium，120 秒，宽度组合）：Off 与 Shadow 的 `search.solverMetrics` 全部字段、`quality.json`、`search.planActions` 完全相同；3 根合计 26 行 Shadow 对照（其中 11 行续接，`OpeningTargetVariant prefix=4`），全部 `same_route=false`（Flash 找到不同路线，Shadow 仍返回旧结果）。示例：`NIBBITS_NORMAL` 8 行、`SLIMES_NORMAL` 9 行、`RUBY_RAIDERS_NORMAL` 9 行。

### 2.5 生成牌组上的协调器对照（2026-10-19）

60 个随机生成的中期铁甲战士牌组（第一、二幕，普通/精英/首领，6–12 张角色卡、0–2 张无色卡、2–5 个遗物、0–2 瓶药水，进阶 10；`.local/tool-tasks/flash-search/deck-survey.sh`）中，KG2 合入后 15 个准入（此前 4 个）。剩余缺口以怪物为主（仪式兽、千足虫、异蛙寄生虫、知识恶魔、碗虫等），其次是 12 张无色卡、5 瓶带生成选择的药水与 MysticLighter。

对准入的牌组跑实机默认 Medium 档位、开启宽度组合的协调器 Off/On（每进程预热 1 次，`coord-deck.sh`）：14 根总耗时 92.6 s → 26.9 s，On 4 好 / 8 同 / 2 差、失血合计 −2；最重的两根 62.0 s → 12.0 s、17.2 s → 6.1 s。Flash 承担主搜索、精炼成员与 TurnBoundaryRescue、OpeningPowerRouteMember、OpeningTargetVariant、DeferredOpeningPower 四类续接。

第 15 根在 On 模式中途中止：Thrash 把 `ModifyDamage` 的小数结果加进卡牌变量，Flash 内核抛出通用参数异常并逃出分发点。修正后该路径抛 `FlashUnsupportedException`，分发点把 Flash 搜索中的 `FlashUnsupportedException`/`FlashCapacityException` 当作内核边界回落现有求解器（`status=boundary`）；该根 On 结果与 Off 相同（失血 74，32.2 s 对 29.4 s）。差分探针同样把这类情况计为 `unsupported`。

生成牌组差分（DX，2026-10-19）：对准入的 32 个生成牌组跑 48 条游走，固定根回归没有覆盖到的内容组合暴露出 10 个牌组的分歧与 2 处崩溃。逐个按现有引擎修正：`PendingPowerChanges` 嵌套排空（Vicious 抽到 Strike 后 Hellraiser 自动打出，外层用过期计数越界，改为先整批取走再派发，对应 `DrainPowerAmountChanges`）；自动打出攻击中的敌人死亡要在后续自动打出选目标前结算；玩家死亡后 DarkEmbrace、Juggling、Hellraiser、Rupture、CardPlayPair 的退役预测状态只在玩家已死亡时按惰性处理；Discovery/Splash 不再置风险位（现有引擎把它们登记为已补偿）；Pillage 读取刚抽到的那张牌而不是手牌末尾；Razor Tooth 的升级照 `Upgrade` 在战斗结束中不生效；自动打出队列在战斗结束时照现有引擎只写消耗标志；Thunderclap 与注入死亡生成的结算顺序；SecondWind 与 BurningSticks 的消耗核对。牌组收进 `coverage/corpora/flash-decks/`（33 份 spec 与 request），回归脚本对其逐个跑差分。修复后全量回归 238 行零差异，只有含 Thrash 的两个牌组按小数卡牌变量边界记 `unsupported`。

牌组语料上的协调器对照（33 个牌组，Medium 档位、宽度组合、每进程预热 1 次）：Off 总耗时 273.0 s；On 在 DX 修正后为 195.7 s，其中一根因单个分支超出 96 张卡牌容量而整次回落、反比 Off 慢（54.2 s → 77.6 s）。子状态超出容量改为只丢弃该子状态后，On 为 128.1 s（约 2.1 倍），该根 10.7 s。质量按协调器自己的口径（胜负、战略失血差、结束回合）比较：4 好 / 20 同 / 9 差，战略失血差合计 +11（每场约 +0.33）；按原始失血比较会误判，例如 IRONCLAD-15 的 On 路线多失 8 HP，但战斗内回复与燃烧之血使两边战略失血差都是 0。剩余差距来自 FlashBeam 比现有搜索简单的保留结构。

保留结构移植（RL）：把现有搜索在对照路线中丢失点的必保机制按出现次数移植进 FlashBeam：每药水分组与全局的防守、进攻、准备（`SetupLaneValue` 与 Scaling+Resource+Control 组合）、牌堆整理、压缩、力量压制与虚弱控制领先者，以及根动作谱系代表（`clamp(width/8, 4, 16)` 组），按 `RankBest` 的必保置换写回，席位总数不变；比较器逐条对照 `BeamRetentionPolicy.Ranking.cs`，只读已由 K4 核对的评估字段（新增 `HandCount` 并纳入 K4）。33 个牌组：质量 6 好 / 22 同 / 5 差、战略失血差合计 −5（优于 Off），On 总耗时 126.6 s → 129.8 s；48 根初始牌组基准对现有搜索 18 好 / 24 同 / 6 差、失血 −46。未移植：选项路由配额、药水配额与谱系、stand-pat 探测、Pareto、力量承诺、回合边界手牌领先者等（仍变差的 IRONCLAD-17 需要力量承诺或 stand-pat 资源探测）。

四角色路线质量（RQ，2026-10-19）：在其他四个角色的 46 个准入牌组上（每个角色最多 12 个，取自 `coverage/corpora/flash-decks/` 的 17 个与随机调查），RL 之后的 On 为 7 好 / 22 同 / 17 差、战略失血差合计 +28。按 RL 的方法定位现有搜索选中路线在 FlashBeam 中首次被丢弃的层：11 根是普通排名席位（4 倍宽度的候选池里被更高分状态挤出），1 根命中战术铺垫与回合边界手牌，3 根在 DropletOfPrecognition 一步无法翻译（当时 Flash 把这瓶药水当作无效果，而现有搜索经 `PotionChoiceSupport` 枚举抽牌堆选牌；SP 批次修正，见 2.6）。随后补齐 `RankBest` 的其余必保代表：战术铺垫、回合边界手牌、焦点目标分组、持续铺垫特征分组、八条泳道与目标压力；`SetupLaneValue` 改读 `StrategicEffects.RetentionValue`（此前误用含充能球与 Soul 的 `PersistentBuffValue`），资源道与控制道补齐星能、可达手牌价值与流沙；新增 6 个 K4 逐字段特征。Flash 特有的 25% 进攻席位只在软预算不超过 30 秒的请求里保留。同机背靠背（Medium、宽度组合、每进程预热 1 次，Off 用同一基线）：46 个牌组 On 156.3 s → 194.6 s（Off 470.4 s），质量 10 好 / 29 同 / 7 差、−16；33 个铁甲战士牌组 On 152.4 s → 185.6 s（Off 273.0 s），7 好 / 23 同 / 3 差、−40（IRONCLAD-17 失血 57 → 18）；48 根初始牌组基准逐根不变。仍变差的 7 根是 NECROBINDER-C-02/C-08/V-13、REGENT-V-03/V-27、SILENT-C-14/V-07：其中 2 根停在 DropletOfPrecognition 的翻译缺口（SP 批次修正后变好），其余是普通排名席位与发现顺序差异。On 耗时增加约 22–25%，来自每个状态新增的可达手牌估值与更多必保代表，留给后续性能批次。

当前总览（SP 合入后，`7dc8a7c9`，机器空闲，Medium、宽度组合、每进程预热 1 次，Off 用同一基线；`rq-measure.sh final`）：四角色 46 个牌组 On 143.9 s 对 Off 470.4 s（约 3.3 倍），12 好 / 29 同 / 5 差、战略失血差合计 −65；铁甲战士 33 个牌组 On 114.8 s 对 Off 273.0 s（约 2.4 倍），9 好 / 20 同 / 4 差、−58。相对 RQ 合入时（194.6 s / 185.6 s），PF、PM、KB、SP 之后两套牌组的 On 耗时分别降 26% 与 38%；铁甲战士新增被采用的 IRONCLAD-02（54→52）、IRONCLAD-50（73→56）变好，IRONCLAD-53 战略失血差 65→66。剩余回落只有两处：REGENT-V-06 的 `ended` 层容量边界（PM 结论），IRONCLAD-27 的直接审计（现有求解器同样无路线）。

### 2.6 Smart 药水审计分发（PT，2026-10-19）

`SmartPotionGradient`（直接审计）与 `SmartOpeningPotionPosterior`（固定前缀续接）在 On 模式下由 FlashBeam 搜索，路线仍由现有引擎回放核对并物化。移植 `RequireAtLeastOne`、精确最少/最多用药次数、累计显式用药上限、药水前缀、`PotionFreePolicyBaseline`、`primaryIncumbent`、Smart 资格 HP 上限剪枝与 `FinalPlanOrdering` 的过滤和排序键；物化后核对显式用药次数与策略，不满足、Flash 无满足路线或内核边界时回落现有求解器，不代替它抛 `PotionPolicyUnsatisfiedException`。K4 新增 `ExplicitPotionUseCount`，K5 新增 Smart 上限使用的剩余回复潜力与战略 HP 下界。

33 个生成牌组（Medium、宽度组合、每进程预热 1 次，同机背靠背）：On 总耗时 140.9 s → 131.4 s，两类审计的旧求解器时间 40.4 s → 18.8 s（`SmartOpeningPotionPosterior` 26.3 → 12.7 s，`SmartPotionGradient` 14.1 → 6.1 s）；战略失血差合计保持 −5，IRONCLAD-37 结束回合 7 → 6，无新增变差。Off/Shadow 在 IRONCLAD-17/46/49 上与改动前逐字段相同（时间、内存、GC 除外）。48 根初始牌组基准逐根质量相同。剩余审计时间来自 Thrash 小数变量与 BeatDown 弃牌堆边界及 IRONCLAD-27 的无满足路线回落。`PlanCommitment` 未接管：缺计划视野延长、CopyPower 计划代表席、AggressivePowerCommitment 与跨回合探测等保留结构。

Smart 药水无路线回落（SP，2026-10-19）：79 个牌组的开启模式日志中 `flash-potion-policy-no-route` 共 14 个回落点（按单遍计）。两处原因都不是保留丢失：(1) 固定前缀续接的前缀以用药开头时，导入按搜索根的药水槽判断“生成的药水”，前缀里已消耗的那瓶被同时计入冻结的前缀计数与 `GeneratedPotionUseCount`，精确次数过滤因此滤掉全部候选；改为按搜索根的累计 `PotionUseCount` 跳过前缀已消费的记录，验证回放用同一口径。(2) `DropletOfPrecognition` 在 Flash 中是空效果，而现有引擎经 `PotionChoiceSupport` 的 `MoveToHand`/抽牌堆选择把选中牌移到手牌底部；照此实现，物化时 `TryBuildPotionChoice` 对全部需要选择的药水构造 `PlanAction.Choice`。回落 14 → 1，剩下的 IRONCLAD-27 直接审计中现有求解器同样 `route_missing=true`，保留保守回落。46 个四角色牌组 On 10 好 / 29 同 / 7 差、−16 → 12 好 / 29 同 / 5 差、−65（只有 NECROBINDER-V-13 65→43、SILENT-V-07 63→46、NECROBINDER-V-25 48→38 三根变化，全部变好）；33 个铁甲战士牌组逐根不变；5 个相关牌组 On 合计 75.2 s → 47.2 s；48 根基准逐根相同。药水使用配额与药水谱系代表仍未移植（本批回落未命中）。

药水约束的续接与审计（PA，2026-10-20）：开启模式下原搜索的每次执行都写一行 `FLASH_LEGACY purpose=<用途> reason=<原因码>`（关闭与对照模式不写），作为“开启模式不调用原搜索”的统计依据。`BeamSolveDispatch.CanTakeOver` 放开药水上下界、无药基线、最早用药回合、`RequireAtLeastOne`、Smart 无 `Disabled` 覆盖与强制用药指令：扩展阶段按累计显式用药数与最早回合限制药水动作，终局照 `FinalPlanOrdering` 移植强制指令的槽位与身份核对、最少/最多次数、Smart/RequireAtLeastOne 资格、死亡保护、Ambergris 与偷窃恢复，每档用药次数分别保留满足与不满足强制指令的代表，物化后由 `FlashPotionBridge.VerifyRoute` 用现有 `EvaluateForcedUses` 复核。Smart 梯度与开局后验去掉 8 个可移植的边界，保留 `potion-policy` 与 `no-searchable-potion` 两个“审计不适用”的语义边界。协调器的无药审计、必需用药审计、无药基线、窄宽度开局搜索与强制用药开局救援的基础分变体改经分发点。根不在普通出牌阶段（回合开始选择）时显式拒绝。仍保留原搜索并记原因码的：PlanCommitment 与早期侦察（PC 及后续批次）、`SecondRankBand` 与 `AggressivePowerCommitment` 两种保路开关、强制救援的另两个变体、新颖度组合与早期回合探索、回合开始选择的根。默认 79 个牌组开启模式结果逐根不变（剩余 `FLASH_LEGACY` 39 次：PlanCommitment 30、容量边界 7、现有求解器同样无路线的审计 2）；药水设置矩阵 1 好 / 14 同 / 1 同损更晚；48 根基准两种模式路线逐根相同。

计划搜索（PC，2026-10-20）：PlanCommitment 本身不强制或禁止动作，只影响三处：跨回合无进展剪枝时延长视野（`PlanHorizonPolicy.ShouldExtend`；FlashBeam 没有这条剪枝，无需移植）、`CopyPower` 计划的保留代表席（按计划收益进度取前 3 组，每组一个代表，只替换进度更低的非必保席位），以及计划收益计数（`CardPlayed` 证据逐转移增量计数，物化后按整条路线重数核对）。分发点接管无药或 deferred power plan 的 Smart 形态计划成员，以及计划阶段自己的深度 1 侦察（Flash 收集侦察层前沿后翻译回现有观察者的候选）；带药水上下界的计划与力量承诺泳道仍保留原搜索并记原因码。默认 79 个牌组中 PlanCommitment 的原搜索调用 30 → 0，结果逐根不变；与 PA 合并后两套牌组仍为 11 好 / 30 同 / 5 差、−65 与 9 好 / 20 同 / 4 差、−58，开启模式剩余的 `FLASH_LEGACY` 只有 REGENT-V-06 的容量边界与 IRONCLAD-27 现有求解器同样无路线的审计。

### 2.7 无人测试实机部署（2026-10-19）

Linux 无头游戏（Medium 档位、宽度组合、`Instant / 0 秒`，铁甲战士初始牌组，`NIBBITS_NORMAL` 原生满血）：On 与 Off 均 Passed、第 9 回合胜利、计划外重算 0；On 最终 59 HP，Off 54 HP，整场耗时 7.1 s 与 6.9 s（含建局）。On 的战后问题包有 6 行 `FLASH_ADOPTED`（全部 `status=verified`，单次 180–467 ms），`diagnostics/settings.json` 与 15 份检查点政策都记录 `"flashSearchMode": "On"`。`CHECKPOINT-PROFILE-CONTRACT` 增加 Flash 模式的记录值优先与旧包回退检查，Passed。该机器首次运行游戏前没有交互档案，用一次 `--force-steam=off` 无头启动在仓库 `.local/headless-seed/` 生成种子档案，并以 `XDG_DATA_HOME` 指向它运行脚本。

其他四个角色（TW 合入后，同一设置，`NIBBITS_NORMAL` 原生满血，同一无头实例依次运行，最后一项带 `--cleanup-instance-on-exit` 并由启动器删除实例目录）：静默猎手、故障机器人、储君、亡灵契约师的初始牌组 On 全部 Passed，分别在第 9、8、9、10 回合胜利，计划外重算 0。静默猎手、故障机器人、储君的战后问题包各有 6、6、7 行 `FLASH_ADOPTED`，全部 `status=verified`，没有回落；亡灵契约师是最后一项，问题包随实例目录一起清理，只保留无人测试结果。

第三幕首领（A3B 与 K4C 合入后，铁甲战士初始牌组，`--act-index-for-test 2` 使首领策略开启）：QUEEN_BOSS 与 AEONGLASS_BOSS 在首领生命 150、玩家生命 999 下 On 胜利（第 26、15 回合），TEST_SUBJECT_BOSS 会分阶段以原生命复活，按 `--expected-player-death` 在原生命下运行至玩家第 5 回合死亡；三场均 Passed、计划外重算 0。问题包各有 6、4、8 行 `FLASH_ADOPTED`，全部 `status=verified`，没有回落，`diagnostics` 记录 `"act3BossStrategy": true`。最后一项为带 `--cleanup-instance-on-exit` 的短战斗，启动器删除了实例目录。

## 3. 已知限制与未验证项

- 只覆盖归档批次与 2.1 所列内容；其他内容在准入阶段拒绝，尚未有差分证据。
- 仍在战斗中的生物身上已移除 Power 的残留条目不能投影：根导入时拒绝，路线核对中遇到时按核对失败丢弃 Flash 路线；敌方离场后的残留按惰性处理（见内容批次归档卷 2.8.1）。
- 选择覆盖卡牌自身的单选、手牌/抽牌堆/弃牌堆选择、回合开始选择、知识恶魔的固定选项与自动打出中的嵌套选择（LR、MB 固定根）；一个动作最多携带 8 个选择，更深的链按容量边界拒绝。
- 结构门禁的 Bash 与 PowerShell 版本都在 Linux 上运行通过（PowerShell 7.6.7）；未在 Windows 上运行。
- 实机部署只在 Linux 无头游戏中验证过 2.7 的五个角色初始牌组对 `NIBBITS_NORMAL` 与铁甲战士对三个第三幕首领；未做可见游戏、Windows 或 Steam 会话验证，设置面板的新选项未在游戏中查看。
