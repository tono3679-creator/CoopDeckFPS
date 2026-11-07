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
| 3. 一人称の腕 | `fox_anim/fox_fp_arms.py`（前腕＝Meshy の高解像度メッシュ、手＝`fox_fp_hands.py` で造形） | `Assets/.../Models/FoxGunner_FPArms.fbx` |
| 4. 確認用レンダー | `fox_anim/check_stance.py`、`check_clip.py`、`debug_segment.py` | コンタクトシート（作業用、保存しない） |
| 5. Unity 取り込み | `FoxGunnerAnimationFactory`（`FoxGunnerAssetFactory.EnsureAll` とブートストラップから） | マスク、`AC_FoxGunner_TP`、体の IK・視線・尻尾/耳、一人称の腕 |

```powershell
$b = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
& $b -b --factory-startup -P ArtSource\Tools\fox_anim\fox_rig.py                                   # ボーン追加とモデル FBX
& $b -b ArtSource\FoxGunner\Animation\FoxGunner_AnimRig.blend -P ArtSource\Tools\fox_anim\build_clips.py   # 全クリップ（約 10 秒）
& $b -b ArtSource\FoxGunner\Animation\FoxGunner_AnimRig.blend -P ArtSource\Tools\fox_anim\fox_fp_arms.py  # 一人称の腕（約 20 秒）
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
| `FP_Grip_L` | Weapon | 一人称の左手の握り（ハンドガードの左側面）。キーは打たず、銃に対する相対位置として Unity が読む |

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
| 一人称の腕（`FirstPersonArms`） | 前腕は Meshy の高解像度メッシュ（234 万面）から切り出し約 1.4 万面。ウェイトは体の腕から、UV は体全体から写す（`body_uvs`：各面は最寄りの体の三角形から UV が連続する半径 3 cm の範囲だけに写し、継ぎ目をまたぐ面やアトラスの隙間を横切る面は 1 枚の三角形に収める。単純な頂点ごとの転写だと継ぎ目で面が引き伸ばされ、黒い三角形が出ていた）。手は Blender で新規造形（`fox_fp_hands.py`：メタボールで手のひら・手首・指 4 本×3 関節・親指を作り、平滑化＋細分化して片手約 9,000 面、指なし手袋・甲のオレンジのプレート、色は体のテクスチャ上の均一な点を参照）。右手はピストルグリップを握る形、左手はハンドガードを握る形を焼き込み、左手に詠唱用のシェイプキー「Open」。毎フレーム、体の手の目標（右）と `FP_Grip_L`（左、銃を握っている間）を体の銃に対する相対位置としてビューモデルの銃へ写し、解析的 2 ボーン IK で腕を合わせる（`ViewModelAnimator` の揺れの後）。手首のねじれは 6 割を前腕へ分散（ねじれた細い手首を防ぐ）。肘は下向きの固定方向。リロード・詠唱の左手の道筋は三人称と同じで、詠唱中は手を開く |

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
- 三人称の指は 4 本まとめて曲がる 1 本骨（体のメッシュの限界）。一人称の手は新規造形で指ごとに曲げてあるが、握り形は固定（右＝グリップ、左＝ハンドガード、左の詠唱＝Open シェイプ）で、指を個別に動かすアニメーションはない。
- 一人称の右手は銃のグリップが画面外のため見えない（従来の配置のまま）。
- 尻尾は脚との衝突判定をしていない（角度の制限と骨盤の前傾補正のみ）。
- NPC（ヴォルト）は仮リグのまま（同じドライバーとイベントで動くので、Humanoid の体に差し替えればこの Controller を使える）。
- 改善の優先度：①Death・Knockback・Revived の手付け（または Meshy の既製モーションのリターゲット、有料・要承認）②走りの接地時間を伸ばす（周期を 0.5〜0.55 秒に）③横走りの脚の交差④ADS など未接続モーションのゲーム側実装⑤尻尾と脚の簡易衝突。
