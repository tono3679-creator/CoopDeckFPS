# CHANGELOG

形式：日付ごとに「追加 / 変更 / 修正 / 検証」。検証していないものは「検証」に書かない。

## 2026-11-09 — 一人称の構図を前の版に寄せ、弾は銃身からまっすぐ飛ぶ

ユーザー指示「構図はなるべく前のやつにしたい。前の構図で弾道だけ修整できない？」、続けて「玉は曲げない。銃身から直線で飛ぶ」。
- まっすぐな弾道は、画面上では必ず銃口から照準へ向かう。だから銃身の線が照準を通らないと揃わない。
- 前の構図（銃口 6° 下）では、銃身が照準より下を向いていた。
- そこで銃身はカメラと平行のまま、前の構図にいちばん近い置き場所を選び直した。
- 途中で、弾道を銃身に沿って出してから照準へ曲げる案（`TracerPath`）を作って撮影までしたが、この指示で取りやめ、コードは残していない。

### 変更
- キツネの一人称のライフル：
  - グリップ `ViewModelGripInCamera` を (0.215, -0.29, 0.38) → (0.234, -0.255, 0.38) へ。
  - 銃身は平行のまま（`ViewModelHipPitch` 0°・`ViewModelHipYaw` 0°）。
  - 銃は画面上で 35° → 27° の傾きになり、前の構図（22°）に近づいた。
  - 銃口の画面位置は左下原点で (0.63, 0.41)。
- テスト：`FoxBodyPlayModeTests` の銃口の高さの範囲を 0.28〜0.42 → 0.28〜0.45 に広げた（0.414 で上限に近かったため）。
- `FOX_ANIMATION.md` を更新。

### 検証
- 候補 6 通りを `Tune_FirstPersonFox` で撮影し、前の構図の銃の輪郭との重なりで比べた：
  - 同じ高さ：31°、85%。
  - 2 cm 上：27°、83%（採用）。
  - 4.4 cm 上：22°、70%。
  - 1.5 cm 右・2 cm 上：25°、79%（左手が 0.6 cm 届かない）。
  - 直前の版：35°、73%。
  - 比較画像は `Screenshots/FoxFP_StraightLevel_Candidates.png`。
- バッチでブートストラップを実行した。別セッションの演出（第 2 弾）とブルームもここで再生成された。
- EditMode：326/326。
- PlayMode（全件）：122 件中 101 件が通過、4 件が失敗、17 件は手動実行用でスキップ。
  - 失敗の 4 件は、バッチ実行のときだけ落ちる既知のもの（ウサギの光・残像の 3 件と `HudPlayModeTests.KeyLabels_SwitchToTheDeviceThePlayerJustUsed`）。
  - 画面上の銃身と弾道の差：静止 0.0°、連射中 0.8〜0.9°。
  - 手首の曲がり：左 15°・右 37°。
  - 範囲を広げたあと `FoxBodyPlayModeTests` を回し直し、8/8 が通過した。
- Windows ビルド（Ally X、1920×1080 全画面、`-perfrun 8`、自動射撃）：
  - 平均 59.9 fps、1% Low 59.9 fps、GPU 平均 5.7 ms。
  - 画面（`PerfCaptures/fp_straight_level.png`）で、銃身の線が照準へ向かい、弾道がその上を飛ぶことを確認した。
- 未確認：人の手でのプレイ、ウサギの一人称の見え方。

## 2026-11-09 — 射撃・スキルの演出 第 2 弾：もっと派手に・くっきり・地面に

ユーザー指示「もっと派手でいい。エフェクトの解像度教えて、ぼやけてるから解像度あげて。一部のエフェクトが宙に浮いてる」。詳細は `WEAPON_SKILL_VFX.md` §8。

### 追加
- `GroundedFx`（Effects）：地面に立つ作りのエフェクトの印。`SkillVfxFactory` が全カードの範囲エフェクトと予告円に付ける。

### 変更
- `SkillCaster`：範囲エフェクト・予告円・飛翔弾の爆発のうち `GroundedFx` のものは、表示位置だけを狙った点の真下の地面（最大 3 m 下、キャラクターは飛ばす）へ下ろす。ダメージの中心は変えていない。
- 品質「中」（Ally X の既定）：粒子 55% → 80%、エフェクトのライトを点灯（`PerformanceSettings`）。
- ブルーム：強さ 0.55 → 0.8、高品質フィルター。既存の `VP_Sandbox` にもブートストラップのたびに反映（`GameAssetFactory.TuneBloom`）。
- スキル・銃の演出を強化：閃光・火花・ライト・画面揺れ・芯の明るさ、敵への命中の大きさ。炎の温度と明るさ。
- 鮮明さ：炎・煙のノイズを 4 段に（`FbFbm`）、輪郭の柔らかさ 0.65 倍（煙は除く）。
- `EffectsCaptureTests`：1920×1080 で撮影（以前は 1280×720）。

### 修正
- 敵を狙ったときに、火炎旋風・焼夷・メテオ（予告円・爆発）・重力系の地面の層が胸の高さに浮いていた。
- サンダーチャージの発動直後に一人称で見えた縦の白い筋（昇る火花を輪の縁から出すようにした）。

### 検証
- テスト：EditMode 326/326、PlayMode 105/105（エディタ）。
  - 追加：`SkillVfxPlayModeTests.GroundEffects_AimedAtAnEnemysChest_StandOnTheFloorUnderIt`。
  - 更新：`WeaponSkillVfxTests`（`GroundedFx` の有無）、`ComboAssetTests`（品質「中」の値）。
- 1080p で撮影して前回と比べた（`Screenshots/Effects/Pass5_*`、まとめ動画 `VFX_Round2.mp4`）。
- Ally X の Release ビルドで、直前のビルドと同じ条件で比べた。平均 59.6〜59.7 fps（前後同じ）、GPU 5.3〜5.4 → 5.7〜6.7 ms。

## 2026-11-09 — 一人称の弾道を銃身に揃え、カード欄を下中央へ

ユーザー指示「ズレて見える弾道と、カード欄を修正して。カード欄は真ん中に移動」。弾道は銃口から照準の先の着弾点へ飛ぶ。そのため画面上では、銃身の線も照準へ向いていないとずれて見える。直前の腰だめの構え（銃口 6° 下）では、画面上で 20° ずれていた。

### 変更
- キツネの一人称のライフル：銃身をカメラの正面と平行にした（`CharacterPrefabBuilder.ViewModelHipPitch` 6° → 0°、新しい `ViewModelHipYaw` 0°。以前は `ViewModelRifleYaw` の -1° を使っていた）。
  - グリップ `ViewModelGripInCamera` を (0.234, -0.275, 0.38) → (0.215, -0.29, 0.38) へ。
  - 銃口の画面位置は左下原点で (0.62, 0.39)。銃は画面上で 35° の傾きで右下の角へ抜ける。
  - ウサギの SMG は変えていない（`ViewModelRiflePitch` 0°・`ViewModelRifleYaw` -1°）。
- `ViewModelAnimator` の射撃の跳ね上がり：1 発ごとに銃口を 2.4° 上げる回転をやめ（`kickPitch` 0）、後ろ 4.5 cm に加えて上へ 0.6 cm 滑らせる（新しい `kickRise`）。回転させると、撃つたびに銃身の線が弾道からずれるため。ウサギにも同じく効く。
- HUD（`HudLayout`）：
  - カード欄（スキル枠 3 つ）を右下から下中央へ。照準の真下に中央揃え。支援品の枠と山札・捨て札の枚数はその左。
  - 武器欄はスキル枠の上から右下の角へ。
  - 引いたカードの表示と知らせはスキル枠の上へ。
- テスト：
  - PlayMode `FoxBodyPlayModeTests.Tracers_LeaveAlongTheBarrel_OnScreen` を追加。静止中と連射中に、画面上の銃身の線と弾道の線の角度差を測る。
  - EditMode `UiDesignSystemTests.Layout_CardHand_IsCentredAtTheBottom_AndLeavesTheLowerRightToTheRifle` を追加し、アンカーの確認を下中央に変えた。
  - `FoxAnimationCaptureTests.Tune_FirstPersonFox` は銃口の画面位置と、銃身と弾道の角度差も出す。
- `FOX_ANIMATION.md`・`UI_DESIGN.md` §4.1・§5・`ARCHITECTURE.md`（`DeckHud`）を更新。

### 検証
- 候補 6 通りを `Tune_FirstPersonFox` で撮影して比べた。銃口 0° はどれも、画面上の銃身と弾道の差が 0.0°（変更前の構えは 20.1°）。左手が届き、手首の曲がりが最も小さい案を選んだ。
- バッチでブートストラップを実行した。PF_Player の Rifle は回転 0、`kickPitch` 0・`kickRise` 0.006。
- EditMode：326/326。
- PlayMode（全件）：121 件中 99 件が通過、5 件が失敗、17 件は手動実行用でスキップ。
  - 失敗のうち 1 件は新しいテスト。待ち方（`WaitForEndOfFrame` はバッチでは動かない）のせいだった。直したあと、関係する 3 クラス（キツネ・HUD・ウサギの 18 件）を回し直し、新しいテストは通過：銃身と弾道の差は静止 0.0°、連射中 0.8°（視点の反動の分）。
  - キツネの一人称のテストは全部通過。手と目標のずれ 0.0 cm、手首の曲がりは左 12°・右 32°、銃口の画面位置 (0.62, 0.39)。
  - 失敗 4 件：
    - `LightningRabbitPlayModeTests` の光と残像の 3 件。どれも `Renderer.isVisible` を見る。
    - `HudPlayModeTests.KeyLabels_SwitchToTheDeviceThePlayerJustUsed`：キーボード入力のあとも Gamepad のまま。
    - どれもバッチ実行のときだけ落ちる。Game ビューがないので `isVisible` が立たず、入力デバイスの扱いも違う。
    - 別セッション（キツネ武器モデル品質改善）が、このブートストラップ後の状態で Editor 上の PlayMode 全件を回し、4 件とも通った（104/104、同セッションの SMG 差し替えを含む）。EditMode も 326/326。
- Windows ビルド（Ally X、1920×1080 全画面、`-perfrun 8`、自動射撃）：
  - 1% Low 59.9 fps、GPU 平均 5.0 ms。
  - 画面（`PerfCaptures/fp_tracer_fix.png`）で、弾道が銃口から銃身の線の延長上を照準へ飛ぶことを確認した。
  - カード欄は下中央にあり、左手とも重ならない。武器欄は右下の角で、機関部の後ろ側に重なる。
  - 比較画像：`Screenshots/FoxFP_TracerAlign_BeforeAfter.png`。
- 見た目の代償：銃の画面上の傾きが 22° から 35° になり、参考画像より少し斜めに右下の角へ落ちる。
- 未確認：人の手でのプレイ、ウサギの一人称の見え方。

## 2026-11-09 — 雷ウサギの Lightning SMG も同じ方法で作り直し

ユーザー依頼「うさぎも作り直して」。詳細は `WEAPON_VISUAL_QUALITY.md` §7。元は `ArtSource/LightningSMG/Backup/20261109_before_quality/`。

### 追加
- `Editor/HardSurfaceWeapon.cs`：武器のデータテクスチャ・`CoopDeck/Weapon/HardSurfaceToon` のマテリアル（初期ルック、一人称／三人称の輪郭と補助ハイライト）・LOD の切替を共通化。
- `rebuild_weapon_hq.py`：材質マスクの塗装・アクセントの色相範囲を `--paint-hue` / `--accent-hue` で指定（既定は Fire Rifle の赤と金）。

### 変更
- Lightning SMG：高精細から 55k＋1.0 mm ベベル＋面積重み付き法線（LOD0 91,290、LOD1 30,000）、色・法線を焼き直し、表面マップと材質マスク（黄橙の塗装・水色のガラス）を追加、2 つのマテリアルを新シェーダーへ。小さい銃なので塗装のエッジは弱め。ソケットは同じ。
- `FireRifleAssetFactory`・`LightningSMGAssetFactory` は `HardSurfaceWeapon` を使う（Fire Rifle の見た目は不変）。
- テスト：`LightningRabbitAssetTests` の SMG（約 90k、新シェーダー・材質マスク）。`WeaponLookCaptureTests` はキャラクターを選べる（`WEAPON_LOOK_CHARACTER`）。

### 検証
- 撮影（`Compare_Rabbit_*.png`）：一人称・三人称とも角の光・階調・つや、輪郭の途切れなし。
- テスト：EditMode 326/326、PlayMode 104/104（Editor 上）、コンソールのエラー 0。
- Release（Ally X、1080p 全画面、ウサギ）：SMG だけ違うビルドで GPU 4.32 → 3.91 ms（静止）、4.15 → 4.22 ms（射撃中）、平均 59.6 fps で同じ。
- 参考：バッチ実行の PlayMode では `LightningRabbitPlayModeTests` の発光・残像 3 件と `HudPlayModeTests.KeyLabels…` が落ちる（Game ビューが無く `Renderer.isVisible` が立たない／入力デバイスの扱い。Unity 6000.6.8f1 セットアップセッションの報告）。Editor 上ではすべて成功。

