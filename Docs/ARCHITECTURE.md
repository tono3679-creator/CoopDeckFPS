# ARCHITECTURE — 実装アーキテクチャ

Phase 2（敵 AI・仮モーション）、Phase 3（デッキ）、Phase 4（コンボ・NPC 仲間）時点で**実際に存在するコード**の構成と、その後のフェーズが接続する場所をまとめる。
仕様の背景は `PROJECT_OVERVIEW.md`、カードは `CARD_SYSTEM.md`、通信は `MULTIPLAYER_PLAN.md` を参照。

---

## 1. 基本方針

| 方針 | 具体策 |
|---|---|
| 入力とシミュレーションの分離 | 入力は `CharacterCommands` 構造体に変換するだけ。キャラクターは `ICharacterCommandSource` からコマンドを受け取って動く。人間・NPC・自動操縦・ネットワークが同じ口を使う |
| ロジックを純 C# に | 移動の数式（`MotorMath`）、武器の状態（`WeaponState`）、HP（`HealthModel`）、ダメージ規則（`DamageRules`）は MonoBehaviour を持たず、EditMode テストで検証する。将来ホスト側で同じコードを実行できる |
| 演出とダメージの分離 | 武器は `Fired` イベントを出すだけ。見た目は `WeaponFx` → `FxPool` が担当。ダメージ結果は `CombatEvents` で通知し、HUD やコンボ判定が購読する |
| データ駆動 | 武器（`WeaponDefinition`）・移動（`MovementProfile`）は ScriptableObject。新しい武器・キャラの調整はアセット追加で済む |
| 依存方向の固定 | asmdef で依存を一方向に限定（下図）。下位アセンブリは上位を参照できない |
| 生成物の再現性 | シーン・Prefab・マテリアル・エフェクト・画質設定はエディタースクリプト（`ProjectBootstrap`）で再生成できる |

---

## 2. アセンブリ構成と依存関係

