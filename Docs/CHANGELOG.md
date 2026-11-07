# CHANGELOG

形式：日付ごとに「追加 / 変更 / 修正 / 検証」。検証していないものは「検証」に書かない。

## 2026-11-07 — Phase 4：NPC 仲間 AI と協力コンボ

Unity は 6000.5.4f1 のまま。追加パッケージ・外部素材・有料サービスなし（効果音はエディターで合成した仮音源）。

### 追加
- アセンブリ `CoopDeck.NPC`：`NpcController`（AI コントローラー）、`NpcDecisionMaker`（Utility AI）、`NpcStateMachine`、`NpcSituation`、`NpcSteering`、`NpcNavigator`、`NpcCardTraits`、`NpcProfile`（SO）。
- NPC 仲間「ヴォルト」（雷）：プレイヤーと同じ `FpsCharacter`／`FpsMotor`／`HitscanWeapon`／`SkillCaster`／`DeckController` を AI が `CharacterCommands` だけで操作。金色の体・紺の手足・シアンのバイザー・頭上の光る輪で、プレイヤー（青）・敵（赤／紫）と区別。頭上に名前と HP。
- NPC の行動：NavMesh 追従（プレイヤーの斜め後ろ・射線の外）、索敵（視線・聴覚・味方への攻撃・チームが撃った敵）、バースト射撃（プレイヤー越しには撃たない）、攻撃予告を見て回避（ダッシュ）、射線を確保できる位置取り（約 1 秒ごとに 13 候補）、近接敵から距離を取る、離れすぎたら合流（26 m）／瞬間移動（48 m・詰まり）。
- 判断：状態機械（追従／交戦／合流／救助／回避／ダウン）＋ Utility AI（回避 → 救援 → コンボ追撃 → 起点づくり → 支援 → 範囲 → 単体／射撃 → 追従）。思考は 0.2 秒ごと。
- 協力コンボ AI：敵の属性（付与者 ID・残り時間・再発動制限）と手札・射程・射線・CD を見て追撃。得意属性の雷カードは味方の相方属性のために時間制限つきで温存（自動 2.5 秒・コンボ優先 6 秒）、火などはプレイヤーの手札に合わせて起点を作る。発動は照準レイが実際に対象に当たったときだけ。
- 独立した NPC デッキ：`DD_Npc_Volt`（8 枚・手札 2）とプリセット `DD_Npc_Blaze`（火・重力）`DD_Npc_Guardian`（支援）。追加カード「雷撃」（`CD_Thunderbolt`）。
- 指示：方針（自動戦闘／集中攻撃／コンボ優先）を V・R3 で切替、集中攻撃の対象を中クリック・X・LT で指定（`CharacterCommands.OrderTacticPressed / OrderMarkPressed`）。
- NPC デッキ編集テスト UI（Tab、パッドはポーズ中に Y）：所持カード、追加・削除、属性の偏り、初期化、プリセット。戦闘中はロック（`TeamEngagementMonitor`）。`CardCollection`／`DeckLoadout`／`DeckLoadoutController`（報酬は `GrantCard`）。
- ダウン・蘇生：`Revivable`／`ReviveModel`。味方が近くに 2.5 秒立つと HP 45% で蘇生。NPC はダウンしたプレイヤーを救助しに来る。NPC は失血後、プレイヤーの横で HP 50% で復帰。プレイヤーは味方がいなければ従来どおり再出現。
- 協力コンボ演出：コンボ名の上に「★ 協力コンボ！ ★」、協力ボーナス +30%、参加者（例：あなた［氷］→ ヴォルト［雷］）、金色の追加 VFX（`FX_Combo_Coop`、追加の層は VFX 中／高のみ）、専用サウンド（`ComboAudio`、`SFX_Combo_*_Coop.wav`）、画面揺れ ×1.4 と短い FOV パンチ（`CameraShaker.Punch`）。
- NPC HUD（`NpcHud`）：名前・HP・方針・行動・使用スキル・手札 2 枚のゲージと温存表示、ダウン／蘇生の進行。敵頭上に「◆氷 → 雷 でコンボ」「⚡ヴォルト 追撃可：超伝導バースト」「◎ 集中」。照準から 120 px 以内では薄く表示。
- 敵の攻撃予告イベント `CombatEvents.ThreatRaised`（`ThreatWarning`）、`ControllerKind`／`IControllerIdentity`、`FpsCharacter.SetCommandSource`／`FindLocalHuman`、`InputGate`、`AllyCollisionFilter`、`ComboPlanner`。
- 無人計測：JSON に NPC の思考回数・スキル使用・コンボ追撃・起点・回避、協力コンボ数、CPU／GPU 時間（FrameTimingManager）、GC Alloc（1 フレームあたり）、GC 回数。`-perfarena` では NPC も一緒にアリーナへ入る。

