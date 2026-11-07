# ARCHITECTURE — 実装アーキテクチャ

Phase 1 時点で**実際に存在するコード**の構成と、その後のフェーズが接続する場所をまとめる。
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
CoopDeck.Core      （依存なし）  CharacterCommands, ICharacterCommandSource, IAimProvider,
                                   Element, TeamId, GameLayers, PerformanceSettings/Bootstrap
   ▲
   ├── CoopDeck.Combat   DamageInfo/Result, IDamageable, ICombatActor, Health(+HealthModel),
   │                     HitZone, DamageRules, CombatEvents,
   │                     CombatStatus(+StatusEffectsModel: シールド/攻撃強化/被ダメ増/継続ダメ), ForceReceiver(吸引)
   ├── CoopDeck.Cards    CardDefinition, SkillEffectSpec, DeckDefinition, ElementStyle（データのみ）
   │     └── CoopDeck.Deck   CardRuntime, DeckState, IRandom/SeededRandom（純 C#）
   │           └── CoopDeck.Items  GaugeItemDefinition, GaugeItemInventory（純 C#）
   ├── CoopDeck.Input    GameInputActions（既定バインド生成）, PlayerInputReader   ← Unity.InputSystem
   ├── CoopDeck.Effects  FxPool, PooledFx, TracerFx, LightFlash
   │
   ├── CoopDeck.Weapons  WeaponDefinition, WeaponState, HitscanWeapon, WeaponFx  ← Combat, Effects
   ├── CoopDeck.Enemies  TargetDummy, PatrolMover, HitFlash                       ← Combat, Effects
   ├── CoopDeck.Player   MovementProfile, MotorMath, FpsMotor, FpsLook,
   │                     FpsCharacter, ViewModelAnimator                          ← Combat, Weapons
   ├── CoopDeck.Skills   SkillCaster（効果の実行）, DeckController（キャラ⇔デッキ⇔アイテム）
   │                                                                              ← Combat, Effects, Cards, Deck, Items, Player
   └── CoopDeck.UI       HudController, DeckHud, HudKit, WorldHealthBar, FrameTimeMonitor,
                         Diagnostics/AutoPerfRunner, AutopilotCommandSource       ← Combat, Weapons, Player, Cards, Deck, Items, Skills, InputSystem, uGUI
   （CoopDeck.Enemies に DamageZone、CoopDeck.Player に PlayerRespawn を追加）

CoopDeck.Editor           ProjectBootstrap ほか生成ツール（カード・アイコン・スキル VFX 含む） ← 全ランタイム + URP
CoopDeck.Tests.EditMode   83 テスト（純ロジック・デッキ規則・アイテム・状態効果・生成データ・入力定義・ティア判定）
CoopDeck.Tests.PlayMode   21 テスト（移動・射撃・デッキ×FPS 統合・仮想キーボード/ゲームパッド・各スキル効果）
```

- **`CoopDeck.Player` は `CoopDeck.Input` を参照しない。** 入力は `ICharacterCommandSource` 経由でのみ届く。
- 予定アセンブリ（フォルダのみ作成済み）：`Cards` `Deck` `Skills` `Combos` `Items` `NPC` `AI` `Animation` `Networking` `Save`。
  - `Deck`（純 C#）→ `Core` のみ参照、`Skills` → `Deck` `Combat` `Effects`、`Combos` → `Combat`、`NPC`/`AI` → `Player` `Deck` `Combat`、`Networking` → 全体（逆参照はしない）。

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
  7. ItemUseRequested / ItemCycleRequested → GaugeItemInventory → DeckState.RestoreCharge
```

射撃（4）とスキル（6）は同じフレーム内の別処理で、互いの状態（弾数・ゲージ）に触れない。攻撃力強化だけは `ICombatActor.Status` 経由で両方のダメージに乗る。