```
CoopDeck.Core      （依存なし）  CharacterCommands（指示ボタン含む）, ICharacterCommandSource, IAimProvider, ILocomotionSource,
                                   ControllerKind/IControllerIdentity, InputGate, Element, TeamId, GameLayers, PerformanceSettings/Bootstrap
   ▲
   ├── CoopDeck.Animation  AnimatorParams, LocomotionMath（純関数）, CharacterAnimatorDriver
   ├── CoopDeck.Combat   DamageInfo/Result, IDamageable, ICombatActor, Health(+HealthModel),
   │                     HitZone, DamageRules, CombatEvents, CombatantRegistry（戦闘者の一覧・最寄りの敵）,
   │                     CombatStatus(+StatusEffectsModel: シールド/攻撃強化/被ダメ増/継続ダメ), ForceReceiver(吸引)
   ├── CoopDeck.Cards    CardDefinition, SkillEffectSpec, DeckDefinition, ElementStyle（データのみ）
   │     └── CoopDeck.Deck   CardRuntime, DeckState, IRandom/SeededRandom, CardCollection, DeckLoadout（純 C#）
   │           └── CoopDeck.Items  GaugeItemDefinition, GaugeItemInventory（純 C#）
   ├── CoopDeck.Input    GameInputActions（既定バインド生成）, PlayerInputReader   ← Unity.InputSystem
   ├── CoopDeck.Effects  FxPool, PooledFx, TracerFx, LightFlash
   │
   ├── CoopDeck.Weapons  WeaponDefinition, WeaponState, HitscanWeapon, WeaponFx, ChargeBurst（連続命中の能力：Chain＝VOLT-9 帯電連射、
   │                     Splash＝IGNIS-27 火種刻印、既存の継続ダメージを付与）  ← Combat, Effects
   ├── CoopDeck.Enemies  TargetDummy, PatrolMover, HitFlash, DamageZone,
   │                     EnemyDefinition, EnemyActor, EnemyProjectile,
   │                     WaveSchedule（純 C#）, WaveDirector                       ← Combat, Effects
   │     └── CoopDeck.AI  EnemyStateMachine（純 C#）, EnemyBrain（NavMeshAgent）    ← Combat, Effects, Enemies, Animation
   ├── CoopDeck.Player   MovementProfile, MotorMath, FpsMotor, FpsLook,
   │                     FpsCharacter, ViewModelAnimator, PlayerRespawn           ← Combat, Weapons, Animation
   ├── CoopDeck.Skills   SkillCaster（＝共通の SkillExecutor：効果の実行・属性ヒットの発行）, DeckController（キャラ⇔デッキ⇔アイテム、練習デッキ）,
   │                     DeckLoadoutController（非戦闘中のデッキ編集）
   │                                                                              ← Combat, Effects, Cards, Deck, Items, Player
   │     └── CoopDeck.Characters  CharacterDefinition（SO：id・表示名・属性・キット）, CharacterKit（体・持つ武器・一人称の腕と武器）,
   │                              CharacterSelection（起動引数 -character <id> / Editor の Play As）, PlayerCharacterSwap（開始時に
   │                              PF_Player の体と一人称部分をキットに差し替える。ゲームプレイの部品は触らない）
   │                              ElectricBodyFx（雷ウサギの発光の強弱・火花・残像・耳の放電。規則は純 C# の ElectricPulse）,
   │                              ICharacterKitListener（キットがプレイヤーに入ったことの通知）
   │                                                                              ← Core, Animation, Effects, Weapons, Player, Skills
   ├── CoopDeck.Combos   ComboDefinition（SO、協力 VFX・音つき）, ComboRule/ComboResolver/ComboDamageBudget/ComboPlanner（純 C#）,
   │                     ComboSystem, ComboEvents, ComboAudio, ElementAuraPresenter（属性の見た目）  ← Core, Combat, Effects
   ├── CoopDeck.NPC      NpcController（AI コントローラー）, NpcDecisionMaker（Utility AI）, NpcStateMachine, NpcSituation,
   │                     NpcSteering, NpcNavigator, NpcCardTraits, NpcProfile（SO）  ← Core, Combat, Cards, Deck, Skills, Player, Weapons, Combos
   └── CoopDeck.UI       HudController, DeckHud, HudKit, WorldHealthBar, FrameTimeMonitor, NpcHud, NpcDeckEditorHud,
                         DamageFeedbackHud（被弾方向・ダウン表示）, ArenaHud（ウェーブ表示）, ComboHud（コンボ名・F4/F6/F7）,
                         Diagnostics/AutoPerfRunner, AutopilotCommandSource       ← Combat, Weapons, Player, Cards, Deck, Items, Skills, Enemies, Combos, NPC, Characters, InputSystem, uGUI
   （Combat に ReviveModel/Revivable・TeamEngagementMonitor・ITrainingTarget・ThreatWarning、Player に AllyCollisionFilter を追加）
   （Combat に ElementAuraModel、Effects に CameraShaker・LightningArcFx・SpiralTrailFx・RadiusScaledFx・VfxQualityGate を追加）
   （スタイライズ雷：Effects に LightningBoltFx（太いボルト＋枝）・SurfaceArcFx（残留アーク）・LightningPath（形と品質ごとの量、純関数）、
    Shader Graph SG_FX_LightningBolt / SG_FX_LightningFlash（Art/VFX/Lightning、LightningVfxFactory が生成）。LIGHTNING_VFX.md）
   （Phase 5 火球：Core に Palette（アートバイブルの色）、Cards に ProjectileSpec、Effects に IRadiusFx・ISurfaceFx・IProjectileFx・
    FireballProjectileFx・FireballExplosionFx を追加。飛翔弾は SkillCaster が判定し、見た目は IProjectileFx が追うだけ。FIREBALL_VFX.md）
   （射撃・スキルの演出 2026-11-09：Effects に IBeamFx（弾道・ビームの共通の口）・ShotTracerFx・ShapeAnimFx・FallingStrikeFx・ThrownBeamFx・
    LightningBeamFx・ImpactShakeFx・SpinFx・RepeatArcsFx、Skills に StatusAuraFx（状態が続く間だけ表示）。生成は Editor/Vfx の VfxKit・
    WeaponVfxFactory・SkillVfxFactory、シェーダーは Art/VFX/Skills の ElementVfx.hlsl。WEAPON_SKILL_VFX.md）
   （同日・第 2 弾：Effects に GroundedFx（地面に立つ作りの印）。SkillCaster は範囲エフェクトと予告円の表示位置だけを、狙った点の真下の
    地面へ下ろす（キャラクターは飛ばす。ダメージの中心は狙った点のまま）。品質「中」でもエフェクトのライトを点ける。WEAPON_SKILL_VFX.md §8）
   （雷ウサギの SMG：Weapons に ChargeBurstSpec・ChargeBurstModel（帯電連射）、Combat の DamageFlags に WeaponProc、Core の IAimProvider に
    SetRecoilRecovery（既定は何もしない）、Characters の CharacterDefinition に startingWeapon（PlayerCharacterSwap が適用）。THUNDERHOP_SMG.md）

   └── CoopDeck.Networking  （Phase 2.5、Netcode for GameObjects 2.13.3）NetworkPlayer（所有者は単独プレイと同じ経路で動かし、
                            他の端末では入力・シミュレーション・カメラを止めて体を見せる）, RemoteCommandSource（ControllerKind.Remote）,
                            RemoteMotion（受け取った位置からの速度、純 C#）, NetPose（ピッチ・接地・スプリント）, NetLaunchOptions（起動引数、純 C#）,
                            NetSmokeSession / NetSmokeWalker / NetSmokeRecorder（Net_Smoke シーンと無人テスト）
                                                                              ← Core, Animation, Combat, Player, Input, Unity.Netcode.Runtime

CoopDeck.Editor           ProjectBootstrap ほか生成ツール（カード・アイコン・スキル VFX・仮リグ・アニメ・敵 Prefab・NavMesh・コンボと VFX 含む）
                          ← 全ランタイム + URP + Unity.AI.Navigation + Unity.Animation.Rigging
CoopDeck.Tests.EditMode   197 テスト（Phase 4 NPC：判断 18・状態機械/操舵/カード特性 8・ロードアウト 3・蘇生/交戦 2・コンボ予測 2・生成資産 7 を含む）（純ロジック・デッキ規則・アイテム・状態効果・属性・コンボ判定・敵の状態遷移・ウェーブ・
                          移動ブレンド・弱点・生成データ（カード／Animator／敵 Prefab／シーン／コンボと VFX）・入力定義・ティア・VFX 品質）
CoopDeck.Tests.PlayMode   53 テスト（NPC 16：追従・射撃・スキル/ゲージ/ドロー・デッキ独立・協力コンボ両方向・×1.3・連鎖防止・ダウン/蘇生/救助・FPS とデッキ循環・指示・回避・入力元の差し替え・編集ロックを含む）（移動・射撃・デッキ×FPS 統合・仮想キーボード/ゲームパッド・各スキル効果・敵の追跡と攻撃・
                          回避・敵弾・ウェーブ進行・NavMesh 上の吸引・入力遅延・コンボ 3 種・連鎖防止・協力・デッキ循環×コンボ）
```

- **`CoopDeck.Player` は `CoopDeck.Input` を参照しない。** 入力は `ICharacterCommandSource` 経由でのみ届く。
- `CoopDeck.Player` と `CoopDeck.AI` はお互いを参照しない。敵はプレイヤーを `CombatantRegistry`（`ICombatActor`）経由でだけ知る。
- `CoopDeck.Skills` と `CoopDeck.Combos` もお互いを参照しない。スキルは `CombatEvents.ElementHitApplied` を出すだけで、コンボの存在を知らない。
- `CoopDeck.NPC` は `CoopDeck.AI`（敵）・`CoopDeck.Input`・`CoopDeck.UI` を参照しない。敵の攻撃は `CombatEvents.ThreatRaised`、敵の位置は `CombatantRegistry` からだけ知る。
- `CoopDeck.Networking` は他のゲームプレイのアセンブリから参照されない（Editor とテストだけが参照する。EditMode テストで確認）。Netcode に依存するのもこのアセンブリだけ。
- 予定アセンブリ（フォルダのみ作成済み）：`Save`。