## 2026-11-09 — キツネの一人称の銃（Fire Rifle）の画質：ジャギー・平坦さ・安っぽさの原因と改善

詳細は `WEAPON_VISUAL_QUALITY.md`。ユーザー依頼「輪郭のジャギー、平坦な装甲、安っぽさを、スタイライズドな造形のまま高品質な FPS 武器の見た目に。先に原因を特定すること」。元のモデル・Prefab は `ArtSource/FireRifle/Backup/20261109_before_quality/`。

### 原因
- この PC（Handheld）は内部 900p を FSR で拡大し、MSAA なし・FXAA だけ。一人称の輪郭線 0.7 mm は約 1 px で点線のように途切れた。
- ポストプロセス（トーンマップ・ブルーム・SMAA）が本体カメラだけで、オーバーレイで描く銃・腕には掛かっていなかった（白飛び。SMAA にしても銃に効かない構成）。
- `CH_Toon` は 2 階調だけで、塗装・樹脂・金属の質感が無い。30k への削減で高精細の丸い角が折れ目になり、平らな面にムラ。

### 追加
- `ArtSource/Tools/rebuild_weapon_hq.py`（Blender）：高精細から 60k＋折れ目に 1.2 mm ベベル＋面積重み付き法線、LOD1 30k、色・法線に加え表面マップ（凸の角・AO・溝）と材質マスク（白塗装・赤塗装・黒い樹脂・金属）を焼く。
- シェーダー `CoopDeck/Weapon/HardSurfaceToon`（`Art/Weapons/Shaders/WPN_HardSurfaceToon.shader`）：トゥーンの 2 階調＋材質ごとのつや・金属反射・帯状ハイライト・エッジハイライト・AO、一人称用のカメラ基準ハイライト、最小・最大ピクセル幅の輪郭線。
- PlayMode `WeaponLookCaptureTests`（Explicit）：サンドボックスの同じフレームを実際のカメラスタックで描画設定 9 通りに撮影し、描画時間を記録。
- EditMode `FireRifleAssetTests`：LOD・新マテリアル・材質マスク・カメラスタック（最後のカメラでポストプロセス、SMAA High、Handheld 1.0）。

### 変更
- Fire Rifle：`FireRifle.fbx` を LOD0 109,436／LOD1 30,000 三角形に、基本色・法線を新しい UV で焼き直し（元の 8K 拡大アトラスから、配色・寸法・ソケットは同じ）、`T_FireRifle_Surface`・`T_FireRifle_MaterialMask` を追加。2 つのマテリアルを新シェーダーへ。`FireRifleAssetFactory` は LOD の切替（画面の 12%）とデータテクスチャの取り込み。
- `CharacterPrefabBuilder`：PlayerCamera はポストプロセスなし＋SMAA High、ViewModelCamera がスタック全体のポストプロセス。
- `RenderingSetup`：Handheld の Render Scale 0.833＋FSR → 1.0。
- `ARCHITECTURE.md` §5・§6 を更新。

### 検証
- 撮影（`Screenshots/WeaponLook/Compare_*.png`）：一人称で縁の階段・輪郭の途切れ・白飛びが消え、装甲に階調・エッジの光・継ぎ目の AO。三人称（1.1 m・3.6 m）も同様。マズルフラッシュはゲームシステム制作セッションが確認済み（黄〜橙で炎らしい）。
- テスト：EditMode 323/323、PlayMode 103/103、コンソールのエラー 0。`FoxBodyPlayModeTests`（手の持ち位置・銃口の画面位置）を含む。
- Release ビルド（Ally X、1080p 全画面）：Handheld・射撃中 60fps 上限で平均 59.9／1% Low 59.9（前後同じ）、GPU 2.87 → 4.71 ms。静止・上限なしで GPU 3.40 → 4.12 ms。High は GPU 7.33 → 7.27 ms。
- 未確認：人の手でのプレイ、RTX 4070 の PC での見た目と負荷、リロード中・スキル詠唱中の一人称の見え方（撮影は待機姿勢と射撃の静止画）。

## 2026-11-09 — 射撃とスキルの演出を火球の品質基準に（迫力と高品質）

詳細は `WEAPON_SKILL_VFX.md`。ユーザー指示「スキルや射撃時のエフェクト周りを改善してほしい。迫力があって、より高品質に」。

### 追加
- `Editor/Vfx/VfxKit`：火球の生成ヘルパーを一般化した共通部品。火球のグラフを属性の色見本で塗り替える `ToneMat`、雷のグラフの `BoltMat`。
- `Editor/Vfx/WeaponVfxFactory`：IGNIS-27・VOLT-9 のマズル（芯・光暈・星形の筋・炎または電撃・火花・煙）、2 層の弾道（`ShotTracerFx`）、着弾（地形：閃光・火花・炎または電流・破片・土煙・焦げ跡／キャラ：火の玉または体を這う電流）。
- `Editor/Vfx/SkillVfxFactory`：初期カード 12 枚それぞれの専用演出（`Prefabs/Skills/<カード>/`）と、詠唱時の手元の閃光（属性ごと、`Prefabs/Skills/Cast/`）。ブートストラップから呼ぶ。
- シェーダー `Art/VFX/Skills/Shaders/ElementVfx.hlsl`：`SG_FX_ElementShell`（盾の膜）、`SG_FX_ElementSigil`（魔法陣・照準・着弾予告・渦）、`SG_FX_ElementCrystal`（氷の結晶）。
- ランタイム（Effects）：`IBeamFx`、`ShotTracerFx`、`ShapeAnimFx`、`FallingStrikeFx`、`ThrownBeamFx`、`LightningBeamFx`、`ImpactShakeFx`、`SpinFx`、`RepeatArcsFx`。（Skills）`StatusAuraFx`。
- テスト：EditMode `WeaponSkillVfxTests` 8、PlayMode `SkillVfxPlayModeTests` 6、撮影 `EffectsCaptureTests`（Explicit、前後比較）。

### 変更
- `SkillCaster`：
  - ビームは `IBeamFx` で再生する。
  - 味方に付く効果（盾・強化・回復）は味方の子にして、付いて回るようにした。
  - マーキングの効果は `attachImpactFx` に従って敵に付ける。
  - 飛翔弾以外のカードも手元の `castFx` を再生する。
  - 事前生成を全カードに広げた。
- `WeaponFx`：弾道は `IBeamFx` で再生する。`TracerFx` も `IBeamFx` を実装。
- `IgnisAssetFactory`・`ThunderhopAssetFactory`：マズル・弾道・着弾の生成を `WeaponVfxFactory` に置き換え、旧来の生成コードを削除。

### 検証
- 撮影で比べながら調整した（`WEAPON_SKILL_VFX.md` §6）。
  - 地面のリングの大きさ、メテオの閃光、フロストノヴァの白さ、氷の槍の出だし、銃口のもや。
  - マーキングの照準を頭上へ、盾とサンダーチャージの一人称での見え方。
- テスト：EditMode 321/321、PlayMode 103/103、コンソールのエラー 0。
- Ally X の Release ビルド（1080p 全画面・内部 900p）で、平均 59.6〜59.9 fps を維持。GPU 3.2〜3.4 ms（改修前 3.4〜3.5 ms、差は誤差の範囲）。
  - 125〜134 ms の引っかかりは改修前のビルドでも同じように出る（既存の未解決問題）。
  - 最初の計測は、通信テストの小窓設定（960×540）がレジストリに残ったまま測っていたので、全画面で測り直した。
- 未確認：人の手でのプレイ、高品質の PC での見た目、他人から見た銃口の演出。

## 2026-11-09 — キツネの一人称の銃を腰だめの低い構えに

ユーザーが示した参考画像（銃を右下に低く構え、銃口は照準の右下、機関部は右下の角の外へ抜ける）に合わせた。

### 変更
- `CharacterPrefabBuilder.ViewModelGripInCamera`：(0.15, -0.20, 0.39) → (0.234, -0.275, 0.38)。`ViewModelRootPosition` も同じだけ動かした：(0.194, -0.245, 0.29)。
- 新しい定数 `CharacterPrefabBuilder.ViewModelHipPitch` = 6°：キツネのライフルの銃口を下げる角度。ウサギの SMG は `ViewModelRiflePitch`（0°）・`ViewModelRifleYaw`（-1°）のまま。ウサギのキットも作り直したので、SMG の画面上の位置は変わらない。
- 新しい定数 `FoxGunnerAnimationFactory.FoxHipTorsoYaw/Lean` = 44°・38°（キツネ専用の胸の向き。以前は 40°・33°）。遠くなったハンドガードに左手を届かせるため。ウサギは `FirstPersonTorsoYaw/Lean` のまま。
- `ViewModelAnimator` の装填の姿勢：12 cm 下げて銃口を 28° 下げる動きをやめた。代わりに銃を時計回りに 18° 傾け、銃口を 6° 上げる。低い構えのままでは装填中に銃が画面から消えたため。
- テスト `FirstPersonArms_ReachInFromTheLowerCorners_WithStraightWrists`：銃口の画面位置（横 0.55〜0.65、下から 0.28〜0.42）の確認を追加。左手の条件は「画面の内側へ十分入る」から「画面の下端で見える」に変えた。

### 検証
- 候補 20 通りを `Tune_FirstPersonFox` で撮り、参考画像と並べて選んだ（銃口の下げ 0〜12°、前後 30〜52 cm、胸の向き）。
  - 前へ 40 cm 以上出すと、左手が 4.6 cm 以上届かない。
  - 胸を 40°・33° のままにすると、2 cm 届かない。
- 起動中の Editor でブートストラップを実行し、PF_Player とキットを作り直した。PlayMode 97/97、EditMode 313/313、コンソールのエラー 0。
  - ブートストラップ直後は、EditMode の 3 件が「PF_FoxGunner_Body のコントローラーが null」で落ちた。Editor 上でコントローラーを作り直したのに、メモリ上の Prefab が古い参照を持ったままになっていたため。Prefab を再インポートしたら通った（ディスク上のファイルは正しい）。
- 一人称の数値：手と目標のずれは静止 0.0 cm、移動中 0.3 cm。手首の曲がりは左 19°・右 38°。ねじれは最大 38°。銃口の画面位置は (0.59, 0.36)（左下原点）。
- 確認した画像：
  - Sandbox のプレイ画面（HUD 込み）：`Screenshots/FoxFP_HipCarry_Game.png`
  - 変更前・参考・変更後・装填中の比較：`Screenshots/FoxFP_HipCarry_BeforeAfter.png`
  - 連射の動画：`Screenshots/Lightning/GunAim_Fox.mp4`
  - 待機・走り・ダッシュ・装填・詠唱：`Screenshots/FoxAnim_FirstPerson.png`
- 見た目の代償：
  - 銃身が照準の 6° 下を向くので、曳光弾は銃口から銃身の線より上（照準）へ向かって出る。
  - 右下の武器欄とカード欄が機関部の後ろ側に重なる。
- 未検証：Windows ビルドと Ally X での性能（描画するものは変わっていない）、ウサギの見た目の撮影。

## 2026-11-08 — Phase 2.5：2 人の移動を同期する最小の通信テスト（Netcode）

詳細は `MULTIPLAYER_PLAN.md` §7.1〜7.3。移動だけ・所有者権威・ローカル接続の最小構成。戦闘・デッキ・コンボの同期は Phase 6。

### 追加
- Netcode for GameObjects 2.13.3（Unity Transport を含む）。`Assets/DefaultNetworkPrefabs.asset` は Netcode が自動で作るプレハブ一覧。
- `CoopDeck.Networking`：`NetworkPlayer`（所有者は単独プレイと同じ経路、他の端末のコピーは入力・シミュレーション・一人称リグを止めて全身を表示）、`RemoteCommandSource`（`ControllerKind.Remote`）、`RemoteMotion`（受け取った位置からの速度）、`NetPose`（ピッチ・接地・スプリントを 4 バイトで）、`NetLaunchOptions`（`-nethost` `-netclient` `-netport` `-netlisten` `-netauto` `-netout` `-netreconnect`）、`NetSmokeSession` / `NetSmokeWalker` / `NetSmokeRecorder`（Net_Smoke の簡易パネルと無人テスト）。
- `Editor/NetSmokeSceneBuilder`：`PF_NetPlayer`（`PF_Player` の Prefab Variant）と `Net_Smoke` シーンを生成し、ビルドの 2 番目のシーンに追加。ブートストラップから呼ぶ。単体はメニュー `CoopDeck > Setup > Build Net Smoke Scene (Phase 2.5)`。
- テスト：EditMode `NetworkingTests` 11（起動引数・`NetPose`・`RemoteMotion`・生成物・依存の向き）、PlayMode `NetworkPlayModeTests` 2（Host 開始〜移動〜退出、コピーの見せ方）。

