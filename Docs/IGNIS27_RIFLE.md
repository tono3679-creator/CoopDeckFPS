# IGNIS27_RIFLE — 炎キツネ専用アサルトライフル「IGNIS-27 ブレイズテイル」

2026-11-08 ユーザー提供の設計データ `Docs/Design/IGNIS27_Blazetail_WeaponData.json`（schema 1、`status: proposal_requires_playtest`）を、このプロジェクトの武器形式に変換して実装した。数値は JSON の初期案のまま（調整は §5 の比較を見てユーザーが判断する）。変換の方針は VOLT-9 サンダーホップ（`THUNDERHOP_SMG.md` §2）と同じ。

| 項目 | 場所 |
|---|---|
| 武器データ | `ScriptableObjects/Weapons/WD_Ignis27Blazetail.asset`（生成：`Editor/IgnisAssetFactory`。数値は作成時だけ書き込み、以降は Inspector の調整を保持） |
| エフェクト | `Prefabs/VFX/Ignis/FX_Ignis_*`（マズルの炎・弾道・着弾 2・火種の残り火・起爆・起爆の飛び火）。毎回作り直す |
| 火種刻印のルール | `Weapons/ChargeBurst.cs`（VOLT-9 の帯電連射と同じ `ChargeBurstSpec` / `ChargeBurstModel` に「範囲（Splash）」の型と継続ダメージを追加） |
| 発射・起爆 | `Weapons/HitscanWeapon.cs`（`ReleaseChargeBurst` の Splash 分岐） |
| 初期武器 | `CD_FoxGunner.startingWeapon` → `PlayerCharacterSwap.Apply` が `HitscanWeapon.SetDefinition`。キャラクター未選択・不明な id のときもキツネの初期武器を当てる |
| HUD | VOLT-9 と同じ武器パネルのゲージ（`WeaponPanel` は能力の名前・属性・必要数を読むので、そのまま「火種刻印」・炎色・6 分割になる） |

キツネの銃のモデル（Fire Rifle）・Prefab・射撃アニメーションはそのまま。リロードのクリップ（1.7 秒）は既存の仕組みで 2.1 秒に引き伸ばされる。これまでの武器データ `WD_AssaultRifle`（プロトAR）は変更していない（他で参照されたときのために残す）。

---

## 1. 実装した仕組み

- **射撃**：既存の `HitscanWeapon` と `WeaponState`（フルオート、弾倉、自動リロード、ブルーム、距離減衰の終点、反動の戻り）。新しい武器クラスは作っていない。
- **火種刻印（`ChargeBurstModel`、VOLT-9 と共通）**：弾が当たるたびに対象ごとにスタック +1。別の敵に当てる、または 2.25 秒以上同じ敵に当てないと 0 に戻る（外れ弾ではリセットしない）。6 で起爆。同じ敵への再起爆は 4 秒後（その間に 6 に達したら満タンのまま待ち、待ち明けの最初の命中で起爆）。撃破した弾ではスタックを捨てる。
- **起爆（新しい型 `ChargeBurstShape.Splash`）**：
  - 主対象に 22。
  - 当たった点から半径 2.5 m 以内（コライダーの最寄り点で判定）の敵を近い順に最大 4 体、各 12。
  - ダメージはすべて `Health.ApplyDamage`（既存のダメージ処理）、`DamageKind.Weapon` ＋ `DamageFlags.WeaponProc`。攻撃力強化（スキル）は弾と同じく乗る。
- **炎の状態異常**：起爆した全対象に
  - 既存の燃焼（`CombatStatus.AddDamageOverTime`、炎）：毎秒 6 × 4 秒（0.5 秒ごとに 3、計 24）。重ね掛けの上限 6 などは既存のまま。
  - 炎の属性ヒット（`CombatEvents.RaiseElementHit`）：既存の `ComboSystem` / `ElementAuraModel` が炎を 4 秒付ける。雷が付いていればオーバーロード、重力なら灼熱特異点。協力 ×1.3・**同じコンボの再発動制限 3 秒**も既存のまま。
- **燃焼の強さ**：JSON は「既存の燃焼を使う」とだけあり数値が無いので、既存の炎カードで最も弱い「火球」の燃焼（毎秒 6）を JSON の 4 秒で使った（ユーザー確認済み）。

### 制約の守り方