---

## 3. 1フレームの流れ（ローカルプレイヤー）

```
PlayerInputReader.GetCommands(dt)          入力デバイス → CharacterCommands
        │   （NPC なら NpcBrain、計測なら AutopilotCommandSource、通信ならネットワークプロキシ）
        ▼
FpsCharacter.Step(dt)
  1. FpsLook.ApplyLook(LookDelta)          視点（yaw=体、pitch=カメラピボット）
  2. FpsMotor.Simulate(Move, Sprint, Jump, Dash)   CharacterController で移動
  3. FpsLook.Tick                          リコイル回復、スプリント時 FOV
  4. HitscanWeapon.SetTrigger / Tick       発射可否→レイキャスト→IDamageable.ApplyDamage
        │                                  └→ IAimProvider.AddRecoil（視点を蹴り上げる）
        ├─ Fired(ShotEvent) ───→ WeaponFx → FxPool（マズル・弾道・着弾）
        │                     └→ ViewModelAnimator（銃のキック）
        └─ Health.ApplyDamage → CombatEvents.DamageApplied ─→ HudController（ヒットマーカー・ダメージ数値）
                                                           └→ （Phase 4）コンボ判定
  5. CardSelectRequested / CardSelectStepRequested → DeckController.SelectedSlot
  6. SkillSlotRequested(slot) → DeckController.UseSlot
        ├─ DeckState.TryUseCard（ゲージ消費・CD開始・0 なら捨て札→ドロー／再シャッフル）
        └─ SkillCaster.CastCard（照準レイ／自分中心 → 効果を順に適用 → VFX を FxPool で再生）
              飛翔弾カード（火球）：左手でチャージ → launchDelay 後に照準の線上へ発射 → 毎フレーム SphereCast → 命中点で同じ効果の適用
              └─ 届いた敵ごとに CombatEvents.ElementHitApplied（属性・詠唱者 ID・チーム・カード強化段階）
                    └─ ComboSystem → ComboResolver（CombatStatus.Elements に付与 or 相方を消費してコンボ）
                          └─ コンボのダメージ（ComboDamage）→ ComboEvents.Triggered/Finished → ComboHud・VFX・画面揺れ
  7. ItemUseRequested / ItemCycleRequested → GaugeItemInventory → DeckState.RestoreCharge
```

コンボは手札・ゲージに触れない（`DeckState` を参照しない）。コンボのダメージは属性ヒットを出さないので連鎖しない。詳細は `CARD_SYSTEM.md` §8「実装」。

射撃（4）とスキル（6）は同じフレーム内の別処理で、互いの状態（弾数・ゲージ）に触れない。攻撃力強化だけは `ICombatActor.Status` 経由で両方のダメージに乗る。

`FpsCharacter.Step` は public なので、将来の固定ティック（ネットワーク）やテストからも同じ処理を呼べる。

### 3.2 1フレームの流れ（NPC 仲間）

```
FpsCharacter.Step(dt)  ← プレイヤーと同じ（移動・視点・射撃・スキル枠・デッキ）
   └ commandSource.GetCommands(dt) = NpcController（AI コントローラー）
        1. 0.2 秒ごと（NPC ごとに位相をずらす）に Think：
             Sense   → NpcSituation（自分・プレイヤー・敵 16 体まで・手札・攻撃予告）。視線の Linecast は近い 8 体だけ
             Decide  → NpcDecisionMaker（Utility AI、純 C#）：回避 900 > 救援 800 > コンボ追撃 700 > 起点 600 > 支援 500
                        > 範囲 400 > 単体 300 > 射撃 200 > 追従 100。射程・射線・CD・ゲージ・手札・方針・温存時間で点数が変わる
        2. 毎フレーム：攻撃予告（ThreatWarning）を反応時間 0.15 秒後に評価して回避
        3. NpcStateMachine（追従／交戦／合流／救助／回避／ダウン）でモードを決める
        4. モードごとの移動先 → NpcNavigator（NavMesh.CalculatePath、0.5 秒ごと・全体で 1 フレーム 2 経路まで）
           プレイヤーの射線上に 0.25 秒いたら横へ退く、近接敵からは距離を取る
        5. 照準（LookDelta）→ スキルは照準レイが対象に当たったらスキル枠を押す、射撃はバースト（プレイヤーが射線上なら撃たない）
        → CharacterCommands（人間と同じ形）
DeckController / SkillCaster / ComboSystem は人間と同じ経路で動く（ElementHit の付与者 ID が NPC になるだけ）
```

### 3.1 1フレームの流れ（敵）

```
WaveDirector.Update                       プレイヤーがアリーナ内 → WaveSchedule.Start / Tick
        └─ 出現する種類 → Instantiate（出現点を順番に使用、AlertOnSpawn = true）
EnemyBrain.Update(dt)
  1. CombatantRegistry.FindNearestHostile   最寄りの生存している敵対者（プレイヤー／将来の NPC）
  2. Physics.Linecast（SightBlockingMask）  視線。キャラクターは遮らず、壁・遮蔽物だけが遮る
  3. EnemyStateMachine.Tick(知覚, 調整値)   Idle → Chase → Windup（予兆）→ 攻撃 → Recover → Chase
        │                                   被弾・出現直後は視線がなくても追跡を開始
  4. NavMeshAgent.SetDestination（0.25 s ごと）、予兆中は停止して対象へ向き直る
  5. Windup 開始：FX_Enemy_Windup を FxPool で再生 + Attack トリガー（WindupStarted イベント）
  6. 攻撃発生：近接＝距離（射程 +0.6 m）と正面 70° を判定して Health.ApplyDamage
             遠距離＝EnemyProjectile を FxPool から発射（毎フレーム SphereCast、EnemyShotMask）
             （AttackReleased イベント）
CharacterAnimatorDriver.Update              ILocomotionSource（NavMeshAgent／FpsMotor の速度）→ MoveX/MoveY/Speed
Health.Died → EnemyActor                    コライダー無効化・撃破 FX・Defeated → WaveDirector → WaveSchedule.NotifyDefeated
```