### 変更
- コンボ VFX（各段階・協力・雷弧）をシーン開始時にプールへ事前生成。無人計測の JSON に重いフレーム上位 8 件と直前の出来事（コンボ・協力・NPC スキル・敵出現・GC）を記録（`worstFrames`）。
- 射撃場の的を `ITrainingTarget` として登録（NPC はプレイヤーが撃った的にだけ参戦。`FindNearestHostile` は既定で的を除外）。
- 各 HUD・計測ツールの「プレイヤーを探す」処理を `FpsCharacter.FindLocalHuman` に変更（NPC も `FpsCharacter` のため）。
- 操作ヒントとポーズ表示に NPC の操作を追加。Player Settings で Frame Timing Stats を有効化。

### 修正（実装中に見つけたもの）
- NPC 自身の射撃で「チームが撃っている的」が更新され続け、射撃場の的を撃ち続ける → 他の味方の命中だけで判定。
- NPC がスキルを 0.2〜0.5 秒間隔で連打する → 通常スキルは 1 秒間隔（コンボ追撃と緊急回復は例外）。
- 温存がペアの両側（火も雷も）にかかり、NPC が火で起点を作れない → 温存は得意属性（追撃役）のカードだけ。
- 同じ攻撃予告に 2 回回避しうる → 処理済みの予告は再評価しない。
- 着弾点指定の範囲カードが横に 2° ずれた瞬間に発動し、敵の横を抜けて遠くの地面に着弾 → 照準レイが対象（または範囲内の地面）に当たるときだけ発動。スキルの照準中は射撃しない（反動で照準がぶれないように）。

### 検証
- コンパイル：エラー・警告 0。Windows Release ビルド：エラー・警告 0（104.4 MB）。ビルドの無人戦闘のスクリーンショットで協力コンボ表示（協力タグ・+30%・参加者）を確認。
- EditMode **197 件**・PlayMode **53 件**、全件成功（Editor とバッチモードの両方）。追加分：EditMode 40（判断 18・状態機械／操舵／カード特性 8・ロードアウト 3・蘇生／交戦 2・コンボ予測 2・生成資産とシーン 7）、PlayMode 16。
- Editor の Play：射撃場でプレイヤーの氷の槍 → 0.22 秒後にヴォルトが雷撃で追撃 → 超伝導バースト（協力 ×1.3、付与者 Player／完成者 ヴォルト）。NPC の手札に雷撃がないときは追撃しない。敵アリーナで索敵・射撃・リロード・回避・スキル循環、NPC のダウン → 失血 → プレイヤーの横で復帰を確認。
- ROG Ally X 実機（Handheld、900p FSR、VFX 中）：NPC 同行のアリーナ戦 60fps 上限 平均 59.9 / 1% Low 59.9（再計測 58.2）、上限なし 91.5 / 81.4。CPU メインスレッド 3.0〜3.8 ms、GPU 2.4〜2.8 ms。NPC の思考は 30 秒で 150 回（5 Hz）。無人の戦闘中に NPC の追撃・起点づくり・回避・協力コンボ（30 秒に 0〜8 回）が発生。
- GC：Release ビルドでは 1 フレームあたりの GC Alloc 量を取得できない（`gcAllocRecorderValid: false`）ため、GC 回数（30 秒に 8〜9 回）で記録。新規コードの毎フレーム／毎思考の確保（`IReadOnlyList` の foreach による列挙子、HUD 文字列の毎回生成）を削減。

