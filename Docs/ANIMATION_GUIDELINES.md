# ANIMATION_GUIDELINES — キャラクターモーション指針

> ゲームの柱の第2位：**自然で滑らかなキャラクターモーション**。
> 不自然なモーションは極力避ける。最初は仮モデル・仮モーションで動作を作り、最終品質のモーションに**コードを変えずに差し替えられる**構造にする。

---

## 1. 基本方針

| 方針 | 内容 |
|---|---|
| 一人称と全身の分離 | 自分の画面に映る**一人称の腕・武器**と、NPC・他プレイヤーから見える**全身モデル**は別のリグ・別の Animator で管理する |
| モーター駆動 | 移動は `FpsMotor`（CharacterController）が決め、アニメーションは**モーターの速度に追従**する。**ルートモーションは使わない**（入力遅延・ネットワーク同期・操作感を優先） |
| 上下半身の分離 | 下半身＝移動、上半身＝エイム・射撃・リロード・スキル・被弾。AvatarMask でレイヤーを分ける |
| IK 補正 | Animation Rigging で左手グリップ、上半身エイム、足の接地を補正する |
| 仮→本番の差し替え | パラメータ名・ステート名・クリップのスロットを固定し、クリップ／モデルの差し替えだけで品質を上げる |

---

## 2. リグ構成

### 2.1 一人称（FP）リグ

```
Camera Pivot（視点）
 └─ ViewModel Root（ViewModel レイヤー、専用オーバーレイカメラで描画）
     ├─ FP Arms（腕メッシュ、Generic リグ）
     └─ Weapon（武器メッシュ、マズル・左手グリップ位置のソケット）
```

- ViewModel は専用レイヤーと URP のオーバーレイカメラで描画し、壁へのめり込みを防ぐ。視野角は本体カメラと別に設定できる。
- 揺れ（ボブ・スウェイ）・リコイルの見た目はプロシージャル処理（Phase 1 実装：`ViewModelAnimator`。ボブ・スウェイ・射撃キック・リロード時の下げ・スプリント時の傾き・着地の沈み込み）とクリップを組み合わせる。カメラ自体の過剰な揺れは避ける。
- FP リグは Generic でよい（腕と武器だけのため）。

### 2.2 全身（TP）リグ

- 人型キャラクターは **Humanoid** リグにし、モーションをリターゲットで共有する。
- 自分自身の全身モデルは、自分のカメラからは描画しない（影のみ描画する設定を検討）。
- NPC・他プレイヤーは全身モデルのみで表示する。

---

## 3. Animator 構成（全身）

### レイヤー

| レイヤー | 内容 | マスク／ブレンド |
|---|---|---|
| Base（Locomotion） | 待機・8方向移動・スプリント・ジャンプ・落下・着地・ダウン・蘇生 | 全身、Override |
| UpperBody | エイム姿勢・射撃・リロード・スキル発動・被弾 | 上半身 AvatarMask、Override |
| Additive | リコイル・被弾リアクションの加算 | 上半身 AvatarMask、Additive |

### Base レイヤーのステート

```
Locomotion（2D Freeform Directional Blend Tree：MoveX, MoveY）
 ├─ Idle（0,0）
 ├─ 前・後・左・右・斜め4方向（8方向）
 └─ Sprint（前方、Speed が閾値超え）
Jump → Fall → Land → Locomotion
Down（IsDown = true） → Revive（Revive トリガー） → Locomotion
```

- 8方向移動は **2D Freeform Directional** の Blend Tree で滑らかにつなぐ。
- スプリントは前方向の Blend Tree 内に配置するか、`Speed` による1D ブレンドで切り替える（実装時に足滑りで比較して決める）。
- ジャンプ・落下・着地は `IsGrounded` と `VerticalSpeed` で遷移する。

### パラメータ（名前は固定）

| パラメータ | 型 | 意味 | 供給元 |
|---|---|---|---|
| `MoveX` | float | ローカル右方向の速度（正規化 or m/s） | モーター速度 |
| `MoveY` | float | ローカル前方向の速度 | モーター速度 |
| `Speed` | float | 水平速度の大きさ | モーター速度 |
| `IsGrounded` | bool | 接地中 | モーター |
| `VerticalSpeed` | float | 垂直速度 | モーター |
| `Fire` | trigger | 射撃 | 武器イベント |
| `Reload` | trigger | リロード開始 | 武器イベント |
| `Skill` | trigger | スキル発動 | スキル実行 |
| `SkillId` | int | 発動したスキルのモーション番号 | `CardDefinition` |
| `Hit` | trigger | 被弾 | `Health` イベント |
| `IsDown` | bool | ダウン中 | 戦闘状態 |
| `Revive` | trigger | 蘇生 | 戦闘状態 |