予兆（0.55〜0.7 s）の間にダッシュ・横移動すれば、近接は射程外・正面外で外れ、弾は発射時点の位置を狙うので当たらない（いずれも PlayMode テストで確認）。

---

## 4. 主要クラス

### 4.1 移動（`CoopDeck.Player`）

| クラス | 役割 |
|---|---|
| `MovementProfile` (SO) | 歩行 6.5 / 走行 9.5 m/s、地上加速 65・減速 50 m/s²、反転時 1.6 倍、空中加速 20、ジャンプ高 1.35 m、重力 26（落下時 1.25 倍）、コヨーテ 0.12 s、先行入力 0.12 s、ダッシュ 19 m/s×0.16 s・2 チャージ（2.2 s で1回復）、接地スナップ 0.35 m |
| `MotorMath` | 地上（一定加速で目標速度へ）、空中（入力方向へ舵取り、速度は「目標速度と現在速度の大きい方」を超えない）、ジャンプ初速、重力の純関数 |
| `FpsMotor` | CharacterController 版の移動。段差（stepOffset 0.4）、坂（slopeLimit 50°）、下り坂・段差下りの接地スナップ、壁接触時は壁に向かう速度成分だけを除去（段差の側面は除外）して引っかかりを防ぐ。天井で上昇速度を打ち消す |
| `FpsLook` | 視点。リコイルは 30% が照準の上昇として残り、70% はばね状に自動復帰。スプリント時 FOV +5°。カメラ自体は揺らさない |
| `FpsCharacter` | 合成ルート兼 `ICombatActor`。射撃中はスプリント解除、スプリント中は射撃不可 |
| `ViewModelAnimator` | 一人称武器の仮アニメ（ボブ、スウェイ、射撃キック、リロード、スプリント、着地） |

### 4.2 射撃（`CoopDeck.Weapons`）

| クラス | 役割 |
|---|---|
| `WeaponDefinition` (SO) | 威力・属性・ヘッドショット倍率・射程・距離減衰（開始・**終点**〔0＝射程〕・最小倍率）・連射・弾倉・リロード・拡散・リコイル（**戻り秒数**〔0＝視点の既定〕）・**固有能力 `ChargeBurstSpec`**（既定は無効）・エフェクト Prefab 参照。プロトAR：18、2 倍、600 RPM、30 発、1.7 s、40→150 m で 0.6 倍。サンダーホップ（雷ウサギ）：`THUNDERHOP_SMG.md` |
| `WeaponState` | 弾数・連射間隔・リロード・ブルーム。待ち時間の端数は「次弾待ちのときだけ」持ち越し、どのフレームレートでも連射速度が正確。リロード中やトリガーを離している間は貯まらない（初弾は必ず1発） |
| `HitscanWeapon` | 照準レイ（`IAimProvider`）に拡散をかけてレイキャスト → `IDamageable`。拡散乱数は `System.Random` でシード指定可能（将来のホスト再現用）。固有能力があれば弾の命中を `ChargeBurstModel`（純 C#）に渡し、条件を満たすと雷撃（`DamageFlags.WeaponProc` のダメージ → 属性ヒット → 既存のコンボ判定）、`ChargeBurstTriggered` を発行 |
| `WeaponFx` | マズルフラッシュ（ViewModel レイヤー）、弾道（移動する光の筋）、着弾（環境／敵で別 Prefab）。帯電連射（Chain）の放電は表示だけ時間差：初撃のバーストと縦ボルト → `DischargeTiming` 後に連鎖先へ横アークと二発目、連鎖先の `HitFlash` も到達時（`IHitFlashView`）、発動者のカメラキック（`CameraShaker.Kick` の長さ指定版）。ダメージは放電のフレームで確定済み（`LIGHTNING_VFX.md` §1.1） |

### 4.3 戦闘（`CoopDeck.Combat`）

- `DamageInfo`：量・属性・種別（Weapon/Skill/Combo/Status/Environment）・フラグ（Headshot/Critical/**ComboDamage**/Coop）・発生源 `ICombatActor`・チーム・位置・法線・方向・ヘッドショット倍率。
- `HitZone`：コライダー単位の部位（Body/Head/Weakpoint/Armor）と対象側倍率。最終倍率＝部位倍率 ×（Head なら武器のヘッドショット倍率）。
- `Health`：`HealthModel` を包み、`Damaged` / `Died` / `Revived` / `Changed` と `CombatEvents` を発火。同チームへのダメージは無効（Neutral は例外）。
- `CombatEvents`：静的イベント。Domain Reload 無効でも安全なよう `SubsystemRegistration` でリセット。

### 4.4 エフェクト（`CoopDeck.Effects`）

- `FxPool`：Prefab 単位のプール。`Spawn(prefab, pos, rot, parent, layer)` のみ。初回のヒッチを防ぐ `Prewarm`。
- `PooledFx`：子の ParticleSystem のバースト数・放出率を `PerformanceSettings.EffectDensity`（High 1.0 / Handheld 0.55）で自動スケール。
- `LightFlash`：短いライト点滅。Handheld ティアでは無効。
- シェーダー `CoopDeck/FX/Additive Soft`：テクスチャ不要の加算発光（UV から円形／帯状を計算、フォグ対応、SRP Batcher 対応）。
- 火球（Phase 5）：Shader Graph 6 種 `CoopDeck/FX/SG_FX_*`（計算は `Art/VFX/Fireball/Shaders/FireballVfx.hlsl`）。炎の本体は半透明、白熱部と火花だけ加算。
  `IRadiusFx`（範囲に合わせて自分の層を伸ばす）・`ISurfaceFx`（命中面・キャラクター命中に合わせる）・`IProjectileFx`（飛翔弾の見た目）。
  `PooledFx.StopEmitting`（放出を止めて自然に消えてから返却）、距離あたりの放出も品質で調整。`ElementAuraPresenter` は炎上中の炎も出す。