### 変更
- `CharacterAnimatorDriver.SetLocomotionSource`：移動の入力元だけを差し替える（コピーは受け取った位置で動くため）。
- `SandboxSceneBuilder.BuildLighting` / `Block` を Editor アセンブリ内で共有（`internal`）。
- `DischargeFxPlayModeTests`：非推奨の `FindObjectsByType(…, FindObjectsSortMode)` を置き換え（警告 2 件）。

### 検証
- ROG Ally X 1 台で Release ビルドを 2 つ（Host 32 秒・Client 27 秒、Client は 14 秒で切断して再参加）。定常時の表示遅れ 0.185 秒、位置誤差 中央値 5 cm・95% 17〜19 cm・最大 26 cm、ヨー誤差 中央値 0.2°。参加・再接続の直後 1.5 秒は最大 0.65〜1.68 m の追いつき。RTT 平均 17 ms。所有権の確認 104 回で違反 0、エラー・警告 0。両方の画面のスクリーンショットで相手の全身とアニメーションを確認。
- EditMode 313/313、PlayMode 97/97、コンソールのエラー 0。
- 未検証：別の PC 同士、人の手での操作、キャラ選択の同期（未実装：相手の体は自分の端末の選択で表示される）。

## 2026-11-08 — 雷ウサギの Death で SMG が腿に入るのを修正

### 修正
- `ArtSource/Tools/rabbit/rabbit_clips.py`：Death で SMG を膝の上から床へまっすぐ移していたため、倒れ始めにグリップが右腿へ 8〜11 mm（3 コマ）入っていた。倒れる途中で外 8 cm・前 6 cm・上 12 cm の弧を描かせる（`DEATH_ARC`）。体から押し出す補正も最大 48 → 21 cm に減った。
- `rabbit_check.py` に `timeline` モード（1 クリップのコマごとの食い込み）。走査（scan）は部位ごとの最悪値しか出さず、コマの間だけの一瞬の値と区別できなかった。

### 検証
- 書き出すコマ（30 fps）で Death の食い込みは最大 7 mm（着地で尻尾の毛が床の SMG に触れる）。全クリップの走査で 5 mm 以上は Rifle_Inspect・Revived（毛先 6〜8 mm）だけ。

## 2026-11-08 — 雷ウサギの全力疾走でストックが右腕に入るのを修正

### 修正
- `ArtSource/Tools/rabbit/rabbit_clips.py`：ウサギの Sprint だけ、銃口の下向きをキツネの 22° から 13° に（`GAIT_TWEAKS["Sprint"]["rifle_pitch"]`）。ウサギは右腕の下にストックを挟む構えなので、22° ではストックの端が右上腕に最大 21 mm 入っていた。10〜16° の範囲ならどれも食い込まない（`rabbit_check.py` scan）。銃を前へ出す案は腕もついて来て悪化したので不採用。
- 全 47 クリップを作り直して `A_LightningRabbit_Set.fbx` を取り込み直し（クリップ表は不変）。

### 検証
- 全クリップの走査：食い込み 5 mm 未満が 43 → 44 クリップ。残りは従来どおり Rifle_Inspect・Revived（毛先 6〜8 mm）と Death（0.2 秒だけ脚に 6〜11 mm）。両手は SMG から外れない（0.0 cm）。
- EditMode 302/302、PlayMode 95/95。

## 2026-11-08 — 炎キツネ専用アサルトライフル「IGNIS-27 ブレイズテイル」と固有能力「火種刻印」

詳細は `IGNIS27_RIFLE.md`。設計データ `Docs/Design/IGNIS27_Blazetail_WeaponData.json` を既存の武器形式に変換（数値は JSON の初期案のまま、燃焼の強さだけ既存の火球カードの値をユーザー確認のうえ使用）。

### 追加
- `WD_Ignis27Blazetail`（1 発 18・480 rpm・28 発・リロード 2.1 秒・射程 85 m・24→60 m で 0.7 倍・頭 1.65 倍）。生成は `Editor/IgnisAssetFactory`（`RunStandalone` あり）。
- 火種刻印：同じ敵に 6 連続命中で起爆。主対象 22、半径 2.5 m の近い敵 4 体に 12、全員に既存の燃焼（毎秒 6・4 秒）と炎の属性ヒット（既存のコンボ処理でオーバーロード・灼熱特異点）。同じ敵は 4 秒に 1 回。
- 炎のエフェクト `Prefabs/VFX/Ignis/`（マズル・弾道・着弾・蓄積中の残り火・起爆・飛び火）。
- テスト `IgnisTests`（5）・`IgnisPlayModeTests`（6、VOLT-9・プロトAR とのバランス比較を含む）。

### 変更
- `ChargeBurstSpec` に型 `shape`（Chain＝従来・既定 / Splash）と飛び火・継続ダメージの項目を追加。既定値で従来の動作（VOLT-9 の帯電連射と雷撃の見た目は不変）。`HitscanWeapon.ReleaseChargeBurst` に Splash の分岐。
- キツネの初期武器を IGNIS-27 に（`CD_FoxGunner.startingWeapon`）。`PlayerCharacterSwap.Apply` は未選択・不明な id でも組み込みのキャラクター（キツネ）の初期武器を当てる（警告文は従来どおり）。
- `ProjectBootstrap` が IGNIS-27 を作ってキツネに割り当てる。
- テスト：`ThunderhopTests` のキツネの初期武器の確認、`FoxBodyPlayModeTests` のリロードのクリップ速度を装備中の武器から計算。
- 拡散：ユーザー決定（ADS は実装しない、腰撃ちの拡散 0）で IGNIS-27 の `baseSpread`・`spreadPerShot`・`maxBloom`・`movingSpread` を 0 に（空中の拡散 1.4 は残す）。
- プロトAR（`WD_AssaultRifle`）・NPC のカービン・VOLT-9 のデータは変更なし。

### 検証
- EditMode 301/301、PlayMode 90/90。
- Editor の Play モード（キツネ・選択なし）：IGNIS-27 と「火種刻印」ゲージ、6 連続命中で起爆（46 ダメージ・2 体に飛び火）、燃焼、NPC の雷との組み合わせでオーバーロード。
- バランス（`IGNIS27_RIFLE.md` §5）：至近は VOLT-9 が約 10% 上、13 m 付近から IGNIS-27 が上（30 m で +37%）、55 m 超は IGNIS-27 だけ。集団には 3 武器中最強。プロトAR より単体 DPS が 8〜25% 低い（調整案 2 つを記載、未適用）。拡散 0 で 17・30 m の命中率は 100%（反動なしの計測）。

## 2026-11-08 — 一人称の銃身と弾道のずれを直す／ライブ Editor でのブートストラップの不具合を直す

ユーザー指摘「銃の向きと飛んでる弾道ずれてない？」。弾は照準の中心から飛び、弾道の光は銃口から命中点へ引くが、一人称の銃が照準より左を向いていた（ウサギ 10°・下 2°、キツネ 6°・下 1°）ため、銃身と弾道が画面上で約 30° ずれて見えた。当たり判定は元から正しく、見た目だけの問題。

### 変更
- `CharacterPrefabBuilder.ViewModelRifleYaw` −6 → −1、`ViewModelRiflePitch` 1 → 0。ウサギの `LightningRabbitAssetFactory.ViewModel` も同じ値を使う（−10・2 から）。銃身の線が照準に向かい、弾道が銃身から出て見える。
- `CharacterPrefabBuilder.RelinkController`：プレイヤーとキャラクターキットを組むとき、体の Animator のコントローラーが空なら読み直す。

### 修正
- ライブ Editor でブートストラップ（`ProjectBootstrap.Run`）を流すと、キツネとウサギの体のコントローラーが「空」の上書きとして PF_Player とウサギのキットに保存され、体が動かなくなっていた（アニメーションの生成がコントローラーを毎回削除して作り直すため、読み込み済みの体のプレハブが削除前のコントローラーを指したままになる）。上の `RelinkController` で解消（キットは正しいコントローラーの上書き、PF_Player は上書きなし）。

### 検証
- 一人称の撮影（`LightningCaptureTests.Capture_GunAim_Rabbit/Fox`、60 fps、9 m の的に連射）：ウサギ・キツネとも銃身の線が照準に向かい、弾道がその延長に見える（`Screenshots/Lightning/gun_aim_compare.png`）。キツネの IGNIS-27 は長いので、銃口は以前と同じく照準のすぐ右。
- `LightningRabbitPlayModeTests` 8/8（一人称の両手は目標に届きずれ 0.0 cm、左手首は画面内、左肘は画面の下端より下）。EditMode 302/302、PlayMode 95/95。
- 未確認：人の手でのプレイ、ビルド。

## 2026-11-08 — VOLT-9 サンダーホップの腰撃ちの拡散を 0 に

ユーザー決定：ADS は実装しない。腰撃ちの拡散は 0、基本は腰撃ちで戦う（IGNIS-27 と同じ）。

### 変更
- `ThunderhopAssetFactory`：`baseSpread` 0.65 → 0、`spreadPerShot` 0.25 → 0、`maxBloom` 1.75 → 0、`movingSpread` 0.6 → 0。`airborneSpread` 1.0（空中、JSON に無い）は腰撃ちとは別の仕組みなので残した。`WD_Volt9Thunderhop.asset` は生成し直し。
- `ThunderhopTests`：拡散 0 を確かめる形に。`THUNDERHOP_SMG.md` の数値表と比較表。

## 2026-11-08 — 帯電連射の放電を「ドン、バチッ」の二段に（表示だけ時間差）

ユーザー指示：基準は `Lightning_Fixed.mp4` の 7.6 秒付近の一人称。左の敵が爆ぜ → 横に太い電撃が飛び → 右の敵がもう一度爆ぜる、が通常の距離・等速で読めるように。ダメージ・対象選定・属性処理・同期は変えず、時間差は表示側で。詳細は `LIGHTNING_VFX.md` §1・§1.1。

### 追加
- `DischargeTiming`（Weapons、純 C#）：連鎖の横アークの開始 75 ms・先端の移動 35 ms・次の連鎖先まで 30 ms。
- `LightningImpactFx`（Effects）：命中を敵の胴幅に合わせて拡大縮小し、床のリングを足元に置く（空中なら出さない）。生成時に音を鳴らす。
- `IHitFlashView`（Combat）と `HitFlash.ShowFlash`：白い明滅を表示側の時刻に移す・弱める（ダメージは不変）。
- `CameraShaker.Kick(euler, offset, duration)`：長さ指定のキック。もっと強いキックの途中なら上書きしない（既存の `Kick` は従来どおり）。
- `ProceduralAudioFactory.EnsureDischargeSound`：破裂音 `SFX_Discharge_Burst` と高めの電気音 `SFX_Discharge_Chain`（コードで合成）。
- テスト：PlayMode `DischargeFxPlayModeTests`（5 件）、EditMode `LightningVfxTests` に時間差 1 件。撮影 `LightningCaptureTests.Capture_Acceptance`（60 fps 等速、`[Explicit]`）。

### 変更
- `WeaponFx`：Chain の放電は、放電のフレームに縦ボルト（終点＝閃光の中心）と初撃のバースト・明滅 0.5・発動者のカメラキック 75 ms。75 ms 後に連鎖先へ横アーク、到達（110 ms）で二発目のバーストと明滅 0.45。連鎖先が表示前に消えたら最後の位置で。Splash（IGNIS-27）は従来どおり同時。
- `LightningBoltFx`：再点滅をやめ、縦ボルトは約 0.1 秒で上から薄くなって消える。枝は着弾側（62〜95%）に短く（0.3〜0.8 m）。横アークは先端が走る（travel）、幅は初撃の 95%、白芯 0.2・縁 2.2 を Renderer ごとに設定。
- 命中 Prefab：星形閃光（胴幅の半分強の白、トゲは輪郭の外、ピーク 45〜50 ms）、白〜黄白の破片 8〜12 本を追加、細い火花 18 → 10、胸元のリングを床の水平リングに（150〜220 ms、直径 3 m）、連鎖先は 68 → 70%。命中は密度で粒を減らさない。
- 雷のシェーダー：衝撃リングが速く広がって細く薄く消える。

### 検証
- 受け入れ動画（`Screenshots/Lightning/Lightning_Accept_Before/After/Compare.mp4`、60 fps 等速、7.6 秒付近と同じ距離・FOV・明るさ・中品質）：縦ボルトと閃光が同じフレーム、右の敵は 117 ms 後に別の瞬間として爆ぜる、縦ボルトは約 100 ms で消えて火花と残留アークが残る、最大発光でも敵の輪郭が見える、連射中の 2 回目も同じ二段、1 体・連鎖先が消えた場合も問題なし（静止画で確認）。
- EditMode 302/302、PlayMode 95/95（新規 5 件を含む。2 人同時の放電で二重再生なし、発動者のカメラだけ揺れる、音が各着弾フレームで鳴る）。
- 未確認：ネットワーク同期（未実装のため）、ビルドでの Ally X 計測、人の手でのプレイ・音の聞こえ方。

