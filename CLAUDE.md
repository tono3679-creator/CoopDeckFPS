# CLAUDE.md — CoopDeckFPS

Unity 6 (URP) co-op roguelike FPS with deck building. Read `Docs/ARCHITECTURE.md` before changing code and
`Docs/DEVELOPMENT_PLAN.md` for the current phase. Report to the user in Japanese.

## Rules
- Work phase by phase. Nothing counts as done until it compiles, its tests pass and it was checked in the Editor or a build.
- Gameplay code lives in `Assets/_Game/Scripts/<System>` with one asmdef per system (`CoopDeck.<System>`).
  Keep dependencies one-directional as in ARCHITECTURE.md §2. `CoopDeck.Player` must not reference `CoopDeck.Input`.
- Characters are driven only through `ICharacterCommandSource` → `CharacterCommands`. Never read input devices in gameplay code.
- Keep rules in plain C# (no MonoBehaviour) where possible and cover them with EditMode tests.
- Damage and visuals stay separate: gameplay raises events; effects are prefabs played through `FxPool`.
- Static events/state must reset in a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` method.
- Do not hand-edit generated scenes/prefabs; change the generator in `Assets/_Game/Scripts/Editor` and rerun
  `CoopDeck > Setup > Run Phase 1 Bootstrap` (or `ProjectBootstrap.RunBatch`). If a live Editor is open, drive it with `unity command` instead of editing YAML.
- No external assets with unclear commercial terms. Ask before using paid services (Uthana, Meshy, …). Never commit API keys (`.mcp.json` is ignored).

## Commands (project must not be open in another Editor for batch runs)
```
set UNITY="C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe"
%UNITY% -batchmode -quit -projectPath . -executeMethod CoopDeck.EditorTools.ProjectBootstrap.RunBatch -logFile Logs/bootstrap.log
%UNITY% -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode.xml
%UNITY% -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults Logs/playmode.xml
%UNITY% -batchmode -quit -projectPath . -executeMethod CoopDeck.EditorTools.BuildScript.BuildWindowsBatch -logFile Logs/build.log
Builds\Windows\CoopDeckFPS.exe -perfrun 30 -perfout PerfCaptures\perf.json
```
With the Editor open, `unity status` / `unity command` (Unity CLI + `com.unity.pipeline`) drive it live.