| 制約（JSON `rules`・依頼） | 方法 | 確認 |
|---|---|---|
| 通常射撃・火種刻印でスキルゲージを消費しない | 武器はデッキを参照しない（既存どおり）。起爆も同じ | PlayMode：射撃と起爆の後も手札 3 枚のゲージが満タン |
| 起爆の追加ヒットは刻印を貯めない | スタックは弾の命中でだけ加算。起爆・飛び火（`WeaponProc`）、燃焼（`DamageKind.Status`）、コンボのダメージは弾ではない | PlayMode：起爆直後のスタック 0、刻印は主対象に残り飛び火先に移らない。飛び火を受けた敵にも直撃 6 発が要る |
| 無限連鎖の防止 | 起爆は弾からしか始まらない。飛び火は最大 4 体・1 回の起爆で同じ敵は 1 回・同じ敵の再起爆 4 秒。燃焼・コンボは新しい起爆を生まない | PlayMode：起爆 1 回の `WeaponProc` は主対象＋飛び火の数だけ。放置しても増えない |
| 既存の燃焼を使い、二重の継続ダメージを作らない | `CombatStatus` の既存の継続ダメージに 1 件足すだけ（武器側にタイマーやダメージ処理は無い） | PlayMode：燃焼 4 秒で 24、終わると消える |
| オーバーロード・灼熱特異点は既存のコンボ処理で | 属性ヒットを `ComboSystem` に渡すだけ（実際の `CB_Overload` / `CB_BurningSingularity`） | PlayMode：雷＋起爆＝オーバーロード（協力）、重力＋起爆＝灼熱特異点、3 秒以内の 2 回目のオーバーロードは発動しない |
| 既存の武器性能を上書きしない | 新しいアセット。追加した項目（`shape` ほか）はすべて既定値で従来どおり（Chain・飛び火 0・継続ダメージ 0）。VOLT-9 の帯電連射の動作・見た目も不変 | EditMode：プロトAR・VOLT-9 の数値、既定の Spec。全テスト成功 |

## 2. JSON → 武器データの対応

| JSON | 値 | 武器データ | 備考 |
|---|---|---|---|
| `weaponId` / `displayName.ja` | ignis27_blazetail / IGNIS-27 ブレイズテイル | `weaponId` = `weapon.ignis27_blazetail`、`displayName` | |
| `description.ja`、`uiCategoryLabelJa` | | `description`、`categoryLabel` = アサルトライフル | |
| `uiAttributeLabelJa` 炎 | | `element` = Fire | 弾の属性は表示とダメージ数値の色だけ（属性ヒットは起爆だけ） |
| `fireMode` / `hitModel` | full_auto / hitscan | `automatic` / `HitscanWeapon`・`pelletsPerShot` 1 | |
| `baseDamagePerBullet` | 18 | `damage` 18 | |
| `fireRateRpm` | 480 | `roundsPerMinute` 480 | 1 秒に 8 発 |
| `magazineSize` / `reloadSeconds` | 28 / 2.10 | `magazineSize` 28 / `reloadTime` 2.1 | |
| `weakPointDamageMultiplier` | 1.65 | `headshotMultiplier` 1.65 | 敵側の弱点倍率はそのまま掛かる |
| `rangeMeters` | 85 | `range` 85 | |
| `damageFalloff` | 24 / 60 / 0.70 | `falloffStart` 24 / `falloffEnd` 60 / `minDamageFactor` 0.7 | 60〜85 m は 0.7 倍 |
| `accuracy.hipfireSpreadDegrees` | 1.75 → **0** | `baseSpread` 0、`spreadPerShot` 0、`maxBloom` 0 | **ユーザー決定（2026-11-08）：ADS は実装しない。腰撃ちの拡散は 0、基本は腰撃ちで戦う。** 弾は照準の中心へまっすぐ飛び、狙いのずれは反動だけになる |
| `accuracy.adsSpreadDegrees` | 0.40 | — | ADS が無いので使わない |
| `accuracy.movingSpreadMultiplier` | 1.40 | `movingSpread` 0 | 腰撃ちの拡散 0 の 1.4 倍＝ 0 |
| `recoil.verticalDegreesPerShot` / `horizontalMaxDegreesPerShot` | 1.05 / 0.35 | `recoilPitch` 1.05 / `recoilYaw` 0.35 | |
| `recoil.recoverySeconds` | 0.20 | `recoilRecoverySeconds` 0.2 | |
| （JSON に無い） | | `airborneSpread` 1.4、`firstShotRecoilMultiplier` 1.3 | 同じアサルトライフルのプロトAR の値（空中の拡散は腰撃ちの拡散とは別の仕組みなので残した） |
| `uniqueAbility`（火種刻印） | 6 / 2.25 / 1 / 22 / 2.5 / 4 / 12 / fire / 4.0 / 4.0 | `chargeBurst`：`requiredStacks` 6、`maxSecondsBetweenHits` 2.25、`stacksPerHit` 1、`primaryDamage` 22、`shape` Splash、`splashRadius` 2.5、`splashTargets` 4、`splashDamage` 12、`element` Fire、`statusDuration` 4（＋`statusDamagePerSecond` 6）、`perTargetCooldown` 4 | `resetStacksOnTargetSwitch` は既存の動作 |
| `rules.*` | | §1「制約の守り方」 | すべて満たす |
| `visuals` | | §3 | |
| `ownerCharacterId` / `startingWeapon` | fire_fox / true | `CD_FoxGunner.startingWeapon` | 既存のキャラクター id `FoxGunner` |