## 2026-11-08 — 帯電連射の雷の色と大きさを直す（動画で確認）

ユーザー：「発光が足りないのかな。動画で見せてくれる？」→ 今の雷と発光を強めた版を動画で比較 →「色と大きさを直す」。詳細は `LIGHTNING_VFX.md`。

### 追加
- PlayMode `LightningCaptureTests`（`[Explicit]`）：雷の確認動画（1280×720・30 fps、一人称・横から 1/4 スロー・寄り・中品質）と 3 コマごとの静止画を `Screenshots/Lightning/` に書き出す。`Capture_GlowBoost` は発光を強めた比較版。
- `LightningPath.StrokeCount`・`LightningBoltFx.DurationFor`・`LightningBoltFx.Strokes`（再点滅）。

### 変更
- 雷のシェーダー 2 つを加算合成からアルファ合成に。白い細い芯と、それを包む太いシアンの帯（外側ほど空色）で描く（`_EdgeColor`・`_HaloOpacity` を追加）。芯 4.5 → 3.2 倍、シアン 1.9 → 1.2 倍（強すぎるとトーンマップで白くなる）。
- メインボルトは 7.5 m の高さから 1.6 m 横にずらして落とす（3.4 m・0.9 m から）。幅 0.5 → 0.6 m。
- 再点滅：0.1 秒おきに 3 回（連鎖先は 2 回）光り直す。長さ 0.22 → 約 0.35 秒（連鎖先 0.25 秒）。
- 命中：星形フラッシュを小さく（画面の上限 15 → 12%）、柔らかい光は白い塊から半透明の青に（敵を隠さない）。

### 検証
- 比較動画（直す前）：発光 1.7 倍にしても明るい背景では白く飛ぶだけだった。
- 直したあとの動画と静止画：一人称で画面の上端から落ちるシアンの雷が約 0.33 秒見える。空と壁の上でもシアンが出る。中品質でも同じ。
- EditMode 296/296。PlayMode は `LightningVfxPlayModeTests` 2/2 と `ThunderhopPlayModeTests` 5/5（PlayMode の全体は未実行）。コンソールのエラー 0。
- ビルドでの Ally X 計測、人の手でのプレイ確認は未。

## 2026-11-08 — 雷ウサギの実ゲーム確認と撮影（Phase 10）

詳細は `LIGHTNING_RABBIT.md` §3.8。コードとアセットの変更はなし（撮影は Editor の Play モード、計測は Release ビルド）。

### 検証
- 撮影：正面・左右の側面・背面・斜め前・戦闘（帯電連射の雷撃・スキル詠唱・ダッシュの残像）・一人称（`Screenshots/Rabbit_Phase10_Sheet_*.png`）。撮影中のコンソールに警告・エラーなし。
- Release ビルド（エラー・警告 0）で `-character LightningRabbit -perfrun 30 -perfarena` を 3 回：自動操縦で移動・射撃・スキル・敵の波 4。平均 59〜60 fps、GPU 約 3.0 ms（キツネ 2.88 ms）。初回だけ 358 ms の引っかかり 1 回、2・3 回目は最悪 33 ms。
- 終了時の異常終了が 1 回（計 9 回中）。ログは正常な終了処理まで出ており、その後の 6 回は再現なし。
- キツネの実行は従来どおり（Fire Rifle・プロトAR）。

## 2026-11-08 — 雷ウサギの質感・発光・自身の雷の演出（Phase 9）

詳細は `LIGHTNING_RABBIT.md` §3.7。VFX Graph は使わず、火球と同じく Particle System・LineRenderer・手書きシェーダーで作った（グラフを公開 API で作れないため。`FIREBALL_VFX.md`）。

### 追加
- `ArtSource/Tools/rabbit/rabbit_look.py`：体と一人称の腕の部位マスク（毛・顔・目・布・革・金属・ガラス、2048）と発光マスク（シアン〜青白の稲妻・レンズ・バッテリー管）を作る。確認画像つき。
- シェーダー `CH_GlowLines`（光る部分を加算で重ねる、強さはスクリプトから）と `CH_ElectricGhost`（ダッシュの残像）。
- 工場 `LightningRabbitLookFactory`：マスクの取り込み設定、LOD ごとと一人称の腕の「光る三角形だけの複製メッシュ」、グローブのレンズと耳先の目印、火花（一人称用・体用）と耳の放電のプレハブ、残像のマテリアル。キットに `ElectricBodyFx` を付ける。
- `ElectricBodyFx`（`CoopDeck.Characters`）：発光の呼吸・全力疾走・詠唱・ダッシュ・被弾での強弱、グローブの火花、体が見えているときだけの残像と耳の放電。規則は純 C# の `ElectricPulse`。
- `ICharacterKitListener`：キットの部品に「プレイヤーに入った」ことを伝える。
- 帯電連射との連動：`ElectricBodyFx` が武器の `ChargeBurst.Progress` と `ChargeBurstTriggered` を読み、帯電がたまるほど稲妻マークが明るく、雷撃で最も強く光ってグローブから火花が出る（`ElectricPulse.ChargeGlow`・`BurstSpike`）。
- テスト `LightningRabbitLookTests`（9 件）と PlayMode 2 件（発光と火花、残像）。

### 変更
- `CharacterAnimatorDriver` に `Dashed` イベントを追加（追記のみ）。
- `PlayerCharacterSwap`：差し替え後にキットの `ICharacterKitListener` へ通知。
- `CoopDeck.Characters` が `CoopDeck.Effects` を参照。
- `LightningRabbitAssetFactory`：仮の一様マスクを廃止し焼いたマスクを使う。体の LOD に発光の複製を足す。キットは `BuildKit` で作る（`ProjectBootstrap` もこれを呼ぶ）。
- 一人称の腕のマテリアルは腕専用の部位マスクを使う。

### 検証
- 起動中の Editor でウサギの生成（体・キット・SMG・定義）を実行。コンパイルの警告・エラーは 0。キツネの生成物は変更なし。
- EditMode 283/283、PlayMode 76/76 成功。
- 帯電連射の連動を追加後：EditMode 291/291、PlayMode 82/82（VOLT-9 で的を撃つテストを追加：発光 0.17 → 帯電中 0.61 → 雷撃 3.09 → 1.5 秒後 0.18）。
- Play モード（ROG Ally X）：ダッシュで発光が 0.05 → 2.07、1.5 秒で 0.18 に戻る。エフェクトあり/なしの差は GPU・メインスレッドとも計測のぶれ以下。残像の焼き込みは 1 体 0.19 ms。
- 撮影：`Screenshots/Rabbit_Phase9_Sheet.png` ほか。

## 2026-11-08 — 雷ウサギを Unity に取り込み、キツネと差し替えて遊べるように（Phase 7〜8）

詳細は `LIGHTNING_RABBIT.md` §3.6。PF_Player はキツネのまま。ウサギは起動引数 `-character LightningRabbit` か、Editor のメニュー CoopDeck > Play As > Lightning Rabbit で選ぶ。

### 追加
- アセンブリ `CoopDeck.Characters`：
  - `CharacterDefinition`：id・表示名・属性・キット。`CD_FoxGunner` と `CD_LightningRabbit` を作成。
  - `CharacterKit`：キャラクターごとの体と一人称部分。
  - `CharacterSelection`：起動引数と Editor の選択。
  - `PlayerCharacterSwap`：開始時に体と一人称部分を差し替える。ゲームプレイの部品は触らない。
- ウサギの資産：
  - 体 `PF_LightningRabbit_Body`：16 万三角形と LOD 2 段、Humanoid、手の IK、視線、ばね。
  - `AC_LightningRabbit_TP`（47 クリップ）。
  - 一人称の腕：ウサギの彫刻から切り出した 4 万三角形、4K。
  - キット `PF_CharacterKit_LightningRabbit`。
  - 工場は `LightningRabbitAssetFactory`、`ArtSource/Tools/rabbit/rabbit_fp_arms.py`。
- `PF_Weapon_LightningSMG`：10 万三角形と LOD 3 万、4K、水色の窓だけが発光。工場は `LightningSMGAssetFactory`。
- `ChainSecondaryMotion`：3 関節の耳とマフラーの帯を動かすばね。
- テスト `LightningRabbitAssetTests`（8 件）と `LightningRabbitPlayModeTests`（5 件）。
- PlayMode テストをキツネに固定する `CharacterSelectionPin`。

### 変更
- `FoxGunnerAnimationFactory`：パス・接頭辞・一人称の設定を引数で渡せるようにした。キツネの値が既定で、キツネの生成物は差分なし。
- `FoxGunnerAnimationFactory`：マフラー（`Scarf_`）を手続き制御の骨に、5 本指の関節（`LeftHandIndex1` など）を上半身マスクに加えた。キツネには該当する骨がない。
- `CharacterPrefabBuilder` に `BuildCharacterKit` と差し替え役の追加。`ProjectBootstrap` がウサギと SMG も作る。
- `FoxGunnerAssetFactory`：取り込みの補助関数を internal に（中身は変更なし）。

### 検証
- 起動中の Editor でブートストラップを実行。コンパイルの警告・エラーは 0。
- EditMode 273/273、PlayMode 74/74 成功。
- Play モードで一人称（`Screenshots/Rabbit_Unity_FirstPerson_Check.png`）と三人称（`Rabbit_Unity_ThirdPerson_Check.png`）を撮影して確認。

## 2026-11-08 — 帯電連射の雷撃をスタイライズした太い雷に作り直し／サンダーホップの数値調整

ユーザー指示：①比較を見て調整案を適用（1 発 8 → 11、縦反動 0.8 → 0.5）。②「細い幾何学ラインに見えて気持ちよさが足りない」雷を、太く読みやすいスタイライズ雷に（Gunfire Reborn の方向性だけを参考、見た目は真似ない）。詳細は `LIGHTNING_VFX.md`・`THUNDERHOP_SMG.md` §5。

### 追加
- Shader Graph `SG_FX_LightningBolt`（白熱コア＋シアンの外側発光、逆向きに流れるノイズ＝UV スクロール、根元から先へ走るパルス）と `SG_FX_LightningFlash`（星形の閃光・衝撃リング・柔らかい光）。HLSL は `Art/VFX/Lightning/Shaders/LightningVfx.hlsl`、グラフ・マテリアル・Prefab は `LightningVfxFactory` が生成。
- `LightningBoltFx`（太いメインボルト＋枝 2〜4 本、強さで太さ・明るさ・枝の数を変える、カメラに近いと細く）、`SurfaceArcFx`（敵の表面に 0.1〜0.2 秒残る電流）、`LightningPath`（交互に折れるジグザグと品質ごとの量、純関数）。
- `FX_Lightning_Bolt`・`FX_Lightning_Hit`（星形の閃光・光・衝撃リング・火花・点光・残留アーク）・`FX_Lightning_Hit_Chain`（小さく暗め）。
- `ChargeBurstSpec.chainBurstFx`（連鎖先の命中）。

### 変更
- 帯電連射：メインボルトは敵の真上から斜めに落ちる形に（銃口からだと視線と平行で潰れ、照準の前で白い塊になった）。連鎖先へは強さ 0.65 の細い雷と小さい命中。命中は撃った側へ少し手前に出して敵にめり込まないように。
- 品質：高（枝 4/2・残留 3・リング・点光）、中＝Ally X 既定（枝 2/1・残留 2・リング）、低（枝なし・残留 1・リングなし）。命中の粒子は画面の 15〜20% まで。
- 最初の細い弧と炸裂（`FX_Thunderhop_Arc`・`FX_Thunderhop_Burst`）は削除。
- サンダーホップ：1 発 11、縦反動 0.5（`WD_Volt9Thunderhop.asset` と生成ツールの初期値）。

### 検証
- EditMode 全 295 件・PlayMode 全 84 件が成功。新規 `LightningVfxTests` 4 件・`LightningVfxPlayModeTests` 2 件。
- 調整後の比較（6 秒、リロード込み、全弾命中）：8 m 131.2 DPS（プロトAR 129.0）、17 m 109.0、30 m 83.5。
- Editor：雷ウサギで撃ち、雷撃の 2・8 フレーム後に一時停止して高・中・低・9 m・3 m を撮影（`Screenshots/Lightning/`）。Editor 上の FPS は雷撃前と同程度。ビルドでの Ally X 計測、人の手でのプレイ確認は未。

## 2026-11-08 — 雷ウサギ専用 SMG「VOLT-9 サンダーホップ」と固有能力「帯電連射」

ユーザー指示：提供された武器設計 JSON（`ThunderHop_SMG_WeaponData.json`）を雷ウサギの初期武器として実装。詳細・対応表・比較は `THUNDERHOP_SMG.md`。

