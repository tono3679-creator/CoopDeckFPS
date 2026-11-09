# FOX_ANIMATION — Fox Gunner のアニメーション（Blender Python → Unity）

> 2026-11-07。外部素材・有料サービスは使っていない（Meshy の追加モーションも未使用）。
> すべてのモーションは Blender Python のスクリプトでキーを生成した**手続き型の制作物**で、モーションキャプチャではない。
> 品質は「暫定版」。ゲーム内で破綻なく動くことは確認済みだが、プロのアニメーターの手付けと同等ではない。

---

## 1. 流れ

| 段階 | 道具 | 出力 |
|---|---|---|
| 1. ボーン追加・ウェイト | `ArtSource/Tools/fox_anim/fox_rig.py` | `ArtSource/FoxGunner/Animation/FoxGunner_AnimRig.blend`、`Assets/.../Models/FoxGunner.fbx`（42 ボーン） |
| 2. 全クリップ生成 | `fox_anim/build_clips.py`（`fox_clips.py` / `fox_gait.py` / `fox_motion.py` / `fox_stance.py`） | `Assets/_Game/Animations/FoxGunner/A_FoxGunner_Set.fbx`（47 テイク）、`A_FoxGunner_Clips.json` |
| 3. 一人称の腕 | `fox_anim/fox_fp_arms.py`（Meshy の高精細スカルプトから腕・手・指をそのまま切り出し、4 万三角形・4K の色/法線/質感マスクを焼く） | `Assets/.../Models/FoxGunner_FPArms.fbx`、`Textures/T_FoxGunner_FPArms_*.png` |
| 4. 確認用レンダー | `fox_anim/check_stance.py`、`check_clip.py`、`debug_segment.py` | コンタクトシート（作業用、保存しない） |
| 5. Unity 取り込み | `FoxGunnerAnimationFactory`（`FoxGunnerAssetFactory.EnsureAll` とブートストラップから） | マスク、`AC_FoxGunner_TP`、体の IK・視線・尻尾/耳、一人称の腕 |

```powershell
$b = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
& $b -b --factory-startup -P ArtSource\Tools\fox_anim\fox_rig.py                                   # ボーン追加とモデル FBX
& $b -b ArtSource\FoxGunner\Animation\FoxGunner_AnimRig.blend -P ArtSource\Tools\fox_anim\build_clips.py   # 全クリップ（約 10 秒）
& $b -b ArtSource\FoxGunner\Animation\FoxGunner_AnimRig.blend -P ArtSource\Tools\fox_anim\fox_fp_arms.py  # 一人称の腕（焼き込み込みで数分）
& $b -b ArtSource\FoxGunner\Animation\FoxGunner_AnimRig.blend -P ArtSource\Tools\fox_anim\check_clip.py -- <出力先> Run_Forward,Sprint frames=8 view=right+front
```
その後 `ProjectBootstrap.RunBatch`（または `CoopDeck > Setup > Run Phase 1 Bootstrap`）。Unity での確認画像：
`-runTests -testPlatform PlayMode -testFilter FoxAnimationCaptureTests`（`Screenshots/FoxAnim_*.png`。通常のテスト実行では動かない）。

---

## 2. リグ（Meshy の 24 ボーン＋18 本）

| 追加ボーン | 親 | 用途 |
|---|---|---|
| `Tail01`〜`Tail04` | Hips | 尻尾（Unity で手続き制御。クリップはキーを持たない） |
| `Ear_L` / `Ear_R` | Head | 耳（同上） |
| `Left/RightFingers1`・`Fingers2`・`Thumb` | Hand | 指（4 本まとめて曲がる。指なし手袋で太い指なので 1 本ずつは作らない） |
| `Weapon` | Spine（UpperChest） | ライフルのグリップ位置（Y＝銃口、Z＝上）。クリップが動かし、`HeldRifle` がここに付く |
| `IK_Hand_L` / `_R` | Weapon | 手の IK 目標。クリップが作者の手の位置にキーを打つ |
| `IK_Elbow_L` / `_R` | Spine | 肘のヒント |
| `FP_Grip_L` | Weapon | 旧：一人称の左手の握り。2026-11-08 の作り直しで一人称の左手も体の `IK_Hand_L` を写すようになり、今は使っていない（旧 `Tune_FirstPersonLayout` のためだけに残っている） |