- パラメータは「アニメーション駆動コンポーネント」（実装：`CharacterAnimatorDriver`）がモーター・武器・ヘルスのイベントから設定する。ゲームロジックは Animator に依存しない。
- オンラインでは、リモートキャラクターは同期された速度・状態からローカルでパラメータを計算する（アニメーション自体は同期しない）。

### 3.1 Phase 2 で実装した仮の Animator（2026-11-07）

生成ツール（`AnimationAssetFactory`）が `Assets/_Game/Animations/Placeholder/` に作る。外部素材は使っていない。

| 資産 | 内容 |
|---|---|
| 仮リグ（`PlaceholderRigBuilder`） | プリミティブで組んだ Generic 階層：`Hips` / `Hips/Spine` / `Hips/Spine/Head` / `Hips/Spine/Shoulder_L・R` / `Hips/Hip_L・R` |
| クリップ 16 本 | `A_Placeholder_Idle`、`Walk_F/FR/R/BR/B/BL/L/FL`、`Fall`、`Death`、`AttackMelee`、`AttackShoot`、`Hit`、`Fire`、`Reload`。回転・位置のカーブをコードで生成 |
| `AC_PlaceholderHumanoid` | Base：`Locomotion`（2D Freeform Directional、MoveX/MoveY、中心に Idle＋8 方向）、`Airborne`（`IsGrounded` が false の間）、`Death`（Any State から `Dead`）。UpperBody（`AM_UpperBody`、Override、重み 1）：`Empty` から `Attack` / `Hit` / `Fire` / `Reload` をトリガーで再生して戻る |
| `AOC_Shooter` | 上記の Override Controller。`AttackMelee` だけを `AttackShoot`（構えて撃つ姿勢）に差し替え |
| `AM_UpperBody` | Spine・Head・両肩が有効、Hips・両脚は無効 |

実装済みのパラメータ：`MoveX` `MoveY` `Speed` `LocomotionSpeed`（歩行クリップの再生速度。足滑り対策で地上速度÷歩幅、0.75〜1.8 倍）`IsGrounded` `VerticalSpeed` `Fire` `Reload` `Attack`（敵の攻撃予兆）`Hit` `Dead`。

計画からの差分（未実装）：
- Sprint は専用クリップではなく `LocomotionSpeed` で歩行を速く再生して代用。Jump / Land は `Airborne` 1 ステート。
- `Skill` / `SkillId` / `IsDown` / `Revive` と Additive レイヤーはまだない（プレイヤーのダウンは `Dead` で代用）。
- 仮リグは Humanoid ではないので、本番モデルでは同じパラメータ名・ステート名で Humanoid 用の Controller を作り、クリップを差し替える。

使われ方：
- 敵：`PF_Enemy_Slasher` は `AC_PlaceholderHumanoid`、`PF_Enemy_Shooter` は `AOC_Shooter`。速度は NavMeshAgent から。
- プレイヤー：2026-11-07 に本番モデル「Fox Gunner」（Humanoid、`PF_FoxGunner_Body`）へ差し替え。`BodyVisual` は入れ子の Prefab で、Controller は `AC_FoxGunner_TP`（パラメータ名は同じ）。影だけ描画（自分のカメラにメッシュは映らない）。速度は `FpsMotor` から。詳細は `CHARACTER_PIPELINE.md`。
- NPC（ヴォルト）は引き続き仮リグと `AC_PlaceholderHumanoid`。
- 一人称の腕・武器はクリップではなく `ViewModelAnimator` のプロシージャル動作（Phase 2 で待機時の呼吸を追加）。武器は 2026-11-07 から本番モデル「Fire Rifle」（`PF_Weapon_FireRifle`、手はまだ仮のカプセル）。同じライフルを全身モデルの右手にも持たせている（素手のモーションのまま。構えのクリップと左手 IK は未対応）。

---

## 4. Animation Rigging

| 制約 | 目的 |
|---|---|
| Two Bone IK（左手） | 左手を武器のグリップソケットに固定。武器やモーションが変わってもズレない |
| Multi-Aim（脊椎・首・頭） | 上半身と頭をエイムポイントに向ける。ピッチ方向の制限を設定 |
| 足 IK／接地 | 段差・坂で足を地面に合わせる（Raycast で接地点を求めて IK ターゲットを移動） |