### 追加
- `WD_Volt9Thunderhop.asset`（8 ダメージ・900 rpm・40 発・リロード 1.65 秒・頭 1.5 倍・射程 55 m・10→24 m で 0.6 倍・拡散 0.65°→2.4°・反動 縦 0.8°／横 0.3°・戻り 0.16 秒・雷属性）と生成ツール `ThunderhopAssetFactory`（ブートストラップから呼ぶ）。
- 帯電連射：同じ敵に 12 連続命中（間隔 2 秒以内）で雷撃 18＋6 m 以内の 1 体に 12、対象ごとの再発動待ち 1.25 秒。ルールは純 C# の `ChargeBurstModel`、雷撃は `HitscanWeapon`（既存の `Health.ApplyDamage`、新フラグ `DamageFlags.WeaponProc`）→ 雷の属性ヒット → 既存の `ComboSystem`。
- 電撃のエフェクト 7 種（シアンのマズル・弾道・着弾 2・帯電中の銃口の火花・雷撃の炸裂・電撃の弧）。
- HUD：武器表示の上に帯電ゲージ（12 分割、雷撃で残像と発光、再発動待ちは点滅）、雷撃のダメージ数値は雷色。
- 武器データに `falloffEnd`（距離減衰の終点）・`recoilRecoverySeconds`（反動の戻り）・`chargeBurst`・`categoryLabel`・`description` を追加。既定値は従来の動作。
- `CharacterDefinition.startingWeapon` と `PlayerCharacterSwap` での適用。ウサギに設定（キツネは空＝プロトAR のまま）。
- `IAimProvider.SetRecoilRecovery`（既定は何もしない）と `FpsLook` の対応。

### 検証
- EditMode 全 290 件・PlayMode 全 81 件が成功。新規：`ThunderhopTests` 7 件、`ThunderhopPlayModeTests` 5 件（12 発目の雷撃と連鎖、範囲外に連鎖しない、雷の付与、スキルゲージ不変、再蓄積・自己連鎖なし、氷との超伝導バースト〔協力〕とコンボ 3 秒制限、フルオート 15 発/秒・40 発・リロード 1.65 秒、キツネの銃との比較）。
- Editor：ウサギを選んで Play、2 体の的へ撃ち続けて雷撃・連鎖・HUD のゲージを撮影（`Screenshots/Thunderhop/`）。押さえ込みなしの自動射撃では反動で照準が的から外れた。
- 比較（6 秒、リロード込み、全弾命中）：8 m で 98.7 DPS（プロトAR 129.0）、17 m で 82.6、30 m で 64.0。JSON の値のままではどの距離でもキツネの銃より弱い（調整は未適用）。
- 未実装：予備弾 200（予備弾の仕組みが無い）、ADS・持ち替え時間・武器ごとの移動速度、効果音、リロードの電池の発光。

## 2026-11-08 — HUD の属性マークを使っているキャラクターから読む

雷ウサギの取り込みでキャラクター定義（`CoopDeck.Characters.CharacterDefinition`）ができたので、左下の属性マークを固定の「火」から定義の属性に変更。

### 変更
- `HudController` が `PlayerCharacterSwap.Current.Element` を読む（キツネ＝火、ウサギ＝雷）。定義が無いときは従来どおり `characterElement`（火）。`CoopDeck.UI` が `CoopDeck.Characters` を参照（逆方向の参照はない）。

### 検証
- EditMode 全 274 件・PlayMode 全 74 件が成功。新しいテスト `VitalsElement_ComesFromTheCharacterInUse`。
- Editor でウサギを選んで Play し、左下に雷マークが出ることを確認（`Screenshots/UI_Redesign/compact_1080p_rabbit.png`）。確認後、キャラクターの選択はキツネに戻した（EditorPrefs を削除）。
- 気づいた点：ウサギでも武器のデータはキツネと同じ（HUD の武器名が「プロトAR」）。SMG 用の武器データができれば HUD は自動で追従する。

## 2026-11-08 — 戦闘 HUD をコンパクトに作り直し

ユーザーの指摘：「UI が大きすぎる」。参考画像（小さく・パネル少なめ）の方向性だけを取り入れ、配色と探索端末らしさは維持（他作品の UI そのものは真似ていない）。詳細は `UI_DESIGN.md` §4・§5。この大きさ・配置で進めることをユーザーが承認。

### 変更
- 文字サイズを約 2 割小さく（本文 20 → 16、最小 18 → 15）、余白 28 → 24。HUD の文字は縁取りで読ませ、パネルは右上の目標だけにした。
- スキル：中央下の大きなカード 3 枚 → 右下の 76×76 の枠（`SkillSlotView`：アイコン・残り回数・ゲージ・CD・レアリティ・キー・同調）。名前と説明はカードを引いたときに下中央へ 2 秒表示。
- 支援品は枠 1 つ（所持数・使用キー・種類の ◆、切り替えると名前と効果を知らせる）、山札・捨て札は小さな文字に。
- HP：左下の 1 本のバー（数値はバーの中）と、その上の行に名前・状態アイコン・回避ランプ。残弾はスキル枠の上の小さな表示。
- 味方：アイコン・名前・方針・細い HP バーの 1 行（ダウン中は残り秒・蘇生の進み）。行動・スキル・手札の表示はやめ、NPC の知らせは戦闘ログへ。
- 通知：目標の下の帯 → 左下の戦闘ログ（5 行、新しいものが下）。目標は右上の小さな枠。コンボ名の板を外し、文字とアイコンを小さく。
- 大きい `CardFrameView` は報酬・交換・図鑑用として残し、F8 のプレビューにスキル枠の各状態を追加。

### 検証
- EditMode 全 265 件・PlayMode 全 69 件が成功。新しいテスト：常に出ている HUD の面積が画面の 8% 未満（現在約 6%、以前は約 20%）。配置の重なりなし・照準周りの空き・7 種類の解像度は引き続き保証（最小文字は 720p で 10 px）。
- Editor の Play モードで 1920×1080・1280×720・1920×1200・2560×1080 と F8 プレビューを撮影し、重なり・はみ出しがないことを目視（`Screenshots/UI_Redesign/compact_*`、`compare_original_vs_compact_1080p.png`）。
- 人の手でのプレイ・実機のコントローラー操作は未確認。

## 2026-11-08 — UI 全面改修 Phase 1・2：灯守隊の探索端末としての戦闘 HUD

ユーザー指示：世界観（エルディア・原脈・灯守隊・継ぎ木港、すべて仮）に合わせて UI を全面改修。まず Phase 1（共通デザインシステム）と Phase 2（戦闘 HUD・スキルカード 3 枚）。詳細は `UI_DESIGN.md`。

### 追加
- 共通デザインシステム `Scripts/UI/Theme`：`UiTokens`（指定パレット・レアリティ色・文字サイズ・コントラスト計算）、`UiSprites`（コード描画の面取り金属フレーム・回路模様・ランプ・属性アイコン 5 種・同調マークなど）、`UiWidgets`（パネル・ラベル・バー・ランプ・キー表示・フォーカス括弧・チップ）、`CardFrameView`（共通カードフレーム）、`TerminalButton`（共通ボタン）、`HudNotifications`（戦闘通知）、`InputGlyphs`（使っている機器でキー表示を切替）、`HudLayout`・`HudSafeFrame`（配置と 16:9 の枠）、`WorldTerms`（仮の固有名詞）。
- 戦闘 HUD の部品 `Scripts/UI/Combat`：`VitalsPanel`（HP・障壁・状態異常チップ・回避ランプ）、`WeaponPanel`（残弾・目盛り・予備・リロード）、`NotificationFeedView`、`GuidePanel`（操作ガイド）。
- 戦闘通知：WAVE 開始・突破、味方のダウン・蘇生・後退、あなたの蘇生。
- F8：UI プレビュー（Editor・開発ビルドのみ、「操作できません」と明記）。原脈同調（金枠・同調マーク・ゲージ 0 で使用不可）、レジェンダリー、強化・変異、ボタンの各状態、ランプ、通知を確認できる。
- テスト：EditMode `UiDesignSystemTests` 19 件、PlayMode `HudPlayModeTests` 2 件。PlayMode のテストアセンブリが `CoopDeck.UI`・`UnityEngine.UI` を参照。

### 変更
- `HudController`・`DeckHud`・`ArenaHud`・`NpcHud`・`ComboHud`・`DamageFeedbackHud` を新しいデザインシステムで作り直し（既存の表示内容・イベントの購読・デバッグキーは維持）。
- キャンバスの拡縮を「幅と高さの中間」から「Expand」に変更し、HUD を 16:9 の枠に収めた（720p は 1080p の縮小、16:10 は上下に余白、ウルトラワイドで HUD が端へ離れない）。
- 画面上部に常に出ていた 4 行の操作説明を廃止：戦闘中は「F1 操作ガイド」の札だけ、F1 で左端に一覧、ポーズ画面に全操作（キーボード・パッドの 2 列）。以前は性能表示と重なっていた。
- 手札：使い切りは捨て札の方へ、ドローは山札の方からスライド。ゲージが減ると残像と発光。キー表示は今の機器のもの（Ally X はパッド表示で開始）。
- コンボ名：2 つの属性アイコンが共鳴し、薄いホログラム板の上に表示。
- `HudKit.PanelColor` がトークンのパネル色になったので、NPC デッキ編集のテスト UI も同じ色になる。

### 検証
- EditMode 全 264 件・PlayMode 全 68 件が成功（開いている Editor で `unity command test run`）。新規テストは配色の指定値、文字のコントラスト（パネルの後ろが白・黒・空・砂でも 4.5:1 以上）、レアリティと属性の色相差、全領域の重なりなし・照準周りの空き、7 種類の解像度での表示範囲と最小文字 12 px、カード表示（残り回数・ラスト1回・使い切り・同調でゲージ 0）、通知、キー表示、属性アイコンの形の違い、手札とデッキの連動・使い切りのスライド・仮想ゲームパッドとキーボードでの表示切替。
- テストで見つかったコントラスト不足を修正：パネルの不透明度 0.9 → 0.94（白の上の補助文字 4.3 → 4.9:1）、危険の文字用に `DangerText` を追加（4.1 → 4.5:1 以上）。
- Editor の Play モードで 1920×1080・1280×720・1920×1200・2560×1080 を撮影し、重なり・はみ出しがないことを目視（`Screenshots/UI_Redesign/` の `before_*` / `after_*` / `compare_*`）。戦闘中（WAVE・通知・状態異常チップ・被弾の残像・コンボ名）、ポーズ画面、F1 ガイド、F8 プレビューを確認。Editor 上の FPS は改修前後で同程度（約 88）。
- 人の手でのプレイ・実機のコントローラー操作は未確認。

## 2026-11-08 — 雷ウサギのモーションを Lightning SMG に合わせて作成（Phase 6〜7）

ユーザー指示：モーションはウサギ用の武器（Lightning SMG）に合わせる。詳細は `LIGHTNING_RABBIT.md` §3.5。Blender 上の作業で、Unity にはまだ入れていない。

### 追加
- `ArtSource/Tools/rabbit/rabbit_weapon.py`：SMG の握り位置（右手＝ピストルグリップ、左手＝縦のフォアグリップ）、リロードのマガジンの位置、インスペクトの手の位置、確認用の描画、解き方に渡す SMG の表面。
- `ArtSource/Tools/rabbit/rabbit_check.py`：構え・クリップの描画と、手の届き・SMG と体の食い込み（両方向、重ね着に惑わされない光線の判定）の数値。`sweep`（構えの候補を比べる）・`scan`（全クリップの食い込みを走査）も。
- 解き方（`rabbit_motion.RabbitRig.solve`）に 2 つの補正：手が握りに届かない分だけ SMG を手へ寄せる、SMG が頭・胸・肩・上腕・腰・脚に入ったら押し出す。

### 変更
- 構え（`rabbit_clips.py`）：肩付けせず、お腹の高さで右寄り・銃口 12° 内向き・右肘を外へ・ストックは右腕の下（頭・頬の毛・マフラーが肩の前を埋めているため）。格子探索で決定。
- ウサギ用に差し替えたクリップ：ADS（照準を目に合わせられないので右頬の横まで上げる「レイズドレディ」）、Downed・Revived（SMG を胸の前で持ち、左手は腰の近くの床）、Death（倒れる途中で右手が離れ、SMG は床へ）、インスペクト・スライド・ダッシュ（銃を顔へ引かず前・下へ）、左ストレイフと後退（胸を開かず SMG を左肩へ寄せる）。
- Skill_QuickCast はキツネの新しい版をそのまま使う（ウサギ専用の時間の付け替えをやめた）。

### 検証
- 全 47 クリップ・全フレームで、SMG を持っている間の手の誤差 0.0 cm。
- SMG と体の食い込み（手・前腕を除く）を全クリップ 10 コマずつ走査：43/47 クリップは 5 mm 未満。残りは Sprint のストックが右肩・上腕に最大 23 mm（見える）、Death の倒れる途中に脚へ 6〜11 mm、Revived・Rifle_Inspect の毛先 6〜8 mm。
- 構え（4 方向・手の拡大）と主なクリップのコマ送りを目視（`ArtSource/LightningRabbit/Inspection/05_motion/`）。
- 書き出し：`ArtSource/LightningRabbit/Export/Unity/A_LightningRabbit_Set.fbx`・`A_LightningRabbit_Clips.json`。Unity での再生は未確認（Phase 8）。

