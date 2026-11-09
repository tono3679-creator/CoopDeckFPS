# THUNDERHOP_SMG — 雷ウサギ専用 SMG「VOLT-9 サンダーホップ」

2026-11-08 ユーザー提供の設計データ `ThunderHop_SMG_WeaponData.json`（schema 1、`status: proposal_requires_playtest`）を、このプロジェクトの武器形式に変換して実装した。
数値は JSON の初期案のまま。ただし 1 発のダメージと縦の反動は、§5 の比較を見てユーザーが調整案を承認した値（8 → 11、0.8 → 0.5）。

| 項目 | 場所 |
|---|---|
| 武器データ | `ScriptableObjects/Weapons/WD_Volt9Thunderhop.asset`（生成：`Editor/ThunderhopAssetFactory`。数値は作成時だけ書き込み、以降は Inspector の調整を保持） |
| エフェクト | `Prefabs/VFX/Thunderhop/FX_Thunderhop_*`（マズル・弾道・着弾 2・帯電中の火花）と、雷撃は共通のスタイライズ雷 `Prefabs/VFX/Lightning/`（`LIGHTNING_VFX.md`）。毎回作り直す |
| 帯電連射のルール | `Weapons/ChargeBurst.cs`（`ChargeBurstSpec` データ、`ChargeBurstModel` 純 C#） |
| 発射・雷撃 | `Weapons/HitscanWeapon.cs`（`ChargeBurst`、`ChargeBurstTriggered`） |
| 初期武器の登録 | `CharacterDefinition.startingWeapon` → `PlayerCharacterSwap.Apply` が `HitscanWeapon.SetDefinition`（キツネは 2026-11-08 から IGNIS-27 ブレイズテイル、`IGNIS27_RIFLE.md`。未選択・不明な id でもキツネの初期武器が付く） |
| HUD | 武器表示の上に帯電ゲージ（`WeaponPanel`）、雷撃のダメージ数値は雷色（`HudController`） |

---

## 1. 実装した仕組み

- **射撃**：既存の `HitscanWeapon` と `WeaponState`（フルオート、連射の端数持ち越し、弾倉、自動リロード、ブルーム）。武器クラスや戦闘 API は置き換えていない。
- **距離減衰の終点**：`WeaponDefinition.falloffEnd` を追加（0＝従来どおり射程まで）。サンダーホップは 10 m から下がり始め 24 m で 0.6 倍、その先は射程 55 m まで 0.6 倍。
- **反動の戻り**：`WeaponDefinition.recoilRecoverySeconds` を追加（0＝視点の既定）。`IAimProvider.SetRecoilRecovery`（既定は何もしない）で `FpsLook` に渡り、指定秒数で 95% 戻る。キツネは 0 で変化なし。
- **帯電連射（`ChargeBurstModel`）**：弾が当たるたびに対象ごとにスタック +1。別の敵に当てる、または 2 秒以上同じ敵に当てないと 0 に戻る（外れ弾ではリセットしない）。12 で雷撃。対象ごとの再発動待ち 1.25 秒の間に 12 に達したら満タンのまま待ち、待ち明けの最初の命中で放つ。撃破した弾ではスタックを捨てる。
- **雷撃**：主対象に 18、6 m 以内の最寄りの敵 1 体に 12。どちらも `Health.ApplyDamage`（既存のダメージ処理）で、`DamageKind.Weapon` ＋新フラグ `DamageFlags.WeaponProc`。攻撃力強化（スキル）は弾と同じく乗る。
- **属性コンボとの接続**：ダメージの後、対象ごとに `CombatEvents.RaiseElementHit`（雷）。付与・持続 4 秒・相方の消費・協力 ×1.3・**同じコンボの再発動制限 3 秒**はすべて既存の `ComboSystem` / `ElementAuraModel` が扱う（武器側に複製していない）。

### 制約の守り方

| 制約 | 方法 | 確認 |
|---|---|---|
| 銃撃でスキルゲージを消費しない | 武器はデッキを参照しない（既存どおり）。雷撃も同じ | PlayMode：射撃と雷撃の後も手札 3 枚のゲージが満タン |
| 雷撃の連鎖でスタックが再蓄積されない | スタックは `FireOnce` の弾の命中でだけ加算。雷撃・コンボのダメージは弾ではない（`WeaponProc`・`ComboDamage`） | PlayMode：雷撃直後のスタック 0、その後 11 発では雷撃しない |
| 電撃が無限連鎖しない | 連鎖は 1 体（データ）・1 回の雷撃で同じ敵は 1 回・対象ごとの再発動待ち 1.25 秒・雷撃は弾からしか始まらない。コンボのダメージは既存の規則で新しい属性ヒットを出さない | PlayMode：雷撃 1 回のダメージイベントは 2 件だけ、放置しても増えない |
| 属性 4 秒・コンボ再発動 3 秒を維持 | 既存の `ComboSystem` に属性ヒットを渡すだけ | PlayMode：氷を付けた敵に雷撃→超伝導バースト（協力）、3 秒以内の 2 回目は発動しない |
| キツネの武器・スキルを壊さない | 追加項目はすべて既定値で従来の動作（`falloffEnd` 0、`recoilRecoverySeconds` 0、`chargeBurst.enabled` false）。プロトAR・NPC のカービンの数値は未変更 | EditMode：プロトAR の数値と設定、全テスト成功 |