### 未確認・残課題
- コンボ多発の計測で 100 ms 級の引っかかりが 6 回中 2 回（1% Low 45.4 / 34.7）。コンボ VFX の事前生成を追加し、重いフレームの記録（`worstFrames`）を入れた後の 3 回では再現しなかったが、原因は未特定。次に出たら JSON の `worstFrames` と Profiler で確認する。
- 人の手でのプレイ確認（NPC の動きの自然さ・強さ・指示とデッキ編集 UI の使い勝手、協力コンボ演出の見え方）。
- NPC の見た目・動きは仮（仮リグと共通の仮アニメーション）。
- 効果音は合成した仮音源（同名ファイルを差し替えれば反映）。
- NPC は 1 体のみ配置（複数 NPC は構造上対応：思考の位相ずらし・経路計算の共有予算・指示は全 NPC が受ける）。
- 作業中に Unity Editor が一度終了した（20:19 頃、ログにクラッシュの痕跡なし）。以降のテストとビルドはバッチモードで実行。

## 2026-11-07 — 属性コンボと VFX（ユーザー指示「Phase 3：属性コンボ・VFX実装」＝計画上の Phase 4 のコンボ部分）

Unity は 6000.5.4f1 のまま。追加パッケージ・外部素材・有料サービスなし。

### 追加
- 属性：`ElementAuraModel`（`CombatStatus.Elements`）で火・氷・雷・重力を 4 秒保持。付与者 ID（`CombatantRegistry.IdOf`）・チーム・カード強化段階を記録し、消費・期限切れ・死亡で解除。氷は移動 55%（体が氷色になる）、重力は 80%。
- スキルが届いた敵ごとに `CombatEvents.ElementHitApplied` を発行（炎上・吸引などダメージのない効果も含む。撃破した一撃でも可）。
- アセンブリ `CoopDeck.Combos`：`ComboDefinition`（SO）、`ComboResolver`・`ComboDamageBudget`（純 C#）、`ComboSystem`、`ComboEvents`、`ElementAuraPresenter`。
- コンボ 3 種：超伝導バースト（氷＋雷：連鎖雷撃）、灼熱特異点（火＋重力：吸引 → 爆発）、オーバーロード（火＋雷：範囲爆発）。4 秒以内・順序不問・対象ごとの再発動制限 3 秒・協力 ×1.3。通常／強化／究極の 3 段階（カードの `UpgradeLevel`）。
- VFX：コンボ 3 種×3 段階、連鎖の雷弧（`LightningArcFx`）、吸引の渦（`SpiralTrailFx` の Trail Renderer）、衝撃波リング（加算シェーダーにリング表示を追加）、属性オーラ 4 種、画面揺れ（`CameraShaker`、照準には影響しない）、コンボ名表示（`ComboHud`）、コンボのダメージ数値（ピンク）。
- VFX 品質：高／中／低（F4、`-vfx`）。Ally X の既定は中。`VfxQualityGate` で追加の層を省略、粒子は画面の 18% まで。`-noshake`。
- コンボ練習デッキ（F7、既存カード 12 枚）、コンボ段階の強制（F6）。
- 無人計測：`-perfpractice`（練習デッキ・スキルのみ 0.8 秒ごと、コンボ回数を記録）、JSON に `vfx`。

### 変更
- 既存スキルの範囲エフェクトは、範囲に合わせて「広がる」ようにした（粒子そのものが 6 倍に拡大して空に飛ぶのをやめた）。
- `HudController` の操作ヒントにコンボとデバッグキーの行を追加。

### 修正（実装中に見つけたもの）
- `FxPool` が同じエフェクトを 2 回プールへ戻すと、同じインスタンスが二重に使われる可能性 → 既に戻っているものは無視。
- 範囲スキルで群れ全体に同じコンボが起きると大きな VFX が重なり敵が見えにくい → 近くの同じ VFX は 1 つにまとめる（ダメージと「×N」表示はそのまま）。
- 氷の付与が見た目で分かりにくい → 体を氷色にし、オーラを強化。
- 灼熱特異点・爆発の粒子が空へ散ってにじむ → 吸い込みを地面と平行にし、火球を短く小さく。