## 2026-11-08 — クイックキャストを「前へ突き出す」モーションに作り直し

ユーザー指示：クイックキャスト（火球など）のモーションが弱い。もっと前へ突き出して迫力を出す。詳細は `FOX_ANIMATION.md` §4・§6、`FIREBALL_VFX.md` §2。

### 調査（旧モーションの問題）
- 手のひらはハンドガードの握り（体の 0.37 m 前）から左へ動くだけで、前へは 0.4 cm しか出ていなかった。胸を左へ 14° ひねっていたため左肩は 5 cm 後ろへ下がっていた（腕の届く範囲の限界に当たっていた）。
- 途中の通過点が目の 10 cm 前・4 cm 下にあり、一人称では手が顔の前に大きく上がって「持ち上げる」動きに見えていた。

### 変更
- `Skill_QuickCast`（`fox_clips.py`、長さ 0.5 秒のまま）：0.055 秒で手を左の脇腹へ引き（指を軽く握り、手のひらは内向き）→ 0.12 秒（発射の瞬間）に手のひらを肩の高さでまっすぐ前へ突き出す（体の 0.54 m 前）→ 発射の反動で手が 4 cm 戻り手首が上へ跳ねる → 保持 → 0.45 秒で銃へ。胸は右へ最大 20° ひねり・12° 前傾・左鎖骨を 18° 前へ出して左肩を 15 cm 前へ運ぶ。銃はほぼ動かさない（一人称の手は銃からの相対位置で置くため）。
- 一人称の腕（`FirstPersonArms`）：詠唱中に手の目標が腕の長さより遠いとき、左肩が手について最大 12 cm 前へ出る（`maxCastReach`、体の胸のひねりの代わり）。腕の切り口は画面の下端より下のまま。
- 発射の瞬間、自分のカメラだけが小さく跳ねる（上へ 0.9°・右へ傾き 0.6°・後ろへ 1.8 cm、0.22 秒で戻る）：`CameraShaker.Kick`（新規、照準の線は動かない。画面揺れの設定・VFX 品質で縮む）を `SkillCaster` の発射時に呼ぶ（`launchKickEuler` / `launchKickOffset`、ワールドカメラを持つ自分のプレイヤーだけ）。

### 追加
- テスト `FoxBodyPlayModeTests.QuickCast_StrikesThePalmOut_InBothViews_AndKicksTheCastersView`：体の手のひらが構えより 12 cm 以上前へ出る・一人称で手を引いてから前へ出る・肩が手について出る・カメラが跳ねて元に戻る。
- 撮影 `FireballCaptureTests.Capture_QuickCastStrike`（手動実行）：一人称のコマ送りと体の 4 枚 → `Screenshots/Fireball_QuickCast.png`・`Fireball_QuickCast_Body.png`。手首の位置・肩の追従・肘と腕の切り口の画面位置をログに出す。

### 検証
- Blender：全コマで手が目標に届く（ずれ 0.0 cm）。ほかの 46 クリップは変化なし（`A_FoxGunner_Clips.json` の差分はクイックキャストのみ）。
- Unity（6000.5.4f1、起動中のエディタでブートストラップ後）：体の手のひら 0.33 → 0.50 m 前、一人称の手首は目から 0.40 → 0.24（引き）→ 0.57 m（突き出し、0.07 秒で 33 cm）。肘は突き出し中に画面左下へ入るが、腕の切り口は常に下端より下。EditMode 245/245・PlayMode 67/67 成功。`Screenshots/Fireball.mp4` を新しいモーションで撮り直した。

## 2026-11-08 — 一人称の腕を元のキツネから作り直し・Fire Rifle の高精細化

ユーザー指示：キツネと武器は既存の高精細モデルを維持し、一人称の腕も元のキツネのメッシュ・テクスチャを使う（低ポリや人間用の仮腕に置き換えない）。腕・指・銃の不自然さをリグ・ウェイト・IK・握り・カメラから調べて、品質を落とさず直す。最適化は RTX 4070 環境で考える（ここでは計測できないので画質を優先）。詳細は `CHARACTER_PIPELINE.md` §6.1・§8.3、`FOX_ANIMATION.md` §6。

### 調査（旧構成の問題）
- 手が元のキツネではなかった（メタボールで造形し直した約 9,000 面の手袋、`fox_fp_hands.py`）。腕は前腕 1.3 倍・上腕 1.1 倍に伸ばし、肩は左右別々に画面の下へ固定、左手は体とは別の握り（`FP_Grip_L`）、指は焼き込みの固定形とシェイプキー 1 つ。銃を目から遠くに置いていたため、これらで無理に届かせていた。

### 変更
- 一人称の腕 `FoxGunner_FPArms.fbx` を作り直し（`fox_fp_arms.py`）：三人称と同じ高精細スカルプトから腕・手・5 本の指をそのまま切り出して 39,999 三角形、4K の色（8K の拡大アトラスから）・法線・質感マスクを焼く。マテリアル `M_FoxGunner_SoftToon_FirstPerson` は体の SoftToon と同じ数値でテクスチャだけ一人称用。指は 3 関節ずつ（41 ボーン）、骨の長さは体と同じ。腕の延長と造形し直した手はやめた。
- `FirstPersonArms` を書き直し：左右とも体の手の目標（`IK_Hand_L/R`）を写す。肩は体の肩の位置（目からの相対）に置き、胸を右に 40° ひねり 33° 前傾。指ごと・関節ごとの握り（右＝グリップ、左＝ハンドガード、詠唱で開く、リロードで半開き）。手首の曲がり上限 38°。左手が届かないときは銃に沿って最大 12 cm 手前で握る。
- 一人称の構図（`CharacterPrefabBuilder`）：グリップを目から右 15 cm・下 20 cm・前 39 cm、銃口を中央へ 6°・下へ 1°、ビューモデルのカメラ 62° → 70°。左手がゲーム画面下中央のカード欄より上に見える。
- Fire Rifle を高精細モデルから作り直し（`refine_prop.py`：3 万三角形・4K の色と法線、`FireRifle_HQ.glb`）。`CH_Toon` に法線マップ（`_NORMALMAP`）、`FireRifleAssetFactory` は 4K・高品質圧縮・法線マップ（BC5）。
- 描画：High 段階（デスクトップ RTX 4070 級）の MSAA を 2× → 4×。
- テスト：`FoxAnimationAssetTests`（一人称の腕＝スカルプトの腕・指の関節・体と同じ骨の長さ）、`FoxGunnerAssetTests`（一人称用の焼き込みと同じ SoftToon 設定）、`FireRifleAssetTests`（三角形数・法線マップ・4K）。撮影はワールドとビューモデルを別々に描いて合成（`FoxAnimationCaptureTests`）、構図の調整用に `Tune_FirstPersonFox`（手動）。

### 検証
- ブートストラップでコンパイルエラー 0（警告 16 件は別セッションの火球テストの非推奨 API のみ）。EditMode 245/245、PlayMode 66 成功・6 スキップ（手動撮影）・0 失敗。`FirstPersonArms_ReachInFromTheLowerCorners_WithStraightWrists` も成功（左手首 y/z −0.46、画面下端 −0.70、手首の曲がり 左 22°・右 38°）。一人称の手と目標のずれは待機・走り・スプリント・リロード・詠唱とも 0.0 cm。
- 撮影 `Screenshots/FoxAnim_FirstPerson.png`（待機・走り・スプリント・リロード・詠唱）と構図の比較 `Screenshots/FoxFP_Tune.png`。手・指・前腕が元のキツネのメッシュと質感で表示されることを確認。
- Windows ビルド成功。ゲーム画面（`-perfrun 6 -perfidle`）で左手がカード欄の上に見えることを確認。この PC（ROG Ally X、Handheld）では平均 60.0 FPS、GPU 4.5 ms、メインスレッド 2.7 ms。RTX 4070（High 段階、MSAA 4×）では計測していない。

## 2026-11-08 — 火球スキルと高品質 VFX（飛翔弾スキル、Shader Graph の炎、アートバイブル）

詳細は `FIREBALL_VFX.md`、配色は `ART_BIBLE.md`、カードは `CARD_SYSTEM.md` §12.1。外部素材・有料サービスなし。VFX Graph は使っていない（グラフをコードで作る公開 API がないため。ユーザー確認済み）。

### 追加
- アートバイブル「夕方の空中遺跡」（ユーザー選択）と `Core/Palette.cs`（環境色・属性ごとの 3 段の色・協力コンボの金・危険色・煙）。`ElementStyle.ColorOf` はパレットから取る（UI・アイコンの色は同じ値）。
- カード「火球」（`CD_Fireball`、火・消費 25・CD 0.5 秒・弾速 45 m/s・直撃 45＋半径 2.5 m に 20＋炎上 毎秒 6×3 秒）を初期デッキの 13 枚目に（ユーザー指示）。アイコン `ICON_Fireball`。
- 飛翔弾スキル：`ProjectileSpec`（弾速・当たり半径・発射までの遅延・見た目の大きさ）、`CardDefinition.castFx / projectileFx / attachImpactFx`。`SkillCaster` が左手でチャージ → 0.12 秒後（クイック詠唱で手のひらが前に出る時刻）に発射 → 照準の線上を球で判定 → 命中点で既存の `Resolve`（ダメージ・範囲・炎上・属性ヒット・コンボ）。見た目は手のひらから出て 6 m で照準の線に合流（ViewModel カメラの手の位置をワールドカメラの同じ画面位置へ変換）。味方・自分はすり抜け、射程切れはダメージなし。イベント `ProjectileLaunched / ProjectileEnded`、`StepProjectiles`（固定ティック・テスト用）、`Prewarm`。
- 演出 5 種（`Prefabs/Skills/Fireball/`）：手元のチャージ、飛翔（白熱コア・炎の殻 2 層・軌跡 2 本・置き去りの炎と火の粉・発射の噴き出し・光）、爆発（閃光・立体の火球・凹凸メッシュの炎の塊・炎・衝撃波リング・火花・跳ねる燃焼片・火の粉・煙・焦げ跡）、敵に絡みつく炎、炎上中の炎。
- `FireballVfxFactory`（メッシュ 6 種・Shader Graph 6 種・マテリアル 18 種・Prefab 5 種を生成）、`ShaderGraphWriter`（URP Unlit の .shadergraph を Custom Function 構成で書き出す、ID は固定で再生成しても差分なし）、`FireballVfx.hlsl`（炎の温度ランプ・3D ノイズ・Dissolve・コア・炎の殻・粒子・煙・衝撃波）。
- `FireballProjectileFx`（発射直後は小さく出て拡大、置き去りの炎は約 3 m 先から、命中で本体が消え軌跡と火の粉は自然に消える、射程切れは溶けて消える）、`FireballExplosionFx`（爆発半径・命中面の向き・キャラクター命中では小さな縦のリングと焦げ跡なし）、`IRadiusFx / ISurfaceFx / IProjectileFx`。
- 炎上の見た目：`StatusEffectsModel.HasDamageOverTime`、`ElementAuraPresenter` が火の継続ダメージの間だけ体に炎を出す（既存の火炎旋風・焼夷グレネードの炎上にも出る）。`PooledFx.StopEmitting`（放出を止めて自然に消してから返却）。
- 無人計測 `-perffireball [間隔]`（アリーナで戦いながら火球を連射、JSON に発射数・命中数・同時飛行数）。
- テスト：EditMode `FireballAssetTests`（11）、PlayMode `FireballPlayModeTests`（6）、手動の撮影 `FireballCaptureTests`。

### 変更
- `PooledFx`：距離あたりの放出（rate over distance）も VFX 品質の粒子数倍率で調整。
- `SkillCaster`：`IRadiusFx` を持つ範囲エフェクトは全体を拡大せず自分で半径に合わせる。1 回の詠唱で同じ敵に着弾エフェクトを 2 回出さない。
- 既存スキル用エフェクト・カード資産：パレット化による色の端数の差と、新しいカードの項目（既定値）が保存されたことによる差分のみ。
- テスト：初期デッキの枚数 12 → 13（`CardDataAssetTests`・`NpcAssetTests`）。PlayMode テストのアセンブリに URP Core Runtime の参照を追加（撮影で Volume を使う）。