- 元の 24 ボーンと T ポーズは変更なし（Humanoid のボーン対応はそのまま）。メッシュ・UV・頂点順も同じ。
- 尻尾・耳・指の頂点はテクスチャの色・元のウェイト・位置から自動で選ぶ（`fox_segment.py`）。尻尾は「太ももに付いていた毛色の頂点を、尻尾の先から塗りつぶし」で、マフラーの垂れ（頭に付いている）と赤い布を除外。
- 尻尾の付け根は骨盤（Hips）に混ぜる。Meshy の元ウェイトは太ももだったため、混ぜると走るたびに付け根が脚に引っぱられて尻尾が伸びて見えた。
- キツネの体と一人称の腕は、画質設定（Handheld は 2 ボーン）に関係なく 1 頂点 4 ボーンで計算する（2 ボーンだと尻尾の付け根と指の関節が平たく崩れる）。

---

## 3. 基本姿勢と制作方法

- **構え**：キツネは腕が短い（肩〜手首 0.39〜0.44 m）のに銃は全長 0.85 m あるため、銃床を右肩の前に寄せ、左手は弾倉（炎の六角ブロック）の下を握るコンパクトな構え。上半身は右へ 30° ひねる（ブレード）。頭は常に正面を見る。
- **ソルバー**（`fox_motion.py`）：骨盤・背骨・首はキャラクター座標の回転、腕と脚は解析的な 2 ボーン IK、手は銃の座標系で位置と向きを指定、足は接地点・かかと/つま先の転がり。Blender のコンストレイントは使わず、全ボーンの姿勢を毎フレーム計算してキーを打つ（結果が毎回同じ）。
- **歩行生成**（`fox_gait.py`）：接地中の足は地面速度ちょうどで後ろへ動く（その場歩き）ので、Unity で設計速度どおりに再生すれば足が滑らない。脚が伸び切る位置では骨盤を自動で下げる。横移動は骨盤を進行方向へ 45〜65° 向け、上半身はねじって銃口を正面に保つ。
- **速度**：歩き 1.48 m/s（0.567 秒周期）、走り 6.41 m/s（0.467 秒）、スプリント 9.58 m/s（0.417 秒）。プレイヤーの移動 6.5 m/s・スプリント 9.5 m/s にほぼ一致。中間速度は `LocomotionMath.RingPlaybackRate` が再生速度を補正。

---

## 4. クリップ一覧（47 本）

状態：◎＝Unity で再生とゲーム接続を確認、○＝Unity で再生を確認（ゲーム側にきっかけがなく、受け口のみ）。品質はすべて暫定版。