`FpsCharacter.Step` は public なので、将来の固定ティック（ネットワーク）やテストからも同じ処理を呼べる。

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
| `WeaponDefinition` (SO) | 威力 18、ヘッドショット 2 倍、600 RPM、30 発、リロード 1.7 s、距離減衰 40 m→150 m で 0.6 倍、拡散（基本 0.2°・1発 +0.2°・最大 +2.4°・移動/空中ペナルティ）、リコイル（縦 0.55°・横 ±0.22°・初弾 1.3 倍）、エフェクト Prefab 参照 |
| `WeaponState` | 弾数・連射間隔・リロード・ブルーム。待ち時間の端数は「次弾待ちのときだけ」持ち越し、どのフレームレートでも連射速度が正確。リロード中やトリガーを離している間は貯まらない（初弾は必ず1発） |
| `HitscanWeapon` | 照準レイ（`IAimProvider`）に拡散をかけてレイキャスト → `IDamageable`。拡散乱数は `System.Random` でシード指定可能（将来のホスト再現用） |
| `WeaponFx` | マズルフラッシュ（ViewModel レイヤー）、弾道（移動する光の筋）、着弾（環境／敵で別 Prefab） |

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

### 4.5 デッキ・スキル（`CoopDeck.Cards / Deck / Items / Skills`）

- `CardDefinition`：ID・名前・説明・属性・レア度・ゲージ最大値・消費量・CD・照準方式（Self / Aim / AimPoint / AreaAroundSelf）・射程・着弾遅延・`SkillEffectSpec` の配列・アイコン・VFX 参照。
- `DeckState`：カード循環の全規則（`CARD_SYSTEM.md` §1）。イベント順は Used → GaugeChanged → Depleted → Reshuffled → Drawn。
- `GaugeItemInventory`：種類ごとの所持数・上限・選択。満タンや空き枠への使用は拒否して消費しない。
- `SkillCaster`：効果 8 種を実行。範囲は `Physics.OverlapSphere` で集め `Health` 単位で重複排除、チームで敵味方を判定。範囲 VFX は半径 1m で作り、効果半径に合わせて拡大。
- `DeckController`：`FpsCharacter` のイベントだけを購読するので、NPC やネットワーク経由でも同じ経路で使える。選択枠は最後に使ったカードに追従。
- `CombatStatus`：シールド（ダメージを先に吸収）・攻撃力強化・被ダメージ増加・継続ダメージ（0.5 秒刻み、最大 6 重）・最後に受けた属性（Phase 4 のコンボ用）。`Health.ApplyDamage` が参照する。

### 4.6 UI・診断（`CoopDeck.UI`）

- `HudController`：コードで uGUI を生成。拡散に連動する照準、ヒットマーカー（ヘッドショット黄・キル赤）、ダメージ数値、弾数・リロード、HP（シールド量・攻撃強化の残り時間つき）、ダッシュ残数、一時停止表示、操作ヒント（F1 で非表示）、F3 性能表示。
- `DeckHud`：手札 3 枚・山札／捨て札枚数・アイテム欄・破棄／ドロー演出（`CARD_SYSTEM.md` §11）。日本語は OS フォント（Yu Gothic UI → Meiryo → MS Gothic）を使用。
- `FrameTimeMonitor`：平均 FPS・1% Low・最悪フレーム。
- `AutoPerfRunner`：ビルドの無人計測（下記 6 章）。

---

## 5. レイヤー・カメラ・描画

| レイヤー | 番号 | 用途 |
|---|---|---|
| Player | 6 | プレイヤーのコライダー。プレイヤーの射撃・移動判定から除外 |
| Enemy | 7 | 敵・的 |
| ViewModel | 8 | 一人称の腕と銃。本体カメラは描かず、オーバーレイカメラ（FOV 55°、near 0.01）だけが描く → 壁にめり込まない |

- 本体カメラ：FOV 68°（スプリントで +5°）、FXAA、ポストプロセス有効。
- ポストプロセス（`VP_Sandbox`）：Bloom（しきい値 1.0・強度 0.55）、Neutral トーンマップ、彩度 +18・コントラスト +8、弱いビネット。カラフルで視認性の高い見た目の土台（トゥーンシェーダーは Phase 5）。
- 自分の体（カプセル）は影だけ描画。

---

## 6. 性能ティア（ROG Ally X 対応）