### 4.5 デッキ・スキル（`CoopDeck.Cards / Deck / Items / Skills`）

- `CardDefinition`：ID・名前・説明・属性・レア度・ゲージ最大値・消費量・CD・照準方式（Self / Aim / AimPoint / AreaAroundSelf）・射程・着弾遅延・`SkillEffectSpec` の配列・アイコン・VFX 参照。
- `DeckState`：カード循環の全規則（`CARD_SYSTEM.md` §1）。イベント順は Used → GaugeChanged → Depleted → Reshuffled → Drawn。
- `GaugeItemInventory`：種類ごとの所持数・上限・選択。満タンや空き枠への使用は拒否して消費しない。
- `SkillCaster`：効果 8 種を実行。範囲は `Physics.OverlapSphere` で集め `Health` 単位で重複排除、チームで敵味方を判定。範囲 VFX は半径 1m で作り、効果半径に合わせて拡大。
- `DeckController`：`FpsCharacter` のイベントだけを購読するので、NPC やネットワーク経由でも同じ経路で使える。選択枠は最後に使ったカードに追従。
- `CombatStatus`：シールド（ダメージを先に吸収）・攻撃力強化・被ダメージ増加・継続ダメージ（0.5 秒刻み、最大 6 重）・最後に受けた属性（Phase 4 のコンボ用）。`Health.ApplyDamage` が参照する。

### 4.6 UI・診断（`CoopDeck.UI`）

2026-11-08 に「灯守隊の探索端末」の見た目へ全面改修（設計は `UI_DESIGN.md`）。すべてコードで uGUI を生成し、色・文字サイズは `UiTokens`、形は `UiSprites`（コード描画）、配置は `HudLayout` から取る。
キャンバスは 1920×1080 から Expand で拡縮し、HUD は 16:9 の枠（`HudSafeFrame`）に収める。

- `HudController`：照準・ヒットマーカー・ダメージ数値、`VitalsPanel`（左下：HP バー・障壁・状態アイコン・回避ランプ）、`WeaponPanel`（スキル枠の上：残弾・リロード）、`NotificationFeedView`（左下の戦闘ログ）、`GuidePanel`（F1 の操作ガイド／ポーズ画面の全操作）、F3 性能表示、F8 `UiStyleGallery`（開発用プレビュー）。
- `DeckHud`：下中央のスキル枠 3 つ（`SkillSlotView`）とその左の支援品の枠・山札／捨て札枚数・引いたカードの名前と説明（スキル枠の上に 2 秒）・知らせ（`CARD_SYSTEM.md` §11）。大きい `CardFrameView` は報酬・図鑑用。日本語は OS フォント（Yu Gothic UI → Meiryo → MS Gothic）を使用。
- `InputGlyphs`：使っている機器に合わせてキー表示を切替（ラベルのためだけにデバイスを読む）。`HudNotifications`：HUD 部品同士の通知の受け口（`SubsystemRegistration` でリセット）。
- `FrameTimeMonitor`：平均 FPS・1% Low・最悪フレーム。
- `AutoPerfRunner`：ビルドの無人計測（下記 6 章）。
- `DamageFeedbackHud`：被弾時の赤い周辺減光（シールドだけで防いだときはシアン）、攻撃が来た方向の矢印（同時に 4 個まで）、ダウン中の「ダウン！ まもなく復活します」。
- `ArenaHud`：右上の目標の枠。現在のエリア（仮名称 `WorldTerms`）、目標（アリーナに入る前は入場の案内、戦闘中は残りの敵、突破後は次のウェーブまで）、WAVE 番号。WAVE の開始・突破を通知へ流す。
- `NpcHud`：味方の 1 行（属性・名前・方針・HP・ダウン／蘇生）と頭上の名札・敵のコンボマーカー。味方のダウン・蘇生・後退を通知へ流す。
- `HudController`：弱点（Critical）ヒットはオレンジのマーカーとダメージ数値。

### 4.7 敵・AI（`CoopDeck.Enemies / AI`）

| クラス | 役割 |
|---|---|
| `EnemyDefinition` (SO) | HP・移動速度・加速・旋回・索敵 30 m／追跡打ち切り 60 m・攻撃種別（近接／弾）・射程・維持距離・ダメージ・予兆／硬直／間隔・近接の角度・弾 Prefab と弾速・半径・予兆／撃破 FX |
| `EnemyActor` | 敵の `ICombatActor`。`CombatantRegistry` に登録、死亡時にコライダーを切って撃破 FX、`Defeated` を発火し 2.2 s 後に削除 |
| `EnemyStateMachine` | 純 C# の状態機械（上記 3.1）。攻撃は予兆 1 回につき必ず 1 回、間隔中は再攻撃しない |
| `EnemyBrain` | 状態機械の意図を NavMeshAgent・向き・攻撃に反映。頭の向き用の `LookTarget` を毎フレーム対象へ移動 |
| `EnemyProjectile` | 見える速さの弾（`PooledFx`）。最初に触れた敵対者に 1 回ダメージを与えて消える |
| `WaveSchedule` | 純 C# のウェーブ進行（出現順は種類を交互に、同時最大数、休憩、最終ウェーブ後は各種類 +1 でループ） |
| `WaveDirector` | アリーナの範囲・出現点・敵 Prefab。全員が範囲外に 6 s 出ると出現を一時停止（残った敵は戦い続ける） |
| `CombatantRegistry`（Combat） | 有効な戦闘者の一覧。AI の対象探索にシーン検索を使わない。`SubsystemRegistration` でリセット |
| `ForceReceiver`（Combat） | 吸引。NavMeshAgent を持つ敵は `agent.Move` で動かすので NavMesh の外へ押し出されない |

