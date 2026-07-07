# SolidWorks Copilot Add-in MVP Spec

## 1. Goal

Build a minimal SolidWorks-native add-in that proves the core product loop:

```text
SolidWorks Task Pane opens
-> user enters a chat command
-> add-in maps the input to a hard-coded command
-> add-in shows an execution plan
-> user confirms
-> local executor calls SolidWorks API
-> native FeatureManager features are created or modified
```

This MVP is intentionally "fake intelligent, real execution."

The goal is not to prove LLM quality. The goal is to prove that a Copilot-like UI inside SolidWorks can safely trigger native SolidWorks API operations.

## 2. Product Shape

Primary product surface:

- SolidWorks Add-in
- Right-side Task Pane
- Chat-style interaction
- Current document status
- Execution plan preview
- Confirm and execute button
- Basic success / failure messages

This should feel like a small Copilot panel embedded in SolidWorks, not an external CAD generator.

## 3. MVP Scope

### In Scope

- C# SolidWorks Add-in
- Task Pane UI
- Chat input box
- Conversation transcript
- Hard-coded command matching
- Execution plan preview
- SolidWorks API executor
- Create a simple mounting plate
- Modify existing named dimensions
- Rebuild model
- Basic error display

### Out of Scope

- LLM integration
- Cloud backend
- Login / user management
- Billing
- Multi-CAD support
- PDM / PLM integration
- Assemblies
- Drawings
- General natural-language understanding
- Arbitrary CAD generation
- Production installer hardening

## 4. Demo User Flow

### Flow A: Create a Part

1. User opens SolidWorks.
2. User enables the add-in.
3. User opens the Copilot Task Pane.
4. User types:

```text
创建一个 100x60x8 的安装板，四个 M6 通孔
```

5. Add-in shows a plan:

```text
将执行：
1. 创建中心矩形草图
2. 拉伸 8mm
3. 创建 4 个通孔
4. 命名特征和关键尺寸
```

6. User clicks `执行`.
7. Add-in calls SolidWorks API.
8. SolidWorks creates native FeatureManager features:

```text
AI_BasePlate
AI_4x_M6_ThroughHoles
```

9. Add-in shows success message.

### Flow B: Modify Thickness

1. User types:

```text
把厚度改成 10mm
```

2. Add-in maps input to `UpdateThickness(10)`.
3. Add-in shows a plan:

```text
将执行：
1. 找到 AI_thickness 尺寸
2. 将厚度改为 10mm
3. 重建模型
```

4. User clicks `执行`.
5. Add-in updates the existing named dimension.
6. Model rebuilds successfully.

### Flow C: Modify Hole Diameter

1. User types:

```text
把孔径改成 8mm
```

2. Add-in maps input to `UpdateHoleDiameter(8)`.
3. Add-in updates the existing named hole diameter dimension.
4. Model rebuilds successfully.

## 5. Architecture

```text
SolidWorks Add-in
  Task Pane UI
    - document status
    - chat transcript
    - input box
    - execution plan
    - execute button

  Command Router
    - hard-coded input matching
    - extracts simple numeric values
    - returns typed command object

  Plan Builder
    - maps command object to human-readable plan

  Executor
    - CreateMountingPlate()
    - UpdateThickness(valueMm)
    - UpdateHoleDiameter(valueMm)
    - Rebuild()

  SolidWorks API Layer
    - select plane
    - create sketch
    - create dimensions
    - create extrude
    - create cut
    - find named dimensions
    - rebuild document
```

## 6. Command Router

The MVP should not call an LLM. Use deterministic command routing.

Example routing:

```text
input contains "安装板" or "mounting plate"
-> CreateMountingPlate(width=100, height=60, thickness=8, holeDiameter=6)

input contains "厚度" and a number
-> UpdateThickness(valueMm=number)

input contains "孔径" and a number
-> UpdateHoleDiameter(valueMm=number)
```

If no command matches, return:

```text
当前 MVP 只支持：
1. 创建安装板
2. 修改厚度
3. 修改孔径
```

## 7. First Supported Commands

### 7.1 CreateMountingPlate

Default parameters:

```text
width = 100mm
height = 60mm
thickness = 8mm
holeDiameter = 6mm
holeOffsetX = 10mm from side edges
holeOffsetY = 10mm from top/bottom edges
```

Expected features:

```text
AI_BasePlate
AI_4x_M6_ThroughHoles
```