### 検証
- コンパイル：エラー・警告 0。Windows Release ビルド：エラー・警告 0。
- EditMode **157 件**・PlayMode **37 件**、全件成功。追加分：属性 8、コンボ判定 26（3 種×順序、不一致 5 通り、期限、再発動制限、協力、段階、バジェット）、生成資産・VFX 品質 12、PlayMode 9（コンボ 3 種の発動とダメージ、連鎖しない・再発動制限、期限切れ・不一致、協力 ×1.3、撃破時のコンボ、群れでの VFX まとめとヒット上限、デッキ循環中のゲージ・破棄が正確）。
- Editor の Play：的の群れで 3 種と強化・究極段階を発動しスクリーンショットで確認。敵アリーナで氷の槍 → 連鎖雷撃により超伝導バーストが 3 体に連鎖（合計 116 ダメージ）。Console のエラー・警告 0。
- ROG Ally X（Handheld、900p FSR）：練習デッキ・スキル連発の戦闘で VFX 中 60fps 上限 平均 59.9 / 1% Low 59.9、上限なし 91.6。VFX 高でも 60fps を維持。

### 未確認・残課題
- 人の手でのプレイ確認（コンボの手応え・見やすさ・強さ）。
- カード強化の入手手段（強化表・報酬）がないため、強化・究極段階は F6 で強制したときだけ出る。
- NPC・オンラインの味方は未実装（協力コンボは 2 人の詠唱者を使ったテストで確認）。
- Shader Graph は使っていない（コードから安定して生成できないため。トゥーンシェーダーと合わせて Phase 5 で導入予定）。
- 究極コンボを連発する重い場面の Ally X 計測（無人計測ではコンボは 30 秒に 2〜9 回）。

## 2026-11-07 — Phase 2：敵 AI・仮モーション・1 対多の戦闘

Unity は 6000.5.4f1 のまま。追加パッケージは Animation Rigging 1.4.1（Unity 公式、無料）のみ。6000.6.8f1 はインストールが完了したが、プロジェクトは更新していない。

### 追加
- アセンブリ `CoopDeck.Animation`（`AnimatorParams` / `LocomotionMath` / `CharacterAnimatorDriver`）、`CoopDeck.AI`（`EnemyStateMachine` / `EnemyBrain`）。`CoopDeck.Enemies` に `EnemyDefinition` / `EnemyActor` / `EnemyProjectile` / `WaveSchedule` / `WaveDirector`。`Core` に `ILocomotionSource`、`Combat` に `CombatantRegistry`。
- 敵 2 種：スラッシャー（近接）とシューター（遠距離弾、胸に弱点コア ×1.75）。予兆（腕を振り上げる姿勢＋発光エフェクト）のあとに攻撃し、予兆中に避けられる。頭は Animation Rigging で対象を向く。
- テストシーン北西に敵アリーナ（26×18 m、シアンの枠、遮蔽物 4、出現点 4）。入るとウェーブ開始（4 種類の編成をループごとに強化）。NavMesh を生成ツールで焼く。
- 仮モーション：仮リグ、生成クリップ 16 本、`AC_PlaceholderHumanoid`（8 方向 Blend Tree・落下・死亡・上半身レイヤー）、シューター用 Override。プレイヤーの全身は影だけ描画。一人称の待機時の呼吸。
- HUD：被弾の周辺減光と方向矢印、ダウン表示、ウェーブ表示（`ArenaHud`）、弱点ヒットのオレンジ表示。
- 無人計測：`-perfarena`（アリーナで敵と戦いながら計測。自動操縦が最寄りの敵を狙う）。JSON に到達ウェーブと最大同時敵数。

### 変更
- 弱点（Weakpoint）部位へのヒットに `DamageFlags.Critical` を付与。
- 吸引（`ForceReceiver`）は NavMeshAgent を持つ対象を `agent.Move` で動かす（NavMesh の外へ出ない）。
- `PooledFx.ReleaseNow`（敵弾の命中時に即座にプールへ戻す）。

### 修正（実装中に見つけたもの）
- アリーナの出現点から遮蔽物の陰に出た敵が、プレイヤーを視認できず待機したままになる → ウェーブで出現した敵は最初から追跡する（`EnemyActor.AlertOnSpawn`）。