| # | クリップ | レイヤー | 状態 | ゲームとの接続 |
|---|---|---|---|---|
| 01 | Combat_Idle | Base | ◎ | 停止中 |
| 02-05 | Walk_Forward/Backward/Left/Right | Base | ◎ | 2D ブレンド（1.48 m/s の輪） |
| 06-09 | Run_Forward/Backward/Left/Right | Base | ◎ | 2D ブレンド（6.41 m/s の輪） |
| 10 | Sprint | Base | ◎ | 9.58 m/s。銃を斜め下に下げる |
| 11-14 | Jump_Start / Jump_Loop / Fall / Land | Base | ◎ | `FpsMotor.Jumped`、接地・垂直速度 |
| 15 | Dash_Forward/Backward/Left/Right | Base | ◎ | `FpsMotor.Dashed`（方向でブレンド） |
| 16 | Slide | Base | ○ | `Slide` パラメータ（スライディングの仕組みはまだない） |
| 17 | Rifle_Idle | （なし） | ○ | Combat_Idle と同じ姿勢。上半身レイヤーは素通しで同じ見た目になるため Animator には置いていない |
| 18 | Rifle_Fire | Additive | ◎ | 射撃ごとに先頭から再生（600 rpm） |
| 19 | Rifle_Reload | UpperBody | ◎ | `ReloadStarted`、長さ 1.7 秒＝武器のリロード時間（速度で合わせる） |
| 20-21 | Rifle_ADS_Enter / Exit（＋ADS_Idle） | UpperBody | ○ | `Aiming`（ADS の操作はまだない） |
| 22 | Rifle_Equip | UpperBody | ◎ | リスポーン時 |
| 23 | Rifle_Inspect | UpperBody | ◎ | 14 秒立ち止まると再生 |
| 24-30 | Skill_QuickCast / Charged_Start・Loop・Release / AreaCast / SelfBuff / Ultimate | UpperBody | ◎ | カード使用時（`SkillMotionRules`：レジェンダリー＝Ultimate、自分・周囲の回復/防御＝SelfBuff、地点・周囲の攻撃＝AreaCast、着弾遅延 0.3 秒以上＝Charged、ほか＝QuickCast） |
| 31 | Hit_Light | Additive | ◎ | 最大 HP の 20% 未満の被弾（シールドで吸収した分も含む） |
| 32 | Hit_Heavy | UpperBody | ◎ | 20% 以上の被弾。脚は止めない |
| 33 | Knockback | Reaction | ○ | `Knockback()`（吹き飛ばしの仕組みはまだない） |
| 34-36 | Downed_Start / Downed_Loop / Revived | Reaction | ◎ | `Revivable` のダウン・蘇生 |
| 37 | Death | Reaction | ◎ | 出血死（ダウン姿勢から倒れる）。リスポーンで解除 |
| 38 | Pickup | UpperBody | ○ | 拾う仕組みはまだない |
| 39 | Interact | UpperBody | ○ | 同上 |
| 40 | Revive_Ally | UpperBody | ◎ | 倒れた味方の蘇生ゲージを進めている間（ループ） |
| 41 | Use_Item | UpperBody | ◎ | ゲージアイテム使用時 |
| 42 | Emote | UpperBody | ○ | 操作の割り当てなし |
| 43 | Fox_Ear_Reaction | （手続き） | ◎ | 被弾で耳を伏せる・詠唱で立てる（`FoxSecondaryMotion`） |
| 44 | Fox_Tail_Ultimate | （手続き） | ◎ | Ultimate で尻尾を扇状に持ち上げる |
| 45 | Fox_Idle_Expression | （手続き） | ◎ | 待機中の耳のぴくっとする動き・尻尾のゆるい揺れ |
| — | Locomotion_Center | Base | ◎ | ブレンドツリーの中心（静止姿勢、歩きと同じ周期）。依頼にはない補助クリップ |

- **Skill_QuickCast**（2026-11-08 作り直し）：0.055 秒で手を左の脇腹へ引く → 0.12 秒（飛翔弾の発射の瞬間）に手のひらを肩の高さでまっすぐ前へ突き出す（体の 0.54 m 前。ハンドガードの握りより 17 cm 前）→ 発射の反動で手が少し戻る → 保持 → 0.45 秒で銃へ。胸を右へ 20° ひねり・12° 前傾・左鎖骨を前へ出して左肩を 15 cm 前へ運ぶ（旧版は胸を逆へひねって左肩が下がり、手は横へ動くだけだった）。銃はほぼ動かさない（一人称の手は銃からの相対位置で置くため、銃を大きく動かすと一人称の手の位置がずれる）。

43〜45 はクリップではなく手続き制御（尻尾・耳をクリップに入れると反応が遅れ、走る速さや旋回に合わせられないため）。

---

## 5. Animator（`AC_FoxGunner_TP`、生成物）

| レイヤー | マスク / 合成 | 内容 |
|---|---|---|
| Base | なし / Override（IK パスあり） | Idle、Locomotion（2D Freeform Directional、MoveX/MoveY を m/s で）、JumpStart→JumpLoop→Fall→Land、Dash（4 方向ブレンド）、Slide |
| UpperBody | `AM_FoxGunner_UpperBody` / Override | Empty（素通し）、Reload、ADS、Equip、Inspect、スキル 7 種、Hit_Heavy、Pickup/Interact/Use_Item/Emote、Revive_Ally |
| Additive | 同上 / Additive | 射撃の反動、軽い被弾 |
| Reaction | なし / Override | Knockback、Downed、Revived、Death |

- パラメータ名は `AnimatorParams`（従来の名前はそのまま、追加分も同じクラス）。`CharacterAnimatorDriver` は Controller にないパラメータを触らないので、敵・NPC の仮 Controller でも同じコードが動く。
- 割り込み：遷移中でも Any State（回避・スキル・被弾）が割り込める（着地→待機の途中で回避が遅れていた）。
- ルートモーションなし（移動は `FpsMotor`）。クリップはすべてその場（原点はかかとの間）。
- 上半身マスクは背骨・頭・腕・指＋`Weapon`/`IK_*`。脚・骨盤（Root）は Base のまま。尻尾・耳はどのクリップにも入れない（取り込みマスク `AM_FoxGunner_ClipImport`）。
- クリップの曲線 `LeftHandIK`（左手が銃に乗っているか）と `CastHand`（左手で詠唱中か）が同名のパラメータに入る。