敵の種類：

| | スラッシャー（近接） | シューター（遠距離） |
|---|---|---|
| 見た目 | 赤い体・黄色い頭・白いバイザー | 紫の体・シアンのバイザー・胸に光る弱点コア |
| HP / 速度 | 140 / 4.2 m/s | 90 / 3.4 m/s |
| 攻撃 | 射程 2.2 m、14 ダメージ、予兆 0.55 s、間隔 1.3 s | 射程 20 m（約 11 m を維持）、弾 9 ダメージ・20 m/s、予兆 0.7 s、間隔 1.8 s |
| 部位 | 胴・頭 | 胴・頭・弱点（×1.75） |

### 4.8 アニメーション（`CoopDeck.Animation`）

- `CharacterAnimatorDriver`：`ILocomotionSource` の速度を向きに対するローカル値（`LocomotionMath.LocalBlend`）にして `MoveX/MoveY` に、歩幅に合わせた再生速度を `LocomotionSpeed` に渡す（減衰 0.1 s）。射撃・リロード・攻撃・被弾はトリガー、死亡は `Dead`。
- プレイヤーの全身（`BodyVisual`）は影だけ描画。`FpsCharacter` が武器・HP のイベントでトリガーを送る。
- 敵の頭は Animation Rigging の Multi-Aim（±70°）で対象を向く。
- Animator の構成は `ANIMATION_GUIDELINES.md` §3.1。

---

### 4.9 NPC 仲間（`CoopDeck.NPC`）と協力

| クラス | 役割 |
|---|---|
| `NpcController` | AI コントローラー（`ICharacterCommandSource`、`ControllerKind.AI`）。知覚・判断・実行。指示（方針・集中攻撃）を受け、失血後はプレイヤーの横で復帰 |
| `NpcDecisionMaker` | Utility AI（純 C#）。コンボ追撃は `ComboPlanner` で「その属性を当てたら今コンボになるか」を確認。得意属性のカードは味方の相方属性のために時間制限つきで温存 |
| `NpcStateMachine` | 純 C#。ダウン > 回避（0.4 秒）> 救助 > 合流（26 m で開始、9 m で終了）> 交戦（2.5 秒残る）> 追従 |
| `NpcSituation` / `NpcCardTraits` | 判断の入力（再利用バッファ、GC なし）／カード効果の要約 |
| `NpcSteering` / `NpcNavigator` | 移動・照準・射線判定の純関数／NavMesh 経路追従 |
| `NpcProfile` (SO) | 名前・得意属性・思考間隔・追従距離・照準・温存時間・回避率など（`NP_Volt`） |
| `ComboPlanner`（Combos） | コンボ判定と同じ規則で「追撃できるか」「何で完成するか」を状態を変えずに答える（NPC と HUD マーカー） |
| `Revivable` / `ReviveModel`（Combat） | ダウン → 近くの味方が 2.5 秒立つと 45% で蘇生、失血まで 25 秒（プレイヤーは 20 秒）。プレイヤーは味方がいなければ従来どおり 2.5 秒で再出現 |
| `TeamEngagementMonitor`（Combat） | 交戦中か（敵 28 m 以内／敵とのダメージから 5 秒）。デッキ編集のロックに使う |
| `AllyCollisionFilter`（Player） | 味方同士の CharacterController の衝突を無視（ふさがない） |
| `NpcHud` / `NpcDeckEditorHud` / `ComboHud`（UI） | NPC パネルと敵頭上マーカー／デッキ編集テスト UI（`InputGate` で操作を止める）／協力コンボ表示 |

- 協力の判定は Phase 3 の `ComboResolver` のまま：付与者 ID ≠ 完成者 ID かつ同チームで ×1.3（`DamageFlags.Coop`）。NPC はこの規則を変えず、狙うだけ。
- 協力コンボ時のみ `ComboDefinition.coopFx`（金色、層は `VfxQualityGate` で中/高のみ）、`coopSound`、画面揺れ ×1.4、FOV +3.5° の短いパンチ（`CameraShaker.Punch`、`-noshake` で無効、VFX 低で半分）。
- 射撃場の的は `ITrainingTarget` として登録。NPC はプレイヤーが撃った的にだけ参戦し、交戦判定にも数えない。

## 5. レイヤー・カメラ・描画

| レイヤー | 番号 | 用途 |
|---|---|---|
| Player | 6 | プレイヤーのコライダー。プレイヤーの射撃・移動判定から除外 |
| Enemy | 7 | 敵・的 |
| ViewModel | 8 | 一人称の腕と銃。本体カメラは描かず、オーバーレイカメラ（FOV 55°、near 0.01）だけが描く → 壁にめり込まない |

- 判定用マスク（`GameLayers`）：プレイヤーの射撃＝Player・ViewModel・UI 以外、敵弾＝Enemy・ViewModel・UI 以外、敵の視線＝Player・Enemy・ViewModel・UI 以外（キャラクター同士は視線を遮らない）。
- NavMesh：`Environment` に置いた `NavMeshSurface`（物理コライダーから生成、Default レイヤー）。データは `Scenes/Sandbox_Phase1_NavMesh.asset`。
- 本体カメラ：FOV 68°（スプリントで +5°）。AA は **SMAA High**（ベースカメラの設定がスタック全体に効く）。**ポストプロセスは無効で、スタックの最後のカメラ（ViewModel のオーバーレイ）が全体に 1 回掛ける**。URP はトーンマップ・ブルーム・SMAA を「ポストプロセスが有効なカメラ」の中で掛けるので、本体カメラで掛けると銃・腕・マズルフラッシュが描かれる前に終わってしまう（`WEAPON_VISUAL_QUALITY.md`）。
- ポストプロセス（`VP_Sandbox`）：Bloom（しきい値 1.0・強度 0.55）、Neutral トーンマップ、彩度 +18・コントラスト +8、弱いビネット。カラフルで視認性の高い見た目の土台（トゥーンシェーダーは Phase 5）。
- 自分の体（カプセル）は影だけ描画。