### 変換しなかった項目（ゲームに仕組みが無い）

| JSON | 理由・扱い |
|---|---|
| `startingReserveAmmo` 168 | 予備弾の仕組みが無い（リロードは無制限）。HUD は「予備 ∞」 |
| `equipSeconds` 0.40 | 武器の持ち替えが無い |
| `adsSpreadDegrees`・`adsEnterSeconds`・`adsMovementSpeedMultiplier` | ADS は実装しない（ユーザー決定） |
| `equippedMovementSpeedMultiplier` 0.97 | 武器ごとの移動速度の仕組みが無い |
| `audioCues` | 武器の効果音の仕組みが未実装 |
| `visuals.reloadVfxStyle`（残り火とエネルギーセルの脈動） | 銃のモデルの部分発光の仕組みが必要。未実装 |
| `rules.multiplayerDamageAuthority` | ネットワーク未実装。ダメージは既存の `Health.ApplyDamage` だけを通るので、権限のある側で処理する形に載せられる |

## 3. エフェクトと HUD

- マズル：白熱の芯＋前方へ 4 本の黄橙の炎の舌＋橙の点光（Handheld では光を省略）。0.1 秒。
- 弾道：金→橙の細い線。着弾：小さな残り火の火花（重力で落ちる。環境は暗め、敵は明るめ）。
- 火種の蓄積中（50% 以上）：2 発に 1 回、銃口から熱で昇る残り火（武器上のフィードバック、`chargeFx`）。
- 起爆：白い閃光・炎の玉（puff 10〜14）・飛び散って落ちる光る破片。高品質だけ橙の点光。飛び火先は同じものを小さく暗く。雷のような落雷（`arcFx`）は無し。
- 燃焼中の炎の見た目は既存の `ElementAuraPresenter`（`FX_Burning`）がそのまま出す。
- HUD：武器パネルの上に炎アイコン・「火種刻印」・6 分割のゲージ（`WeaponPanel` は能力の名前・属性・必要数・進み・再発動待ちを読むだけなので変更なし）。起爆のダメージ数値は橙。

## 4. 確認したこと

- EditMode `IgnisTests` 5 件：JSON の値の変換、キツネ＝IGNIS-27・ウサギ＝VOLT-9・プロトAR の数値は未変更、火種刻印のルール（6 発・2.25 秒・対象切替・同じ敵 4 秒）、既定の Spec が Chain・継続ダメージ無し（古いデータの動作が変わらない）。
- PlayMode `IgnisPlayModeTests` 6 件：
  - 6 発目で起爆。主対象 6×18＋22、2.5 m 以内の 2 体に 12、範囲外 0。炎の属性ヒット 3 件と炎の付与、3 体とも燃焼、燃焼 4 秒で 24 で終わる。スキルゲージ不変。
  - 範囲内に 6 体いても飛び火は近い 4 体だけ。
  - 起爆・飛び火・燃焼でスタックが貯まらない。刻印は主対象に残る。放置しても起爆・追加ダメージは起きない。飛び火を受けた敵にも直撃 6 発が要る。
  - 実際のコンボデータで：味方の雷＋起爆＝オーバーロード（協力）、3 秒以内に飛び火で炎が届いてもオーバーロードは再発動しない、味方の重力＋起爆＝灼熱特異点。
  - フルオート 8 発/秒・28 発・自動リロード 2.1 秒。
  - VOLT-9・プロトAR との比較（§5）。
- 既存テストの修正：`ThunderhopTests`（キツネの初期武器が IGNIS-27 であることの確認に変更、ゲームシステム制作セッション了承済み）、`FoxBodyPlayModeTests`（リロードのクリップ速度を「1.7 秒の武器なら 1.0」から「装備中の武器のリロード時間に合わせた値」に変更。キツネのリロードは 2.1 秒になるので 0.81 倍速）。
- 全テスト：EditMode 301 件、PlayMode 90 件が成功。
- Editor の Play モード（キツネ、選択なし）：武器は IGNIS-27、HUD に「火種刻印」ゲージ。6 連続命中で起爆し主対象 22＋近くの 2 体に 12（計 46）、主対象が燃える。NPC ヴォルトの雷が付いていた的では、起爆の炎でオーバーロードが起きた（既存のコンボ処理）。画像 `Screenshots/Ignis_*.png`。

## 5. VOLT-9・プロトAR との比較

PlayMode テスト（`Balance_AgainstVolt9AndTheOldRifle`）：10 秒間撃ち続け（リロード込み、拡散なし＝全弾命中、胴）。燃焼・起爆・雷撃を含む。