### 検証
- コンパイル：エラー・警告 0（起動中の Editor）。Windows Release ビルド：エラー・警告 0。
- EditMode **111 件**・PlayMode **28 件**、全件成功（Editor 内で実行）。追加分：敵の状態遷移 8、ウェーブ進行 5、移動ブレンド・弱点・対象探索 5、生成資産（Animator・マスク・クリップ・敵 Prefab・プレイヤーの体・シーンの NavMesh／アリーナ）10、近接の追跡と命中・予兆中の回避・敵弾の命中・横移動での回避・ウェーブ進行・NavMesh 上の吸引・入力遅延 7。
- 入力遅延：仮想ゲームパッドの RT が処理されたフレームで発砲・ヒット（追加 0 フレーム）。
- Editor の Play モード：Wave 1 の 3 体がすべて追跡・予兆・攻撃（修正後）、Wave 2 でシューターが約 11 m を保って弾を撃つ、敵の攻撃でプレイヤーが倒れ 2.5 秒後に復活、アリーナを出るとウェーブが一時停止。Console のエラー・警告 0。スクリーンショットで敵の外見・予兆姿勢・HUD を確認。
- ROG Ally X（Handheld、900p FSR）：`-perfarena` で 60fps 上限 平均 59.9 / 1% Low 58.2・59.8、上限なし 平均 91.2 / 1% Low 70.3（最大 6 体同時、ウェーブ 3〜4 まで）。

### 未確認・残課題
- 人の手でのプレイ確認（敵の強さ・予兆の見やすさ・操作感）。移動・視点・射撃の調整値は Phase 1 のまま（調整項目は `DEVELOPMENT_PLAN.md` Phase 2 に記録）。
- マズルフラッシュ・弾道・着弾エフェクトの調整は未着手。
- 本番モデル・Humanoid リグ、左手 IK・足 IK、スキル／ダウン／蘇生のモーション、画面表示までの入力遅延の計測。
- 最初の計測 1 回だけ 1% Low 46 fps が出た（旧自動操縦、以後再現せず、原因未特定）。
- ウェーブの終わり（報酬・ステージ進行）はなく、ループし続ける。

## 2026-11-07 — Phase 3：ゲージ制スキルデッキと FPS 戦闘の統合

ユーザー指示により Phase 2 より先に実装。Unity は 6000.5.4f1 のまま（アップグレードしていない）。

### 追加
- アセンブリ `CoopDeck.Cards`（`CardDefinition` / `SkillEffectSpec` / `DeckDefinition` / `ElementStyle`）、`CoopDeck.Deck`（`CardRuntime` / `DeckState` / `SeededRandom`）、`CoopDeck.Items`（`GaugeItemDefinition` / `GaugeItemInventory`）、`CoopDeck.Skills`（`SkillCaster` / `DeckController`）。
- 戦闘：`CombatStatus` + `StatusEffectsModel`（シールド、攻撃力強化、被ダメージ増加、継続ダメージ、最後の属性）、`ForceReceiver`（吸引）、`DamageResult.Absorbed`、`ICombatActor.Status`。攻撃力強化は通常射撃にも乗る。
- カード 12 種（資料指定の 8 種＋オリジナル 4 種：フロストノヴァ、サンダーチャージ、焼夷グレネード、グラビティスパイク）と 12 枚の初期デッキ、チャージポーション（選択カード +40）と共鳴結晶（手札全体 +25）。数値は `CARD_SYSTEM.md` §12。
- 生成ツール：カードアイコン 12 枚（`CardIconPainter`、外部素材なし）、属性別スキル VFX 21 個（ビーム・着弾・範囲・オーラ・着弾予告）、カード／デッキ／アイテムのアセット。
- 入力：カード選択（1/2/3、十字←→）。アイテム切替のパッド割り当てを十字→から十字↓へ変更。既存の入力アセットに不足アクションがあれば生成ツールが自動で作り直す。
- HUD：`DeckHud`（手札 3 枚・アイコン・属性・ゲージ・消費・残り回数・CD・Q/E/F と LB/RB/Y・山札／捨て札・アイテム欄・選択枠・破棄／ドロー演出・通知）。HP 欄にシールドと攻撃強化の残り時間。F1 で操作ヒント切替。
- テストシーン：的の密集エリア（5 体）、ダメージ床、プレイヤーの仮リスポーン（`PlayerRespawn`）。
- 無人計測の自動操縦がスキルも 2.2 秒ごとに使用。