---

## 6. IK・手続き制御

| 部品 | 内容 |
|---|---|
| 手の IK（Animation Rigging） | `TwoBoneIKConstraint` ×2。目標は `IK_Hand_L/R`（クリップが作者の手にキーを打つ）、ヒントは `IK_Elbow_*`。Humanoid 変換の誤差やクリップのブレンド中でも、手と銃のずれは 0.0 cm（PlayMode で計測） |
| 視線（`FoxBodyAnimator`） | Humanoid の Look At（Base の IK パス）で背骨と頭をエイム方向へ。視線は頭を向けるので、両手で普通に構えている間だけ銃口の誤差を見て目標を少しずつ補正：水平 0.0°、上 35° で 0.2°。ダウン・死亡中は 0、スプリント中は 20% |
| 尻尾・耳（`FoxSecondaryMotion`） | 減衰ばね（`AngleSpring`）。尻尾：待機のゆっくりした揺れ、旋回の遅れ、速度で揺れ幅・周期、落下で上がる、着地で揺れる、ダウンで垂れる、Ultimate で扇状、骨盤の前傾ぶんを打ち消して垂れ下がりを保つ。耳：ぴくっと動く、走ると後ろへ、被弾で伏せる、詠唱で立つ、ダウンで垂れる。画面外では止まる（自分の体＝影のための常時更新は除く） |
| 一人称の腕（`FirstPersonArms`、2026-11-08 作り直し） | 三人称と同じキツネの高精細スカルプト（Meshy の 234 万面）から、上腕の途中より先の腕・手・5 本の指をそのまま切り出して 4 万三角形に減らす（`fox_fp_arms.py`、39,999 三角形・41 ボーン）。造形し直した手や仮の手は使わない。UV は 4K で開き直し、色は 8K に拡大した体のアトラス（`ArtSource/FoxGunner/Export/FoxGunner_atlas_x4.png`）から、法線は高精細メッシュから、質感マスク（SoftToon の毛・布・金属の区別）は体のマスクから焼く。マテリアル `M_FoxGunner_SoftToon_FirstPerson` は体の SoftToon と同じ数値で、テクスチャだけ一人称用（`FoxGunnerLookFactory.EnsureFirstPersonMaterial`）。ウェイトは体の腕から写し、骨の長さは体と同じ（差 2 mm 以内を EditMode で検査）。指はメッシュのつながりで 5 本に切り分け（右手は指が触れているので、4 つにしか分かれないときは一番幅の広い塊を 2 つに割る）、各指に 3 関節（指の長さの 0・45・75%、親指は 0・40・70%）。手のひら（`Palm`→`PalmOut` の目印）から各関節の曲げ軸を求め、Unity で指ごと・関節ごとに握りの角度をかける（右＝ピストルグリップ、左＝ハンドガード、詠唱で開く、リロードで半開き）。毎フレーム、体の左右の手の目標（クリップが打つ `IK_Hand_L/R`）を体の銃に対する相対位置としてビューモデルの銃へ写し、解析的 2 ボーン IK で腕を合わせる（`ViewModelAnimator` の揺れの後）。手首のねじれは 6 割を前腕へ分散し、手首の曲がりは 38° まで（越えた分は前腕の向きへ戻す）。左手が届かないときは銃に沿って最大 12 cm 手前へずらして握る。詠唱中（左手が銃を離れている間）に手の目標が腕の長さより遠いときは、左肩が手について最大 12 cm 前へ出る（`maxCastReach`、体の胸のひねりの代わり。腕の切り口は画面の下端より下のまま） |
| 一人称の構図 | 肩はキツネ自身の肩の位置（体の Head と両肩のボーンから求めた目からの相対位置、`FirstPersonArms.ShoulderOffsets`）に置き、胸を構えの向きへ右に 44° ひねり 38° 前傾させる（キツネ専用の `FoxGunnerAnimationFactory.FoxHipTorsoYaw/Lean`。ウサギは `FirstPersonTorsoYaw/Lean` の 40°・33° のまま）。腕は伸ばさず体と同じ長さ。2026-11-09 からは、ユーザーが示した参考画像に合わせて銃を腰だめに低く構える。グリップは目から右 23.4 cm・下 25.5 cm・前 38 cm、銃身はカメラの正面と平行（下げ 0°・左右 0°、`CharacterPrefabBuilder.ViewModelGripInCamera` / `ViewModelHipPitch` / `ViewModelHipYaw`）。銃口は照準の右下（左下原点で横 0.63・縦 0.41）にあり、銃は画面上で 27° の傾きで右下へ寝る。左手は画面の下端でハンドガードを握る。銃身が視線と平行なので、画面上の銃身の線はちょうど照準へ向かい、弾は銃身からまっすぐ照準の先の着弾点へ飛ぶ（画面上の銃身と弾道の差は静止 0.0°、連射中 0.8〜0.9°。連射中の分は視点の反動）。弾道は曲げない（ユーザー指示「玉は曲げない。銃身から直線で飛ぶ」）。銃身を平行にすると、画面上の傾きは銃身の置き場所だけで決まる（照準から銃口への向きと同じ。高く置くほど寝る）。ユーザーが選んだ前の構図（グリップ右 23.4・下 27.5 cm、銃口 6° 下げ、22°）は弾道が銃身から 20° ずれて見えたので、銃身を平行にした候補の中から、輪郭がいちばん重なり傾きも近いものを選んだ：同じ高さ 31°・重なり 85%、2 cm 上 27°・83%（採用）、4.4 cm 上 22°・70%（`Screenshots/FoxFP_StraightLevel_Candidates.png`）。同じ日の別の候補（右 21.5・下 29 cm、35°）は銃が斜めに右下の角へ抜けた。弾道を銃身に沿って出してから照準へ曲げる案も試したが、採らなかった。射撃の跳ね上がりは銃を傾けず、1 発ごとに後ろへ 4.5 cm・上へ 0.6 cm 滑らせるだけ（`ViewModelAnimator.kickBack/kickRise`、`kickPitch` 0）。1 発ごとに銃口を 2.4° 跳ね上げていたころは、撃つたびに銃身の線が弾道からずれた。以前の構えは銃口 0°（銃身の線がちょうど照準に向かう）、グリップ右 15 cm・下 20 cm・前 39 cm で、銃が画面の右半分を覆っていた。2026-11-08 に左 6°・下 1° から 1°・0° へ直したのは、銃身が照準の左を向いて弾道とずれて見えたため。ウサギの SMG は `ViewModelRiflePitch`（0°）のまま。装填中は銃を時計回りに 18° 傾け、銃口を 6° 上げる（`ViewModelAnimator.reloadOffset/reloadEuler`）。以前の「12 cm 下げて銃口を 28° 下げる」動きでは、低く構えた銃が画面の外へ消えた。カード欄（スキル枠・支援品・山札）は下中央、武器欄は右下の角（`HudLayout`）。武器欄は機関部の後ろ側に重なる。ビューモデルのカメラは縦 70°（世界のカメラは 68°）。これより銃を遠くすると、体と同じ腕の長さでは左手が届かない（前 40 cm で 4.6 cm 足りない）。胸を 40°・33° のままにすると 2 cm 足りない。両肘は画面の外。手と目標のずれ 0.0 cm、手首の曲がりは左 15〜16°・右 37°。配置は `FoxAnimationCaptureTests.Tune_FirstPersonFox`（手動実行。候補を `Logs/fp_fox.json` か環境変数 `FP_FOX` に並べると、2 列のシートと、各手の目標とのずれ・手首の曲がり・肘と手の画面位置を出す）で決めた。旧 `Tune_FirstPersonLayout` は旧構成（伸ばした腕・固定の肩・`FP_Grip_L`）用 |
| 旧構成の問題（2026-11-08 に調査） | ①手が元のキツネではなかった（メタボールで造形し直した約 9,000 面の手袋、`fox_fp_hands.py`）②腕を前腕 1.3 倍・上腕 1.1 倍に伸ばしていた（銃を目から遠くに置いたため）③肩を左右別々に画面の下へ固定していた ④指の握りは焼き込みの固定形とシェイプキー 1 つだけ ⑤左手は体とは別の握り（`FP_Grip_L`）。この組み合わせで腕・指・銃の位置関係が体とずれて不自然に見えていた。今の構成は体と同じメッシュ・質感・骨の長さ・手の目標を使う |