| 距離 | IGNIS-27 | VOLT-9 | プロトAR（キツネの旧武器） |
|---|---|---|---|
| 8 m | 109.7 DPS（56 発、起爆 2 回、燃焼 45） | **120.8**（100 発、雷撃 6 回） | 118.8（66 発） |
| 17 m | **109.7** | 99.7 | 118.8 |
| 30 m | **104.9** | 76.8 | 118.8 |
| 50 m | **88.1** | 76.8 | 114.6 |
| 70 m | **79.5** | 0（射程 55 m の外） | 106.0 |
| 12 m の 4 体の集団（合計） | **130.4** | 122.7 | 118.8 |

| 撃破時間 | IGNIS-27 | VOLT-9 | プロトAR |
|---|---|---|---|
| シューター（90）8 m / 30 m | 0.53 / 0.65 秒 | 0.57 / 0.77 秒 | 0.43 / 0.43 秒 |
| スラッシャー（140）8 m / 30 m | 0.78 / 0.78 秒 | 0.77 / 1.23 秒 | 0.73 / 0.73 秒 |

操作性：

| | IGNIS-27 | VOLT-9 | プロトAR |
|---|---|---|---|
| 1 発 / 連射 | 18（頭 29.7）/ 480 rpm | 11（頭 16.5）/ 900 rpm | 18（頭 36）/ 600 rpm |
| 弾倉 / 撃ち切り / リロード | 28 / 3.4 秒 / 2.1 秒 | 40 / 2.6 秒 / 1.65 秒 | 30 / 2.9 秒 / 1.7 秒 |
| 減衰 | 24→60 m で 0.7 倍、射程 85 m | 10→24 m で 0.6 倍、射程 55 m | 40→150 m で 0.6 倍、射程 150 m |
| 拡散 | **0**（腰撃ちの拡散 0、ADS なし。2026-11-08 ユーザー決定） | **0**（同じくユーザー決定 2026-11-08。以前は 0.65° → 約 1 秒で 2.4°） | 0.2°（ほぼ広がらない） |
| 命中率（拡散あり・反動なし、0.6×2 m の的、撃ちっぱなし / 6 発ずつ 0.4 秒休み） | 17 m・30 m ともに 100%（拡散 0 にする前は 17 m：71% / 89%、30 m：38% / 44%） | 拡散 0 で全弾が照準の中心へ（拡散 0 にする前の値は 17 m：60% / 96%、30 m：38% / 63%） | どちらも 100% |
| 反動の残り（1 発の 30% が戻らない）| 0.32°/発 → 毎秒 2.5°、28 発で約 9° | 0.15°/発 → 毎秒 2.25°、40 発で約 6° | 0.17°/発 → 毎秒 1.65°、30 発で約 5° |
| 固有能力 | 火種刻印：6 発で 22＋2.5 m の 4 体に 12＋燃焼 24、炎（オーバーロード・灼熱特異点の起点） | 帯電連射：12 発で 18＋6 m の 1 体に 12、雷 | なし |

分かったこと：
- **役割は設計どおり**：至近（〜10 m）は VOLT-9 が約 10% 上。13 m あたりから IGNIS-27 が上になり、30 m で +37%、55〜85 m は IGNIS-27 だけが届く。敵の集団には火種刻印の飛び火と燃焼で 3 武器中いちばん強い。
- **火種刻印の頻度**：同じ敵には 4 秒に 1 回なので、単体相手では 10 秒で 2 回（リロードの間も含む）。単体 DPS への上乗せは約 +10（22＋燃焼 24 の 2 回分）。
- **プロトAR より単体火力が低い**：キツネの持ち替えで、単体相手の DPS は近距離で −8%、30 m で −12%、70 m で −25% 下がる（集団相手は +10%）。
- **精度は反動だけで決まる**：拡散 0 なので弾は照準の中心へ飛ぶ。反動の残り（1 発 0.32°）は 6 発で約 1.6°＋戻りきらない分 約 0.1° → 30 m で胴の中心から約 0.9 m 上。押さえなくても 6 発の区切り撃ちは 30 m の人型の胴〜頭に収まる計算で、撃ちっぱなしでは 28 発で約 9° 上がるので押さえ込みが要る（計算。人の手での確認待ち）。
- **（拡散 0 にする前）** ADS が無いので JSON の腰撃ち拡散（1.75°）がそのまま撃ち続けたときの精度になり、30 m では 6 発ずつ区切っても 44% だった。ユーザー決定で解消。

調整案（未適用。プレイして判断するための候補）：
1. 反動：縦 1.05 → 0.8（VOLT-9 と同じ考え方）。撃ちっぱなしの上がり方が 28 発で約 9° → 約 7° になる。
2. 単体火力をプロトAR 並みにするなら：1 発 18 → 20、または 480 → 540 rpm。