- 実装状況（Phase 2）：敵の頭の **Multi-Aim** のみ（`Rig` + `MultiAimConstraint`、`LookTarget` を対象の胸の高さへ毎フレーム移動、左右・上下 ±70°）。左手 Two Bone IK と足 IK は本番モデル導入時に追加する（仮リグには手・足のボーンがない）。
- IK のウェイトはステートに応じて切り替える（例：リロード中は左手 IK を弱める、ダウン中は Aim を切る）。
- Ally X では足 IK を遠距離キャラクターで無効化するなど、性能ティアで負荷を調整できるようにする。

---

## 5. 命名規則とインポート設定

### クリップ名

`A_<Character>_<Action>_<Variant>`

| 例 | 意味 |
|---|---|
| `A_Common_Run_Fwd` | 共通の前方ラン |
| `A_Common_Run_FwdLeft` | 共通の左前ラン |
| `A_Ignis_Skill_Meteor` | イグニス固有のメテオ発動 |
| `A_FP_AR_Reload_Empty` | 一人称アサルトライフルの弾切れリロード |
| `A_Placeholder_Idle` | 仮モーション |

- 共通モーションは `Common`、一人称は `FP`、仮は `Placeholder` を使う。
- Animator Controller：`AC_<Character>_<Rig>`（例：`AC_Common_TP`、`AC_FP_AR`）。
- AvatarMask：`AM_<Part>`（例：`AM_UpperBody`）。
- 配置：`Assets/_Game/Animations/<Character>/` と `Assets/_Game/Art/Characters/<Character>/`。

### FBX インポート設定

| 項目 | 全身（TP） | 一人称（FP） |
|---|---|---|
| Rig | Humanoid（アバターはキャラクターごと） | Generic |
| Root Motion | 使わない（Bake Into Pose を適切に設定） | 使わない |
| Loop | 移動・待機は Loop Time + Loop Pose | 待機・射撃ループ等 |
| 圧縮 | Optimal（品質問題があれば Keyframe Reduction の誤差を調整） | 同左 |
| Scale | 1単位 = 1m に統一 | 同左 |

---

## 6. 仮モーション優先のワークフロー

1. 仮モデル（プリミティブまたは仮の Humanoid）と仮クリップで、ステート・パラメータ・遷移を完成させる。
2. ゲームプレイと同期（射撃・リロード時間など）をイベントで確認する。
3. 最終モデル・最終クリップに**差し替えるだけ**で動くことを確認する（パラメータ名・ステート名は変更しない）。
4. 差し替え後に §8 の品質チェックを行う。

- リロード時間などゲームロジック上の時間は `WeaponDefinition` が正本。クリップは長さを合わせるか、再生速度で合わせる。

---

## 7. モーションの入手方針

| 優先度 | 方法 | 条件 |
|---|---|---|
| 1 | 高品質な既製モーション（ライセンスが明確なパック） | **商用利用条件を確認・記録**してから導入 |
| 2 | 固有スキルのみ、Uthana などの AI モーション生成 | **用途と費用をユーザーに提示し、事前確認を得てから**生成。勝手に課金・生成しない |
| 3 | Blender での補正 | 足滑り、手の位置、ループのつなぎ目、ポーズの誇張 |

- 既存ゲームのモーションを複製・抽出して使わない。

---

## 8. 品質チェックリスト

- [ ] **足滑り**：移動速度とクリップの歩幅が一致している（Blend Tree の閾値をモーター速度に合わせる）
- [ ] **ブレンド時間**：遷移が速すぎて弾けない／遅すぎて反応が鈍く見えない（目安 0.1〜0.25 秒）
- [ ] **遷移の割り込み**：射撃・リロード・スキル・被弾が移動中でも自然に割り込める
- [ ] **方向転換**：8方向の切り替えで足がもつれない
- [ ] **ジャンプ・着地**：着地の硬直が操作を妨げない（ロジックは止めず、見た目だけ）
- [ ] **エイム範囲**：上下のエイム角（例：-70°〜+70°）で上半身が破綻しない
- [ ] **左手 IK**：全モーションで左手がグリップから外れない（リロード中の意図的な離し以外）
- [ ] **一人称**：画面端で腕が切れない、武器がカメラに近すぎない、揺れが酔いを誘わない
- [ ] **ネットワーク**：リモートキャラクターが同期された速度だけで自然に見える
- [ ] **性能**：Ally X で Animator・IK のコストが許容範囲（Profiler で確認）