### 検証
- EditMode 245/245。PlayMode 65 成功・6 スキップ（手動撮影）・1 失敗（`FoxBodyPlayModeTests.FirstPersonArms_ReachInFromTheLowerCorners_WithStraightWrists`、別セッションが作業中だった一人称の腕の作り直しによるもの。その作業の後の全件実行では 66 成功・失敗 0 との報告）。Shader Graph 6 種はエラーなくコンパイル。コンパイル警告 0（テストの非推奨 API 呼び出し 4 か所を修正後、火球 PlayMode 6/6 で確認）。
- 撮影 `Screenshots/Fireball_*.png`（一人称の発射〜遠くの爆発、飛翔の横・前・後ろ、敵・床・壁での爆発と余韻、炎上、品質 3 段階）。
- Windows ビルド成功（258.6 MB、エラー・警告 0）。ROG Ally X（Handheld）で敵ウェーブと戦いながら 0.3 秒ごとに火球：VFX 中・60 上限 平均 60.0／1% Low 58.4、上限なし 平均 91.1（従来と同等）、GPU 3.6〜3.8 ms、VFX 高・低も 60.0／60.0。実戦中にプレイヤーの火球 → ヴォルトの雷で協力コンボ（オーバーロード）が発生することを確認。

## 2026-11-08 — 雷属性ウサギ（2 体目のプレイアブル）の調査・Meshy 生成・検査

詳細は `LIGHTNING_RABBIT.md`。有料サービス Meshy を使用（ユーザー承認済み、40 クレジット：複数画像からの 3D 生成 35〔Ultra 形状・4K テクスチャ〕＋自動リギング 5。残高 1,576 → 1,536）。Unity 側はまだ変更していない。

### 追加
- Meshy の出力 `ArtSource/LightningRabbit/Meshy/`：高精細マスター（939,370 三角形、閉じたメッシュ）、30,862 三角形の下地と 4K の基本色、24 ボーンのリグ（身長 1.70 m）と歩き・走り。
- 検査結果 `ArtSource/LightningRabbit/Inspection/`（四面・歩き・走り・手と頭の拡大）、比較画像 `Screenshots/Rabbit_Meshy_vs_Reference.png`。手は左右とも 5 本の指が分かれて生成された。
- `ArtSource/Tools/render_closeups.py`（Blender）：手・頭などの拡大を、色つきと形だけ（灰色＋くぼみの陰影）で複数のモデルを同じ枠に並べて描く（`--grey-only` で形だけ）。
- ゲーム用の体をユーザー指示「極力滑らかに」で 16 万三角形に（キツネは 4 万）。遠距離用に 4 万・1.5 万の LOD を作る方針。`ArtSource/LightningRabbit/Export/LightningRabbit_HQ.glb`（79,902 頂点、24 ボーンのウェイト）、均一 UV の 4K 基本色、高精細からの 4K 法線マップ。
- `ArtSource/Tools/decimation_study.py`（Blender）：高精細を複数の三角形数に削減し、隣り合う面の角度と高精細からのずれ（部位別）を測る。結果は `LIGHTNING_RABBIT.md` §3.2。
- `ArtSource/Tools/rebuild_body.py`（Blender）：共有の `refine_character.py rebuild` を変更せずに呼び、体をキャラクター名で書き出す。
- ウサギのリグ `ArtSource/Tools/rabbit/`（`rabbit_common` / `rabbit_segment` / `rabbit_rig` / `rabbit_debug`）：指 30（1 本ずつ 3 関節）・耳 6・尻尾 2・マフラー 8・銃と IK 5 の計 51 ボーンを追加して 75 ボーン、部位の自動切り分けとウェイト、曲げ検査の画像、LOD 3 段（16 万 / 4 万 / 1.5 万）入りの FBX（`ArtSource/LightningRabbit/Export/Unity/LightningRabbit.fbx`、まだ Assets には入れていない）。詳細は `LIGHTNING_RABBIT.md` §3.3。

- ウサギの武器「Lightning SMG」（ユーザーの設定画から）：Meshy で 3D 化（35 クレジット、ユーザー承認済み。残高 1,536 → 1,501）、高精細から 10 万三角形・4K の色と法線マップで作り直し、遠景用 LOD1（3 万）、全長 0.52 m、ソケット `Muzzle`・`LeftHandGrip`。`ArtSource/LightningSMG/`。詳細は `LIGHTNING_RABBIT.md` §3.4。
- ツール：`crop_reference.py`（設定画から各面を切り出して AI 拡大）、`rebuild_prop_hq.py`（共有の `refine_prop.py` を変えずに 4K の元テクスチャへ対応）、`fp_view_check.py`（一人称の構図で描いて等倍で切り出す）。
- ウサギのモーション生成スクリプト（`ArtSource/Tools/rabbit/rabbit_motion.py`・`rabbit_clips.py`・`rabbit_build_clips.py`、キツネの `fox_anim` を読むだけで使う）。ユーザーの指示で武器に合わせるため未実行。

### 検証
- Blender の曲げ検査（腕・肘・股関節・膝・ひねり・耳・マフラー・尻尾・握り）で、マフラーの筋状の伸びと指の間の裂け目を見つけて修正。
- SMG を一人称の構図（1920×1080、縦 70°）で描き、等倍の切り出しでテクスチャの画素の粗さが見えないこと、作り直しで照準・レール・銃口の角がくっきりしたことを確認。Unity での確認はまだ。FBX を読み直して 75 ボーン・LOD 3 つ・1 頂点最大 4 ボーン・身長 1.70 m・+Z 向きを確認。Unity での確認はまだ。
- `Docs/LIGHTNING_RABBIT.md`：外見仕様、キツネとのシルエットの違い、三面図からの大きさ（耳の先まで約 1.70 m）、Meshy の設定と費用、予想される問題（指・耳・マフラー）、既存構成の調査結果、Phase 4 以降の計画、確認事項。
- 参照画像 `ArtSource/LightningRabbit/Reference/`（ユーザー提供の三面図の原画と、Meshy 用の正方形 PNG）。
- `ArtSource/Tools/prepare_reference.py`（Blender）：三面図の 1 枚を縮小せずに背景色で正方形へ広げ、体の外接枠を出力する。

### 変更
- `.gitattributes`：`*.webp` を Git LFS の対象に。

## 2026-11-08 — 一人称の腕の構図を作り直し（銃を持つ腕が変に見える）

詳細は `FOX_ANIMATION.md` §6「一人称の構図」。

### 修正
- 左手の握り `FP_Grip_L`（`fox_rig.py` の `FP_LEFT_*`）：指先が下を向く握りに前腕が下から来ていたため、手首が 125° 折れて手が籠手の上に折り返し、黒い塊が腕から生えているように見えていた。ハンドガードの左下を下から包み、指先が前腕の延長（前・右上）を向く握りに変更（手首の曲がり 5〜9°）。
- 左前腕が画面下中央から垂直に立ち上がり、肘があごの下にあるように見えていた。

### 変更
- 一人称の腕を長く（`fox_fp_arms.lengthen_arms`：前腕 1.3 倍・上腕 1.1 倍、伸ばすのは素肌の部分だけで籠手・肘・手の形は保つ）。ビューモデルの銃はキツネ自身の目より遠くにあり、体と同じ長さでは肘を画面外に出せないため。
- 肩を左右別々に画面の下へ配置し、肘のヒントを固定値に（`FoxGunnerAnimationFactory.FirstPersonLeftShoulder/RightShoulder/LeftElbow/RightElbow`、`FirstPersonArms.SetElbowHints`）。左前腕は左下から斜めに入り、肘は画面下端のすぐ外。右前腕は右手の向きに沿って右下から（手首の曲がり 24°）。旧 `FirstPersonShoulders` / `FirstPersonYaw` は削除。
- ビューモデルの銃を 3.5 cm 上・2 cm 左へ、5° 前傾、ヨー -6° → -7°（`CharacterPrefabBuilder.ViewModelRootPosition` / `ViewModelRiflePitch`）。右手の指がグリップにかかるのが画面下端に見える。

### 追加
- 手動実行の `FoxAnimationCaptureTests.Tune_FirstPersonLayout`：JSON の候補ごとに一人称の画面と手首の拡大を撮り、画面上の肘の位置から肩と肘のヒントを解き、手首の曲がりを記録する（JSON がなければ現在の配置を撮る）。
- テスト `FirstPersonArms_ReachInFromTheLowerCorners_WithStraightWrists`（左手は画面内・左肘は画面下端の外で手より左・右手は下端に届く・手首の曲がり 左 30° 右 40° 未満）。
- `Capture_FirstPerson` にスプリントのコマを追加。

### 検証
- EditMode 234/234、PlayMode 60/60（手動実行の撮影 4 件を除く）、コンパイル警告 0。Windows ビルド成功。
- 待機の手首の曲がり 左 5°（旧い握りでは 125°）・右 24°、手と目標のずれ 0.0 cm（待機・走り・スプリント・リロード・詠唱）、手首に残るねじれ最大 72°（上限 75°）。
- 撮影 `FoxAnim_FirstPerson.png`（待機・走り・スプリント・リロード・詠唱）と、ビルドの実画面 `PerfCaptures/fp_view_after.png`（比較 `Screenshots/FoxAnim_FPArms_BeforeAfter.png`）：左前腕が左下から斜めに入り、左手の指がハンドガードにかかる。ゲーム画面ではカード欄が左前腕の下側を、弾数欄が右手（画面右下の角）を隠す。
- ビルドの待機計測（`-perfrun 6 -perfidle`、1920×1080）：平均 60.0 FPS、1% Low 60.0。

## 2026-11-08 — Fox Gunner の高品質化（均一な 4K テクスチャ・法線マップ・4 万三角形の体）

詳細は `CHARACTER_PIPELINE.md` §8・§8.1・§8.2。ボーン構成・アニメーション・配色は変えていない。外部ツール：Real-ESRGAN（BSD-3、ローカル実行、リポジトリには入れない）。

### 追加
- `ArtSource/Tools/refine_character.py`：UV 島の外を埋める（dilate）→ Real-ESRGAN でタイル分割の 4 倍拡大（upscale）→ 同じ島を面積比でそろえて 4096 に詰め直し、旧 UV から色を焼き直す（bake）。`--check` で旧・新の比較画像。
- `ArtSource/FoxGunner/Export/FoxGunner_HQ.glb`（同じ 24 ボーン・メッシュ・スキン、新しい UV、4K テクスチャ入り）。
- テスト `BaseColor_Is4K_HighQuality_WithEvenTexelDensity`（4096・BC7・異方性、テクセル密度の 95%/5% 比 1.8 未満）。
- 法線マップ `T_FoxGunner_Normal.png`（4096、Meshy の 230 万三角形の高精細モデルから接空間でベイク、`refine_character.py normalmap`）。頂点は増やさず、溶けていたゴーグルの面取り・ベルト・ブーツ・マフラーのしわの陰影を高精細と同じにする。
- `SG_Character_SoftToon` に `_NormalMap` / `_NormalStrength`。一人称の腕用に法線マップなしの `M_FoxGunner_SoftToon_FirstPerson`。
- テスト `Body_ShadesWithTheHighResNormalMap_AndTheFirstPersonArmsDoNot`、比較画像 `FoxLook_NormalMap_SideLight.png`（横からの光で法線マップなし／あり）。

### 変更
- `T_FoxGunner_BaseColor.png` を 2048 → 4096（テクセル密度の差 約 3 倍 → 1.5 倍、線画のギザギザとノイズを除去）。取り込みは BC7・トライリニア・異方性 8。
- `fox_common.EXPORT_GLB` を `FoxGunner_HQ.glb` に。`fox_rig.py`・`build_clips.py`・`fox_fp_arms.py` を再実行して `FoxGunner.fbx`（42 ボーン）・`A_FoxGunner_Set.fbx`・`FoxGunner_FPArms.fbx` を新しい UV で再出力。
- 部位マスクを 1024 → 2048 で作り直し（毛 38.3%・顔 4.2%・布 35.0%・革 19.0%・金属 3.4%。テクセル数の割合なので、旧アトラスで顔に多く割かれていた分が減った）。
- `FoxGunnerLookFactory.BenchmarkBatch` が変更した画質レベルを元に戻すように。古い API 呼び出し（CS0618）2 件を置き換え。
- 体の FBX の接線を MikkTSpace で計算（法線マップ用）。一人称の腕は `M_FoxGunner_SoftToon_FirstPerson` を使う（`FoxGunnerAnimationFactory` の 1 行）。
- 体を Meshy の高精細モデルから 40,000 三角形で作り直し（`refine_character.py rebuild`：削減・均一 UV・色と法線マップの焼き直し・24 ボーンのウェイト転写）。`FoxGunner_HQ.glb`・`T_FoxGunner_BaseColor.png`・`T_FoxGunner_Normal.png` を置き換え、`fox_anim` 一式（尻尾 785・耳 1,095/1,111・指 618/435 頂点）と一人称の腕を再出力。テストの予算を 30k〜45k 三角形・6 万頂点に（Unity で 52,554 頂点）。