### 修正（実装中に見つけたもの）
- 長いカード名がキー表示に重なる → 自動縮小。操作ヒントの 1 行目が画面上端で切れる → 位置を調整。

### 検証
- コンパイル：エラー・警告 0（起動中の Editor で `unity recompile`）。
- EditMode **83 件**・PlayMode **21 件**、全件成功（Editor 内で実行）。デッキ規則、アイテム、状態効果、生成データ、入力割り当て、Q/E/F・LB/RB/Y の仮想デバイス入力、射撃との並行使用、デッキ一巡と再シャッフル、各スキル効果を含む。
- Editor の Play モードで自動操縦がスキルと射撃を並行使用し、手札・山札・捨て札が循環（合計 12 枚を維持）することを確認。
- Windows ビルド（102.9 MB、エラー・警告 0）を Ally X で実行：Handheld・60fps 上限で平均 59.9 / 1% Low 59.9 fps。1080p のスクリーンショットで HUD の可読性と照準が隠れないことを確認。

### 未確認・残課題
- 人の手でのプレイ確認（カードの手触り・バランス・HUD の見やすさ）。
- ステージクリア報酬（3 枚から 1 枚）、カード強化表、パッシブカードの効果は未実装。
- 属性コンボの判定は Phase 4。
- 敵が攻撃してこないため、シールドと回復はダメージ床でのみ確認できる。

## 2026-11-07 — Phase 1：環境構築と FPS 最小実装

### 環境
- Unity CLI 1.0.0-beta.17 を導入（`irm https://unity.com/install.ps1 | iex`）。Unity アカウントでサインインし、Unity Personal ライセンスを有効化。
- `unity projects create` で Unity 6 URP テンプレートから `C:\dev\CoopDeckFPS` を作成（Unity 6000.5.4f1、Unity Cloud 連携なし）。
  - 主なパッケージ：URP 17.5.0、Input System 1.19.0、AI Navigation 2.0.13、Test Framework 1.7.0、uGUI 2.5.0、Pipeline 0.10.0-exp.1（Unity CLI から Editor を操作）、Cinemachine 3.1.7（CLI が自動追加。現状未使用）。
  - Animation Rigging・Netcode for GameObjects は使い始めるフェーズ（2 / 2.5）で追加する。
- Unity 6000.6.8f1 はダウンロード中（回線が遅く数時間）。完了後に Git チェックポイントを作ってからアップグレードする。
- 開発機は ROG Ally X（Ryzen Z1 Extreme、Radeon 内蔵、1920×1080 120 Hz）。