---

## 6. 性能ティア（ROG Ally X 対応）

| | High（メインPC） | Handheld（ROG Ally X） |
|---|---|---|
| URP アセット | `URP_High` | `URP_Handheld` |
| Render Scale | 1.0 | 1.0（2026-11-09 まで 0.833＋FSR。一人称の銃が階段状になったため変更、GPU 約 +0.7 ms） |
| アップスケール | Auto | Auto（拡大なし） |
| MSAA | 4x | なし（2x は約 +3 ms） |
| AA（カメラ） | SMAA High＋MSAA | SMAA High |
| 影 | 70 m / 2048 / 4 カスケード / ソフト | 35 m / 1024 / 2 カスケード / ハード |
| SSAO | テンプレート既定 | 無効 |
| エフェクト品質（VFX） | 高（粒子 1.0・エフェクト用ライトあり） | 中（粒子 0.55・ライトなし）。低（0.3・追加の層なし・画面揺れ半分）も選べる |
| フレームペース | vSync 1 | 120 Hz パネルなら vSync 2（=60fps）、それ以外は 60fps 上限 |

- 判定（`PerformanceBootstrap`、シーン読み込み前）：コマンドライン `-tier high|handheld` → 端末名に `ROG Ally`/`RC71L`/`RC72L` → GPU 名が型番なしの内蔵 Radeon → それ以外は High。
- `-uncapped` で vSync とフレーム上限を外す（余力の計測用）。
- VFX 品質はティアに従う（High→高、Handheld→中）。プレイ中は F4、起動時は `-vfx low|medium|high` で変更でき、変更後はティアに追従しない。追加の層は `VfxQualityGate` で省略、粒子数は `PooledFx` が品質の倍率で調整。`-noshake` で画面揺れなし。
- 品質レベル名は `High` / `Handheld`（テンプレートの `PC` / `Mobile` を改名）。

### 無人計測

```
Builds\Windows\CoopDeckFPS.exe -perfrun 30 -perfout C:\temp\perf.json [-tier handheld] [-uncapped] [-perfidle] [-perfarena]
```

3 秒待機後に 30 秒間計測し（既定は自動操縦で視点を振りながら連射）、JSON（平均 FPS・1% Low・最悪 ms・ティア・解像度・GPU 等）と同名の PNG スクリーンショットを書き出して終了する。結果は `DEVELOPMENT_PLAN.md` の記録表に転記する。
`-perfarena` はプレイヤーを敵アリーナに置き（計測中は倒れない）、自動操縦が最寄りの敵を狙って戦う。JSON に到達ウェーブと最大同時敵数が入る。
`-perfpractice` はコンボ練習デッキでスキルだけを 0.8 秒ごとに使い、コンボ回数を数える（`-vfx` と組み合わせて品質ごとに比較）。

---

## 7. エディターツール（`CoopDeck.Editor`）

| メニュー / メソッド | 内容 |
|---|---|
| `CoopDeck > Setup > Run Phase 1 Bootstrap` / `ProjectBootstrap.RunBatch` | レイヤー、URP ティア、Player Settings、入力アセット、マテリアル、エフェクト Prefab、武器・移動データ、仮アニメーション（`AnimationAssetFactory`）、敵データと敵 Prefab（`EnemyFactory`、仮リグは `PlaceholderRigBuilder`）、プレイヤー／的 Prefab、`Sandbox_Phase1` シーン（敵アリーナと NavMesh の生成を含む）、ビルド設定を生成。データ資産（武器・移動・入力・敵）は既存なら保持。仮アニメーションは既存なら保持し、`-coopdeckOverwriteAnimations` で作り直す |
| `CoopDeck > Setup > Build Net Smoke Scene (Phase 2.5)` / `NetSmokeSceneBuilder.BuildStandalone`（ブートストラップ内では `Build`） | `PF_NetPlayer`（PF_Player の Variant ＋ NetworkObject・所有者権威の NetworkTransform・`NetworkPlayer`）と `Net_Smoke` シーン（NetworkManager・Unity Transport・30 Hz・接続承認で出現位置・シーン管理なし）を生成し、ビルドの 2 番目のシーンに追加。シーンは追加読み込みで作って閉じるので、開いているシーンは変わらない |
| `CoopDeck > Build > Windows (Development/Release)` / `BuildScript.BuildWindowsBatch` | `Builds/Windows/CoopDeckFPS.exe` を出力（Mono、x64） |
| `FireballVfxFactory.EnsureAll`（ブートストラップ内） | 火球のメッシュ・Shader Graph（`ShaderGraphWriter`）・マテリアル・Prefab を毎回生成。`CardDataFactory.EnsureFireball` がカードと初期デッキへの追加 |

バッチ実行例（プロジェクトを Editor で開いていない状態で）：

```
"C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe" -batchmode -quit -projectPath C:\dev\CoopDeckFPS -executeMethod CoopDeck.EditorTools.ProjectBootstrap.RunBatch -logFile bootstrap.log
"C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe" -batchmode -projectPath C:\dev\CoopDeckFPS -runTests -testPlatform EditMode -testResults editmode.xml
"C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe" -batchmode -projectPath C:\dev\CoopDeckFPS -runTests -testPlatform PlayMode -testResults playmode.xml
```

---

## 8. 生成物とフォルダ