---

## 7. ゲームとの接続

`FpsCharacter` が `CharacterAnimationEvents` を持ち、射撃・リロード・被弾・ジャンプ・回避・スプリント・エイム方向・ダウン/蘇生/出血死/リスポーン・味方の蘇生をドライバーへ渡す。`DeckController` がカード使用（`SkillMotionRules`）とアイテム使用を渡す。どれもゲームの処理を待たせない（カードの効果は押した瞬間に出て、モーションは後から見た目だけ追う）。プレイヤー・NPC（仮リグ）とも同じ経路。

---

## 8. 検証（2026-11-07、Unity 6000.5.4f1）

- EditMode 232/232、PlayMode 59/59（撮影・診断用の 3 件は手動実行のみ）。一人称の手と目標のずれ 0.0 cm、手首に残るねじれ最大 69°。追加テスト：
  - `FoxAnimationAssetTests`：依頼の全モーションが Humanoid クリップでループ設定が正しい、クリップが銃と IK 目標を動かし尻尾・耳を触らない、左手の曲線、速度の輪、4 レイヤーとマスク、全ステートにモーションと出口、マスク、追加ボーンとウェイト、体の IK/視線/尻尾、ドライバーの速度設定
  - `FoxAnimationLogicTests`：再生速度補正、重い被弾の判定、カードごとの詠唱モーション、ばね、尻尾・耳の目標、仮 Controller で警告が出ないこと
  - `FoxBodyPlayModeTests`：実プレイヤーで射撃・リロード・カード・ジャンプ・回避・被弾・ダウン・出血死・リスポーンが正しいレイヤーとステートに届く、移動・横移動中も両手が銃から離れない、エイムに銃口が追従する、尻尾と耳が動く
  - 既存の `PlayerBodyPlayModeTests`：6.5 m/s 走行時の接地足の滑り 0.18 m/s（以前の Meshy 走りの時は 0.25）