### 修正
- 一人称の左前腕（肘の内側）に黒い三角形が出ていた。原因は前腕の UV 転写：頂点ごとに最寄りの面から UV を取る方式（Data Transfer）だと、UV の継ぎ目をまたぐ面の角が別々の島に乗り、面がアトラスを横切って伸びる（新しい UV では継ぎ目が増え目立った）。さらに体から切り出した腕は肘付近の大きな三角形が欠けていた。`fox_fp_arms.body_uvs` に置き換え：UV は体全体から取り、各面は中心に最寄りの体の三角形から UV が連続する半径 3 cm の範囲にだけ写す。継ぎ目の端をまたぐ面・アトラスの隙間を横切る面（14,000 面中 680 面）は 1 枚の三角形に収める。`FoxGunner_FPArms.fbx` を再出力（頂点・面数は同じ）。

- 4 万三角形への切り替え後、一人称の左前腕の肌に 1 ピクセルの暗い点が出ていた。原因は部位マスク：各テクセルを基本色の 1 テクセルの色で判定するため、4K（Real-ESRGAN）の塗りの細かなムラで孤立したテクセルが別の部位になり、トゥーンの陰影が点状に変わっていた。`FoxGunnerLookFactory.RemoveSpeckles`（3×3 の多数決 2 回：同じ部位の隣が 1 つ以下で、5 つ以上が別の部位なら合わせる）を判定とにじみ出しの間に追加し、マスクを作り直し（9,409 テクセルを修正、部位の割合はほぼ同じ：毛 38.2%・顔 4.2%・布 35.1%・革 18.3%・金属 4.1%）。

### 検証
- 部位マスクの修正：EditMode 234/234、PlayMode 59/59、`FoxAnimationCaptureTests` 3/3。`FoxAnim_FirstPerson.png` の左前腕に点なし（細い赤線はテクスチャの傷の模様）。
- 一人称の腕の UV 修正：Blender で体と一人称の前腕を同じカメラ 4 方向から平坦テクスチャで描画して一致を確認（黒い部分なし）、伸びた面 0。Unity の一人称の撮影（`FoxAnim_FirstPerson.png`）で左前腕がきれいなことを確認。
- 4 万三角形の体で EditMode 234/234、PlayMode 59/59、`FoxAnimationCaptureTests` 3/3（`Diagnose_TailShape`：尻尾の届き 0.69 m・スケール 1.0）、コンパイル警告 0。`FoxHQ_ThreeStages.png`（元・15k＋4K テクスチャ・40k＋法線マップ）。
- 4 万三角形の負荷：キツネ 9 体でキツネなし 2.48 ms → 4.32 ms。Windows ビルド（164 MB、エラー 0）の `-perfrun 30`：平均 59.98 FPS、1% low 58.4 FPS、GPU 2.60 ms、メインスレッド 3.58 ms（プレイヤーの体は影だけ）。
- 15k＋法線マップの段階：EditMode 234/234、PlayMode 59/59、`FoxAnimationCaptureTests` 3/3（コンパイル警告 0）。一人称の手の見た目に変化なし（`FoxAnim_FirstPerson_Hands.png`）。
- 15k＋法線マップの負荷（Handheld、キツネ 9 体）：キツネなし 2.95 ms、Shader Graph 版＋輪郭線 3.50 ms。
- テクスチャのみの段階：EditMode 233/233、PlayMode 59/59、`FoxAnimationCaptureTests` 3/3。
- Unity での撮影：`Screenshots/FoxHQ_BeforeAfter.png`（上が変更前）、`FoxLook_*.png`、`FoxAnim_*.png`（全モーション・一人称の腕の色）。
- ROG Ally X（Handheld、キツネ 9 体、1280×720）：キツネなし 2.45 ms、Shader Graph 版＋輪郭線 3.16 ms。

## 2026-11-07 — Fox Gunner のアニメーション一式（Blender Python → Unity）

外部素材・有料サービスなし。詳細は `FOX_ANIMATION.md`。モーションはスクリプトで生成した暫定版（モーキャプ・手付けではない）。

### 追加
- `ArtSource/Tools/fox_anim/`：尻尾 4・耳 2・指 6 のボーンと自動ウェイト、銃ボーンと手の IK 目標（`fox_rig.py`）、姿勢ソルバー（FK＋解析 2 ボーン IK＋足の接地）、接地足が滑らない歩行生成、47 クリップ（依頼の 01〜42＋補助）、確認用レンダー、一人称の腕（前腕＝Meshy 高解像度メッシュ、手＝Blender で新規造形した指 3 関節の手袋の手と詠唱用シェイプキー：`fox_fp_arms.py` / `fox_fp_hands.py`）。
- Unity：`FoxGunnerAnimationFactory`（Humanoid 取り込み・クリップ表・マスク・4 レイヤーの `AC_FoxGunner_TP`・体の IK/視線/尻尾耳・一人称の腕）、`FoxBodyAnimator`（エイム方向への視線と銃口補正）、`FoxSecondaryMotion`（尻尾・耳のばね、耳のリアクション、Ultimate の尻尾、待機の表情）、`FirstPersonArms`（体の手の目標をビューモデルの銃へ写す 2 ボーン IK）、`CharacterAnimationEvents`（ゲームのイベント→アニメーション）、`SkillMotionRules`（カード→詠唱モーション）。
- `AnimatorParams` に移動・武器・スキル・反応・インタラクションのパラメータ、`SkillMotion` / `InteractionMotion`。`Revivable.Active` / `IsReviving`、`FpsMotor.DashDirection`。

### 変更
- `FoxGunner.fbx` を 42 ボーンで再出力（元の 24 ボーン・メッシュ・UV は同じ）。`AC_FoxGunner_TP` を速度 1D ブレンドから 4 レイヤーの構成へ（パラメータ名は従来どおり）。Meshy の歩き・走り・待機クリップは残す（新 Controller では未使用）。
- `HeldRifle` を右手から `Weapon` ボーンへ。一人称の仮の手（カプセル）をキツネの腕に置き換え。
- `CharacterAnimatorDriver`：m/s の速度リングと再生速度補正、Controller にないパラメータは触らない型付き API。`FpsCharacter` のアニメーション配線を `CharacterAnimationEvents` へ。

### 検証
- EditMode 232/232、PlayMode 59/59（撮影・診断の手動テスト 3 件は除く）。一人称の手のずれ 0.0 cm、手首のねじれは前腕へ 6 割分散（残り最大 69°）。手と銃のずれ 0.0 cm、エイム時の銃口誤差 0.0°（上 35° で 0.2°）、6.5 m/s 走行時の接地足の滑り 0.18 m/s。
- Windows ビルド成功（116 MB、エラー・警告 0）。ROG Ally X（Handheld）で平均 59.9 FPS、メインスレッド 3.7 ms、GPU 2.4 ms。
- Unity での撮影（`Screenshots/FoxAnim_*.png`）で全モーションと一人称の腕を目視確認。

## 2026-11-07 — Fox Gunner の質感改善（フィギュアっぽさを減らす）

外部素材・有料サービスなし（Shader Graph の作りは Unity 公式サンプル「Custom Lighting」を参考、Unity Companion License）。手順と数値は `CHARACTER_PIPELINE.md` §7。

### 追加
- 部位マスク（毛・顔・目・布・革・金属・ガラス）をテクスチャの色・スキンのボーン・位置から自動生成する `FoxGunnerLookFactory`。質感パラメータのマスク `T_FoxGunner_SurfaceMask` と URP Lit 用 `T_FoxGunner_MetallicSmoothness`。
- Shader Graph `SG_Character_SoftToon`＋`FoxSoftToonLighting.hlsl`（部位ごとの柔らかい陰影、顔・毛の暖色、素材ごとのつや、控えめなリム）と、表面色から線色を取る細い輪郭線 `CH_Outline`。
- 比較撮影（同じ照明で変更前・URP Lit・Shader Graph 版、サンドボックスの照明でも）と Ally X での負荷計測のバッチ。

### 変更
- `PF_FoxGunner_Body`（とプレイヤーの体）のマテリアルを `M_FoxGunner_SoftToon`＋`M_FoxGunner_Outline` に。変更前の `M_FoxGunner_Toon` は残し、`Materials/Backup/` にも複製。

### 検証
- EditMode 214/214、PlayMode 54/54（Unity 6000.5.4f1）。追加：部位マスクの割合、Shader Graph と輪郭線シェーダーのコンパイル・影と深度のパス、体のマテリアル構成。
- 同じ照明での比較画像（`Screenshots/FoxLook_*.png`）で、2 段の硬い陰影と太い黒線（尻尾の縁の黒い塊を含む）がなくなり、毛・顔の暖かいグラデーション、布・革・金属の違いが出たことを確認。
- ROG Ally X（Handheld 画質）で、画面を埋めるキツネ 9 体の描画が 3.98 → 4.50 ms（キツネなし 3.16 ms）。

## 2026-11-07 — プレイヤーの武器「Fire Rifle」（Meshy → Blender → Unity）

有料サービス Meshy を使用（ユーザー承認済み、30 クレジット：4 枚の図からの 3D 生成）。手順は `CHARACTER_PIPELINE.md` §6。

### 追加
- 三面図下段の 4 つの図から作ったトゥーン調の炎属性ライフル：FBX（7,678 三角形、全長 0.85 m、原点はグリップ、`Muzzle`・`LeftHandGrip` ソケット付き）、2K テクスチャ、発光マップ、`PF_Weapon_FireRifle`。GLB は `ArtSource/FireRifle/Export/`。
- Blender スクリプト `ArtSource/Tools/prepare_prop_for_unity.py`（頂点の結合、角度による硬い角、向き・長さ・原点の調整、ソケット、FBX/GLB の書き出し）。
- `FireRifleAssetFactory`（メニュー `CoopDeck > Art > Import Fire Rifle`）、`ToonOutlineNormalsPostprocessor`（`Art/Weapons/` 以下のモデルに輪郭線用の平均法線を UV3 で追加）。
- トゥーンシェーダーに発光（`_EMISSION`）と、UV3 の平均法線で押し出す輪郭線（`_OUTLINE_UV3_NORMALS`）。どちらも切り替え式で、キツネの見た目は変わらない。

### 変更
- 一人称の仮の箱の銃をこのライフルに置き換え（輪郭の細い専用マテリアル、11 cm 前・3.5 cm 下・銃口 6° 内向き）。武器とスキルの発射位置はライフルの `Muzzle`。
- 体の右手にも同じライフルを持たせる（待機姿勢で前下がり 35° の構え、影のみ描画）。

### 検証
- EditMode 212/212、PlayMode 54/54（Unity 6000.5.4f1）。追加：`FireRifleAssetTests`（5 件）。
- サンドボックスシーンで描画を確認（`Screenshots/FireRifle_FirstPerson.png`、`FireRifle_ThirdPerson.png`）：一人称の位置と輪郭・発光、三人称の構え（真横から前下がり 35°）。

## 2026-11-07 — プレイヤーの本番モデル「Fox Gunner」（Meshy → Blender → Unity）

有料サービス Meshy を使用（ユーザー承認済み、35 クレジット：複数画像からの 3D 生成 30＋自動リギング 5）。Blender 5.2.3（GPL、winget の公式版）を追加。手順は `CHARACTER_PIPELINE.md`。

### 追加
- 三面図から作ったトゥーン調のキツネのキャラクター：FBX（Humanoid、15,350 三角形、24 ボーン、身長 1.6 m）、2K テクスチャ、歩き・走りのクリップ、仮の待機クリップ（歩行の平均姿勢＋呼吸）、`AC_FoxGunner_TP`、`PF_FoxGunner_Body`。GLB は `ArtSource/FoxGunner/Export/`。
- トゥーンシェーダー `CoopDeck/Character/Toon`（2 階調＋落ち影、追加ライト、環境光、リム、輪郭線、Forward+ 対応）。
- Blender スクリプト `ArtSource/Tools/inspect_character.py`（検査）と `prepare_for_unity.py`（頂点の結合・法線の平滑化・原点と縮尺と向きの修正・書き出し）。
- `FoxGunnerAssetFactory`（取り込み設定・明示的な Humanoid の骨対応・マテリアル・Animator・Prefab・プレビュー画像。メニュー `CoopDeck > Art > Import Fox Gunner`）。

### 変更
- `PF_Player` の `BodyVisual` を仮リグからこのモデルに差し替え（入れ子 Prefab、影のみ描画）。`ProjectBootstrap` がモデルの資産も生成する。NPC は仮リグのまま。
- 走りクリップを移動速度に合わせて速く再生（6.5 m/s で約 1.6 倍、9.5 m/s で約 2.4 倍）。

### 検証
- EditMode 207/207、PlayMode 54/54（Unity 6000.5.4f1）。追加：`FoxGunnerAssetTests`（10 件）、`PlayerBodyPlayModeTests`（6.5 m/s 走行で接地中の足の滑り 0.25 m/s）。
- エディターで描画を確認（`Screenshots/FoxGunner_Preview.png`、`PlayerBody_Preview.png`）：トゥーン陰影・輪郭線・影、一人称カメラから前方に体の影が映る。

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