## 2. JSON → 武器データの対応

| JSON | 値 | 武器データ | 備考 |
|---|---|---|---|
| `weaponId` / `displayName.ja` | volt9_thunderhop / VOLT-9 サンダーホップ | `weaponId` = `weapon.volt9_thunderhop`、`displayName` | プロジェクトの id 形式に合わせた |
| `description.ja`、`presentation.uiCategoryLabelJa` | | `description`、`categoryLabel` = サブマシンガン | 新しい表示用の項目 |
| `uiAttributeLabelJa` 雷 | | `element` = Lightning | 弾の属性は従来どおり属性ヒットを出さない（付与は雷撃だけ） |
| `fireMode` full_auto / `hitModel` hitscan | | `automatic` = true / `HitscanWeapon`・`pelletsPerShot` 1 | |
| `baseDamagePerBullet` | 8 | `damage` **11** | JSON の 8 ではキツネの銃より弱かったため、ユーザー承認で調整（§5） |
| `fireRateRpm` | 900 | `roundsPerMinute` 900 | 1 秒に 15 発 |
| `magazineSize` / `reloadSeconds` | 40 / 1.65 | `magazineSize` 40 / `reloadTime` 1.65 | |
| `weakPointDamageMultiplier` | 1.5 | `headshotMultiplier` 1.5 | 頭への倍率。敵側の弱点倍率（シューターのコア ×1.75）はそのまま掛かる |
| `rangeMeters` | 55 | `range` 55 | |
| `damageFalloff` 10 / 24 / 0.6 | | `falloffStart` 10 / `falloffEnd` 24（新）/ `minDamageFactor` 0.6 | |
| `accuracy.hipfireSpreadDegrees` | 2.4 → **0** | `baseSpread` 0、`spreadPerShot` 0、`maxBloom` 0 | **ユーザー決定（2026-11-08）：ADS は実装しない。腰撃ちの拡散は 0、基本は腰撃ちで戦う**（IGNIS-27 と同じ）。弾は照準の中心へまっすぐ飛び、狙いのずれは反動だけ。※最初は ADS の 0.65° を止まって撃ち始めた時の精度、撃ち続けると約 1 秒で 2.4° に広がる形にしていた |
| `accuracy.adsSpreadDegrees` | 0.65 | — | ADS が無いので使わない |
| `accuracy.movingSpreadMultiplier` | 1.25 | `movingSpread` 0 | 腰撃ちの拡散 0 の 1.25 倍＝ 0。空中の拡散 1.0°（`airborneSpread`、JSON に無い）は腰撃ちとは別の仕組みなので残した |
| `recoil.verticalDegreesPerShot` / `horizontalMaxDegreesPerShot` | 0.8 / 0.3 | `recoilPitch` **0.5** / `recoilYaw` 0.3 | 縦はユーザー承認で 0.8 → 0.5（§5）。初弾倍率は 1.0（SMG） |
| `recoil.recoverySeconds` | 0.16 | `recoilRecoverySeconds` 0.16（新） | |
| `uniqueAbility`（帯電連射） | 12 / 2.0 / 1 / 18 / 1 / 6 / 12 / lightning / 1.25 | `chargeBurst`（新、§1） | `statusDurationSeconds` 4 は既存の属性持続（4 秒）を使う |
| `rules.*` | | §1「制約の守り方」 | すべて満たす |
| `visuals.emissiveColor` #5ADCF4 | | エフェクトの色 | 短いシアンの電撃（マズル・弾道・着弾・炸裂・弧） |
| `ownerCharacterId` / `startingWeapon` | lightning_rabbit / true | `CD_LightningRabbit.startingWeapon` | 既存のキャラクター id `LightningRabbit` に対応 |

### 変換しなかった項目（ゲームに仕組みが無い）

| JSON | 理由・扱い |
|---|---|
| `startingReserveAmmo` 200 | 予備弾の仕組みが無い（リロードは無制限、弾の拾得も無い）。入れると弾切れで戦えなくなるため未実装。HUD は「予備 ∞」 |
| `equipSeconds` 0.35 | 武器の持ち替えが無い |
| `adsSpreadDegrees`（ADS として）・`adsEnterSeconds`・`adsMovementSpeedMultiplier` | ADS（覗き込み）が無い。ADS の拡散は通常時の精度に流用 |
| `equippedMovementSpeedMultiplier` 1.05 | 武器ごとの移動速度の仕組みが無い（移動はキャラクター共通の `MovementProfile`） |
| `audioCues` | 武器の効果音の仕組みが未実装（キツネの銃も無音） |
| `visuals.reloadVfxStyle`（電池の脈動） | 銃のモデルの電池部分を光らせる仕組みが必要。未実装 |