- Windows ビルド成功（116 MB、エラー・警告 0）。ROG Ally X（Handheld、60 上限）で平均 59.9 FPS、1% Low 48.5、メインスレッド 3.7 ms、GPU 2.4 ms（Phase 4 と同等）。
- 撮影（`Screenshots/FoxAnim_Locomotion/Weapon_Skills/Reactions/FirstPerson.png`）で、全モーションの見た目と一人称の腕を確認。

---

## 9. 残っている問題・次にやること

- 手続き生成なので動きは「破綻しない」水準。重心移動・タメ・フォロースルーの作り込みは手付けやモーキャプに劣る。特に Death（倒れ方）、Knockback、Slide、Revived の起き上がりは硬い。
- 走り（6.4 m/s）・スプリントは脚が短いので回転が速く、接地時間が非常に短い（数フレーム）。カートゥーン調としては成立するが、実在の走りではない。
- 横走りは脚が大きく開く場面がある。ADS・拾う・インタラクト・エモート・スライディング・吹き飛ばしはゲームに操作がないため、受け口だけ（ゲームで実際に使ったときに再調整が要る）。
- 三人称の指は 4 本まとめて曲がる 1 本骨（体のメッシュの限界）。一人称の手は元のスカルプトの指に 3 関節ずつのボーンがあるが、握りの角度は指ごとの固定値（右＝グリップ、左＝ハンドガード、詠唱で開く）で、引き金を引くなど指を個別に動かすアニメーションはない。
- 一人称の右手は太い銃本体の陰でほとんど見えない（目の位置からはグリップの右側が見えない）。見えるのは銃と、ハンドガードを握る左手（甲・前腕の毛並み・指先）。リロード中は銃が下がり、左手は画面の下へ出る。
- 一人称の肘のヒントと胸のひねりは待機の構えに合わせた固定値。リロード・詠唱で左手が銃を離れても肘の向きは固定のまま（大きく崩れはしないが、手付けほど自然ではない）。銃の位置や胸のひねりを変えたら `Tune_FirstPersonFox` で確かめ直す。
- 旧構成の手の造形スクリプト（`fox_fp_hands.py` / `test_hands.py`）は 2026-11-08 に削除した（Git の履歴に残っている）。
- 尻尾は脚との衝突判定をしていない（角度の制限と骨盤の前傾補正のみ）。
- NPC（ヴォルト）は仮リグのまま（同じドライバーとイベントで動くので、Humanoid の体に差し替えればこの Controller を使える）。
- 改善の優先度：①Death・Knockback・Revived の手付け（または Meshy の既製モーションのリターゲット、有料・要承認）②走りの接地時間を伸ばす（周期を 0.5〜0.55 秒に）③横走りの脚の交差④ADS など未接続モーションのゲーム側実装⑤尻尾と脚の簡易衝突。