### 追加
- アセンブリ：`CoopDeck.Core / Combat / Input / Effects / Weapons / Player / Enemies / UI / Editor` と EditMode・PlayMode テスト（構成は ARCHITECTURE.md）。
- 入力：`GameInputActions`（キーボード＆マウス／ゲームパッドの既定バインド）、`GameControls.inputactions`、`PlayerInputReader`（マウス感度、スティックの応答カーブと加速、一時停止とカーソルロック、再開クリックで誤射しない）。
- 移動：`FpsMotor`（加減速、スプリント、2 チャージの回避ダッシュ、コヨーテタイム、ジャンプ先行入力、空中制御、接地スナップ、壁ずり、天井判定）、`FpsLook`（リコイルの自動復帰、スプリント FOV）。
- 射撃：`WeaponDefinition`（仮アサルトライフル `WD_AssaultRifle`）、`WeaponState`、`HitscanWeapon`（拡散・ブルーム・距離減衰・部位倍率・リコイル・自動リロード）、`WeaponFx`。
- 戦闘：`DamageInfo`、`Health`/`HealthModel`、`HitZone`（頭・胴、ヘッドショット 2 倍）、`CombatEvents`、`ICombatActor`。
- エフェクト：`FxPool`/`PooledFx`（性能ティアで粒子数を調整）、`TracerFx`、`LightFlash`、シェーダー `CoopDeck/FX/Additive Soft`、Prefab 5 種（マズル・弾道・着弾×2・撃破）。
- 的：`TargetDummy`（撃破 → 3 秒後に復活）、`PatrolMover`（移動標的）、`HitFlash`、頭上 HP バー。
- HUD：拡散連動の照準、ヒットマーカー、ダメージ数値、弾数・リロード、HP、ダッシュ残数、スキル枠 3 つ（Phase 3 の受け口）、一時停止表示、操作ヒント、F3 性能表示。
- 一人称の仮ビュー：ブロック形状のライフルと手、`ViewModelAnimator`（ボブ・スウェイ・キック・リロード・スプリント・着地）、ViewModel 専用オーバーレイカメラ。
- 性能ティア：`PerformanceBootstrap`（Ally X を自動判定、`-tier` / `-uncapped`）、URP アセット `URP_High` / `URP_Handheld`（0.833 倍＋FSR、影縮小、SSAO 無効）。
- 無人計測：`AutoPerfRunner`（`-perfrun`）と `AutopilotCommandSource`。
- エディターツール：`CoopDeck > Setup > Run Phase 1 Bootstrap`（レイヤー・画質・Player Settings・アセット・Prefab・シーンを生成）、`CoopDeck > Build > Windows`。
- シーン `Sandbox_Phase1`：射撃場（静止・移動・高所の的、距離マーカー）、移動コース（階段・20°スロープ・高さ違いのジャンプ台・低い天井のトンネル・柱）、坂テスト（15/30/45° と登れない 55°）。
- ドキュメント 7 点、README、CLAUDE.md、`.gitignore`（Library 等と `.mcp.json` を除外）、`.gitattributes`（Unity YAML、Git LFS 対象）。

### 修正（実装中に見つけたもの）
- 連射：トリガーを押したままリロード／待機すると待ち時間が貯まり、次に数発まとめて出る問題を修正。端数の持ち越しは次弾待ちの間だけにした（テスト追加）。
- 画質：テンプレートの「Mobile」レベルが Windows ビルドから除外されていたため、Ally X で Handheld に切り替わらなかった。両レベルを Windows に含めるよう生成ツールを修正。
- 一人称の銃が低すぎて画面外に出ていたため位置を調整。弾数表示の「0」と「/ 30」の重なりを修正。

### 検証
- バッチモードのコンパイル：エラー・警告 0（`CoopDeck.*`）。
- EditMode 38 件・PlayMode 9 件、全件成功（移動速度、走行、停止、ジャンプ高、段差、壁ずり、回避、部位ダメージ、リコイル、自動リロード、コマンド駆動のキャラクター統合、入力定義、ティア判定）。
- Windows ビルド成功（約 103 MB）。Ally X 実機で起動し、自動操縦で射撃・撃破・リロードが動作することをスクリーンショットで確認。
- Ally X 性能：Handheld・60fps 上限で平均 59.9 / 1% Low 58.1 fps。上限なしで平均 96.9 fps（DEVELOPMENT_PLAN.md の記録表）。
- 起動中の Editor（`unity open`）を Unity CLI ＋ Pipeline で操作：`Sandbox_Phase1` を開いて Play、フレーム進行を確認、`script eval` で自動操縦に切り替えて射撃（的 4 体にダメージ）、Game ビューをキャプチャ、コンソールのエラー・警告 0 件。

- ユーザーが Editor で `Sandbox_Phase1` をプレイし、操作感 OK（2026-11-07）。
- GitHub（`tono3679-creator/CoopDeckFPS`）へ push。LFS オブジェクト 2 件を含む。

### 未確認・残課題
- 6000.6.8f1 へのアップグレード（ダウンロード待ち）。
- 操作感の確認をデバイス別（キーボード＆マウス／Ally X 内蔵ゲームパッド）に記録すること（Phase 2 の調整時）。
- 上限なし計測で最悪フレーム 17〜25 ms のスパイクが散発（Profiler で原因調査予定）。