Expected named dimensions:

```text
AI_width
AI_height
AI_thickness
AI_hole_diameter
AI_hole_offset_x
AI_hole_offset_y
```

Implementation requirement:

- Create native sketch geometry.
- Add dimensions where possible.
- Create native boss extrude.
- Create native cut extrude or hole feature.
- Name features clearly.
- Rebuild after execution.

### 7.2 UpdateThickness

Input examples:

```text
把厚度改成 10mm
thickness 10
set thickness to 10mm
```

Expected behavior:

- Find existing `AI_thickness` dimension.
- Set value in meters using SolidWorks API.
- Rebuild model.
- Show success or failure.

### 7.3 UpdateHoleDiameter

Input examples:

```text
把孔径改成 8mm
hole diameter 8
set hole diameter to 8mm
```

Expected behavior:

- Find existing `AI_hole_diameter` dimension.
- Set value in meters using SolidWorks API.
- Rebuild model.
- Show success or failure.

## 8. UI Requirements

### Task Pane Layout

Minimal layout:

```text
Header
  SolidWorks Copilot MVP
  Current document status

Conversation
  User message
  Assistant response
  Execution plan

Input area
  Text input
  Send button

Plan area
  Plan steps
  Execute button
  Cancel button
```

### Current Document Status

Show one of:

```text
No document open
Part document active
Assembly document active
Unsupported document type
```

For MVP, only part documents are supported.

### Conversation States

Required message states:

- User input
- Matched command
- Unsupported command
- Plan ready
- Executing
- Success
- Error

## 9. Executor Requirements

The executor should be deterministic and defensive.

Required behaviors:

- Check that SolidWorks is running.
- Check that an active part document exists.
- Clear selection before operations.
- Select a stable base plane.
- Avoid relying only on English plane names.
- Use fallback plane selection through feature traversal.
- Name created features.
- Name dimensions where possible.
- Rebuild after changes.
- Return structured result:

```text
success: true / false
message: string
createdFeatures: string[]
modifiedDimensions: string[]
error: string | null
```

## 10. Key Technical Lessons from API Validation

From the initial VBA validation:

1. Plane selection cannot rely only on `"Front Plane"`.
   - Localized SolidWorks may use `"前视基准面"`.
   - Executor needs a feature-tree fallback.

2. Cut direction must be handled explicitly.
   - `FeatureCut4` direction parameters can determine whether the cut succeeds.
   - Productized executor should infer direction from sketch plane, body, and expected boolean result.

3. Creating a feature is not enough.
   - The product must create editable dimensions and preserve design intent.

## 11. Data Model

For this MVP, use simple in-memory command objects.

Example:

```json
{
  "type": "CreateMountingPlate",
  "parameters": {
    "widthMm": 100,
    "heightMm": 60,
    "thicknessMm": 8,
    "holeDiameterMm": 6
  }
}
```

Example:

```json
{
  "type": "UpdateThickness",
  "parameters": {
    "valueMm": 10
  }
}
```

Future versions can replace the hard-coded router with an LLM that emits the same command objects.

## 12. Success Criteria

The MVP is successful if all are true:

- Add-in loads inside SolidWorks.
- Task Pane opens and displays the chat UI.
- User can enter a text command.
- Add-in maps supported inputs to fixed commands.
- Add-in shows an execution plan before modifying the model.
- User can confirm execution.
- SolidWorks API creates native FeatureManager features.
- Created features are not imported bodies.
- User can modify thickness through a second chat command.
- User can modify hole diameter through a third chat command.
- Model rebuilds successfully after each operation.
- Errors are shown clearly in the Task Pane.

## 13. Non-Goals for This MVP

Do not optimize for:

- Beautiful UI
- General prompt understanding
- Multi-step reasoning
- Cloud sync
- Team collaboration
- Login
- Pricing
- Production-grade installer
- Full undo/redo stack
- Enterprise security

This MVP is a technical-product prototype. It should make the end-to-end loop visible and testable.

## 14. Follow-Up After MVP

If the MVP works, next steps:

1. Add explicit dimension constraints and equation support.
2. Add rollback/checkpoint behavior.
3. Add feature tree context display.
4. Add selection-aware commands.
5. Interview users to choose the first real workflow scope.
6. Replace hard-coded command router with LLM-generated command objects.
7. Add backend auth and usage tracking.