## 3. エフェクトと HUD

- マズル：小さな白熱の芯＋前方へ 4 本の火花＋シアンの点光（Handheld では光を省略）。0.09 秒。
- 弾道：白→シアンの細い線。着弾：シアンの火花（環境は暗め、敵は明るめ）。
- 帯電中（50% 以上）：2 発に 1 回、銃口にノイズで揺れる小さな火花（武器上のフィードバック）。
- 雷撃（`LIGHTNING_VFX.md`）：敵の真上から斜めに落ちる太い雷（白い芯＋シアンの発光、枝 2〜4 本）と、星形の閃光・衝撃リング・火花・敵の表面に 0.1〜0.2 秒残る電流。連鎖先へは細めの雷（強さ 0.65）と小さい命中。品質（高・中・低）で枝・残留アーク・リング・点光が減る。
- HUD：武器表示の上に雷アイコン・「帯電連射」・12 分割のゲージ。雷撃で空になるとき残像と発光、再発動待ちで満タンのときは点滅。雷撃のダメージ数値は大きめの雷色で、ヒットマーカーは出さない。

## 4. 確認したこと

- EditMode `ThunderhopTests` 7 件：帯電のルール（12 発・対象切替・2 秒・再発動待ち・無効時）、距離減衰の終点、JSON の値、ウサギの初期武器とキツネの武器が未変更。
- PlayMode `ThunderhopPlayModeTests` 5 件：12 発目で雷撃（主対象 12×8＋18、6 m 以内の 1 体に 12、範囲外は 0、雷の属性ヒット 2 件と属性の付与）、スキルゲージ不変、再蓄積・自己連鎖なし、氷との超伝導バースト（協力）とコンボの 3 秒制限、フルオート 15 発/秒・40 発・自動リロード 1.65 秒、キツネの銃との比較。
- Editor：ウサギを選んで Play し、HUD の武器名・帯電ゲージ、12 発目の雷撃（弧 2 本・雷色の数値 18 / 12）を撮影（`Screenshots/Thunderhop/`、Git 対象外）。
- 全テスト：EditMode 290 件、PlayMode 81 件が成功。

## 5. キツネの銃（プロトAR）との比較

PlayMode テストで 6 秒間撃ち続けた結果（リロード込み、拡散なし＝全弾命中、単体の的）。調整後（1 発 11）と、JSON のまま（1 発 8）：

| 距離 | サンダーホップ | プロトAR | 比 |
|---|---|---|---|
| 8 m | **131.2 DPS**（65 発、雷撃 4 回）／JSON のまま 98.7 | 129.0 DPS（43 発） | 102%（JSON 77%） |
| 17 m | **109.0 DPS**／JSON のまま 82.6 | 129.0 DPS | 84%（JSON 64%） |
| 30 m | **83.5 DPS**／JSON のまま 64.0 | 129.0 DPS | 65%（JSON 50%） |

| | サンダーホップ | プロトAR |
|---|---|---|
| 1 発 | 11（頭 16.5）※JSON 8 | 18（頭 36） |
| 連射 | 900 rpm（15 発/秒） | 600 rpm |
| 弾倉を撃ち切る時間 / リロード | 2.6 秒 / 1.65 秒 | 2.9 秒 / 1.7 秒 |
| 減衰 | 10→24 m で 0.6 倍 | 40→150 m で 0.6 倍 |
| 拡散 | **0**（腰撃ち、2026-11-08 ユーザー決定。以前は 0.65°→撃ち続けて 2.4°） | 0.2°（ほぼ広がらない） |
| 反動（縦/発） | 0.5°（戻り 0.16 秒）※JSON 0.8° | 0.55°（戻り 約 0.3 秒） |
| 追加 | 帯電連射：雷撃 18＋近くの敵 12、雷の属性でコンボの起点・完成 | なし |
| スラッシャー（HP 140）を倒す時間 8 m / 20 m | 約 0.73 秒 / 約 1.0 秒（JSON のまま 約 1.0 / 1.4 秒） | 約 0.7 秒 / 約 0.7 秒 |

- JSON の値のままでは、どの距離でもキツネの銃より弱かった（近距離でも約 77%）。2026-11-08 にユーザーが調整案を承認：**1 発 8 → 11、縦の反動 0.8 → 0.5**。
- 反動（JSON の 0.8°）：撃ち続けると 1 発あたり 0.24° が戻らずに残り（40 発で約 10°）、Editor の自動射撃では押さえ込まないと 9 m 先の的から照準が外れた。
- 調整後：近距離（10 m まで）でキツネの銃と同じくらい、減衰で 17 m では 84%、30 m では 65%。連鎖先への 12 と雷のコンボ（超伝導バースト・オーバーロード）が上乗せになる。縦の反動は 1 発あたり残る分が 0.15°（40 発で約 6°）に下がった（人の手での確認待ち）。