```
Assets/_Game/
  Art/{Characters,Environments,Weapons,VFX}/Materials   生成マテリアル（URP Lit / Unlit / FX）
  Art/Environments/Textures/T_Grid.png                   床グリッド（2 m 目盛り）
  Art/VFX/Shaders/FX_AdditiveSoft.shader
  Prefabs/Characters/PF_Player.prefab                     プレイヤー（カメラ・ViewModel 込み）
  Prefabs/Enemies/PF_TargetDummy.prefab                   的（胴・頭 HitZone、HP バー、復活）
  Prefabs/VFX/FX_*.prefab                                 マズル・弾道・着弾×2・撃破
  ScriptableObjects/Weapons/WD_AssaultRifle.asset
  ScriptableObjects/Characters/MP_Default.asset
  Settings/Input/GameControls.inputactions
  Settings/Rendering/URP_{High,Handheld}(_Renderer).asset, VP_Sandbox.asset
  Scenes/Sandbox_Phase1.unity                             射撃場＋移動コース＋的の密集エリア＋ダメージ床＋敵アリーナ（26×18 m、遮蔽物 4、出現点 4）
  ScriptableObjects/Cards/CD_*.asset, Cards/Decks/DD_Starter.asset   カード 12 種と初期デッキ
  ScriptableObjects/Items/GI_*.asset                      チャージポーション、共鳴結晶
  Art/UI/CardIcons/ICON_*.png                             生成アイコン（差し替え可）
  Prefabs/Skills/FX_Skill_*.prefab                        属性別スキル VFX（21 個）
  Animations/Placeholder/A_Placeholder_*.anim             仮クリップ 16 本（待機・8 方向歩行・落下・死亡・近接・射撃姿勢・被弾・射撃・リロード）
  Animations/Placeholder/AC_PlaceholderHumanoid.controller, AOC_Shooter.overrideController, AM_UpperBody.mask
  ScriptableObjects/Enemies/ED_Slasher.asset, ED_Shooter.asset   敵の調整値
  Prefabs/Enemies/PF_Enemy_Slasher.prefab, PF_Enemy_Shooter.prefab, PF_EnemyProjectile.prefab
  Prefabs/VFX/FX_Enemy_Windup.prefab                      敵の攻撃予兆
  Scenes/Sandbox_Phase1_NavMesh.asset                     シーンの NavMesh データ（敵アリーナはシーン北西）
  ScriptableObjects/Combos/CB_*.asset                     コンボ 3 種（3 段階の数値と VFX 参照）
  ScriptableObjects/Cards/Decks/DD_ComboPractice.asset    コンボ練習デッキ（既存カード 12 枚）
  Prefabs/Combos/FX_Combo_*.prefab, FX_Aura_*.prefab      コンボ VFX（3 種×3 段階＋連鎖・爆発・雷弧）と属性オーラ 4 種
  Art/VFX/Fireball/{Meshes,Materials,Shaders}            火球のメッシュ 6・マテリアル 18・Shader Graph 6 と FireballVfx.hlsl
  Prefabs/Skills/Fireball/FX_*.prefab                     火球のチャージ・飛翔・爆発・敵に絡む炎・炎上
  ScriptableObjects/Cards/CD_Fireball.asset               火球（初期デッキ 13 枚目）
```

テンプレート由来の `Assets/Settings`（PC/Mobile アセット）、`Assets/Scenes/SampleScene`、`TutorialInfo` は削除せず残している（現在は未使用）。

---

## 9. 次フェーズの接続点

| フェーズ | 接続先 |
|---|---|
| 2 敵 AI（実装済み） | 新しい敵は `EnemyDefinition` と Prefab の追加で作れる。特殊な行動は `EnemyStateMachine` に状態を足すか、別の Brain を作る |
| 2 モーション（実装済み・仮） | 本番モデルは `AC_PlaceholderHumanoid` と同じパラメータ名で Humanoid の Controller を作り、`CharacterAnimatorDriver` の Animator を差し替える。一人称は現状プロシージャル（`ViewModelAnimator`） |
| 2.5 通信（移動のみ実装済み） | `PF_NetPlayer`（PF_Player の Prefab Variant）＋ `Net_Smoke` シーン（`NetSmokeSceneBuilder`）。所有者権威の `NetworkTransform` と `NetPose`。他の端末のコピーは `RemoteCommandSource`・`FpsCharacter` 停止・一人称リグ非表示・`CharacterAnimatorDriver.SetLocomotionSource(RemoteMotion)`。Phase 6 でホスト権威（`CharacterCommands` をホストへ送り、`FpsCharacter.Step` をホストで実行）に移す。`MULTIPLAYER_PLAN.md` §7 |
| 3 デッキ（実装済み） | 報酬は `DeckState.AddCard`、強化は `UpgradeCard`（強化表は未実装）、パッシブは `DeckState.Passives` |
| 4 コンボ（実装済み） | 新コンボは `ComboDefinition` アセットの追加（種類は ChainBurst / Singularity / Explosion）。段階は `CardRuntime.UpgradeLevel`。オンラインではホストの `ComboSystem` だけが判定し、`ComboEvents` をクライアントへ送る想定 |
| 4 NPC（実装済み） | 新しい NPC は `NpcProfile` と初期デッキ（`DeckDefinition`）と見た目を変えた Prefab で作れる（`NpcFactory`）。行動の傾向は `NpcProfile` と `NpcDecisionMaker` の点数帯 |
| 4 NPC デッキ強化 | ステージ報酬は `DeckLoadoutController.GrantCard`（所持）→ 非戦闘中の編集で採用。強化は `DeckState.UpgradeCard` |
| 6 オンライン | `FpsCharacter.SetCommandSource` でネットワーク入力に差し替え。NPC 思考・ダメージ・カード消費・コンボ・蘇生はホストだけで動かす（`MULTIPLAYER_PLAN.md` §5） |
| 5 演出 | `WeaponDefinition` / 将来の `CardDefinition` が参照するエフェクト Prefab を差し替え、`PooledFx` で粒子数をティア調整 |