| | High（メインPC） | Handheld（ROG Ally X） |
|---|---|---|
| URP アセット | `URP_High` | `URP_Handheld` |
| Render Scale | 1.0 | **0.833**（1080p パネルで内部 900p） |
| アップスケール | Auto | **FSR** |
| MSAA | 2x | なし |
| 影 | 70 m / 2048 / 4 カスケード / ソフト | 35 m / 1024 / 2 カスケード / ハード |
| SSAO | テンプレート既定 | 無効 |
| エフェクト粒子数 | 1.0 | 0.55、エフェクト用ライト無効 |
| フレームペース | vSync 1 | 120 Hz パネルなら vSync 2（=60fps）、それ以外は 60fps 上限 |

- 判定（`PerformanceBootstrap`、シーン読み込み前）：コマンドライン `-tier high|handheld` → 端末名に `ROG Ally`/`RC71L`/`RC72L` → GPU 名が型番なしの内蔵 Radeon → それ以外は High。
- `-uncapped` で vSync とフレーム上限を外す（余力の計測用）。
- 品質レベル名は `High` / `Handheld`（テンプレートの `PC` / `Mobile` を改名）。

### 無人計測

```
Builds\Windows\CoopDeckFPS.exe -perfrun 30 -perfout C:\temp\perf.json [-tier handheld] [-uncapped] [-perfidle]
```

3 秒待機後に 30 秒間計測し（既定は自動操縦で視点を振りながら連射）、JSON（平均 FPS・1% Low・最悪 ms・ティア・解像度・GPU 等）と同名の PNG スクリーンショットを書き出して終了する。結果は `DEVELOPMENT_PLAN.md` の記録表に転記する。

---

## 7. エディターツール（`CoopDeck.Editor`）

| メニュー / メソッド | 内容 |
|---|---|
| `CoopDeck > Setup > Run Phase 1 Bootstrap` / `ProjectBootstrap.RunBatch` | レイヤー、URP ティア、Player Settings、入力アセット、マテリアル、エフェクト Prefab、武器・移動データ、プレイヤー／的 Prefab、`Sandbox_Phase1` シーン、ビルド設定を生成。データ資産（武器・移動・入力）は既存なら保持 |
| `CoopDeck > Build > Windows (Development/Release)` / `BuildScript.BuildWindowsBatch` | `Builds/Windows/CoopDeckFPS.exe` を出力（Mono、x64） |

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
  Scenes/Sandbox_Phase1.unity                             射撃場＋移動コース＋的の密集エリア＋ダメージ床
  ScriptableObjects/Cards/CD_*.asset, Cards/Decks/DD_Starter.asset   カード 12 種と初期デッキ
  ScriptableObjects/Items/GI_*.asset                      チャージポーション、共鳴結晶
  Art/UI/CardIcons/ICON_*.png                             生成アイコン（差し替え可）
  Prefabs/Skills/FX_Skill_*.prefab                        属性別スキル VFX（21 個）
```

テンプレート由来の `Assets/Settings`（PC/Mobile アセット）、`Assets/Scenes/SampleScene`、`TutorialInfo` は削除せず残している（現在は未使用）。

---

## 9. 次フェーズの接続点

| フェーズ | 接続先 |
|---|---|
| 2 敵 AI | `ICombatActor` を実装した敵、`Health`/`HitZone` はそのまま。NavMesh は AI Navigation |
| 2 モーション | `ViewModelAnimator` を Animator 駆動に置き換え。全身は `BodyVisual` を差し替え、`FpsMotor.Velocity`/`IsGrounded` を Animator パラメータへ |
| 2.5 通信 | `ICharacterCommandSource` のネットワーク版、`FpsCharacter.Step` を固定ティックで呼ぶ |
| 3 デッキ（実装済み） | 報酬は `DeckState.AddCard`、強化は `UpgradeCard`（強化表は未実装）、パッシブは `DeckState.Passives` |
| 4 コンボ | `CombatEvents.DamageApplied` を購読、`DamageInfo.Element`・`CombatStatus.Model.LastElement` と `DamageFlags.ComboDamage` で連鎖制御 |
| 4 NPC | NPC 用の `ICharacterCommandSource` がスキル枠を押すだけで `DeckController` が同じ規則でカードを使う（8 枚・2 枠は `Rebuild` の引数） |
| 5 演出 | `WeaponDefinition` / 将来の `CardDefinition` が参照するエフェクト Prefab を差し替え、`PooledFx` で粒子数をティア調整 |
